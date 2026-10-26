using System.Collections.Generic;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.Presets;

public class EqBandSettings
{
    public double FrequencyHz { get; set; }
    public double GainDb { get; set; }
    public double Q { get; set; }
    public FilterType FilterType { get; set; }
}

public class EqPreset
{
    public EqPreset(string name, List<EqBandSettings> bands)
    {
        Name = name;
        Bands = bands;
    }

    public string Name { get; set; }
    public List<EqBandSettings> Bands { get; set; }

    /// <summary>
    /// Pitch shift in semitones (0 = none). Separate from the per-band EQ
    /// settings since pitch isn't a band — it's a global effect.
    /// </summary>
    public double PitchSemitones { get; set; }

    /// <summary>
    /// 0..1 "cheap/overdriven mic" character (distortion + compression).
    /// 0 = clean.
    /// </summary>
    public double MicCrunch { get; set; }

    /// <summary>Input gain in dB applied before everything else.</summary>
    public double InputGainDb { get; set; }

    /// <summary>0..1 noise gate amount. 0 = off.</summary>
    public double Gate { get; set; }

    /// <summary>0..1 loudness (voice compression) amount. 0 = off.</summary>
    public double Loudness { get; set; }

    public override string ToString() => Name;
}
