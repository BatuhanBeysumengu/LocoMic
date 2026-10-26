using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EqualizerHelper.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EqualizerHelper.Audio;

/// <summary>Live-tunable soundboard settings shared by every output chain.</summary>
public class SoundboardSettings
{
    /// <summary>0..1 volume applied to all sounds.</summary>
    public double MasterVolume { get; set; } = 0.6;

    /// <summary>Turn sounds down automatically while the user is talking.</summary>
    public bool DuckWhileTalking { get; set; } = true;

    /// <summary>Also play sounds into the "hear yourself" monitor.</summary>
    public bool HearInMonitor { get; set; } = true;

    /// <summary>How far sounds are turned down while talking.</summary>
    public const double DuckAmountDb = -14;

    /// <summary>Voice level above which ducking kicks in.</summary>
    public const double DuckThresholdDb = -42;
}

/// <summary>
/// Plays audio files (songs, sound effects) into the processed mic so other
/// apps hear them along with the voice. One <see cref="SoundboardSampleProvider"/>
/// lives in each output chain (main output, monitor); this controller opens a
/// separate decoder for each, on the calling (UI) thread, so the audio
/// threads never touch the file system to start a sound.
/// </summary>
public sealed class Soundboard
{
    private readonly List<(SoundboardSampleProvider Mixer, bool IsMain)> _mixers = new();

    public SoundboardSettings Settings { get; } = new();

    /// <summary>
    /// Raised (on an audio thread) when a non-looping sound reaches its end on
    /// the main output.
    /// </summary>
    public event EventHandler<Guid>? SoundEnded;

    /// <summary>True while the engine is running and sounds can be played.</summary>
    public bool IsAvailable => _mixers.Count > 0;

    /// <summary>
    /// Starts (or restarts) a sound. Returns false if the engine isn't running.
    /// </summary>
    /// <exception cref="Exception">The file couldn't be opened or decoded.</exception>
    public bool Play(Guid id, string path, Func<double> getVolume, Func<bool> getLoop)
    {
        if (_mixers.Count == 0)
        {
            return false;
        }

        // Open every decoder first so a bad file fails before anything plays.
        var streams = new List<(SoundboardSampleProvider Mixer, SoundStream Stream)>();
        try
        {
            foreach (var (mixer, _) in _mixers)
            {
                streams.Add((mixer, new SoundStream(id, path, mixer.SampleRate, getVolume, getLoop)));
            }
        }
        catch
        {
            foreach (var (_, stream) in streams)
            {
                stream.Dispose();
            }

            throw;
        }

        foreach (var (mixer, stream) in streams)
        {
            mixer.Add(stream);
        }

        return true;
    }

    public void Stop(Guid id)
    {
        foreach (var (mixer, _) in _mixers)
        {
            mixer.Remove(id);
        }
    }

    public void StopAll()
    {
        foreach (var (mixer, _) in _mixers)
        {
            mixer.RemoveAll();
        }
    }

    internal void Attach(SoundboardSampleProvider mixer, bool isMain)
    {
        _mixers.Add((mixer, isMain));
        if (isMain)
        {
            mixer.StreamEnded += id => SoundEnded?.Invoke(this, id);
        }
    }

    /// <summary>Called by the engine once outputs have stopped pulling audio.</summary>
    internal void DetachAll()
    {
        foreach (var (mixer, _) in _mixers)
        {
            mixer.DisposeStreams();
        }

        _mixers.Clear();
    }
}

/// <summary>
/// One decoded sound file, converted to mono at the chain's sample rate.
/// Loops back to the start when its loop flag is set.
/// </summary>
internal sealed class SoundStream : IDisposable
{
    private readonly AudioFileReader _reader;
    private readonly ISampleProvider _mono;
    private readonly Func<double> _getVolume;
    private readonly Func<bool> _getLoop;

