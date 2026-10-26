using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class CompressorTests
{
    private const double SampleRate = 48000;

    private static double MeasureSteadyStateOutputDb(Compressor compressor, double inputAmplitude, int sampleCount = 48000)
    {
        double sumSquares = 0;
        int measured = 0;
        const int warmup = 24000; // let the envelope settle (attack/release time constants are a few ms/tens of ms)

        for (int n = 0; n < sampleCount; n++)
        {
            float x = (float)(inputAmplitude * Math.Sin(2 * Math.PI * 1000 * n / SampleRate));
            float y = compressor.ProcessSample(x);

            if (n >= warmup)
            {
                sumSquares += (double)y * y;
                measured++;
            }
        }

        double rms = Math.Sqrt(sumSquares / measured);
        return 20 * Math.Log10(rms);
    }

    [Fact]
    public void ProcessSample_BelowThreshold_IsUnaffected()
    {
        var compressor = new Compressor(SampleRate) { ThresholdDb = -10, Ratio = 4 };

        // -30dB amplitude is well below the -10dB threshold.
        double inputAmplitude = Math.Pow(10, -30.0 / 20.0);
        double outputDb = MeasureSteadyStateOutputDb(compressor, inputAmplitude);
        double inputDb = 20 * Math.Log10(inputAmplitude / Math.Sqrt(2)); // sine RMS = amplitude/sqrt(2)

        Assert.True(Math.Abs(outputDb - inputDb) < 0.5,
            $"Expected ~{inputDb:F1} dB (no reduction), measured {outputDb:F1} dB");
    }

    [Fact]
    public void ProcessSample_AboveThreshold_ReducesGainByRatio()
    {
        const double thresholdDb = -20;
        const double ratio = 4;
        var compressor = new Compressor(SampleRate) { ThresholdDb = thresholdDb, Ratio = ratio };

        // 0dBFS amplitude (1.0) sine, well above threshold.
        double outputDb = MeasureSteadyStateOutputDb(compressor, 1.0);

        double inputDb = 20 * Math.Log10(1.0 / Math.Sqrt(2));
        double expectedOutputDb = thresholdDb + (inputDb - thresholdDb) / ratio;

        // The envelope follower tracks instantaneous |sample|, not true RMS,
        // so for an oscillating sine it settles a bit below the textbook
        // RMS-based formula — allow for that known peak-vs-RMS gap rather
        // than requiring exact agreement.
        Assert.True(Math.Abs(outputDb - expectedOutputDb) < 2.0,
            $"Expected ~{expectedOutputDb:F1} dB, measured {outputDb:F1} dB");
    }

    [Fact]
    public void ProcessSample_MakeupGain_AddsBackLevel()
    {
        var withoutMakeup = new Compressor(SampleRate) { ThresholdDb = -20, Ratio = 4, MakeupGainDb = 0 };
        var withMakeup = new Compressor(SampleRate) { ThresholdDb = -20, Ratio = 4, MakeupGainDb = 6 };

        double dbWithoutMakeup = MeasureSteadyStateOutputDb(withoutMakeup, 1.0);
        double dbWithMakeup = MeasureSteadyStateOutputDb(withMakeup, 1.0);

        Assert.True(Math.Abs((dbWithMakeup - dbWithoutMakeup) - 6.0) < 0.5,
            $"Expected makeup gain to add ~6 dB, measured difference {dbWithMakeup - dbWithoutMakeup:F1} dB");
    }
}
