namespace EqualizerHelper.Dsp;

public class EqBand
{
    public EqBand(double frequencyHz, double gainDb, double q, FilterType filterType, bool enabled = true)
    {
        FrequencyHz = frequencyHz;
        GainDb = gainDb;
        Q = q;
        FilterType = filterType;
        Enabled = enabled;
    }

    public double FrequencyHz { get; set; }
    public double GainDb { get; set; }
    public double Q { get; set; }
    public FilterType FilterType { get; set; }
    public bool Enabled { get; set; }
}
