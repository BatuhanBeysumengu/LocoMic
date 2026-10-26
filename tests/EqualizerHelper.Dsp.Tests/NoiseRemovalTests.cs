using System;
using EqualizerHelper.Audio;
using NAudio.Wave;

namespace EqualizerHelper.Dsp.Tests;

public class NoiseRemovalTests
{
    private const int SampleRate = 48000;
    private const int FrameSize = 480;

    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(float[] samples, int channels)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            int count = Math.Min(buffer.Length, _samples.Length - _position);
            _samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }

    private static float[] WhiteNoise(int count, double amplitude, int seed = 42)
    {
        var random = new Random(seed);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2 - 1) * amplitude);
        }

        return samples;
    }

    // Reads in uneven block sizes, like a real audio device would.
    private static float[] ReadAll(ISampleProvider provider, int total)
    {
        var output = new float[total];
        int position = 0;
        int[] blockSizes = { 441, 960, 128, 1000 };
        for (int b = 0; position < total; b++)
        {
            int size = Math.Min(blockSizes[b % blockSizes.Length] * provider.WaveFormat.Channels, total - position);
            size -= size % provider.WaveFormat.Channels;
            int read = provider.Read(output.AsSpan(position, size));
            Assert.Equal(size, read);
            position += read;
        }

        return output;
    }

    private static double RmsDb(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float s in samples)
        {
            sum += (double)s * s;
        }

        return 10 * Math.Log10(sum / samples.Length + 1e-20);
    }

    [Fact]
    public void Enabled_RemovesStationaryNoise()
    {
        var input = WhiteNoise(SampleRate * 3, 0.05);
        using var denoiser = new NoiseRemovalSampleProvider(new ArraySampleProvider(input, 1), () => true, () => 1.0);

        var output = ReadAll(denoiser, input.Length);

        // Skip the first second while RNNoise adapts to the noise.
        double inputDb = RmsDb(input.AsSpan(SampleRate));
        double outputDb = RmsDb(output.AsSpan(SampleRate));
        Assert.True(outputDb < inputDb - 15, $"Expected at least 15 dB reduction, got {inputDb - outputDb:0.0} dB");
    }

    [Fact]
    public void Disabled_IsExactPassthrough()
    {
        var input = WhiteNoise(SampleRate, 0.05);
        using var denoiser = new NoiseRemovalSampleProvider(new ArraySampleProvider(input, 1), () => false, () => 1.0);

        var output = ReadAll(denoiser, input.Length);

        Assert.Equal(input, output);
    }

    [Fact]
    public void ZeroStrength_PassesOriginalDelayedByOneFrame()
    {
        var input = WhiteNoise(SampleRate, 0.05);
        using var denoiser = new NoiseRemovalSampleProvider(new ArraySampleProvider(input, 1), () => true, () => 0.0);

        var output = ReadAll(denoiser, input.Length);

        for (int i = 0; i < FrameSize; i++)
        {
            Assert.Equal(0f, output[i]);
        }

        for (int i = FrameSize; i < input.Length; i++)
        {
            Assert.Equal(input[i - FrameSize], output[i], 1e-5f);
        }
    }

    [Fact]
    public void Stereo_ProcessesBothChannels()
    {
        var mono = WhiteNoise(SampleRate * 3, 0.05);
        var stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++)
        {
            stereo[2 * i] = mono[i];
            stereo[2 * i + 1] = mono[i];
        }

        using var denoiser = new NoiseRemovalSampleProvider(new ArraySampleProvider(stereo, 2), () => true, () => 1.0);
        var output = ReadAll(denoiser, stereo.Length);

        var left = new float[mono.Length];
        var right = new float[mono.Length];
        for (int i = 0; i < mono.Length; i++)
        {
            left[i] = output[2 * i];
            right[i] = output[2 * i + 1];
        }

        double inputDb = RmsDb(mono.AsSpan(SampleRate));
        Assert.True(RmsDb(left.AsSpan(SampleRate)) < inputDb - 15);
        Assert.True(RmsDb(right.AsSpan(SampleRate)) < inputDb - 15);
    }

    [Fact]
    public void Constructor_RejectsNon48kAudio()
    {
        var source = new ArraySampleProvider(new float[10], 1);
        var wrongRate = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(source, 44100);

        Assert.Throws<ArgumentException>(() => new NoiseRemovalSampleProvider(wrongRate, () => true, () => 1.0));
    }
}
