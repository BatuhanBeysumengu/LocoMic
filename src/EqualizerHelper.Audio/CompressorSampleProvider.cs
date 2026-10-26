using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Wraps a source ISampleProvider with per-channel dynamic range compression,
/// driven by a single 0..1 "crunch amount" read live via a closure. 0 is a
/// true passthrough. Threshold/ratio/makeup gain all scale with the amount;
/// attack/release stay fixed at voice-chat-appropriate values.
/// </summary>
public class CompressorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly Compressor[] _compressors;
    private readonly Func<double> _getCrunchAmount;

    public CompressorSampleProvider(ISampleProvider source, double sampleRate, Func<double> getCrunchAmount)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _getCrunchAmount = getCrunchAmount;

        _compressors = new Compressor[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _compressors[c] = new Compressor(sampleRate) { AttackMs = 5, ReleaseMs = 80 };
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        double amount = _getCrunchAmount();
        if (amount <= 0)
        {
            return samplesRead;
        }

        double thresholdDb = -amount * 28.0;
        double ratio = 1.0 + amount * 7.0;
        double makeupGainDb = amount * 10.0;

        for (int c = 0; c < _channels; c++)
        {
            _compressors[c].ThresholdDb = thresholdDb;
            _compressors[c].Ratio = ratio;
            _compressors[c].MakeupGainDb = makeupGainDb;
        }

        for (int i = 0; i + _channels <= samplesRead; i += _channels)
        {
            for (int c = 0; c < _channels; c++)
            {
                buffer[i + c] = _compressors[c].ProcessSample(buffer[i + c]);
            }
        }

        return samplesRead;
    }
}
