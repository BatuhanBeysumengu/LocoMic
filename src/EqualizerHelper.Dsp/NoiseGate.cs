using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Downward expander-style noise gate: while the detected level sits below
/// ThresholdDb (for longer than HoldMs) the signal is turned down by RangeDb
/// rather than fully muted, which sounds more natural between words. Opens
/// fast, closes slowly, with a few dB of hysteresis so it doesn't chatter
/// around the threshold.
///
/// Works on a detection level supplied by the caller (e.g. the max of all
/// channels in a frame) and returns the linear gain to apply, so one
/// instance can gate every channel identically.
/// </summary>
public class NoiseGate
{
    private const double HysteresisDb = 4;

    private readonly double _sampleRate;
    private double _envelope;
    private double _gain = 1;
    private int _holdSamplesLeft;
    private bool _open;

    public double ThresholdDb { get; set; } = -60;
    public double RangeDb { get; set; } = -40;
    public double AttackMs { get; set; } = 2;
    public double HoldMs { get; set; } = 150;
    public double ReleaseMs { get; set; } = 120;

    public NoiseGate(double sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public float NextGain(float detectionLevel)
    {
        // Peak-ish envelope: near-instant rise, ~20 ms fall.
        double level = Math.Abs(detectionLevel);
        double envCoeff = level > _envelope ? 0 : Math.Exp(-1.0 / (0.020 * _sampleRate));
        _envelope = envCoeff * _envelope + (1 - envCoeff) * level;

        double envelopeDb = 20 * Math.Log10(Math.Max(_envelope, 1e-9));
        double closeThresholdDb = ThresholdDb - HysteresisDb;

        if (envelopeDb >= ThresholdDb)
        {
            _open = true;
            _holdSamplesLeft = (int)(HoldMs / 1000.0 * _sampleRate);
        }
        else if (_open && envelopeDb < closeThresholdDb)
        {
            if (_holdSamplesLeft > 0)
            {
                _holdSamplesLeft--;
            }
            else
            {
                _open = false;
            }
        }

        double target = _open ? 1.0 : Math.Pow(10, RangeDb / 20.0);
        double timeMs = target > _gain ? AttackMs : ReleaseMs;
        double coeff = Math.Exp(-1.0 / (timeMs / 1000.0 * _sampleRate));
        _gain = coeff * _gain + (1 - coeff) * target;

        return (float)_gain;
    }

    public void Reset()
    {
        _envelope = 0;
        _gain = 1;
        _holdSamplesLeft = 0;
        _open = false;
    }
}
