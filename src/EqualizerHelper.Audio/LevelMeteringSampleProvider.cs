using System;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Passes audio through unchanged while tracking the peak sample magnitude of
/// the most recently read block, for driving a simple level meter in the UI.
/// </summary>
public class LevelMeteringSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public LevelMeteringSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float CurrentLevel { get; private set; }

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        float peak = 0f;
        for (int i = 0; i < samplesRead; i++)
        {
            float abs = Math.Abs(buffer[i]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        CurrentLevel = peak;
        return samplesRead;
    }
}
