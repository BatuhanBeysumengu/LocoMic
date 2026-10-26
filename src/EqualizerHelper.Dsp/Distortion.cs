using System;

namespace EqualizerHelper.Dsp;

/// <summary>
/// Soft-clipping waveshaper (tanh saturation). Stateless — one shared
/// instance works for any number of channels, but it's exposed as a small
/// class for consistency with the other DSP stages and so each channel can
/// optionally get its own instance if ever needed.
/// </summary>
public class Distortion
{
    /// <summary>1.0 = clean (no drive). Higher values clip/saturate harder.</summary>
    public double Drive { get; set; } = 1.0;

    public float ProcessSample(float input)
    {
        if (Drive <= 1.0)
        {
            return input;
        }

        double driven = input * Drive;
        double shaped = Math.Tanh(driven);

        // Full-scale input always maps back to full-scale output regardless
        // of drive, so increasing drive adds grit without just getting quieter.
        double normalizer = Math.Tanh(Drive);
        return (float)(shaped / normalizer);
    }
}
