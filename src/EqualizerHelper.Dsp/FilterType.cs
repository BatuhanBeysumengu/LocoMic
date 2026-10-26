namespace EqualizerHelper.Dsp;

public enum FilterType
{
    Peaking,
    LowShelf,
    HighShelf,

    /// <summary>
    /// Constant 0dB-peak-gain bandpass. Not meant to be applied to the live
    /// signal (its GainDb is ignored) — used for measuring how much energy a
    /// signal has around a given frequency.
    /// </summary>
    BandPass,
}
