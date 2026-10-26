using System;
using System.Collections.Generic;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Cascades a set of EqBands into a multi-band parametric EQ, keeping one
/// BiquadFilter per band per audio channel so each channel's filter history
/// stays independent.
/// </summary>
public class EqChain
{
    private readonly List<EqBand> _bands;
    private readonly int _channelCount;
    private readonly double _sampleRate;
    private readonly BiquadFilter[][] _filters; // [bandIndex][channel]

    public EqChain(IEnumerable<EqBand> bands, int channelCount, double sampleRate)
    {
        _bands = new List<EqBand>(bands);
        _channelCount = channelCount;
        _sampleRate = sampleRate;

        _filters = new BiquadFilter[_bands.Count][];
        for (int b = 0; b < _bands.Count; b++)
        {
            _filters[b] = new BiquadFilter[channelCount];
            for (int c = 0; c < channelCount; c++)
            {
                _filters[b][c] = new BiquadFilter(_bands[b], sampleRate);
            }
        }
    }

    public IReadOnlyList<EqBand> Bands => _bands;

    /// <summary>
    /// Processes an interleaved float buffer in place. Recomputes filter
    /// coefficients from the current band settings first, so UI changes to a
    /// band's frequency/gain/Q take effect on the next buffer.
    /// </summary>
    public void ProcessBuffer(float[] buffer, int sampleCount) => ProcessBuffer(buffer.AsSpan(0, sampleCount));

    /// <summary>
    /// Processes an interleaved float buffer in place, starting at <paramref name="offset"/>,
    /// for <paramref name="sampleCount"/> samples. Matches ISampleProvider.Read offset/count semantics.
    /// </summary>
    public void ProcessBuffer(float[] buffer, int offset, int sampleCount) => ProcessBuffer(buffer.AsSpan(offset, sampleCount));

    /// <summary>
    /// Processes an interleaved float buffer in place.
    /// </summary>
    public void ProcessBuffer(Span<float> buffer)
    {
        for (int b = 0; b < _bands.Count; b++)
        {
            if (!_bands[b].Enabled)
            {
                continue;
            }

            for (int c = 0; c < _channelCount; c++)
            {
                _filters[b][c].UpdateCoefficients(_bands[b], _sampleRate);
            }
        }

        for (int i = 0; i + _channelCount <= buffer.Length; i += _channelCount)
        {
            for (int c = 0; c < _channelCount; c++)
            {
                int index = i + c;
                float sample = buffer[index];
                for (int b = 0; b < _bands.Count; b++)
                {
                    if (_bands[b].Enabled)
                    {
                        sample = _filters[b][c].ProcessSample(sample);
                    }
                }

                buffer[index] = sample;
            }
        }
    }

    public void Reset()
    {
        foreach (var bandFilters in _filters)
        {
            foreach (var filter in bandFilters)
            {
                filter.Reset();
            }
        }
    }
}
