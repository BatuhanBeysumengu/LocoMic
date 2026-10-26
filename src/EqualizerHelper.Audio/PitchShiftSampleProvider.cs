using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Wraps a source ISampleProvider, running each channel through its own
/// PitchShifter. Passes audio through unchanged (no added latency) whenever
/// the ratio is 1.0, since the shifter's overlap-add otherwise adds a small
/// fixed delay even at an effectively-no-op ratio.
/// </summary>
public class PitchShiftSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly PitchShifter[] _shifters;
    private readonly Func<double> _getPitchRatio;
    private float[] _channelInputBuffer = Array.Empty<float>();
    private float[] _channelOutputBuffer = Array.Empty<float>();

    public PitchShiftSampleProvider(ISampleProvider source, Func<double> getPitchRatio)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _getPitchRatio = getPitchRatio;

        _shifters = new PitchShifter[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _shifters[c] = new PitchShifter();
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        double ratio = _getPitchRatio();
        if (Math.Abs(ratio - 1.0) < 0.001)
        {
            return samplesRead;
        }

        int frameCount = samplesRead / _channels;
        if (_channelInputBuffer.Length < frameCount)
        {
            _channelInputBuffer = new float[frameCount];
            _channelOutputBuffer = new float[frameCount];
        }

        for (int c = 0; c < _channels; c++)
        {
            for (int i = 0; i < frameCount; i++)
            {
                _channelInputBuffer[i] = buffer[i * _channels + c];
            }

            _shifters[c].PitchRatio = ratio;
            _shifters[c].Process(
                _channelInputBuffer.AsSpan(0, frameCount),
                _channelOutputBuffer.AsSpan(0, frameCount));

            for (int i = 0; i < frameCount; i++)
            {
                buffer[i * _channels + c] = _channelOutputBuffer[i];
            }
        }

        return samplesRead;
    }
}
