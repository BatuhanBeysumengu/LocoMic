using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class DistortionTests
{
    [Fact]
    public void ProcessSample_AtUnityDrive_IsPassthrough()
    {
        var distortion = new Distortion { Drive = 1.0 };

        Assert.Equal(0.5f, distortion.ProcessSample(0.5f), precision: 5);
        Assert.Equal(-0.3f, distortion.ProcessSample(-0.3f), precision: 5);
    }

    [Fact]
    public void ProcessSample_AtHighDrive_StaysWithinUnitRange()
    {
        var distortion = new Distortion { Drive = 8.0 };

        for (float x = -1f; x <= 1f; x += 0.05f)
        {
            float y = distortion.ProcessSample(x);
            Assert.True(Math.Abs(y) <= 1.001f, $"Expected |{y}| <= 1 for input {x}");
        }
    }

    [Fact]
    public void ProcessSample_AtHighDrive_ReducesCrestFactorOfASineWave()
    {
        const int sampleCount = 1000;
        const double sampleRate = 48000;
        const double frequency = 200;

        double CrestFactor(Distortion? distortion)
        {
            double peak = 0, sumSquares = 0;
            for (int n = 0; n < sampleCount; n++)
            {
                float x = (float)Math.Sin(2 * Math.PI * frequency * n / sampleRate);
                float y = distortion?.ProcessSample(x) ?? x;
                peak = Math.Max(peak, Math.Abs(y));
                sumSquares += y * y;
            }

            double rms = Math.Sqrt(sumSquares / sampleCount);
            return peak / rms;
        }

        double cleanCrest = CrestFactor(null);
        double drivenCrest = CrestFactor(new Distortion { Drive = 10.0 });

        Assert.True(drivenCrest < cleanCrest,
            $"Expected driven crest factor ({drivenCrest:F2}) to be lower than clean ({cleanCrest:F2}) — clipping should square off the waveform.");
    }
}
