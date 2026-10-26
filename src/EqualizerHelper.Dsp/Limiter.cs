using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Brickwall peak limiter. The envelope rises instantly to any peak, so the
/// gain it returns always keeps |sample * gain| at or under the ceiling;
/// it then recovers smoothly over ReleaseMs. Like <see cref="NoiseGate"/>,
/// it takes a caller-supplied detection level and returns a linear gain so
/// all channels can be limited together.
/// </summary>
public class Limiter
{
    private readonly double _sampleRate;
    private double _envelope;

    public double CeilingDb { get; set; } = -1;
    public double ReleaseMs { get; set; } = 60;

    public Limiter(double sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public float NextGain(float detectionLevel)
    {
        double level = Math.Abs(detectionLevel);
        double ceiling = Math.Pow(10, CeilingDb / 20.0);

        if (level > _envelope)
        {
            _envelope = level;
        }
        else
        {
            double releaseCoeff = Math.Exp(-1.0 / (ReleaseMs / 1000.0 * _sampleRate));
            _envelope = releaseCoeff * _envelope + (1 - releaseCoeff) * level;
        }

        return _envelope > ceiling ? (float)(ceiling / _envelope) : 1f;
    }

    public void Reset() => _envelope = 0;
}
