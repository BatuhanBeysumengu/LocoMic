using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class NoiseGateTests
{
    private const double SampleRate = 48000;

    private static float RunSine(NoiseGate gate, double amplitudeDb, int sampleCount)
    {
        double amplitude = Math.Pow(10, amplitudeDb / 20.0);
        float lastGain = 1;
        for (int n = 0; n < sampleCount; n++)
        {
            float x = (float)(amplitude * Math.Sin(2 * Math.PI * 300 * n / SampleRate));
            lastGain = gate.NextGain(x);
        }

        return lastGain;
    }

    [Fact]
    public void NextGain_LoudSignal_StaysOpen()
    {
        var gate = new NoiseGate(SampleRate) { ThresholdDb = -50 };

        float gain = RunSine(gate, -20, 48000);

        Assert.InRange(gain, 0.99f, 1.0f);
    }

    [Fact]
    public void NextGain_QuietSignal_IsAttenuatedByRange()
    {
        var gate = new NoiseGate(SampleRate) { ThresholdDb = -40, RangeDb = -40 };

        float gain = RunSine(gate, -60, 48000);

        double gainDb = 20 * Math.Log10(gain);
        Assert.InRange(gainDb, -41, -38);
    }

    [Fact]
    public void NextGain_SignalStops_HoldsOpenBrieflyThenCloses()
    {
        var gate = new NoiseGate(SampleRate) { ThresholdDb = -40, RangeDb = -40, HoldMs = 150 };
        RunSine(gate, -20, 24000);

        // 50 ms of silence: still inside the 150 ms hold, so barely any reduction.
        float duringHold = RunSine(gate, -100, (int)(0.050 * SampleRate));
        Assert.True(duringHold > 0.9f, $"Expected gate to still be open during hold, gain was {duringHold}");

        // A further second of silence: fully closed.
        float afterHold = RunSine(gate, -100, 48000);
        Assert.True(afterHold < 0.02f, $"Expected gate to be closed, gain was {afterHold}");
    }

    [Fact]
    public void NextGain_ReopensQuicklyWhenSpeechReturns()
    {
        var gate = new NoiseGate(SampleRate) { ThresholdDb = -40, RangeDb = -40 };
        RunSine(gate, -100, 48000);

        // 20 ms of speech should be enough to fully open (2 ms attack).
        float gain = RunSine(gate, -20, (int)(0.020 * SampleRate));

        Assert.True(gain > 0.95f, $"Expected gate to reopen, gain was {gain}");
    }
}
