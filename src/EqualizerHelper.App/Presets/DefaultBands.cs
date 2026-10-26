using System.Collections.Generic;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.Presets;

/// <summary>
/// Fixed set of band roles used for the mic EQ: frequency/Q/filter type stay
/// put, only gain is meant to be tuned (by hand or via a preset).
/// </summary>
public static class DefaultBands
{
    public static readonly (string Label, double FrequencyHz, double Q, FilterType FilterType)[] Roles =
    {
        ("Rumble", 100, 0.9, FilterType.LowShelf),
        ("Mud", 250, 1.0, FilterType.Peaking),
        ("Body", 1000, 1.0, FilterType.Peaking),
        ("Presence", 3000, 1.0, FilterType.Peaking),
        ("Sibilance", 6000, 1.0, FilterType.Peaking),
        ("Air", 10000, 0.9, FilterType.HighShelf),
    };

    public static List<EqBand> CreateBands()
    {
        var bands = new List<EqBand>();
        foreach (var role in Roles)
        {
            bands.Add(new EqBand(role.FrequencyHz, 0, role.Q, role.FilterType));
        }

        return bands;
    }
}
