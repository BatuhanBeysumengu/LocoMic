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

        using (var icon = AppIconFactory.CreateIcon())
        {
            Icon = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

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
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null)
        {
            return;
        }

        _hotkeys.UnregisterAll();
        var failed = new List<string>();

        foreach (var (action, gesture) in _viewModel.Hotkeys)
        {
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

    /// <summary>
    /// Sidebar navigation: each nav RadioButton carries the x:Name of its page
    /// in CommandParameter (Tag holds its icon glyph); show that page only.
    /// </summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Fires once during InitializeComponent for the default (Home) item,
        // before the pages exist; Home is already the visible page then.
        if (PagesHost == null || sender is not System.Windows.Controls.RadioButton { CommandParameter: string pageName })
        {
            return;
        }

        foreach (UIElement page in PagesHost.Children)
        {
            page.Visibility = page is FrameworkElement element && element.Name == pageName
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsSidebarExpanded = !_viewModel.IsSidebarExpanded;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
