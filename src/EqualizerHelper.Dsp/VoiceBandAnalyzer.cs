using System;
using System.Collections.Generic;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Measures how much energy a mono signal has around a set of frequencies,
/// using one-off BandPass BiquadFilters per band.
/// </summary>
public static class VoiceBandAnalyzer
{
    private const int WarmupSamples = 2048;
    private const double FloorDb = -80;

    public static double[] MeasureBandEnergiesDb(
        float[] monoSamples,
        double sampleRate,
        IReadOnlyList<(double FrequencyHz, double Q)> bands)
    {
        var result = new double[bands.Count];

        for (int b = 0; b < bands.Count; b++)
        {
            var measurementBand = new EqBand(bands[b].FrequencyHz, 0, bands[b].Q, FilterType.BandPass);
            var filter = new BiquadFilter(measurementBand, sampleRate);

            double sumSquares = 0;
            int measured = 0;

            for (int n = 0; n < monoSamples.Length; n++)
            {
                float y = filter.ProcessSample(monoSamples[n]);
                if (n >= WarmupSamples)
                {
                    sumSquares += (double)y * y;
                    measured++;
                }
            }

            if (measured <= 0)
            {
                result[b] = FloorDb;
                continue;
            }

            double rms = Math.Sqrt(sumSquares / measured);
            result[b] = rms > 0 ? Math.Max(20 * Math.Log10(rms), FloorDb) : FloorDb;
        }

        return result;
    }
}
