using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class BiquadFilterTests
{
    private const double SampleRate = 48000;

    /// <summary>
    /// Feeds a steady-state sine wave through the filter and measures the
    /// resulting gain in dB, after discarding the initial transient.
    /// </summary>
    private static double MeasureGainDb(BiquadFilter filter, double inputFrequencyHz, int sampleCount = 8192, int warmupSamples = 2048)
    {
        double sumSquaresIn = 0;
        double sumSquaresOut = 0;
        int measured = 0;

        for (int n = 0; n < sampleCount; n++)
        {
            float x = (float)Math.Sin(2 * Math.PI * inputFrequencyHz * n / SampleRate);
            float y = filter.ProcessSample(x);

            if (n >= warmupSamples)
            {
                sumSquaresIn += x * x;
                sumSquaresOut += y * y;
                measured++;
            }
        }

        double rmsIn = Math.Sqrt(sumSquaresIn / measured);
        double rmsOut = Math.Sqrt(sumSquaresOut / measured);
        return 20 * Math.Log10(rmsOut / rmsIn);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(-9)]
    public void Peaking_AtCenterFrequency_AppliesConfiguredGain(double gainDb)
    {
        var band = new EqBand(1000, gainDb, 1.0, FilterType.Peaking);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 1000);

        Assert.True(Math.Abs(measuredGainDb - gainDb) < 0.5,
            $"Expected ~{gainDb} dB at center frequency, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void Peaking_FarFromCenterFrequency_IsNearlyUnaffected()
    {
        var band = new EqBand(1000, 12, 1.0, FilterType.Peaking);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 100);

        Assert.True(Math.Abs(measuredGainDb) < 1.0,
            $"Expected ~0 dB far from center frequency, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void LowShelf_WellBelowCutoff_AppliesConfiguredGain()
    {
        var band = new EqBand(200, 6, 1.0, FilterType.LowShelf);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 20);

        Assert.True(Math.Abs(measuredGainDb - 6) < 0.5,
            $"Expected ~6 dB well below cutoff, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void LowShelf_WellAboveCutoff_IsNearlyUnaffected()
    {
        var band = new EqBand(200, 6, 1.0, FilterType.LowShelf);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 4000);

        Assert.True(Math.Abs(measuredGainDb) < 0.5,
            $"Expected ~0 dB well above cutoff, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void HighShelf_WellAboveCutoff_AppliesConfiguredGain()
    {
        var band = new EqBand(4000, 8, 1.0, FilterType.HighShelf);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 18000);

        Assert.True(Math.Abs(measuredGainDb - 8) < 0.5,
            $"Expected ~8 dB well above cutoff, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void HighShelf_WellBelowCutoff_IsNearlyUnaffected()
    {
        var band = new EqBand(4000, 8, 1.0, FilterType.HighShelf);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 200);

        Assert.True(Math.Abs(measuredGainDb) < 0.5,
            $"Expected ~0 dB well below cutoff, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void BandPass_AtCenterFrequency_HasNearUnityGain()
    {
        var band = new EqBand(1000, 0, 1.4, FilterType.BandPass);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 1000);

        Assert.True(Math.Abs(measuredGainDb) < 0.5,
            $"Expected ~0 dB at center frequency, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void BandPass_FarFromCenterFrequency_IsStronglyAttenuated()
    {
        var band = new EqBand(1000, 0, 1.4, FilterType.BandPass);
        var filter = new BiquadFilter(band, SampleRate);

        double measuredGainDb = MeasureGainDb(filter, 100);

        Assert.True(measuredGainDb < -15,
            $"Expected strong attenuation an octave+ away, got {measuredGainDb:F2} dB");
    }

    [Fact]
    public void EqChain_CascadesMultipleBands()
    {
        var bands = new[]
        {
            new EqBand(300, -6, 1.0, FilterType.Peaking),
            new EqBand(3000, 6, 1.0, FilterType.Peaking),
        };
        var chain = new EqChain(bands, channelCount: 1, sampleRate: SampleRate);

        const int sampleCount = 8192;
        const int warmup = 2048;
        var buffer300 = new float[sampleCount];
        var buffer3000 = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            buffer300[n] = (float)Math.Sin(2 * Math.PI * 300 * n / SampleRate);
            buffer3000[n] = (float)Math.Sin(2 * Math.PI * 3000 * n / SampleRate);
        }

        chain.ProcessBuffer(buffer300, sampleCount);
        chain.Reset();
        chain.ProcessBuffer(buffer3000, sampleCount);

        double rmsIn300 = Rms(SineBuffer(300, sampleCount), warmup);
        double rmsOut300 = Rms(buffer300, warmup);
        double gain300Db = 20 * Math.Log10(rmsOut300 / rmsIn300);

        double rmsIn3000 = Rms(SineBuffer(3000, sampleCount), warmup);
        double rmsOut3000 = Rms(buffer3000, warmup);
        double gain3000Db = 20 * Math.Log10(rmsOut3000 / rmsIn3000);

        Assert.True(Math.Abs(gain300Db - (-6)) < 0.5, $"Expected ~-6 dB at 300Hz, got {gain300Db:F2} dB");
        Assert.True(Math.Abs(gain3000Db - 6) < 0.5, $"Expected ~6 dB at 3000Hz, got {gain3000Db:F2} dB");
    }

    private static float[] SineBuffer(double frequencyHz, int sampleCount)
    {
        var buffer = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            buffer[n] = (float)Math.Sin(2 * Math.PI * frequencyHz * n / SampleRate);
        }

        return buffer;
    }

    private static double Rms(float[] buffer, int warmupSamples)
    {
        double sumSquares = 0;
        int measured = 0;
        for (int n = warmupSamples; n < buffer.Length; n++)
        {
            sumSquares += buffer[n] * buffer[n];
            measured++;
        }

        return Math.Sqrt(sumSquares / measured);
    }
}
