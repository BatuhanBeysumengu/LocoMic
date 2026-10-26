using CommunityToolkit.Mvvm.ComponentModel;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.ViewModels;

/// <summary>
/// UI-bindable wrapper around a single EqBand. Writes through to the wrapped
/// band immediately, since the live EqChain reads band values on every buffer.
/// </summary>
public partial class EqBandViewModel : ObservableObject
{
    private readonly EqBand _band;

    public EqBandViewModel(string label, EqBand band)
    {
        Label = label;
        _band = band;
    }

    public string Label { get; }

    public EqBand Band => _band;

    public double FrequencyHz
    {
        get => _band.FrequencyHz;
        set
        {
            if (_band.FrequencyHz != value)
            {
                _band.FrequencyHz = value;
                OnPropertyChanged();
            }
        }
    }

    public double GainDb
    {
        get => _band.GainDb;
        set
        {
            if (_band.GainDb != value)
            {
                _band.GainDb = value;
                OnPropertyChanged();
            }
        }
    }

    public double Q
    {
        get => _band.Q;
        set
        {
            if (_band.Q != value)
            {
                _band.Q = value;
                OnPropertyChanged();
            }
        }
    }

    public bool Enabled
    {
        get => _band.Enabled;
        set
        {
            if (_band.Enabled != value)
            {
                _band.Enabled = value;
                OnPropertyChanged();
            }
        }
    }
}