    public SoundStream(Guid id, string path, int sampleRate, Func<double> getVolume, Func<bool> getLoop)
    {
        Id = id;
        _getVolume = getVolume;
        _getLoop = getLoop;
        _reader = new AudioFileReader(path);

        try
        {
            ISampleProvider source = _reader;
            source = source.WaveFormat.Channels switch
            {
                1 => source,
                2 => new StereoToMonoSampleProvider(source),
                _ => throw new NotSupportedException($"Sounds with {source.WaveFormat.Channels} channels aren't supported; use a mono or stereo file."),
            };

            _mono = source.WaveFormat.SampleRate == sampleRate ? source : new WdlResamplingSampleProvider(source, sampleRate);
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public Guid Id { get; }

    public float Volume => (float)Math.Clamp(_getVolume(), 0, 1);

    /// <summary>Fills <paramref name="buffer"/>; returns false once a non-looping sound has finished.</summary>
    public bool Read(Span<float> buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = _mono.Read(buffer[filled..]);
            filled += read;
            if (read > 0)
            {
                continue;
            }

            if (!_getLoop() || _reader.Length == 0)
            {
                buffer[filled..].Clear();
                return false;
            }

            _reader.Position = 0;
        }

        return true;
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Mixes the soundboard's active sounds into the voice signal. While the
/// voice is above <see cref="SoundboardSettings.DuckThresholdDb"/> the sounds
/// are smoothly turned down, and a safety limiter keeps voice + sounds from
/// clipping. Start/stop requests arrive through a queue and are applied at
/// the start of the next audio block.
/// </summary>
public sealed class SoundboardSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly Func<SoundboardSettings> _getSettings;
    private readonly bool _isMonitor;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly List<SoundStream> _active = new();
    private readonly Limiter _limiter;
    private readonly double _duckAttackCoeff;
    private readonly double _duckReleaseCoeff;
    private readonly double _voiceReleaseCoeff;
    private float[] _mix = Array.Empty<float>();
    private float[] _scratch = Array.Empty<float>();
    private double _voiceEnvelope;
    private double _duckGain = 1;

    public SoundboardSampleProvider(ISampleProvider source, Func<SoundboardSettings> getSettings, bool isMonitor)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _getSettings = getSettings;
        _isMonitor = isMonitor;
        SampleRate = source.WaveFormat.SampleRate;
        _limiter = new Limiter(SampleRate);
        _duckAttackCoeff = Math.Exp(-1.0 / (0.040 * SampleRate));
        _duckReleaseCoeff = Math.Exp(-1.0 / (0.600 * SampleRate));
        _voiceReleaseCoeff = Math.Exp(-1.0 / (0.150 * SampleRate));
    }

    public int SampleRate { get; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    internal event Action<Guid>? StreamEnded;

    internal void Add(SoundStream stream) => _commands.Enqueue(() =>
    {
        RemoveNow(stream.Id);
        _active.Add(stream);
    });

    internal void Remove(Guid id) => _commands.Enqueue(() => RemoveNow(id));

    internal void RemoveAll() => _commands.Enqueue(DisposeStreams);

    internal void DisposeStreams()
    {
        foreach (var stream in _active)
        {
            stream.Dispose();
        }

        _active.Clear();
    }

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        while (_commands.TryDequeue(out var command))
        {
            command();
        }

        if (_active.Count == 0)
        {
            return samplesRead;
        }

        int frames = samplesRead / _channels;
        if (_mix.Length < frames)
        {
            _mix = new float[frames];
            _scratch = new float[frames];
        }

        var mix = _mix.AsSpan(0, frames);
        var scratch = _scratch.AsSpan(0, frames);
        mix.Clear();

        for (int s = _active.Count - 1; s >= 0; s--)
        {
            var stream = _active[s];
            bool stillPlaying = stream.Read(scratch);
            float volume = stream.Volume;
            for (int i = 0; i < frames; i++)
            {
                mix[i] += scratch[i] * volume;
            }

            if (!stillPlaying)
            {
                _active.RemoveAt(s);
                stream.Dispose();
                StreamEnded?.Invoke(stream.Id);
            }
        }

        var settings = _getSettings();
        float master = _isMonitor && !settings.HearInMonitor ? 0f : (float)Math.Clamp(settings.MasterVolume, 0, 1);
        double duckThreshold = Math.Pow(10, SoundboardSettings.DuckThresholdDb / 20.0);
        double duckTarget = Math.Pow(10, SoundboardSettings.DuckAmountDb / 20.0);

        for (int i = 0; i < frames; i++)
        {
            int frame = i * _channels;

            float voicePeak = 0;
            for (int c = 0; c < _channels; c++)
            {
                voicePeak = Math.Max(voicePeak, Math.Abs(buffer[frame + c]));
            }

            _voiceEnvelope = voicePeak > _voiceEnvelope
                ? voicePeak
                : _voiceReleaseCoeff * _voiceEnvelope + (1 - _voiceReleaseCoeff) * voicePeak;

            double target = settings.DuckWhileTalking && _voiceEnvelope > duckThreshold ? duckTarget : 1.0;
            double coeff = target < _duckGain ? _duckAttackCoeff : _duckReleaseCoeff;
            _duckGain = coeff * _duckGain + (1 - coeff) * target;

            float music = mix[i] * master * (float)_duckGain;
            float peak = 0;
            for (int c = 0; c < _channels; c++)
            {
                buffer[frame + c] += music;
                peak = Math.Max(peak, Math.Abs(buffer[frame + c]));
            }

            float gain = _limiter.NextGain(peak);
            if (gain < 1f)
            {
                for (int c = 0; c < _channels; c++)
                {
                    buffer[frame + c] *= gain;
                }
            }
        }

        return samplesRead;
    }

    private void RemoveNow(Guid id)
    {
        for (int s = _active.Count - 1; s >= 0; s--)
        {
            if (_active[s].Id == id)
            {
                _active[s].Dispose();
                _active.RemoveAt(s);
            }
        }
    }
}
