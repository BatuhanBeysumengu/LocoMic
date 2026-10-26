using System;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Keeps the audio waiting between the mic and an output device short.
/// The mic and the output (e.g. VB-Cable, headphones) run on separate
/// hardware clocks that never match exactly; if the mic is even slightly
/// faster, unread audio piles up in the capture buffer and the voice drifts
/// further and further behind. Trimming the backlog back down whenever it
/// grows past a small threshold keeps the delay bounded for any session length.
/// </summary>
public static class CaptureBacklog
{
    /// <summary>Above this much buffered audio, the backlog is trimmed.</summary>
    public static readonly TimeSpan MaxLatency = TimeSpan.FromMilliseconds(150);

    /// <summary>How much audio is kept after trimming (enough to avoid underruns).</summary>
    public static readonly TimeSpan TargetLatency = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Drops the oldest audio from <paramref name="buffer"/> if it holds more
    /// than <paramref name="max"/>, leaving <paramref name="target"/>.
    /// Returns the duration that was dropped.
    /// </summary>
    public static TimeSpan Trim(BufferedWaveProvider buffer, TimeSpan max, TimeSpan target)
    {
        if (buffer.BufferedDuration <= max)
        {
            return TimeSpan.Zero;
        }

        var format = buffer.WaveFormat;
        int excess = buffer.BufferedBytes - (int)(target.TotalSeconds * format.AverageBytesPerSecond);
        excess -= excess % format.BlockAlign;
        if (excess <= 0)
        {
            return TimeSpan.Zero;
        }

        var scratch = new byte[Math.Min(excess, 64 * 1024)];
        int dropped = 0;
        while (dropped < excess)
        {
            int read = buffer.Read(scratch.AsSpan(0, Math.Min(scratch.Length, excess - dropped)));
            if (read <= 0)
            {
                break;
            }

            dropped += read;
        }

        return TimeSpan.FromSeconds((double)dropped / format.AverageBytesPerSecond);
    }

    public static TimeSpan Trim(BufferedWaveProvider buffer) => Trim(buffer, MaxLatency, TargetLatency);
}
