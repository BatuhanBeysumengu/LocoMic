using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Wraps a source ISampleProvider, running each read block through an EqChain
/// unless bypass is active.
/// </summary>
public class EqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EqChain _chain;
    private readonly Func<bool> _isBypassed;

    public EqSampleProvider(ISampleProvider source, EqChain chain, Func<bool> isBypassed)
    {
        _source = source;
        _chain = chain;
        _isBypassed = isBypassed;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        if (!_isBypassed())
        {
            _chain.ProcessBuffer(buffer[..samplesRead]);
        }

        return samplesRead;
    }
}
