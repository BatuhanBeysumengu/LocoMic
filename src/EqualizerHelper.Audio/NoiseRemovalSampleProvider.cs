using System;
using System.Collections.Generic;
using NAudio.Wave;
using RNNoise.NET;

namespace EqualizerHelper.Audio;

/// <summary>
/// AI noise removal using RNNoise (Xiph's recurrent-neural-network speech
/// denoiser). RNNoise works on fixed 480-sample frames of 48 kHz mono audio,
/// so each channel gets its own denoiser state and FIFO, and the output runs
/// one frame (10 ms) behind the input while enabled.
///
/// Strength blends the denoised frame with the original (1 = fully denoised).
/// The source must be 48 kHz; the engine resamples the mic before this.
/// </summary>
public sealed class NoiseRemovalSampleProvider : ISampleProvider, IDisposable
{
    public const int RequiredSampleRate = 48000;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _frameSize = Native.FRAME_SIZE;
    private readonly IntPtr[] _states;
    private readonly Queue<float>[] _pendingInput;
    private readonly Queue<float>[] _pendingOutput;
    private readonly float[] _frameIn;
    private readonly float[] _frameOut;
    private readonly Func<bool> _isEnabled;
    private readonly Func<double> _getStrength;
    private bool _wasEnabled;
    private bool _disposed;

    public NoiseRemovalSampleProvider(ISampleProvider source, Func<bool> isEnabled, Func<double> getStrength)
    {
        if (source.WaveFormat.SampleRate != RequiredSampleRate)
        {
            throw new ArgumentException($"RNNoise needs {RequiredSampleRate} Hz audio, got {source.WaveFormat.SampleRate} Hz.", nameof(source));
        }

        _source = source;
        _channels = source.WaveFormat.Channels;
        _isEnabled = isEnabled;
        _getStrength = getStrength;
        _frameIn = new float[_frameSize];
        _frameOut = new float[_frameSize];

        _states = new IntPtr[_channels];
        _pendingInput = new Queue<float>[_channels];
        _pendingOutput = new Queue<float>[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _states[c] = Native.rnnoise_create(IntPtr.Zero);
            _pendingInput[c] = new Queue<float>(_frameSize * 2);
            _pendingOutput[c] = new Queue<float>(_frameSize * 2);
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Speech probability (0..1) RNNoise reported for the most recent frame.</summary>
    public float VoiceProbability { get; private set; }

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);
        bool enabled = _isEnabled() && !_disposed;

        if (!enabled)
        {
            _wasEnabled = false;
            return samplesRead;
        }

        if (!_wasEnabled)
        {
            ResetQueues();
            _wasEnabled = true;
        }

        float strength = (float)Math.Clamp(_getStrength(), 0, 1);
        int frames = samplesRead / _channels;

        for (int c = 0; c < _channels; c++)
        {
            var input = _pendingInput[c];
            var output = _pendingOutput[c];

            for (int i = 0; i < frames; i++)
            {
                input.Enqueue(buffer[i * _channels + c]);
            }

            while (input.Count >= _frameSize)
            {
                ProcessFrame(c, input, output, strength);
            }

            for (int i = 0; i < frames; i++)
            {
                buffer[i * _channels + c] = output.Dequeue();
            }
        }

        return samplesRead;
    }

    private unsafe void ProcessFrame(int channel, Queue<float> input, Queue<float> output, float strength)
    {
        for (int i = 0; i < _frameSize; i++)
        {
            // RNNoise expects 16-bit-scaled samples in float form.
            _frameIn[i] = input.Dequeue() * Native.SIGNAL_SCALE;
        }

        fixed (float* inPtr = _frameIn)
        fixed (float* outPtr = _frameOut)
        {
            VoiceProbability = Native.rnnoise_process_frame(_states[channel], outPtr, inPtr);
        }

        for (int i = 0; i < _frameSize; i++)
        {
            float dry = _frameIn[i];
            float wet = _frameOut[i];
            output.Enqueue((dry + (wet - dry) * strength) * Native.SIGNAL_SCALE_INV);
        }
    }

    private void ResetQueues()
    {
        for (int c = 0; c < _channels; c++)
        {
            _pendingInput[c].Clear();
            _pendingOutput[c].Clear();

            // One frame of silence up front keeps output exactly one frame
            // behind input, so every Read can be satisfied in full.
            for (int i = 0; i < _frameSize; i++)
            {
                _pendingOutput[c].Enqueue(0f);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var state in _states)
        {
            if (state != IntPtr.Zero)
            {
                Native.rnnoise_destroy(state);
            }
        }
    }
}
