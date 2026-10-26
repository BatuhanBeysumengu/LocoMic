using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EqualizerHelper.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EqualizerHelper.Audio;

/// <summary>
/// Captures a physical microphone, runs it through an EqChain built from the
/// given bands, and renders the result to an output device (normally
/// VB-Audio Virtual Cable's "CABLE Input", so other apps can pick up "CABLE
/// Output" as their microphone). Optionally also renders the same processed
/// signal to a monitor device (e.g. headphones) for live tuning.
/// </summary>
public class MicProcessingEngine : IDisposable
{
    private WasapiCapture? _capture;
    private WasapiOut? _mainOutput;
    private WasapiOut? _monitorOutput;
    private BufferedWaveProvider? _mainBuffer;
    private BufferedWaveProvider? _monitorBuffer;
    private LevelMeteringSampleProvider? _inputMeter;
    private LevelMeteringSampleProvider? _outputMeter;
    private AnalysisTapSampleProvider? _analysisTap;
    private NoiseRemovalSampleProvider? _mainNoiseRemoval;
    private NoiseRemovalSampleProvider? _monitorNoiseRemoval;

    public bool Bypass { get; set; }

    /// <summary>Silences both the main output and the monitor.</summary>
    public bool Muted { get; set; }

    /// <summary>
    /// Whether the user's own voice goes to the monitor (headphones). The
    /// monitor also plays when only the soundboard's "hear sounds" is on.
    /// </summary>
    public bool MonitorVoiceEnabled { get; set; } = true;

    private bool _monitorPlaying;

    /// <summary>
    /// 1.0 = no pitch change. &lt;1 lowers pitch, &gt;1 raises it. Applied
    /// last in the chain (after the analysis tap), so Auto-Tune's band
    /// analysis always sees the natural-pitch signal regardless of this.
    /// </summary>
    public double PitchRatio { get; set; } = 1.0;

    /// <summary>
    /// 0 = clean (passthrough, no cost). 1 = max "cheap/overdriven mic"
    /// character: distortion + heavy compression. Applied after pitch shift.
    /// </summary>
    public double MicCrunchAmount { get; set; }

    /// <summary>Character effect (robot, radio, echo...) applied after mic crunch.</summary>
    public VoiceEffectType VoiceEffect { get; set; }

    /// <summary>
    /// Input gain + noise gate (start of chain) and loudness compressor +
    /// safety limiter (end of chain). Mutate the properties live.
    /// </summary>
    public DynamicsSettings Dynamics { get; } = new();

    /// <summary>AI (RNNoise) background-noise removal, applied first in the chain.</summary>
    public bool NoiseRemovalEnabled { get; set; }

    /// <summary>0..1 blend between the original (0) and fully denoised (1) signal.</summary>
    public double NoiseRemovalStrength { get; set; } = 1.0;

    /// <summary>Songs and sound effects mixed into the processed mic.</summary>
    public Soundboard Soundboard { get; } = new();

    public bool IsRunning { get; private set; }
    public float InputLevel => _inputMeter?.CurrentLevel ?? 0f;
    public float OutputLevel => _outputMeter?.CurrentLevel ?? 0f;

    /// <summary>
    /// Sample rate of the internal processing chain (the capture device's
    /// native format), valid while <see cref="IsRunning"/>.
    /// </summary>
    public double SampleRate { get; private set; }

    /// <summary>
    /// Raised when the capture or an output device stops on its own (e.g. a
    /// Bluetooth headset switching profiles, or the device being unplugged),
    /// as opposed to the user clicking Stop. The exception is null if NAudio
    /// didn't report one.
    /// </summary>
    public event EventHandler<Exception?>? DeviceStoppedUnexpectedly;

