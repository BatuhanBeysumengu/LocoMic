using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class LimiterTests
{
    private const double SampleRate = 48000;

    [Fact]
    public void NextGain_NeverLetsOutputExceedCeiling()
    {
        var limiter = new Limiter(SampleRate) { CeilingDb = -1 };
        double ceiling = Math.Pow(10, -1 / 20.0);
        var random = new Random(1234);

        for (int n = 0; n < 96000; n++)
        {
            // Mix of a loud sine and random spikes well above 0 dBFS.
            float x = (float)(1.5 * Math.Sin(2 * Math.PI * 200 * n / SampleRate));
            if (random.NextDouble() < 0.001)
            {
                x = (float)(random.NextDouble() * 4 - 2);
            }

            float y = x * limiter.NextGain(x);
            Assert.True(Math.Abs(y) <= ceiling + 1e-6, $"Sample {n}: |{y}| exceeded ceiling {ceiling}");
        }
    }

    [Fact]
    public void NextGain_BelowCeiling_IsUnity()
    {
        var limiter = new Limiter(SampleRate) { CeilingDb = -1 };

        for (int n = 0; n < 48000; n++)
        {
            float x = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * n / SampleRate));
            Assert.Equal(1f, limiter.NextGain(x));
        }
    }

    [Fact]
    public void NextGain_RecoversAfterPeak()
    {
        var limiter = new Limiter(SampleRate) { CeilingDb = -1, ReleaseMs = 60 };
        limiter.NextGain(2f);

        float gain = 0;
        for (int n = 0; n < 48000; n++)
        {
            gain = limiter.NextGain(0.1f);
        }

        Assert.Equal(1f, gain);
    }
}
