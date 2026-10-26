using System;
using System.IO;
using System.Linq;
using EqualizerHelper.App.Presets;

namespace EqualizerHelper.Dsp.Tests;

public class PresetFileTests
{
    private static EqPreset SamplePreset(string name = "My Voice")
    {
        var preset = PresetStore.GetBuiltInPresets().First(p => p.Name == "Voice Clarity");
        preset.Name = name;
        preset.PitchSemitones = 3;
        preset.MicCrunch = 0.25;
        preset.InputGainDb = 6;
        preset.NoiseRemovalStrength = 0.7;
        return preset;
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsAllSettings()
    {
        var original = SamplePreset();

        var loaded = PresetFile.Deserialize(PresetFile.Serialize(new[] { original })).Single();

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Bands.Select(b => b.GainDb), loaded.Bands.Select(b => b.GainDb));
        Assert.Equal(original.Bands.Select(b => b.Q), loaded.Bands.Select(b => b.Q));
        Assert.Equal(original.PitchSemitones, loaded.PitchSemitones);
        Assert.Equal(original.MicCrunch, loaded.MicCrunch);
        Assert.Equal(original.InputGainDb, loaded.InputGainDb);
        Assert.Equal(original.Gate, loaded.Gate);
        Assert.Equal(original.Loudness, loaded.Loudness);
        Assert.Equal(original.NoiseRemoval, loaded.NoiseRemoval);
        Assert.Equal(original.NoiseRemovalStrength, loaded.NoiseRemovalStrength);
    }

    [Fact]
    public void Write_ThenRead_WorksThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + PresetFile.Extension);
        try
        {
            PresetFile.Write(path, SamplePreset("File Test"));
            Assert.Equal("File Test", PresetFile.Read(path).Single().Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"something\": \"else\"}")]
    [InlineData("{\"format\": \"EqualizerHelper.Preset\", \"version\": 1, \"presets\": []}")]
    [InlineData("{\"format\": \"EqualizerHelper.Preset\", \"version\": 99, \"presets\": []}")]
    [InlineData("{\"format\": \"EqualizerHelper.Preset\", \"version\": 1, \"presets\": [{\"name\": \"x\", \"bands\": []}]}")]
    [InlineData("{\"format\": \"EqualizerHelper.Preset\", \"version\": 1, \"presets\": [{\"name\": \"  \", \"bands\": []}]}")]
    public void Deserialize_RejectsInvalidFiles(string json)
    {
        Assert.Throws<InvalidDataException>(() => PresetFile.Deserialize(json));
    }

    [Fact]
    public void Deserialize_ClampsOutOfRangeValuesAndIgnoresBandFrequencies()
    {
        var preset = SamplePreset();
        preset.PitchSemitones = 100;
        preset.InputGainDb = 500;
        preset.Gate = -3;
        preset.Bands[0].GainDb = 99;
        preset.Bands[0].Q = 0;
        preset.Bands[0].FrequencyHz = 1;

        var loaded = PresetFile.Deserialize(PresetFile.Serialize(new[] { preset })).Single();

        Assert.Equal(12, loaded.PitchSemitones);
        Assert.Equal(24, loaded.InputGainDb);
        Assert.Equal(0, loaded.Gate);
        Assert.Equal(15, loaded.Bands[0].GainDb);
        Assert.Equal(0.1, loaded.Bands[0].Q);
        Assert.Equal(DefaultBands.Roles[0].FrequencyHz, loaded.Bands[0].FrequencyHz);
    }

    [Fact]
    public void MakeUniqueName_AddsNumberSuffixWhenTaken()
    {
        Assert.Equal("Bass", PresetFile.MakeUniqueName("Bass", new[] { "Flat" }));
        Assert.Equal("Bass (2)", PresetFile.MakeUniqueName("Bass", new[] { "bass" }));
        Assert.Equal("Bass (3)", PresetFile.MakeUniqueName("Bass", new[] { "Bass", "Bass (2)" }));
    }

    [Fact]
    public void SuggestedFileName_RemovesCharactersWindowsDoesNotAllow()
    {
        Assert.Equal("Deep Bass (Fun).eqpreset", PresetFile.SuggestedFileName("Deep Bass (Fun)"));
        Assert.Equal("a_b_c.eqpreset", PresetFile.SuggestedFileName("a/b:c"));
    }
}
