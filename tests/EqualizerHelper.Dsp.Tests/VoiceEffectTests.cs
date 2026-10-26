using System;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.Dsp.Tests;

public class VoiceEffectTests
{
    private const double SampleRate = 48000;

    private static double SineGainDb(VoiceEffectType type, double frequency)
    {
        var effect = new VoiceEffect(SampleRate) { Type = type };
        double inSum = 0, outSum = 0;
        int total = (int)SampleRate, warmup = total / 2;
        for (int n = 0; n < total; n++)
        {
            // Quiet input so the radio/telephone saturation stays in its linear range.
            float x = (float)(0.05 * Math.Sin(2 * Math.PI * frequency * n / SampleRate));
            float y = effect.ProcessSample(x);
            if (n >= warmup)
            {
                inSum += x * x;
                outSum += y * y;
            }
        }

        return 10 * Math.Log10(outSum / inSum);
    }

    [Fact]
    public void None_IsExactPassthrough()
    {
        var effect = new VoiceEffect(SampleRate);
        for (int n = 0; n < 1000; n++)
        {
            float x = (float)Math.Sin(n * 0.1);
            Assert.Equal(x, effect.ProcessSample(x));
        }
    }

    [Theory]
    [InlineData(VoiceEffectType.Telephone)]
    [InlineData(VoiceEffectType.Radio)]
    public void BandLimitedEffects_CutLowsAndHighsButKeepTheVoiceRange(VoiceEffectType type)
    {
        double low = SineGainDb(type, 80);
        double mid = SineGainDb(type, 1200);
        double high = SineGainDb(type, 9000);

        Assert.True(mid - low > 20, $"{type}: 80 Hz should be at least 20 dB below 1.2 kHz (was {mid - low:0.0} dB)");
        Assert.True(mid - high > 20, $"{type}: 9 kHz should be at least 20 dB below 1.2 kHz (was {mid - high:0.0} dB)");
        Assert.InRange(mid, -6, 6);
    }

    [Fact]
    public void Echo_RepeatsTheSoundAfter300Milliseconds()
    {
        var effect = new VoiceEffect(SampleRate) { Type = VoiceEffectType.Echo };
        int delay = (int)(0.300 * SampleRate);

        var output = new float[delay * 2 + 10];
        output[0] = effect.ProcessSample(1f);
        for (int n = 1; n < output.Length; n++)
        {
            output[n] = effect.ProcessSample(0f);
        }

        Assert.Equal(1f, output[0]);
        Assert.Equal(0.45f, output[delay], 3);
        Assert.Equal(0.45f * 0.4f, output[delay * 2], 3);
        Assert.Equal(0f, output[delay / 2]);
    }

    [Fact]
    public void Hall_LeavesADecayingTail()
    {
        var effect = new VoiceEffect(SampleRate) { Type = VoiceEffectType.Hall };
        effect.ProcessSample(1f);

        double early = 0, late = 0;
        for (int n = 1; n < (int)(2 * SampleRate); n++)
        {
            float y = effect.ProcessSample(0f);
            if (n < SampleRate / 4) early += y * y;
            else if (n > 1.5 * SampleRate) late += y * y;
        }

        Assert.True(early > 1e-4, "the hall should ring after a sound");
        Assert.True(late < early * 1e-3, "the tail should die away");
    }

    [Theory]
    [InlineData(VoiceEffectType.Robot)]
    [InlineData(VoiceEffectType.Hall)]
    [InlineData(VoiceEffectType.Echo)]
    [InlineData(VoiceEffectType.Radio)]
    public void LoudInput_StaysStableAndBounded(VoiceEffectType type)
    {
        var effect = new VoiceEffect(SampleRate) { Type = type };
        for (int n = 0; n < 5 * SampleRate; n++)
        {
            float x = (float)(0.9 * Math.Sin(2 * Math.PI * 220 * n / SampleRate));
            float y = effect.ProcessSample(x);
            Assert.True(float.IsFinite(y) && Math.Abs(y) < 4f, $"{type} produced {y} at sample {n}");
        }
    }

    [Fact]
    public void ChangingEffect_ClearsOldTails()
    {
        var effect = new VoiceEffect(SampleRate) { Type = VoiceEffectType.Echo };
        effect.ProcessSample(1f);

        effect.Type = VoiceEffectType.Hall;
        effect.Type = VoiceEffectType.Echo;

        for (int n = 0; n < SampleRate; n++)
        {
            Assert.Equal(0f, effect.ProcessSample(0f));
        }
    }
}
