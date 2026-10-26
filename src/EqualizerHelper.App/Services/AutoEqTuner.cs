using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EqualizerHelper.App.ViewModels;
using EqualizerHelper.Audio;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.Services;

public enum AutoEqTunerResult
{
    Converged,
    ReachedMaxIterations,
    Cancelled,
}

/// <summary>
/// Iteratively listens to the processed mic signal, measures the loudness of
/// each EQ band, and nudges gains toward a target voice curve. Not a neural
/// model — a damped measure/adjust/re-measure loop reusing the app's own
/// BiquadFilter-based band analysis, so it only runs for a few seconds when
/// explicitly invoked rather than continuously.
/// </summary>
public static class AutoEqTuner
{
    private const int MaxIterations = 4;
    private static readonly TimeSpan ListenDuration = TimeSpan.FromSeconds(3);
    private const double ToleranceDb = 1.5;
    private const double DampingFactor = 0.6;
    private const double MaxGainDb = 15;

    // Target relative balance between bands, reusing the hand-tuned "Voice
    // Clarity" preset deltas (see PresetStore) as a natural-voice reference
    // shape — less rumble/mud, a presence lift, tamed sibilance, a touch of
    // air — rather than inventing a second set of numbers.
    private static readonly double[] TargetShapeDb = { -3, -3, 0, 4, -2, 2 };

    public static async Task<AutoEqTunerResult> RunAsync(
        MicProcessingEngine engine,
        IReadOnlyList<EqBandViewModel> bands,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (bands.Count != TargetShapeDb.Length)
        {
            throw new InvalidOperationException($"AutoEqTuner expects {TargetShapeDb.Length} bands, got {bands.Count}.");
        }

        var bandFreqQs = bands.Select(b => (b.FrequencyHz, b.Q)).ToList();
        bool priorBypass = engine.Bypass;
        engine.Bypass = false;

        try
        {
            for (int iteration = 1; iteration <= MaxIterations; iteration++)
            {
                progress.Report($"Listening... ({iteration}/{MaxIterations}) — keep talking naturally");

                float[] samples;
                try
                {
                    samples = await engine.CaptureSamplesAsync(ListenDuration, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return AutoEqTunerResult.Cancelled;
                }

                double[] measuredDb = VoiceBandAnalyzer.MeasureBandEnergiesDb(samples, engine.SampleRate, bandFreqQs);
                double mean = measuredDb.Average();
                double[] shape = measuredDb.Select(d => d - mean).ToArray();

                var errors = new double[bands.Count];
                double maxAbsError = 0;
                for (int i = 0; i < bands.Count; i++)
                {
                    errors[i] = TargetShapeDb[i] - shape[i];
                    maxAbsError = Math.Max(maxAbsError, Math.Abs(errors[i]));
                }

                if (maxAbsError <= ToleranceDb)
                {
                    progress.Report("Converged.");
                    return AutoEqTunerResult.Converged;
                }

                for (int i = 0; i < bands.Count; i++)
                {
                    double newGain = bands[i].GainDb + errors[i] * DampingFactor;
                    bands[i].GainDb = Math.Clamp(newGain, -MaxGainDb, MaxGainDb);
                }
            }

            progress.Report("Reached max iterations.");
            return AutoEqTunerResult.ReachedMaxIterations;
        }
        finally
        {
            engine.Bypass = priorBypass;
        }
    }
}
