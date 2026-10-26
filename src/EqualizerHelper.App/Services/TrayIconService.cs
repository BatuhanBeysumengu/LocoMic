using System;
using System.ComponentModel;
using System.Windows.Forms;
using EqualizerHelper.App.ViewModels;

namespace EqualizerHelper.App.Services;

/// <summary>
/// Owns the Windows system tray icon: a generated equalizer-bars glyph (no
/// binary .ico asset needed) plus a context menu for Show/Start-Stop/Exit.
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly ToolStripMenuItem _startStopItem;
    private readonly ToolStripMenuItem _muteItem;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        var menu = new ContextMenuStrip();

        var showItem = new ToolStripMenuItem("Show Equalizer Helper");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(showItem);

        _startStopItem = new ToolStripMenuItem("Start");
        _startStopItem.Click += (_, _) => _viewModel.ToggleStartStopCommand.Execute(null);
        menu.Items.Add(_startStopItem);

        _muteItem = new ToolStripMenuItem("Mute");
        _muteItem.Click += (_, _) => _viewModel.IsMuted = !_viewModel.IsMuted;
        menu.Items.Add(_muteItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIconFactory.CreateIcon(),
            Visible = true,
            ContextMenuStrip = menu,
            Text = "Equalizer Helper",
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateStatus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsRunning) or nameof(MainViewModel.IsMuted) or nameof(MainViewModel.SelectedPreset))
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        bool running = _viewModel.IsRunning;
        _startStopItem.Text = running ? "Stop" : "Start";
        _muteItem.Checked = _viewModel.IsMuted;

        // The tray tooltip doubles as feedback for hotkeys used while the window is hidden.
        string state = !running ? "Stopped" : _viewModel.IsMuted ? "Muted" : "Running";
        string text = $"Equalizer Helper — {state}";
        if (_viewModel.SelectedPreset != null)
        {
            text += $" — {_viewModel.SelectedPreset.Name}";
        }

        // NotifyIcon.Text is limited to 127 characters.
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