    public void Start(MMDevice micDevice, MMDevice outputDevice, IReadOnlyList<EqBand> bands, MMDevice? monitorDevice = null, bool monitorEnabled = true)
    {
        Stop();

        _capture = new WasapiCapture(micDevice);
        WaveFormat captureFormat = _capture.WaveFormat;
        int channels = captureFormat.Channels;

        // The whole chain runs at 48 kHz because RNNoise requires it; mics
        // running at another rate are resampled once, up front.
        const int sampleRate = NoiseRemovalSampleProvider.RequiredSampleRate;
        SampleRate = sampleRate;

        _mainBuffer = new BufferedWaveProvider(captureFormat, TimeSpan.FromSeconds(1)) { DiscardOnBufferOverflow = true };
        var mainChain = new EqChain(bands, channels, sampleRate);
        _mainNoiseRemoval = new NoiseRemovalSampleProvider(To48k(_mainBuffer), () => NoiseRemovalEnabled && !Bypass, () => NoiseRemovalStrength);
        var mainInputDynamics = new InputDynamicsSampleProvider(_mainNoiseRemoval, sampleRate, () => Dynamics, () => Bypass);
        _inputMeter = new LevelMeteringSampleProvider(mainInputDynamics);
        var mainEq = new EqSampleProvider(_inputMeter, mainChain, () => Bypass);
        _outputMeter = new LevelMeteringSampleProvider(mainEq);
        _analysisTap = new AnalysisTapSampleProvider(_outputMeter);
        var mainPitch = new PitchShiftSampleProvider(_analysisTap, () => PitchRatio);
        var mainDistortion = new DistortionSampleProvider(mainPitch, () => MicCrunchAmount);
        var mainCompressor = new CompressorSampleProvider(mainDistortion, sampleRate, () => MicCrunchAmount);

        WaveFormat outputMixFormat;
        using (var client = outputDevice.CreateAudioClient())
        {
            outputMixFormat = client.MixFormat;
        }

        var mainEffect = new VoiceEffectSampleProvider(mainCompressor, () => VoiceEffect, () => Bypass);
        var mainOutputDynamics = new OutputDynamicsSampleProvider(mainEffect, sampleRate, () => Dynamics, () => Bypass);
        var mainSoundboard = new SoundboardSampleProvider(mainOutputDynamics, () => Soundboard.Settings, isMonitor: false);
        Soundboard.Attach(mainSoundboard, isMain: true);
        var mainResampled = new MediaFoundationResampler(new MuteSampleProvider(mainSoundboard, () => Muted).ToWaveProvider(), outputMixFormat)
        {
            ResamplerQuality = 60,
        };

        _mainOutput = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 20);
        _mainOutput.Init(mainResampled);

        if (monitorDevice != null)
        {
            _monitorBuffer = new BufferedWaveProvider(captureFormat, TimeSpan.FromSeconds(1)) { DiscardOnBufferOverflow = true };
            var monitorChain = new EqChain(bands, channels, sampleRate);
            _monitorNoiseRemoval = new NoiseRemovalSampleProvider(To48k(_monitorBuffer), () => NoiseRemovalEnabled && !Bypass, () => NoiseRemovalStrength);
            var monitorInputDynamics = new InputDynamicsSampleProvider(_monitorNoiseRemoval, sampleRate, () => Dynamics, () => Bypass);
            var monitorEq = new EqSampleProvider(monitorInputDynamics, monitorChain, () => Bypass);
            var monitorPitch = new PitchShiftSampleProvider(monitorEq, () => PitchRatio);
            var monitorDistortion = new DistortionSampleProvider(monitorPitch, () => MicCrunchAmount);
            var monitorCompressor = new CompressorSampleProvider(monitorDistortion, sampleRate, () => MicCrunchAmount);

            WaveFormat monitorMixFormat;
            using (var client = monitorDevice.CreateAudioClient())
            {
                monitorMixFormat = client.MixFormat;
            }

            var monitorEffect = new VoiceEffectSampleProvider(monitorCompressor, () => VoiceEffect, () => Bypass);
            var monitorOutputDynamics = new OutputDynamicsSampleProvider(monitorEffect, sampleRate, () => Dynamics, () => Bypass);
            var monitorSoundboard = new SoundboardSampleProvider(monitorOutputDynamics, () => Soundboard.Settings, isMonitor: true, () => MonitorVoiceEnabled);
            Soundboard.Attach(monitorSoundboard, isMain: false);
            var monitorResampled = new MediaFoundationResampler(new MuteSampleProvider(monitorSoundboard, () => Muted).ToWaveProvider(), monitorMixFormat)
            {
                ResamplerQuality = 60,
            };

            _monitorOutput = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 20);
            _monitorOutput.Init(monitorResampled);
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnDeviceStoppedUnexpectedly;
        _mainOutput.PlaybackStopped += OnDeviceStoppedUnexpectedly;
        if (_monitorOutput != null)
        {
            _monitorOutput.PlaybackStopped += OnDeviceStoppedUnexpectedly;
        }

