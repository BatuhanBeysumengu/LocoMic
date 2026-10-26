using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class VoiceBandAnalyzerTests
{
    private const double SampleRate = 48000;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void MeasureBandEnergiesDb_SineAtBandFrequency_IsLoudestInThatBand(int targetBandIndex)
    {
        var bands = new (double FrequencyHz, double Q)[]
        {
            (100, 0.9),
            (250, 1.0),
            (1000, 1.0),
            (3000, 1.0),
            (6000, 1.0),
            (10000, 0.9),
        };

        double toneFrequency = bands[targetBandIndex].FrequencyHz;
        const int sampleCount = 16384;
        var samples = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            samples[n] = (float)Math.Sin(2 * Math.PI * toneFrequency * n / SampleRate);
        }

        double[] energiesDb = VoiceBandAnalyzer.MeasureBandEnergiesDb(samples, SampleRate, bands);

        for (int i = 0; i < energiesDb.Length; i++)
        {
            if (i == targetBandIndex)
            {
                continue;
            }

            Assert.True(energiesDb[targetBandIndex] > energiesDb[i],
                $"Expected band {targetBandIndex} ({toneFrequency}Hz) to measure louder than band {i}, " +
                $"got {energiesDb[targetBandIndex]:F2}dB vs {energiesDb[i]:F2}dB");
        }
    }
}
