using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EqualizerHelper.App.Services;

namespace EqualizerHelper.Dsp.Tests;

public class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "locomic-settings-" + Guid.NewGuid());

    private string SettingsFile => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static AppSettings WithSounds(params string[] names) => new()
    {
        Sounds = names.Select(n => new SavedSound { Path = @"C:\Users\x\Masaüstü\soundpad\" + n + ".mp3", Volume = 0.5, Loop = true }).ToList(),
        Theme = "Locomotive",
    };

    [Fact]
    public void Save_ThenLoad_KeepsTheSoundListInOrder()
    {
        AppSettingsStore.Save(WithSounds("Gamze Aksoy", "ajdar-kelini-s", "Özlem Gürses"), SettingsFile);

        var loaded = AppSettingsStore.Load(SettingsFile);

        Assert.Equal(new[] { "Gamze Aksoy", "ajdar-kelini-s", "Özlem Gürses" },
            loaded.Sounds!.Select(s => Path.GetFileNameWithoutExtension(s.Path)));
        Assert.All(loaded.Sounds!, s => Assert.True(s.Loop));
    }

    [Fact]
    public void Save_ReplacesTheFile_AndLeavesNoTempFileBehind()
    {
        AppSettingsStore.Save(WithSounds("one"), SettingsFile);
        AppSettingsStore.Save(WithSounds("one", "two"), SettingsFile);

        Assert.Equal(2, AppSettingsStore.Load(SettingsFile).Sounds!.Count);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Save_WhileAnotherProgramBrieflyHoldsTheFile_StillSucceeds()
    {
        AppSettingsStore.Save(WithSounds("one"), SettingsFile);

        // Simulate e.g. an antivirus scan holding the file for a moment.
        var holder = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Delay(120).ContinueWith(_ => holder.Dispose());

        AppSettingsStore.Save(WithSounds("one", "two", "three"), SettingsFile);
        await release;

        Assert.Equal(3, AppSettingsStore.Load(SettingsFile).Sounds!.Count);
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var loaded = AppSettingsStore.Load(SettingsFile);

        Assert.Null(loaded.Sounds);
        Assert.True(loaded.SoundHotkeysEnabled);
    }
}
