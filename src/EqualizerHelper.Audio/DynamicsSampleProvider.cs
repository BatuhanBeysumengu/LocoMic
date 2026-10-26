using System;
using EqualizerHelper.Dsp;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Live-tunable dynamics settings for the voice chain. Gate and Loudness are
/// 0..1 "amount" knobs (0 = off) so the UI can stay as simple sliders.
/// </summary>
public class DynamicsSettings
{
    public double InputGainDb { get; set; }
    public double Gate { get; set; }
    public double Loudness { get; set; }

    /// <summary>Gate amount 0..1 → open threshold, -70 dB (barely there) to -30 dB (aggressive).</summary>
    public static double GateThresholdDb(double gate) => -70 + 40 * gate;
}

/// <summary>
/// Input gain followed by the noise gate, applied at the start of the chain
/// (before EQ) so the gate keys off the raw voice level.
/// </summary>
public class InputDynamicsSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly NoiseGate _gate;
    private readonly Func<DynamicsSettings> _getSettings;
    private readonly Func<bool> _isBypassed;

    public InputDynamicsSampleProvider(ISampleProvider source, double sampleRate, Func<DynamicsSettings> getSettings, Func<bool> isBypassed)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _gate = new NoiseGate(sampleRate);
        _getSettings = getSettings;
        _isBypassed = isBypassed;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);
        if (_isBypassed())
        {
            return samplesRead;
        }

        var settings = _getSettings();
        float inputGain = (float)Math.Pow(10, settings.InputGainDb / 20.0);
        bool gateOn = settings.Gate > 0;
        if (gateOn)
        {
            _gate.ThresholdDb = DynamicsSettings.GateThresholdDb(settings.Gate);
        }
        else
        {
            _gate.Reset();
        }

        for (int i = 0; i + _channels <= samplesRead; i += _channels)
        {
            float peak = 0;
            for (int c = 0; c < _channels; c++)
            {
                buffer[i + c] *= inputGain;
                peak = Math.Max(peak, Math.Abs(buffer[i + c]));
            }

            if (gateOn)
            {
                float gain = _gate.NextGain(peak);
                for (int c = 0; c < _channels; c++)
                {
                    buffer[i + c] *= gain;
                }
            }
        }

        return samplesRead;
    }
}

/// <summary>
/// "Loudness" compressor (evens out the voice and brings it up) followed by
/// an always-on -1 dBFS safety limiter, applied at the very end of the chain
/// so nothing that reaches Discord/OBS can clip.
/// </summary>
public class OutputDynamicsSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly Compressor[] _compressors;
    private readonly Limiter _limiter;
    private readonly Func<DynamicsSettings> _getSettings;
    private readonly Func<bool> _isBypassed;

    public OutputDynamicsSampleProvider(ISampleProvider source, double sampleRate, Func<DynamicsSettings> getSettings, Func<bool> isBypassed)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _getSettings = getSettings;
        _isBypassed = isBypassed;
        _limiter = new Limiter(sampleRate);

        _compressors = new Compressor[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _compressors[c] = new Compressor(sampleRate) { AttackMs = 10, ReleaseMs = 150 };
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);
        if (_isBypassed())
        {
            return samplesRead;
        }

        double loudness = _getSettings().Loudness;
        bool compress = loudness > 0;
        if (compress)
        {
            // Gentle-to-firm voice compression; makeup gain recovers most of
            // the reduction so the slider reads as "louder and more even".
            double thresholdDb = -12 - 18 * loudness;
            double ratio = 1.5 + 2.5 * loudness;
            double makeupDb = -thresholdDb * (1 - 1 / ratio) * 0.6;
            foreach (var compressor in _compressors)
            {
                compressor.ThresholdDb = thresholdDb;
                compressor.Ratio = ratio;
                compressor.MakeupGainDb = makeupDb;
            }
        }

        for (int i = 0; i + _channels <= samplesRead; i += _channels)
        {
            float peak = 0;
            for (int c = 0; c < _channels; c++)
            {
                if (compress)
                {
                    buffer[i + c] = _compressors[c].ProcessSample(buffer[i + c]);
                }

                peak = Math.Max(peak, Math.Abs(buffer[i + c]));
            }

            float gain = _limiter.NextGain(peak);
            if (gain < 1f)
            {
                for (int c = 0; c < _channels; c++)
                {
                    buffer[i + c] *= gain;
                }
            }
        }

        return samplesRead;
    }
}
