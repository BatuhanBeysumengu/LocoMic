using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqualizerHelper.App.Presets;
using EqualizerHelper.App.Services;
using EqualizerHelper.App.Views;
using EqualizerHelper.Audio;
using EqualizerHelper.Dsp;

namespace EqualizerHelper.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public const string VirtualCableDownloadUrl = "https://vb-audio.com/Cable/";

    private readonly MicProcessingEngine _engine = new();
    private readonly DispatcherTimer _meterTimer;
    private CancellationTokenSource? _autoTuneCts;
    private double[]? _preAutoTuneSnapshot;
    private bool _settingsLoaded;

    public MainViewModel()
    {
        CaptureDevices = new ObservableCollection<AudioDeviceInfo>(DeviceManager.GetCaptureDevices());
        RenderDevices = new ObservableCollection<AudioDeviceInfo>(DeviceManager.GetRenderDevices());

        var vbCable = DeviceManager.FindVirtualCableInput(RenderDevices);
        VirtualCableDetected = vbCable != null;

        var savedSettings = AppSettingsStore.Load();
        Hotkeys = HotkeyGesture.FromSettings(savedSettings.Hotkeys);

        Sounds = new ObservableCollection<SoundItemViewModel>();
        foreach (var saved in savedSettings.Sounds ?? new List<SavedSound>())
        {
            AddSoundItem(new SoundItemViewModel(saved.Path, saved.Volume, saved.Loop));
        }

        soundVolume = savedSettings.SoundVolume;
        duckSounds = savedSettings.DuckSounds;
        hearSounds = savedSettings.HearSounds;
        soundHotkeysEnabled = savedSettings.SoundHotkeysEnabled;
        selectedTheme = ThemeService.Find(savedSettings.Theme);
        _engine.Soundboard.Settings.MasterVolume = soundVolume;
        _engine.Soundboard.Settings.DuckWhileTalking = duckSounds;
        _engine.Soundboard.Settings.HearInMonitor = hearSounds;
        _engine.Soundboard.SoundEnded += OnSoundEnded;

        SelectedCaptureDevice = CaptureDevices.FirstOrDefault(d => d.Id == savedSettings.CaptureDeviceId)
            ?? CaptureDevices.FirstOrDefault();
        SelectedRenderDevice = RenderDevices.FirstOrDefault(d => d.Id == savedSettings.RenderDeviceId)
            ?? vbCable
            ?? RenderDevices.FirstOrDefault();

        var monitorCandidate = RenderDevices.FirstOrDefault(d => d.Id == savedSettings.MonitorDeviceId)
            ?? RenderDevices.FirstOrDefault(d => vbCable == null || d.Id != vbCable.Id);
        SelectedMonitorDevice = monitorCandidate;
        MonitorEnabled = monitorCandidate != null && savedSettings.MonitorEnabled;

        var roles = DefaultBands.Roles;
        var bands = DefaultBands.CreateBands();
        Bands = new ObservableCollection<EqBandViewModel>(
            bands.Select((band, i) => new EqBandViewModel(roles[i].Label, band)));

        Presets = new ObservableCollection<EqPreset>(PresetStore.LoadAllPresets().Prepend(NewPresetItem));
        SelectedPreset = Presets.FirstOrDefault(p => p != NewPresetItem && p.Name == savedSettings.PresetName)
            ?? Presets.FirstOrDefault(p => p != NewPresetItem);

        _settingsLoaded = true;

        StatusMessage = VirtualCableDetected
            ? "VB-Audio Virtual Cable detected."
            : "VB-Audio Virtual Cable not found. Install it so Discord/OBS/Zoom can use the processed mic.";

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _meterTimer.Tick += (_, _) =>
        {
            InputLevel = _engine.InputLevel;
            OutputLevel = _engine.OutputLevel;
        };
        _meterTimer.Start();

        _engine.DeviceStoppedUnexpectedly += OnEngineDeviceStoppedUnexpectedly;

        StartupRegistration.RepairIfMoved();
        startWithWindows = StartupRegistration.IsEnabled;
    }

    private void OnEngineDeviceStoppedUnexpectedly(object? sender, Exception? ex)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsRunning = false;
            StatusMessage = ex != null
                ? $"Stopped unexpectedly: {ex.Message}"
                : "Stopped unexpectedly. The microphone or output device may have changed (common with Bluetooth headsets switching profiles) or been disconnected.";
        });
    }

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; }
    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; }
    public ObservableCollection<EqBandViewModel> Bands { get; }
    public ObservableCollection<EqPreset> Presets { get; }

    /// <summary>
    /// Placeholder shown at the top of the preset list; picking it creates a
    /// new preset instead of loading one.
    /// </summary>
    public static readonly EqPreset NewPresetItem = new("+ New preset...", new List<EqBandSettings>());

    [ObservableProperty]
    private AudioDeviceInfo? selectedCaptureDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedRenderDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedMonitorDevice;

    [ObservableProperty]
    private bool monitorEnabled;

    [ObservableProperty]
    private EqPreset? selectedPreset;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool bypass;

    [ObservableProperty]
    private bool isMuted;

    /// <summary>Songs and sound effects the user can play into the mic.</summary>
    public ObservableCollection<SoundItemViewModel> Sounds { get; }

    [ObservableProperty]
    private double soundVolume;

    [ObservableProperty]
    private bool duckSounds;

    [ObservableProperty]
    private bool hearSounds;

    [ObservableProperty]
    private bool soundHotkeysEnabled;

    public IReadOnlyList<AppTheme> Themes => ThemeService.Themes;

    [ObservableProperty]
    private AppTheme selectedTheme;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        ThemeService.Apply(value);
        SavePersistedSettings();
    }

    partial void OnSoundHotkeysEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HotkeySummary));
        SavePersistedSettings();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Play/stop the Nth sound in the list (1-based), e.g. from a numpad hotkey.</summary>
    public void ToggleSoundByNumber(int number)
    {
        if (number >= 1 && number <= Sounds.Count)
        {
            ToggleSound(Sounds[number - 1]);
        }
    }

    partial void OnSoundVolumeChanged(double value)
    {
        _engine.Soundboard.Settings.MasterVolume = value;
        SavePersistedSettings();
    }

    partial void OnDuckSoundsChanged(bool value)
    {
        _engine.Soundboard.Settings.DuckWhileTalking = value;
        SavePersistedSettings();
    }

    partial void OnHearSoundsChanged(bool value)
    {
        _engine.Soundboard.Settings.HearInMonitor = value;
        SavePersistedSettings();
        UpdateMonitorPlayback();
    }

    [RelayCommand]
    private void AddSounds()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add sounds",
            Filter = "Audio files (*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.aiff)|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.aiff;*.aif|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (string path in dialog.FileNames)
        {
            if (Sounds.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddSoundItem(new SoundItemViewModel(path));
        }

        SavePersistedSettings();
        StatusMessage = IsRunning
            ? "Sound added. Press ▶ to play it into your mic."
            : "Sound added. Press Start, then ▶ to play it into your mic.";
    }

    [RelayCommand]
    private void ToggleSound(SoundItemViewModel? sound)
    {
        if (sound == null)
        {
            return;
        }

        if (sound.IsPlaying)
        {
            _engine.Soundboard.Stop(sound.Id);
            sound.IsPlaying = false;
            return;
        }

        if (!_engine.Soundboard.IsAvailable)
        {
            StatusMessage = "Press Start first, then play sounds into your mic.";
            return;
        }

        if (!sound.FileExists)
        {
            StatusMessage = $"Can't find \"{sound.Name}\" any more. It may have been moved or deleted.";
            return;
        }

        try
        {
            _engine.Soundboard.Play(sound.Id, sound.Path, () => sound.Volume, () => sound.Loop);
            sound.IsPlaying = true;
            StatusMessage = $"Playing \"{sound.Name}\" into your mic.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't play \"{sound.Name}\": {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveSound(SoundItemViewModel? sound)
    {
        if (sound == null)
        {
            return;
        }

        _engine.Soundboard.Stop(sound.Id);
        sound.Changed -= OnSoundChanged;
        Sounds.Remove(sound);
        RenumberSounds();
        SavePersistedSettings();
    }

    [RelayCommand]
    private void StopAllSounds()
    {
        _engine.Soundboard.StopAll();
        foreach (var sound in Sounds)
        {
            sound.IsPlaying = false;
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        // Stopping the engine (or losing a device) stops every sound with it.
        if (!value)
        {
            foreach (var sound in Sounds)
            {
                sound.IsPlaying = false;
            }
        }
    }

    private void AddSoundItem(SoundItemViewModel sound)
    {
        sound.Changed += OnSoundChanged;
        Sounds.Add(sound);
        sound.Number = Sounds.Count;
    }

    /// <summary>Moves a sound to a new position (its hotkey number follows the position).</summary>
    public void MoveSound(SoundItemViewModel sound, int newIndex)
    {
        int oldIndex = Sounds.IndexOf(sound);
        newIndex = Math.Clamp(newIndex, 0, Sounds.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Sounds.Move(oldIndex, newIndex);
        RenumberSounds();
        SavePersistedSettings();
        StatusMessage = $"\"{sound.Name}\" is now sound {sound.Number}.";
    }

    [RelayCommand]
    private void MoveSoundUp(SoundItemViewModel? sound)
    {
        if (sound != null)
        {
            MoveSound(sound, Sounds.IndexOf(sound) - 1);
        }
    }

    [RelayCommand]
    private void MoveSoundDown(SoundItemViewModel? sound)
    {
        if (sound != null)
        {
            MoveSound(sound, Sounds.IndexOf(sound) + 1);
        }
    }

    private void RenumberSounds()
    {
        for (int i = 0; i < Sounds.Count; i++)
        {
            Sounds[i].Number = i + 1;
        }
    }

    private void OnSoundChanged(object? sender, EventArgs e) => SavePersistedSettings();

    private void OnSoundEnded(object? sender, Guid id)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var sound = Sounds.FirstOrDefault(s => s.Id == id);
            if (sound != null)
            {
                sound.IsPlaying = false;
            }
        });
    }

    /// <summary>One-line summary of the current hotkeys for the Settings page.</summary>
    public string HotkeySummary
    {
        get
        {
            var parts = Hotkeys
                .Where(kv => kv.Value != null && !HotkeyGesture.IsSoundAction(kv.Key))
                .Select(kv => $"{HotkeyGesture.DisplayName(kv.Key)}: {kv.Value}")
                .ToList();
            var firstPreset = new HotkeyGesture(HotkeyGesture.PresetNumberModifiers, System.Windows.Input.Key.D1);
            var lastPreset = new HotkeyGesture(HotkeyGesture.PresetNumberModifiers, System.Windows.Input.Key.D9);
            parts.Add($"Preset 1–9: {firstPreset} … {lastPreset}");

            if (SoundHotkeysEnabled)
            {
                parts.Add($"Sounds 1–9: {Hotkeys[HotkeyAction.Sound1]?.ToString() ?? "–"} … {Hotkeys[HotkeyAction.Sound9]?.ToString() ?? "–"}");
                parts.Add($"Stop all sounds: {Hotkeys[HotkeyAction.StopAllSounds]?.ToString() ?? "–"}");
            }

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>The user's current global hotkeys (null = disabled for that action).</summary>
    public Dictionary<HotkeyAction, HotkeyGesture?> Hotkeys { get; private set; }

    /// <summary>Raised after the user edits the hotkeys, so the window can re-register them.</summary>
    public event EventHandler? HotkeysChanged;

    /// <summary>
    /// Raised with true while the Hotkeys dialog is open (so the window can
    /// release every global hotkey and the dialog can capture those keys),
    /// then with false when it closes.
    /// </summary>
    public event EventHandler<bool>? HotkeyEditingChanged;

    [ObservableProperty]
    private float inputLevel;

    [ObservableProperty]
    private float outputLevel;

    [ObservableProperty]
    private bool virtualCableDetected;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isAutoTuning;

    [ObservableProperty]
    private bool canRevertAutoTune;

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private double pitchSemitones;

    [ObservableProperty]
    private double micCrunch;

    [ObservableProperty]
    private double inputGainDb;

    public static VoiceEffectType[] VoiceEffects { get; } = Enum.GetValues<VoiceEffectType>();

    [ObservableProperty]
    private VoiceEffectType voiceEffect;

    partial void OnVoiceEffectChanged(VoiceEffectType value) => _engine.VoiceEffect = value;

    [ObservableProperty]
    private double gate;

    [ObservableProperty]
    private double loudness;

    [ObservableProperty]
    private bool noiseRemoval;

    [ObservableProperty]
    private double noiseRemovalStrength = 1.0;

    partial void OnStartWithWindowsChanged(bool value)
    {
        StartupRegistration.SetEnabled(value);
    }

    partial void OnPitchSemitonesChanged(double value)
    {
        _engine.PitchRatio = Math.Pow(2, value / 12.0);
    }

    partial void OnMicCrunchChanged(double value)
    {
        _engine.MicCrunchAmount = value;
    }

    partial void OnInputGainDbChanged(double value) => _engine.Dynamics.InputGainDb = value;

    partial void OnGateChanged(double value)
    {
        _engine.Dynamics.Gate = value;
        OnPropertyChanged(nameof(GateDisplay));
    }

    partial void OnLoudnessChanged(double value) => _engine.Dynamics.Loudness = value;

    partial void OnNoiseRemovalChanged(bool value) => _engine.NoiseRemovalEnabled = value;

    partial void OnNoiseRemovalStrengthChanged(double value) => _engine.NoiseRemovalStrength = value;

    public string GateDisplay => Gate <= 0 ? "Off" : $"{DynamicsSettings.GateThresholdDb(Gate):0} dB";

    partial void OnBypassChanged(bool value)
    {
        _engine.Bypass = value;
    }

    partial void OnIsMutedChanged(bool value)
    {
        _engine.Muted = value;
    }

    public void ExecuteHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleBypass:
                Bypass = !Bypass;
                StatusMessage = Bypass ? "Bypass on (hotkey) — you're sending your unprocessed mic." : "Bypass off (hotkey).";
                break;
            case HotkeyAction.ToggleMute:
                IsMuted = !IsMuted;
                StatusMessage = IsMuted ? "Muted (hotkey)." : "Unmuted (hotkey).";
                break;
            case HotkeyAction.NextPreset:
                StepPreset(+1);
                break;
            case HotkeyAction.PreviousPreset:
                StepPreset(-1);
                break;
            case HotkeyAction.StopAllSounds:
                StopAllSounds();
                break;
            default:
                if (HotkeyGesture.SoundNumber(action) is int number)
                {
                    ToggleSoundByNumber(number);
                }

                break;
        }
    }

    /// <summary>Selects the Nth preset in the list (1-based, not counting "+ New preset...").</summary>
    public void SelectPresetByNumber(int number)
    {
        var presets = Presets.Where(p => p != NewPresetItem).ToList();
        if (number < 1 || number > presets.Count)
        {
            return;
        }

        SelectedPreset = presets[number - 1];
        StatusMessage = $"Preset: {SelectedPreset.Name} (hotkey).";
    }

    private void StepPreset(int delta)
    {
        var presets = Presets.Where(p => p != NewPresetItem).ToList();
        if (presets.Count == 0)
        {
            return;
        }

        int index = SelectedPreset == null ? -1 : presets.IndexOf(SelectedPreset);
        int next = ((index + delta) % presets.Count + presets.Count) % presets.Count;
        SelectedPreset = presets[next];
        StatusMessage = $"Preset: {SelectedPreset.Name} (hotkey).";
    }

    [RelayCommand]
    private void EditHotkeys()
    {
        var dialog = new HotkeysDialog(Hotkeys)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        // While a key is registered globally, Windows delivers it to the
        // hotkey instead of the dialog, so pause them all while editing.
        HotkeyEditingChanged?.Invoke(this, true);
        bool saved;
        try
        {
            saved = dialog.ShowDialog() == true;
        }
        finally
        {
            HotkeyEditingChanged?.Invoke(this, false);
        }

        if (!saved)
        {
            return;
        }

        Hotkeys = dialog.Result;
        OnPropertyChanged(nameof(HotkeySummary));
        SavePersistedSettings();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedCaptureDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnSelectedRenderDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnMonitorEnabledChanged(bool value)
    {
        _engine.MonitorVoiceEnabled = value;
        SavePersistedSettings();
        UpdateMonitorPlayback();
    }

    private void UpdateMonitorPlayback()
    {
        if (IsRunning && !_engine.UpdateMonitorPlayback())
        {
            StatusMessage = "No headphones were selected when you started — stop and start again to hear yourself or the sounds.";
        }
    }

    private void SavePersistedSettings()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        AppSettingsStore.Save(new AppSettings
        {
            CaptureDeviceId = SelectedCaptureDevice?.Id,
            RenderDeviceId = SelectedRenderDevice?.Id,
            MonitorDeviceId = SelectedMonitorDevice?.Id,
            MonitorEnabled = MonitorEnabled,
            PresetName = SelectedPreset?.Name,
            Hotkeys = HotkeyGesture.ToSettings(Hotkeys),
            Sounds = Sounds.Select(s => new SavedSound { Path = s.Path, Volume = s.Volume, Loop = s.Loop }).ToList(),
            SoundVolume = SoundVolume,
            DuckSounds = DuckSounds,
            HearSounds = HearSounds,
            SoundHotkeysEnabled = SoundHotkeysEnabled,
            Theme = SelectedTheme.Name,
        });
    }

    partial void OnSelectedPresetChanged(EqPreset? oldValue, EqPreset? newValue)
    {
        if (newValue == NewPresetItem)
        {
            // Put the previous selection back first (deferred, since the
            // ComboBox is still mid-change), then ask for the new preset.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                SelectedPreset = oldValue;
                NewPreset();
            });
            return;
        }

        NotifyPresetActionsChanged();

        if (newValue == null)
        {
            return;
        }

        ApplyPreset(newValue);
        SavePersistedSettings();
    }

    private void ApplyPreset(EqPreset preset)
    {
        for (int i = 0; i < Bands.Count && i < preset.Bands.Count; i++)
        {
            Bands[i].GainDb = preset.Bands[i].GainDb;
            Bands[i].Q = preset.Bands[i].Q;
        }

        PitchSemitones = preset.PitchSemitones;
        MicCrunch = preset.MicCrunch;
        InputGainDb = preset.InputGainDb;
        Gate = preset.Gate;
        Loudness = preset.Loudness;
        NoiseRemoval = preset.NoiseRemoval;
        NoiseRemovalStrength = preset.NoiseRemovalStrength;
        VoiceEffect = preset.VoiceEffect;
    }

    [RelayCommand]
    private void ToggleStartStop()
    {
        if (IsRunning)
        {
            _autoTuneCts?.Cancel();
            _engine.Stop();
            IsRunning = false;
            StatusMessage = "Stopped.";
            return;
        }

        if (SelectedCaptureDevice == null || SelectedRenderDevice == null)
        {
            StatusMessage = "Select a microphone and an output device first.";
            return;
        }

        if (MonitorEnabled && SelectedMonitorDevice == null)
        {
            StatusMessage = "Select a device to hear yourself through, or turn off \"Hear yourself\".";
            return;
        }

        try
        {
            var bandModels = Bands.Select(b => b.Band).ToList();
            _engine.Bypass = Bypass;
            _engine.Start(SelectedCaptureDevice.Device, SelectedRenderDevice.Device, bandModels, SelectedMonitorDevice?.Device, MonitorEnabled);
            IsRunning = true;
            StatusMessage = VirtualCableDetected
                ? "Running. In Discord/OBS/Zoom, select \"CABLE Output\" as your microphone."
                : "Running, but VB-Audio Virtual Cable wasn't detected as the output — other apps won't see this mic.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        CaptureDevices.Clear();
        foreach (var device in DeviceManager.GetCaptureDevices())
        {
            CaptureDevices.Add(device);
        }

        RenderDevices.Clear();
        foreach (var device in DeviceManager.GetRenderDevices())
        {
            RenderDevices.Add(device);
        }

        var vbCable = DeviceManager.FindVirtualCableInput(RenderDevices);
        VirtualCableDetected = vbCable != null;
        if (vbCable != null)
        {
            SelectedRenderDevice = vbCable;
        }
    }

    [RelayCommand]
    private void OpenVirtualCableDownloadPage()
    {
        Process.Start(new ProcessStartInfo(VirtualCableDownloadUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task ToggleAutoTuneAsync()
    {
        if (IsAutoTuning)
        {
            _autoTuneCts?.Cancel();
            return;
        }

        if (!IsRunning)
        {
            StatusMessage = "Start the mic first, then try Auto-Tune Voice.";
            return;
        }

        _preAutoTuneSnapshot = Bands.Select(b => b.GainDb).ToArray();
        CanRevertAutoTune = false;
        IsAutoTuning = true;
        _autoTuneCts = new CancellationTokenSource();

        var progress = new Progress<string>(message => StatusMessage = message);

        try
        {
            var result = await AutoEqTuner.RunAsync(_engine, Bands, progress, _autoTuneCts.Token);
            switch (result)
            {
                case AutoEqTunerResult.Cancelled:
                    RestoreAutoTuneSnapshot();
                    StatusMessage = "Auto-tune cancelled; reverted.";
                    break;
                case AutoEqTunerResult.Converged:
                    CanRevertAutoTune = true;
                    StatusMessage = "Auto-tune converged. Keep these settings, or Revert.";
                    break;
                case AutoEqTunerResult.ReachedMaxIterations:
                    CanRevertAutoTune = true;
                    StatusMessage = "Auto-tune finished (best effort). Keep these settings, or Revert.";
                    break;
            }
        }
        catch (Exception ex)
        {
            RestoreAutoTuneSnapshot();
            StatusMessage = $"Auto-tune failed: {ex.Message}";
        }
        finally
        {
            IsAutoTuning = false;
            _autoTuneCts?.Dispose();
            _autoTuneCts = null;
        }
    }

    [RelayCommand]
    private void KeepAutoTune()
    {
        CanRevertAutoTune = false;

        var preset = PromptAndSaveCurrentAsPreset(initialName: "", cancelText: "Don't Save");
        StatusMessage = preset != null
            ? $"Saved \"{preset.Name}\" as a preset and kept these settings."
            : "Kept auto-tuned settings.";
    }

    /// <summary>
    /// Creates a preset of the user's own from the current settings, so it
    /// can be tweaked further and stored with Save.
    /// </summary>
    private void NewPreset()
    {
        var preset = PromptAndSaveCurrentAsPreset(initialName: "", cancelText: "Cancel");
        if (preset != null)
        {
            StatusMessage = $"Created \"{preset.Name}\" from the current settings. Adjust it, then click Save.";
        }
    }

    /// <summary>
    /// Stores the current EQ, pitch and crunch into the selected preset.
    /// </summary>
    [RelayCommand]
    private void SavePreset()
    {
        var selected = SelectedPreset;
        if (selected == null || selected == NewPresetItem)
        {
            NewPreset();
            return;
        }

        StoreUserPreset(CreatePresetFromCurrentSettings(selected.Name));
        StatusMessage = $"Saved changes to \"{selected.Name}\".";
    }

    /// <summary>
    /// Deletes the selected user preset, or — for a built-in preset the user
    /// has edited — resets it to its original settings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        var preset = SelectedPreset;
        if (preset == null || !CanDeletePreset())
        {
            return;
        }

        var builtIn = PresetStore.GetBuiltInPreset(preset.Name);
        string question = builtIn != null
            ? $"Reset \"{preset.Name}\" to its original settings?"
            : $"Delete the preset \"{preset.Name}\"?";

        var answer = System.Windows.MessageBox.Show(
            question,
            builtIn != null ? "Reset Preset" : "Delete Preset",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        PresetStore.DeleteUserPreset(preset.Name);

        if (builtIn != null)
        {
            CopySettings(builtIn, preset);
            ApplyPreset(preset);
            NotifyPresetActionsChanged();
            StatusMessage = $"Reset \"{preset.Name}\" to its original settings.";
        }
        else
        {
            Presets.Remove(preset);
            SelectedPreset = Presets.FirstOrDefault(p => p != NewPresetItem);
            StatusMessage = $"Deleted \"{preset.Name}\".";
        }
    }

    /// <summary>
    /// Saves the current settings (under the selected preset's name) to a
    /// .eqpreset file that can be shared and imported elsewhere.
    /// </summary>
    [RelayCommand]
    private void ExportPreset()
    {
        var selected = SelectedPreset;
        if (selected == null || selected == NewPresetItem)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export preset",
            Filter = PresetFile.DialogFilter,
            DefaultExt = PresetFile.Extension,
            FileName = PresetFile.SuggestedFileName(selected.Name),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            PresetFile.Write(dialog.FileName, CreatePresetFromCurrentSettings(selected.Name));
            StatusMessage = $"Exported \"{selected.Name}\" to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't export the preset: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports one or more .eqpreset files as user presets. Names that are
    /// already taken get a " (2)" style suffix rather than overwriting.
    /// </summary>
    [RelayCommand]
    private void ImportPresets()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import presets",
            Filter = PresetFile.DialogFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var imported = new List<string>();
        var failed = new List<string>();
        EqPreset? last = null;

        foreach (string path in dialog.FileNames)
        {
            try
            {
                foreach (var preset in PresetFile.Read(path))
                {
                    var existingNames = Presets.Where(p => p != NewPresetItem).Select(p => p.Name).Append(NewPresetItem.Name);
                    preset.Name = PresetFile.MakeUniqueName(preset.Name, existingNames);
                    PresetStore.SaveUserPreset(preset);
                    Presets.Add(preset);
                    imported.Add(preset.Name);
                    last = preset;
                }
            }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException)
            {
                failed.Add($"{System.IO.Path.GetFileName(path)} ({ex.Message})");
            }
        }

        if (last != null)
        {
            SelectedPreset = last;
        }

        StatusMessage = (imported.Count, failed.Count) switch
        {
            (> 0, 0) => $"Imported {string.Join(", ", imported.Select(n => $"\"{n}\""))}.",
            (> 0, _) => $"Imported {imported.Count} preset(s). Couldn't import: {string.Join("; ", failed)}",
            _ => $"Couldn't import: {string.Join("; ", failed)}",
        };
    }

    public bool IsUserPresetSelected =>
        SelectedPreset != null && SelectedPreset != NewPresetItem && !PresetStore.IsBuiltInName(SelectedPreset.Name);

    public string DeleteButtonText =>
        SelectedPreset != null && PresetStore.IsBuiltInName(SelectedPreset.Name) ? "Reset" : "Delete";

    private bool CanDeletePreset() =>
        IsUserPresetSelected
        || (SelectedPreset != null && PresetStore.IsBuiltInName(SelectedPreset.Name) && PresetStore.HasUserVersion(SelectedPreset.Name));

    private string? ValidateNewPresetName(string name)
    {
        if (name == NewPresetItem.Name)
        {
            return "Please pick a different name.";
        }

        return Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ? $"A preset called \"{name}\" already exists. Select it and click Save to update it, or pick a different name."
            : null;
    }

    private EqPreset? PromptAndSaveCurrentAsPreset(string initialName, string cancelText)
    {
        var dialog = new NamePresetDialog(initialName: initialName, cancelText: cancelText, validate: ValidateNewPresetName)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var preset = CreatePresetFromCurrentSettings(dialog.PresetName);
        StoreUserPreset(preset);
        return preset;
    }

    private EqPreset CreatePresetFromCurrentSettings(string name)
    {
        var bandSettings = Bands.Select(b => new EqBandSettings
        {
            FrequencyHz = b.FrequencyHz,
            GainDb = b.GainDb,
            Q = b.Q,
            FilterType = b.Band.FilterType,
        }).ToList();

        return new EqPreset(name, bandSettings)
        {
            PitchSemitones = PitchSemitones,
            MicCrunch = MicCrunch,
            InputGainDb = InputGainDb,
            Gate = Gate,
            Loudness = Loudness,
            NoiseRemoval = NoiseRemoval,
            NoiseRemovalStrength = NoiseRemovalStrength,
            VoiceEffect = VoiceEffect,
        };
    }

    private void StoreUserPreset(EqPreset preset)
    {
        PresetStore.SaveUserPreset(preset);

        // Update an existing entry in place (rather than replacing it in the
        // collection) so the ComboBox selection and list position stay put.
        var existing = Presets.FirstOrDefault(p => p != NewPresetItem && p.Name == preset.Name);
        if (existing != null)
        {
            CopySettings(preset, existing);
            SelectedPreset = existing;
            NotifyPresetActionsChanged();
            return;
        }

        Presets.Add(preset);
        SelectedPreset = preset;
    }

    private static void CopySettings(EqPreset from, EqPreset to)
    {
        to.Bands = from.Bands.Select(b => new EqBandSettings
        {
            FrequencyHz = b.FrequencyHz,
            GainDb = b.GainDb,
            Q = b.Q,
            FilterType = b.FilterType,
        }).ToList();
        to.PitchSemitones = from.PitchSemitones;
        to.MicCrunch = from.MicCrunch;
        to.InputGainDb = from.InputGainDb;
        to.Gate = from.Gate;
        to.Loudness = from.Loudness;
        to.NoiseRemoval = from.NoiseRemoval;
        to.NoiseRemovalStrength = from.NoiseRemovalStrength;
        to.VoiceEffect = from.VoiceEffect;
    }

    private void NotifyPresetActionsChanged()
    {
        OnPropertyChanged(nameof(IsUserPresetSelected));
        OnPropertyChanged(nameof(DeleteButtonText));
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RevertAutoTune()
    {
        RestoreAutoTuneSnapshot();
        CanRevertAutoTune = false;
        StatusMessage = "Reverted to the settings from before Auto-Tune.";
    }

    private void RestoreAutoTuneSnapshot()
    {
        if (_preAutoTuneSnapshot == null)
        {
            return;
        }

        for (int i = 0; i < Bands.Count && i < _preAutoTuneSnapshot.Length; i++)
        {
            Bands[i].GainDb = _preAutoTuneSnapshot[i];
        }
    }

    public void Dispose()
    {
        _autoTuneCts?.Cancel();
        _engine.DeviceStoppedUnexpectedly -= OnEngineDeviceStoppedUnexpectedly;
        _meterTimer.Stop();
        _engine.Dispose();
    }
}
