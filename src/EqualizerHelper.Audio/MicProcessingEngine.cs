using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EqualizerHelper.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;

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

    public bool Bypass { get; set; }

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

    /// <summary>
    /// Input gain + noise gate (start of chain) and loudness compressor +
    /// safety limiter (end of chain). Mutate the properties live.
    /// </summary>
    public DynamicsSettings Dynamics { get; } = new();

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
        double sampleRate = captureFormat.SampleRate;
        SampleRate = sampleRate;

        _mainBuffer = new BufferedWaveProvider(captureFormat) { DiscardOnBufferOverflow = true };
        var mainChain = new EqChain(bands, channels, sampleRate);
        var mainInputDynamics = new InputDynamicsSampleProvider(_mainBuffer.ToSampleProvider(), sampleRate, () => Dynamics, () => Bypass);
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

        var mainOutputDynamics = new OutputDynamicsSampleProvider(mainCompressor, sampleRate, () => Dynamics, () => Bypass);
        var mainResampled = new MediaFoundationResampler(mainOutputDynamics.ToWaveProvider(), outputMixFormat)
        {
            ResamplerQuality = 60,
        };

        _mainOutput = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 20);
        _mainOutput.Init(mainResampled);

        if (monitorDevice != null)
        {
            _monitorBuffer = new BufferedWaveProvider(captureFormat) { DiscardOnBufferOverflow = true };
            var monitorChain = new EqChain(bands, channels, sampleRate);
            var monitorInputDynamics = new InputDynamicsSampleProvider(_monitorBuffer.ToSampleProvider(), sampleRate, () => Dynamics, () => Bypass);
            var monitorEq = new EqSampleProvider(monitorInputDynamics, monitorChain, () => Bypass);
            var monitorPitch = new PitchShiftSampleProvider(monitorEq, () => PitchRatio);
            var monitorDistortion = new DistortionSampleProvider(monitorPitch, () => MicCrunchAmount);
            var monitorCompressor = new CompressorSampleProvider(monitorDistortion, sampleRate, () => MicCrunchAmount);

            WaveFormat monitorMixFormat;
            using (var client = monitorDevice.CreateAudioClient())
            {
                monitorMixFormat = client.MixFormat;
            }

            var monitorOutputDynamics = new OutputDynamicsSampleProvider(monitorCompressor, sampleRate, () => Dynamics, () => Bypass);
            var monitorResampled = new MediaFoundationResampler(monitorOutputDynamics.ToWaveProvider(), monitorMixFormat)
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
        if (_monitorOutput != null)
        {
            _monitorOutput.Play();
            if (!monitorEnabled)
            {
                _monitorOutput.Pause();
            }
        }

        IsRunning = true;
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
    /// Live-toggles whether the monitor device is actually playing, without
    /// tearing down and rebuilding the pipeline. Returns false (no-op) if no
    /// monitor device was selected when the engine was started.
    /// </summary>
    public bool SetMonitorEnabled(bool enabled)
    {
        if (_monitorOutput == null)
        {
            return false;
        }

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

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _mainBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        _monitorBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
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

        _mainBuffer = null;
        _monitorBuffer = null;
        _inputMeter = null;
        _outputMeter = null;
        _analysisTap = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();
}
