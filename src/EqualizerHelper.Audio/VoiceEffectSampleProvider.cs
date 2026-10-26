using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Applies the selected <see cref="VoiceEffectType"/> (robot, radio, echo...)
/// to each channel, read live via a closure. Skipped while bypassed.
/// </summary>
public class VoiceEffectSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly VoiceEffect[] _effects;
    private readonly Func<VoiceEffectType> _getType;
    private readonly Func<bool> _isBypassed;

    public VoiceEffectSampleProvider(ISampleProvider source, Func<VoiceEffectType> getType, Func<bool> isBypassed)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _getType = getType;
        _isBypassed = isBypassed;
        _effects = new VoiceEffect[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _effects[c] = new VoiceEffect(source.WaveFormat.SampleRate);
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        var type = _isBypassed() ? VoiceEffectType.None : _getType();
        foreach (var effect in _effects)
        {
            effect.Type = type;
        }

        if (type == VoiceEffectType.None)
        {
            return samplesRead;
        }

        for (int i = 0; i + _channels <= samplesRead; i += _channels)
        {
            for (int c = 0; c < _channels; c++)
            {
                buffer[i + c] = _effects[c].ProcessSample(buffer[i + c]);
            }
        }

        return samplesRead;
    }
}
