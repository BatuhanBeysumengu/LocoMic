using System;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Outputs silence while muted, keeping the stream running so un-muting is
/// instant and the output device never sees a gap or restart.
/// </summary>
public class MuteSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<bool> _isMuted;

    public MuteSampleProvider(ISampleProvider source, Func<bool> isMuted)
    {
        _source = source;
        _isMuted = isMuted;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);
        if (_isMuted())
        {
            buffer[..samplesRead].Clear();
        }

        return samplesRead;
    }
}
