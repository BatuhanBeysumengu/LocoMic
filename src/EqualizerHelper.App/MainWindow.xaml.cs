using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using EqualizerHelper.App.Services;
using EqualizerHelper.App.ViewModels;

namespace EqualizerHelper.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly TrayIconService _trayIcon;
    private GlobalHotkeyService? _hotkeys;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Window/taskbar icon from the multi-size app icon (also used for the tray).
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/EqualizerHelper;component/Assets/app.ico"));

        _trayIcon = new TrayIconService(_viewModel);
        _trayIcon.ShowRequested += (_, _) => RestoreFromTray();
        _trayIcon.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            Close();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        DarkTitleBar.Apply(this);

        _hotkeys = new GlobalHotkeyService(hwnd);
        RegisterHotkeys();
        _viewModel.HotkeysChanged += (_, _) => RegisterHotkeys();
        _viewModel.HotkeyEditingChanged += (_, editing) =>
        {
            if (editing)
            {
                _hotkeys?.UnregisterAll();
            }
            else
            {
                RegisterHotkeys();
            }
        };

        // Sound hotkeys (plain numpad keys by default) are only held while the
        // engine runs, so the numpad types normally in other apps otherwise.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsRunning))
            {
                RegisterHotkeys();
            }
        };
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null)
        {
            return;
        }

        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        bool soundKeysActive = _viewModel.IsRunning && _viewModel.SoundHotkeysEnabled;
        foreach (var (action, gesture) in _viewModel.Hotkeys)
        {
            if (HotkeyGesture.IsSoundAction(action) && !soundKeysActive)
            {
                continue;
            }

            if (gesture != null && !_hotkeys.Register(gesture, () => _viewModel.ExecuteHotkey(action)))
            {
                failed.Add(gesture.ToString());
            }
        }

        for (int number = 1; number <= 9; number++)
        {
            int presetNumber = number;
            var gesture = new HotkeyGesture(HotkeyGesture.PresetNumberModifiers, Key.D0 + number);
            if (!_hotkeys.Register(gesture, () => _viewModel.SelectPresetByNumber(presetNumber)))
            {
                failed.Add(gesture.ToString());
            }
        }

        if (failed.Count > 0)
        {
            _viewModel.StatusMessage =
                $"Some hotkeys are already used by another app and won't work: {string.Join(", ", failed)}. Change them under Hotkeys.";
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _hotkeys?.Dispose();
        _trayIcon.Dispose();
        _viewModel.Dispose();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Open the Home/Settings menu on a normal left click, under the button.
        if (MenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MenuButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ShowHome_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, string.Empty);

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, "· Settings");

    private void ShowPage(UIElement page, string pageName)
    {
        foreach (UIElement child in PagesHost.Children)
        {
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
        }

        PageNameText.Text = pageName;
    }

    // Soundboard drag-and-drop: drag a sound by its number or name onto
    // another row to take that row's position (and hotkey number).
    private System.Windows.Point _soundDragStart;
    private SoundItemViewModel? _soundDragItem;

    private void SoundDrag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _soundDragStart = e.GetPosition(this);
        _soundDragItem = (sender as FrameworkElement)?.DataContext as SoundItemViewModel;
    }

    private void SoundDrag_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_soundDragItem == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _soundDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _soundDragItem;
        _soundDragItem = null;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, item, System.Windows.DragDropEffects.Move);
    }

    private void SoundRow_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool isSound = e.Data.GetDataPresent(typeof(SoundItemViewModel));
        e.Effects = isSound ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        if (isSound && sender is FrameworkElement row)
        {
            row.Tag = "DropTarget";
        }

        e.Handled = true;
    }

    private void SoundRow_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement row)
        {
            row.Tag = null;
        }
    }

    private void SoundRow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SoundItemViewModel target } row)
        {
            return;
        }

        row.Tag = null;
        if (e.Data.GetData(typeof(SoundItemViewModel)) is SoundItemViewModel dragged)
        {
            _viewModel.MoveSound(dragged, _viewModel.Sounds.IndexOf(target));
        }
    }

    private void SoundMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItemViewModel sound)
        {
            _viewModel.MoveSoundUpCommand.Execute(sound);
        }
    }

    private void SoundMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoundItemViewModel sound)
        {
            _viewModel.MoveSoundDownCommand.Execute(sound);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
