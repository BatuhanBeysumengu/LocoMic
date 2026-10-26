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

        Presets = new ObservableCollection<EqPreset>(PresetStore.GetBuiltInPresets().Concat(PresetStore.LoadUserPresets()));
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == savedSettings.PresetName) ?? Presets.FirstOrDefault();

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

    partial void OnBypassChanged(bool value)
    {
        _engine.Bypass = value;
    }

    partial void OnSelectedCaptureDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnSelectedRenderDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value) => SavePersistedSettings();

    partial void OnMonitorEnabledChanged(bool value)
    {
        SavePersistedSettings();

        if (IsRunning && !_engine.SetMonitorEnabled(value))
        {
            StatusMessage = "No monitor device was active when you started — stop and start again to enable live monitoring.";
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
        });
    }

    partial void OnSelectedPresetChanged(EqPreset? value)
    {
        if (value == null)
        {
            return;
        }

        for (int i = 0; i < Bands.Count && i < value.Bands.Count; i++)
        {
            Bands[i].GainDb = value.Bands[i].GainDb;
            Bands[i].Q = value.Bands[i].Q;
        }

        PitchSemitones = value.PitchSemitones;
        MicCrunch = value.MicCrunch;

        SavePersistedSettings();
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

        var dialog = new NamePresetDialog { Owner = System.Windows.Application.Current?.MainWindow };
        if (dialog.ShowDialog() == true)
        {
            var bandSettings = Bands.Select(b => new EqBandSettings
            {
                FrequencyHz = b.FrequencyHz,
                GainDb = b.GainDb,
                Q = b.Q,
                FilterType = b.Band.FilterType,
            }).ToList();

            var preset = new EqPreset(dialog.PresetName, bandSettings) { PitchSemitones = PitchSemitones, MicCrunch = MicCrunch };
            PresetStore.SaveUserPreset(preset);

            var existing = Presets.FirstOrDefault(p => p.Name == preset.Name);
            if (existing != null)
            {
                Presets.Remove(existing);
            }

            Presets.Add(preset);
            SelectedPreset = preset;

            StatusMessage = $"Saved \"{preset.Name}\" as a preset and kept these settings.";
        }
        else
        {
            StatusMessage = "Kept auto-tuned settings.";
        }
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
