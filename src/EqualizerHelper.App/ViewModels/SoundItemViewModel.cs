using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EqualizerHelper.App.ViewModels;

/// <summary>One song or sound effect in the soundboard list.</summary>
public partial class SoundItemViewModel : ObservableObject
{
    public SoundItemViewModel(string path, double volume = 1.0, bool loop = false)
    {
        Path = path;
        this.volume = volume;
        this.loop = loop;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public bool FileExists => File.Exists(Path);

    [ObservableProperty]
    private double volume;

    [ObservableProperty]
    private bool loop;

    [ObservableProperty]
    private bool isPlaying;

    /// <summary>1-based position in the list (sounds 1–9 have hotkeys).</summary>
    [ObservableProperty]
    private int number;

    /// <summary>Raised when the user changes something that should be saved.</summary>
    public event EventHandler? Changed;

    partial void OnVolumeChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnLoopChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
}
