using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class PitchShifterTests
{
    private const double SampleRate = 48000;

    [Theory]
    [InlineData(150, 0.75)]
    [InlineData(150, 1.0)]
    [InlineData(150, 1.5)]
    [InlineData(150, 0.5945)] // -9 semitones, close to the "Deep Bass (Fun)" shift
    public void Process_ShiftsSineFrequencyByPitchRatio(double inputFrequency, double pitchRatio)
    {
        var shifter = new PitchShifter { PitchRatio = pitchRatio };

        const int sampleCount = 96000; // 2 seconds
        var input = new float[sampleCount];
        var output = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            input[n] = (float)Math.Sin(2 * Math.PI * inputFrequency * n / SampleRate);
        }

        shifter.Process(input, output);

        const int warmup = 8000; // well past the grain/hop startup latency
        int crossings = 0;
        for (int n = warmup + 1; n < sampleCount; n++)
        {
            bool wasNegative = output[n - 1] < 0;
            bool isNegative = output[n] < 0;
            if (wasNegative != isNegative)
            {
                crossings++;
            }
        }

        double measuredSeconds = (sampleCount - warmup - 1) / SampleRate;
        double measuredFrequency = crossings / 2.0 / measuredSeconds;
        double expectedFrequency = inputFrequency * pitchRatio;

        Assert.True(Math.Abs(measuredFrequency - expectedFrequency) < expectedFrequency * 0.1,
            $"Expected ~{expectedFrequency:F1} Hz, measured {measuredFrequency:F1} Hz");
    }

    [Fact]
    public void Process_LatencyStaysBoundedOverSustainedPitchDown()
    {
        // A pure frequency-domain check wouldn't catch growing latency (the
        // pitch stays correct throughout even if increasingly delayed) — this
        // test specifically targets that failure mode by tracking when two
        // short bursts, one early and one several seconds later, show up in
        // the output.
        const double pitchRatio = 0.7435; // ~ -5 semitones, matches "Deep Bass (Fun)"
        var shifter = new PitchShifter { PitchRatio = pitchRatio };

        const int sampleRate = (int)SampleRate;
        const int totalSamples = sampleRate * 10;
        const int burstLength = 2000;
        const int earlyBurstStart = 2000;
        const int lateBurstStart = sampleRate * 8;

        var input = new float[totalSamples];
        InjectTone(input, earlyBurstStart, burstLength, 400);
        InjectTone(input, lateBurstStart, burstLength, 400);

        var output = new float[totalSamples];
        shifter.Process(input, output);

        int earlyBurstOutputStart = FindFirstAboveThreshold(output, 0, earlyBurstStart + 20000, 0.1f);
        int lateBurstOutputStart = FindFirstAboveThreshold(output, lateBurstStart - 5000, lateBurstStart + 20000, 0.1f);

        Assert.True(earlyBurstOutputStart >= 0, "Early burst was not found in output.");
        Assert.True(lateBurstOutputStart >= 0, "Late burst was not found in output.");

        int earlyDelay = earlyBurstOutputStart - earlyBurstStart;
        int lateDelay = lateBurstOutputStart - lateBurstStart;

        const int toleranceSamples = 6000; // ~125ms — covers grain-boundary jitter, not unbounded growth
        Assert.True(Math.Abs(lateDelay - earlyDelay) < toleranceSamples,
            $"Latency grew over time: early delay={earlyDelay} samples, late delay={lateDelay} samples");
    }

    private static void InjectTone(float[] buffer, int start, int length, double frequency)
    {
        for (int i = 0; i < length && start + i < buffer.Length; i++)
        {
            buffer[start + i] = (float)Math.Sin(2 * Math.PI * frequency * i / SampleRate);
        }
    }

    private static int FindFirstAboveThreshold(float[] buffer, int searchStart, int searchEnd, float threshold)
    {
        searchStart = Math.Max(0, searchStart);
        searchEnd = Math.Min(buffer.Length, searchEnd);
        for (int n = searchStart; n < searchEnd; n++)
        {
            if (Math.Abs(buffer[n]) > threshold)
            {
                return n;
            }
        }

        return -1;
    }
}
