using System;
using EqualizerHelper.Audio;
using NAudio.Wave;

namespace EqualizerHelper.Dsp.Tests;

public class CaptureBacklogTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>
    /// Simulates a mic whose clock runs <paramref name="inputSpeed"/> times as
    /// fast as the output, for <paramref name="minutes"/> of 10 ms blocks, and
    /// returns the largest backlog seen.
    /// </summary>
    private static TimeSpan SimulateDrift(double inputSpeed, double minutes, bool trim)
    {
        var buffer = new BufferedWaveProvider(Format, TimeSpan.FromSeconds(30)) { DiscardOnBufferOverflow = true };
        int outputBlock = Format.AverageBytesPerSecond / 100; // 10 ms
        var readScratch = new byte[outputBlock];
        double inputCarry = 0;
        var worst = TimeSpan.Zero;

        // Start with a little audio buffered, as a real output would.
        buffer.AddSamples(new byte[outputBlock * 2], 0, outputBlock * 2);

        int blocks = (int)(minutes * 60 * 100);
        for (int i = 0; i < blocks; i++)
        {
            inputCarry += outputBlock * inputSpeed;
            int inputBytes = (int)inputCarry;
            inputBytes -= inputBytes % Format.BlockAlign;
            inputCarry -= inputBytes;
            buffer.AddSamples(new byte[inputBytes], 0, inputBytes);

            if (trim)
            {
                CaptureBacklog.Trim(buffer);
            }

            if (buffer.BufferedDuration > worst)
            {
                worst = buffer.BufferedDuration;
            }

            buffer.Read(readScratch.AsSpan(0, outputBlock));
        }

        return worst;
    }

    [Fact]
    public void WithoutTrimming_FastMicClock_DelayKeepsGrowing()
    {
        // Sanity check of the simulation: 1% drift over 10 minutes = ~6 s of delay.
        var worst = SimulateDrift(inputSpeed: 1.01, minutes: 10, trim: false);
        Assert.True(worst > TimeSpan.FromSeconds(5), $"expected a large backlog, got {worst.TotalMilliseconds} ms");
    }

    [Fact]
    public void WithTrimming_FastMicClock_DelayStaysBounded()
    {
        var worst = SimulateDrift(inputSpeed: 1.01, minutes: 10, trim: true);
        Assert.True(worst <= CaptureBacklog.MaxLatency + TimeSpan.FromMilliseconds(15),
            $"backlog grew to {worst.TotalMilliseconds} ms");
    }

    [Fact]
    public void WithTrimming_MatchingClocks_NothingIsDropped()
    {
        var buffer = new BufferedWaveProvider(Format, TimeSpan.FromSeconds(5));
        int block = Format.AverageBytesPerSecond / 100;
        buffer.AddSamples(new byte[block * 3], 0, block * 3);

        var dropped = CaptureBacklog.Trim(buffer);

        Assert.Equal(TimeSpan.Zero, dropped);
        Assert.Equal(block * 3, buffer.BufferedBytes);
    }

    [Fact]
    public void Trim_LeavesTargetLatencyAndWholeFrames()
    {
        var buffer = new BufferedWaveProvider(Format, TimeSpan.FromSeconds(5));
        buffer.AddSamples(new byte[Format.AverageBytesPerSecond], 0, Format.AverageBytesPerSecond); // 1 s

        CaptureBacklog.Trim(buffer);

        Assert.InRange(buffer.BufferedDuration.TotalMilliseconds, 39, 41);
        Assert.Equal(0, buffer.BufferedBytes % Format.BlockAlign);
    }
}
