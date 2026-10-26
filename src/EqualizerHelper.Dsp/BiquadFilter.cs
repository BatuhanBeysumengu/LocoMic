using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// A single second-order IIR filter section, with coefficients computed per the
/// RBJ "Audio EQ Cookbook" formulas. Holds its own history, so each channel of
/// audio needs its own instance.
/// </summary>
public class BiquadFilter
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    public BiquadFilter(EqBand band, double sampleRate)
    {
        UpdateCoefficients(band, sampleRate);
    }

    public void UpdateCoefficients(EqBand band, double sampleRate)
    {
        double w0 = 2 * Math.PI * band.FrequencyHz / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double a = Math.Pow(10, band.GainDb / 40.0);

        double b0, b1, b2, a0, a1, a2;

        switch (band.FilterType)
        {
            case FilterType.LowShelf:
            {
                // Q is used as the shelf slope "S" per the RBJ cookbook.
                double alpha = sinW0 / 2 * Math.Sqrt((a + 1 / a) * (1 / band.Q - 1) + 2);
                double sqrtA = Math.Sqrt(a);
                b0 = a * ((a + 1) - (a - 1) * cosW0 + 2 * sqrtA * alpha);
                b1 = 2 * a * ((a - 1) - (a + 1) * cosW0);
                b2 = a * ((a + 1) - (a - 1) * cosW0 - 2 * sqrtA * alpha);
                a0 = (a + 1) + (a - 1) * cosW0 + 2 * sqrtA * alpha;
                a1 = -2 * ((a - 1) + (a + 1) * cosW0);
                a2 = (a + 1) + (a - 1) * cosW0 - 2 * sqrtA * alpha;
                break;
            }
            case FilterType.HighShelf:
            {
                double alpha = sinW0 / 2 * Math.Sqrt((a + 1 / a) * (1 / band.Q - 1) + 2);
                double sqrtA = Math.Sqrt(a);
                b0 = a * ((a + 1) + (a - 1) * cosW0 + 2 * sqrtA * alpha);
                b1 = -2 * a * ((a - 1) + (a + 1) * cosW0);
                b2 = a * ((a + 1) + (a - 1) * cosW0 - 2 * sqrtA * alpha);
                a0 = (a + 1) - (a - 1) * cosW0 + 2 * sqrtA * alpha;
                a1 = 2 * ((a - 1) - (a + 1) * cosW0);
                a2 = (a + 1) - (a - 1) * cosW0 - 2 * sqrtA * alpha;
                break;
            }
            case FilterType.BandPass:
            {
                // Constant 0dB peak gain bandpass (RBJ cookbook); GainDb is ignored.
                double alpha = sinW0 / (2 * band.Q);
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0 = 1 + alpha;
                a1 = -2 * cosW0;
                a2 = 1 - alpha;
                break;
            }
            default: // Peaking
            {
                double alpha = sinW0 / (2 * band.Q);
                b0 = 1 + alpha * a;
                b1 = -2 * cosW0;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2 * cosW0;
                a2 = 1 - alpha / a;
                break;
            }
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public float ProcessSample(float input)
    {
        double x0 = input;
        double y0 = _b0 * x0 + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        _x2 = _x1;
        _x1 = x0;
        _y2 = _y1;
        _y1 = y0;

        return (float)y0;
    }

    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0;
    }
}
