using System;

namespace EqualizerHelper.Dsp;

public enum VoiceEffectType
{
    None,
    Robot,
    Radio,
    Telephone,
    Echo,
    Hall,
}

/// <summary>
/// Character effects for the fun presets. Stateful (filters, delay lines),
/// so each audio channel needs its own instance; changing
/// <see cref="Type"/> clears all state so tails don't leak between effects.
/// </summary>
public class VoiceEffect
{
    private readonly double _sampleRate;
    private VoiceEffectType _type;

    // Robot: ring modulator + short metallic comb.
    private const double RobotCarrierHz = 60;
    private double _carrierPhase;
    private readonly DelayLine _robotComb;

    // Radio / telephone: band-limiting filters (two stages each for a steeper slope).
    private readonly SimpleFilter[] _highPass = new SimpleFilter[2];
    private readonly SimpleFilter[] _lowPass = new SimpleFilter[2];

    // Echo: a single long feedback delay.
    private readonly DelayLine _echo;

    // Hall: small Schroeder reverb (parallel combs into series all-passes).
    private static readonly double[] CombMs = { 29.7, 37.1, 41.1, 43.7 };
    private static readonly double[] AllPassMs = { 5.0, 1.7 };
    private readonly DelayLine[] _combs;
    private readonly float[] _combDamp;
    private readonly DelayLine[] _allPasses;

    public VoiceEffect(double sampleRate)
    {
        _sampleRate = sampleRate;
        _robotComb = new DelayLine(Samples(6));
        _echo = new DelayLine(Samples(300));
        _combs = Array.ConvertAll(CombMs, ms => new DelayLine(Samples(ms)));
        _combDamp = new float[_combs.Length];
        _allPasses = Array.ConvertAll(AllPassMs, ms => new DelayLine(Samples(ms)));
        for (int i = 0; i < 2; i++)
        {
            _highPass[i] = new SimpleFilter();
            _lowPass[i] = new SimpleFilter();
        }
    }

    public VoiceEffectType Type
    {
        get => _type;
        set
        {
            if (_type == value)
            {
                return;
            }

            _type = value;
            Reset();

            (double hp, double lp) = value switch
            {
                VoiceEffectType.Radio => (500.0, 2800.0),
                VoiceEffectType.Telephone => (300.0, 3400.0),
                _ => (0.0, 0.0),
            };
            if (hp > 0)
            {
                foreach (var f in _highPass) f.SetHighPass(hp, _sampleRate);
                foreach (var f in _lowPass) f.SetLowPass(lp, _sampleRate);
            }
        }
    }

    public float ProcessSample(float x) => _type switch
    {
        VoiceEffectType.Robot => Robot(x),
        VoiceEffectType.Radio => BandLimit(x, drive: 3.0f, outputGain: 1.4f),
        VoiceEffectType.Telephone => BandLimit(x, drive: 1.5f, outputGain: 1.5f),
        VoiceEffectType.Echo => Echo(x),
        VoiceEffectType.Hall => Hall(x),
        _ => x,
    };

    public void Reset()
    {
        _carrierPhase = 0;
        _robotComb.Clear();
        _echo.Clear();
        foreach (var c in _combs) c.Clear();
        foreach (var a in _allPasses) a.Clear();
        Array.Clear(_combDamp);
        foreach (var f in _highPass) f.Reset();
        foreach (var f in _lowPass) f.Reset();
    }

    private int Samples(double ms) => Math.Max(1, (int)(ms / 1000.0 * _sampleRate));

    private float Robot(float x)
    {
        _carrierPhase += 2 * Math.PI * RobotCarrierHz / _sampleRate;
        if (_carrierPhase > 2 * Math.PI)
        {
            _carrierPhase -= 2 * Math.PI;
        }

        // Ring modulation halves the average level; the comb adds a metallic ring.
        float ring = x * (float)Math.Sin(_carrierPhase) * 1.6f;
        float delayed = _robotComb.Read();
        _robotComb.Write(ring + delayed * 0.5f);
        return ring + delayed * 0.5f;
    }

    private float BandLimit(float x, float drive, float outputGain)
    {
        float y = x;
        foreach (var f in _highPass) y = f.Process(y);
        foreach (var f in _lowPass) y = f.Process(y);

        // Gentle saturation for that cheap-speaker sound.
        return (float)Math.Tanh(y * drive) / drive * outputGain;
    }

    private float Echo(float x)
    {
        const float feedback = 0.4f;
        const float wet = 0.45f;
        float delayed = _echo.Read();
        _echo.Write(x + delayed * feedback);
        return x + delayed * wet;
    }

    private float Hall(float x)
    {
        const float feedback = 0.78f;
        const float damping = 0.3f;
        const float wet = 0.35f;
        const float allPassGain = 0.7f;

        float sum = 0;
        for (int i = 0; i < _combs.Length; i++)
        {
            float delayed = _combs[i].Read();
            // One-pole low-pass in the loop makes the tail darker, like a real room.
            _combDamp[i] = delayed * (1 - damping) + _combDamp[i] * damping;
            _combs[i].Write(x + _combDamp[i] * feedback);
            sum += delayed;
        }

        float y = sum / _combs.Length;
        foreach (var allPass in _allPasses)
        {
            float delayed = allPass.Read();
            float input = y + delayed * allPassGain;
            allPass.Write(input);
            y = delayed - input * allPassGain;
        }

        return x + y * wet;
    }

    /// <summary>Fixed-length circular delay: Read returns the sample written Length samples ago.</summary>
    private sealed class DelayLine
    {
        private readonly float[] _buffer;
        private int _position;

        public DelayLine(int length) => _buffer = new float[length];

        public float Read() => _buffer[_position];

        public void Write(float value)
        {
            _buffer[_position] = value;
            _position = (_position + 1) % _buffer.Length;
        }

        public void Clear()
        {
            Array.Clear(_buffer);
            _position = 0;
        }
    }

    /// <summary>RBJ-cookbook high/low-pass biquad (Q = 0.707).</summary>
    private sealed class SimpleFilter
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        public void SetHighPass(double frequency, double sampleRate) => Set(frequency, sampleRate, highPass: true);

        public void SetLowPass(double frequency, double sampleRate) => Set(frequency, sampleRate, highPass: false);

        private void Set(double frequency, double sampleRate, bool highPass)
        {
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cos = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * 0.7071);
            double a0 = 1 + alpha;

            double b0 = highPass ? (1 + cos) / 2 : (1 - cos) / 2;
            double b1 = highPass ? -(1 + cos) : 1 - cos;
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b0 / a0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }

        public float Process(float x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return (float)y;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;
    }
}