        _capture.StartRecording();
        _mainOutput.Play();
        MonitorVoiceEnabled = monitorEnabled;
        _monitorPlaying = false;
        UpdateMonitorPlayback();

        IsRunning = true;
    }

    private static ISampleProvider To48k(BufferedWaveProvider buffer)
    {
        var samples = buffer.ToSampleProvider();
        return samples.WaveFormat.SampleRate == NoiseRemovalSampleProvider.RequiredSampleRate
            ? samples
            : new WdlResamplingSampleProvider(samples, NoiseRemovalSampleProvider.RequiredSampleRate);
    }

    /// <summary>
    /// Captures the next <paramref name="duration"/> of the processed (post-EQ)
    /// signal for offline analysis, e.g. by AutoEqTuner. Requires the engine
    /// to be running.
    /// </summary>
    public Task<float[]> CaptureSamplesAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _analysisTap == null)
        {
            throw new InvalidOperationException("The engine must be running to capture samples for analysis.");
        }

        int targetSamples = (int)(duration.TotalSeconds * SampleRate);
        return _analysisTap.ArmAndCaptureAsync(targetSamples, cancellationToken);
    }

    /// <summary>
    /// Plays the monitor device while the voice or the soundboard should be
    /// heard on it, and pauses it otherwise, without rebuilding the pipeline.
    /// Call after changing <see cref="MonitorVoiceEnabled"/> or the
    /// soundboard's HearInMonitor. Returns false if the monitor should play
    /// but no monitor device was selected when the engine was started.
    /// </summary>
    public bool UpdateMonitorPlayback()
    {
        bool enabled = MonitorVoiceEnabled || Soundboard.Settings.HearInMonitor;
        if (_monitorOutput == null)
        {
            return !enabled;
        }

        if (enabled == _monitorPlaying)
        {
            return true;
        }

        _monitorPlaying = enabled;
        if (enabled)
        {
            // While paused, capture keeps feeding the monitor buffer in the
            // background; without clearing it, resuming would play through
            // that backlog instead of live audio.
            _monitorBuffer?.ClearBuffer();
            _monitorOutput.Play();
        }
        else
        {
            _monitorOutput.Pause();
        }

        return true;
    }

    /// <summary>Audio currently waiting between the mic and the main output (for diagnostics).</summary>
    public TimeSpan MainBacklog => _mainBuffer?.BufferedDuration ?? TimeSpan.Zero;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // The mic and each output run on their own clocks, so trim any backlog
        // after every block; otherwise the voice slowly drifts seconds behind.
        if (_mainBuffer != null)
        {
            _mainBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            CaptureBacklog.Trim(_mainBuffer);
        }

        if (_monitorBuffer != null)
        {
            _monitorBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            CaptureBacklog.Trim(_monitorBuffer);
        }
    }

    private void OnDeviceStoppedUnexpectedly(object? sender, StoppedEventArgs e)
    {
        IsRunning = false;
        DeviceStoppedUnexpectedly?.Invoke(this, e.Exception);
    }

    public void Stop()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnDeviceStoppedUnexpectedly;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }

        if (_mainOutput != null)
        {
            _mainOutput.PlaybackStopped -= OnDeviceStoppedUnexpectedly;
            _mainOutput.Stop();
            _mainOutput.Dispose();
            _mainOutput = null;
        }

        if (_monitorOutput != null)
        {
            _monitorOutput.PlaybackStopped -= OnDeviceStoppedUnexpectedly;
            _monitorOutput.Stop();
            _monitorOutput.Dispose();
            _monitorOutput = null;
        }

        // Only after both outputs have stopped pulling audio.
        Soundboard.DetachAll();
        _mainNoiseRemoval?.Dispose();
        _monitorNoiseRemoval?.Dispose();
        _mainNoiseRemoval = null;
        _monitorNoiseRemoval = null;

        _mainBuffer = null;
        _monitorBuffer = null;
        _inputMeter = null;
        _outputMeter = null;
        _analysisTap = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();
}
