using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Real-time single-channel pitch shifter using a time-domain granular
/// overlap-add technique (no FFT): every <c>hopSize</c> real-time samples, a
/// Hann-windowed grain is read from input history with an internal step of
/// <see cref="PitchRatio"/> (reading slower stretches the grain content =
/// lower pitch, reading faster compresses it = higher pitch), then
/// overlap-added into the output at the same <c>hopSize</c> cadence so
/// duration is preserved while pitch changes.
/// </summary>
public class PitchShifter
{
    private readonly int _grainSize;
    private readonly int _hopSize;
    private readonly float[] _window;

    private readonly float[] _history;
    private readonly int _historyCapacity;

    private readonly float[] _outputAccum;
    private readonly int _outputCapacity;

    private long _totalWritten;
    private long _nextGrainAt;
    private double _readPos;
    private long _outputWritePos;
    private long _outputReadPos;

    public double PitchRatio { get; set; } = 1.0;

    public PitchShifter(int grainSize = 1024, int hopSize = 512)
    {
        _grainSize = grainSize;
        _hopSize = hopSize;
        _window = BuildHannWindow(grainSize);

        _historyCapacity = grainSize * 8;
        _history = new float[_historyCapacity];

        _outputCapacity = grainSize * 4;
        _outputAccum = new float[_outputCapacity];

        _nextGrainAt = grainSize;
    }

    private static float[] BuildHannWindow(int size)
    {
        var window = new float[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1)));
        }

        return window;
    }

    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        for (int i = 0; i < input.Length; i++)
        {
            _history[(int)(_totalWritten % _historyCapacity)] = input[i];
            _totalWritten++;

            if (_totalWritten >= _nextGrainAt)
            {
                EmitGrain();
                _nextGrainAt += _hopSize;
            }

            output[i] = ReadOutputSample();
        }
    }

    private void EmitGrain()
    {
        for (int k = 0; k < _grainSize; k++)
        {
            double samplePos = _readPos + k * PitchRatio;
            float sample = ReadHistory(samplePos) * _window[k];

            int idx = (int)((_outputWritePos + k) % _outputCapacity);
            _outputAccum[idx] += sample;
        }

        // Scaling by PitchRatio (not a fixed hop) keeps consecutive grains
        // sampling the same continuous "stretched" timeline, so their
        // overlap-add reinforces the shifted frequency instead of the
        // original one. The cost: for PitchRatio != 1 this position drifts
        // away from real-time input arrival a little more with every grain.
        // Left unchecked that drift grows without bound (several seconds of
        // added latency after sustained talking), so it's resynced as soon
        // as it exceeds a couple of grain-lengths — frequent small phase
        // resets instead of a rare huge one, keeping total output delay
        // bounded to roughly two grain-lengths throughout.
        _readPos += _hopSize * PitchRatio;
        _outputWritePos += _hopSize;

        double nextGrainMaxNeeded = _readPos + (_grainSize - 1) * PitchRatio;
        bool driftedTooFar = Math.Abs(_totalWritten - _readPos - _grainSize) > _grainSize;
        bool runningAheadOfArrivedData = nextGrainMaxNeeded > _totalWritten - 1;
        if (driftedTooFar || runningAheadOfArrivedData)
        {
            _readPos = Math.Max(0, _totalWritten - _grainSize);
        }
    }

    private float ReadHistory(double pos)
    {
        if (_totalWritten <= 0)
        {
            return 0f;
        }

        if (pos < 0)
        {
            pos = 0;
        }
        else if (pos > _totalWritten - 1)
        {
            pos = _totalWritten - 1;
        }

        int i0 = (int)pos;
        int i1 = i0 + 1 > _totalWritten - 1 ? i0 : i0 + 1;
        float frac = (float)(pos - i0);

        float s0 = _history[i0 % _historyCapacity];
        float s1 = _history[i1 % _historyCapacity];
        return s0 + (s1 - s0) * frac;
    }

    private float ReadOutputSample()
    {
        int idx = (int)(_outputReadPos % _outputCapacity);
        float sample = _outputAccum[idx];
        _outputAccum[idx] = 0f;
        _outputReadPos++;
        return sample;
    }

    public void Reset()
    {
        Array.Clear(_history, 0, _history.Length);
        Array.Clear(_outputAccum, 0, _outputAccum.Length);
        _totalWritten = 0;
        _nextGrainAt = _grainSize;
        _readPos = 0;
        _outputWritePos = 0;
        _outputReadPos = 0;
    }
}
