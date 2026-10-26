using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Classic feedforward peak compressor: an exponential envelope follower
/// tracks signal level (fast attack, slower release), and whenever it
/// exceeds Threshold, gain is reduced by Ratio before MakeupGainDb is added
/// back. Stateful (the envelope persists across calls) — needs one instance
/// per channel.
/// </summary>
public class Compressor
{
    private readonly double _sampleRate;
    private double _envelope;

    public double ThresholdDb { get; set; }
    public double Ratio { get; set; } = 1;
    public double AttackMs { get; set; } = 5;
    public double ReleaseMs { get; set; } = 80;
    public double MakeupGainDb { get; set; }

    public Compressor(double sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public float ProcessSample(float input)
    {
        double inputLevel = Math.Abs(input);

        double attackCoeff = Math.Exp(-1.0 / (AttackMs / 1000.0 * _sampleRate));
        double releaseCoeff = Math.Exp(-1.0 / (ReleaseMs / 1000.0 * _sampleRate));
        double coeff = inputLevel > _envelope ? attackCoeff : releaseCoeff;
        _envelope = coeff * _envelope + (1 - coeff) * inputLevel;

        double envelopeDb = 20 * Math.Log10(Math.Max(_envelope, 1e-6));
        double gainReductionDb = 0;
        if (Ratio > 1 && envelopeDb > ThresholdDb)
        {
            gainReductionDb = (ThresholdDb - envelopeDb) * (1 - 1 / Ratio);
        }

        double gainLinear = Math.Pow(10, (gainReductionDb + MakeupGainDb) / 20.0);
        return (float)(input * gainLinear);
    }

    public void Reset() => _envelope = 0;
}
