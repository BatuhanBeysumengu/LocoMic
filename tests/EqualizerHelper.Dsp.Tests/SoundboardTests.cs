using System;
using System.IO;
using EqualizerHelper.Audio;
using NAudio.Wave;

namespace EqualizerHelper.Dsp.Tests;

public class SoundboardTests : IDisposable
{
    private const int SampleRate = 48000;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eqh-soundboard-" + Guid.NewGuid());

    public SoundboardTests() => Directory.CreateDirectory(_dir);

    private readonly System.Collections.Generic.List<Soundboard> _boards = new();

    public void Dispose()
    {
        // Close any sound files still open before deleting them.
        foreach (var board in _boards)
        {
            board.DetachAll();
        }

        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Endless voice input at a constant level (0 = silence).</summary>
    private sealed class ConstantSource : ISampleProvider
    {
        private readonly float _level;
        public ConstantSource(float level, int channels) { _level = level; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels); }
        public WaveFormat WaveFormat { get; }
        public int Read(Span<float> buffer) { buffer.Fill(_level); return buffer.Length; }
    }

    private string WriteSine(double seconds, int fileRate = 44100, int channels = 1, double amplitude = 0.5)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(fileRate, channels));
        int frames = (int)(seconds * fileRate);
        for (int n = 0; n < frames; n++)
        {
            float s = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * n / fileRate));
            for (int c = 0; c < channels; c++)
            {
                writer.WriteSample(s);
            }
        }

        return path;
    }

    private (Soundboard Board, SoundboardSampleProvider Mixer) CreateBoard(ISampleProvider voice, bool isMonitor = false)
    {
        var board = new Soundboard();
        _boards.Add(board);
        board.Settings.MasterVolume = 1.0;
        board.Settings.DuckWhileTalking = false;
        var mixer = new SoundboardSampleProvider(voice, () => board.Settings, isMonitor);
        board.Attach(mixer, isMain: !isMonitor);
        return (board, mixer);
    }

    private static float[] Read(ISampleProvider provider, double seconds)
    {
        var buffer = new float[(int)(seconds * SampleRate) * provider.WaveFormat.Channels];
        for (int pos = 0; pos < buffer.Length; pos += 960)
        {
            provider.Read(buffer.AsSpan(pos, Math.Min(960, buffer.Length - pos)));
        }

        return buffer;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    [Fact]
    public void Play_MixesResampledSoundIntoEveryChannel()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 2));
        Assert.True(board.Play(Guid.NewGuid(), WriteSine(1.0, fileRate: 44100), () => 1.0, () => false));

        var output = Read(mixer, 0.5);

        // 0.5-amplitude sine has RMS ~0.354, in both left and right.
        double left = Rms(output);
        Assert.InRange(left, 0.30, 0.40);
        for (int i = 0; i < output.Length; i += 2)
        {
            Assert.Equal(output[i], output[i + 1]);
        }
    }

    [Fact]
    public void NonLoopingSound_EndsAndRaisesEvent()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 1));
        Guid id = Guid.NewGuid();
        Guid? ended = null;
        board.SoundEnded += (_, endedId) => ended = endedId;
        board.Play(id, WriteSine(0.2), () => 1.0, () => false);

        var output = Read(mixer, 0.5);

        Assert.Equal(id, ended);
        Assert.Equal(0, Rms(output.AsSpan((int)(0.3 * SampleRate))), 6);
    }

    [Fact]
    public void LoopingSound_KeepsPlayingPastItsEnd()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 1));
        bool ended = false;
        board.SoundEnded += (_, _) => ended = true;
        board.Play(Guid.NewGuid(), WriteSine(0.2), () => 1.0, () => true);

        var output = Read(mixer, 1.0);

        Assert.False(ended);
        Assert.True(Rms(output.AsSpan((int)(0.8 * SampleRate))) > 0.3);
    }

    [Fact]
    public void Stop_SilencesTheSound()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 1));
        Guid id = Guid.NewGuid();
        board.Play(id, WriteSine(2.0), () => 1.0, () => false);
        Read(mixer, 0.1);

        board.Stop(id);
        var output = Read(mixer, 0.2);

        Assert.Equal(0, Rms(output), 6);
    }

    [Fact]
    public void Ducking_LowersSoundsWhileTalking()
    {
        // A steady quiet "voice" (-30 dBFS DC offset is enough to trigger ducking).
        var voiceLevel = 0.03f;
        var (board, mixer) = CreateBoard(new ConstantSource(voiceLevel, channels: 1));
        board.Settings.DuckWhileTalking = true;
        board.Play(Guid.NewGuid(), WriteSine(3.0), () => 1.0, () => false);

        var output = Read(mixer, 2.0);
        var music = Array.ConvertAll(output, s => s - voiceLevel);

        double duckedDb = 20 * Math.Log10(Rms(music.AsSpan(SampleRate)) / 0.3536);
        Assert.InRange(duckedDb, SoundboardSettings.DuckAmountDb - 1.5, SoundboardSettings.DuckAmountDb + 1.5);
    }

    [Fact]
    public void LoudVoicePlusLoudSound_NeverClips()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0.9f, channels: 2));
        board.Play(Guid.NewGuid(), WriteSine(1.0, amplitude: 0.95), () => 1.0, () => false);

        var output = Read(mixer, 0.5);
        double ceiling = Math.Pow(10, -1 / 20.0);

        foreach (float s in output)
        {
            Assert.True(Math.Abs(s) <= ceiling + 1e-6, $"Sample {s} exceeded the -1 dBFS ceiling");
        }
    }

    [Fact]
    public void Monitor_IsSilentWhenHearSoundsIsOff()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 1), isMonitor: true);
        board.Settings.HearInMonitor = false;
        board.Play(Guid.NewGuid(), WriteSine(1.0), () => 1.0, () => false);

        Assert.Equal(0, Rms(Read(mixer, 0.3)), 6);
    }

    [Fact]
    public void Play_WithBadFile_ThrowsAndPlaysNothing()
    {
        var (board, mixer) = CreateBoard(new ConstantSource(0, channels: 1));
        string bad = Path.Combine(_dir, "not-audio.mp3");
        File.WriteAllText(bad, "definitely not audio");

        Assert.ThrowsAny<Exception>(() => board.Play(Guid.NewGuid(), bad, () => 1.0, () => false));
        Assert.Equal(0, Rms(Read(mixer, 0.1)), 6);
    }

    [Fact]
    public void Play_WhenEngineNotRunning_ReturnsFalse()
    {
        var board = new Soundboard();
        Assert.False(board.Play(Guid.NewGuid(), WriteSine(0.1), () => 1.0, () => false));
    }
}
