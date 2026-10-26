using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Wraps a source ISampleProvider with tanh-saturation distortion, driven by
/// a single 0..1 "crunch amount" read live via a closure (same pattern as
/// Bypass/PitchRatio). 0 is a true passthrough — no processing cost.
/// </summary>
public class DistortionSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Distortion _distortion = new();
    private readonly Func<double> _getCrunchAmount;

    public DistortionSampleProvider(ISampleProvider source, Func<double> getCrunchAmount)
    {
        _source = source;
        _getCrunchAmount = getCrunchAmount;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        double amount = _getCrunchAmount();
        if (amount <= 0)
        {
            return samplesRead;
        }

        _distortion.Drive = 1.0 + amount * 7.0;
        for (int i = 0; i < samplesRead; i++)
        {
            buffer[i] = _distortion.ProcessSample(buffer[i]);
        }

        return samplesRead;
    }
}
