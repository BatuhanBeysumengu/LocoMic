using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace EqualizerHelper.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    // Running more than one instance would have them fight over the same
    // mic/output devices, and both independently read-modify-write the same
    // presets.json/settings.json files, silently losing each other's changes.
    private const string SingleInstanceMutexName = "EqualizerHelper-SingleInstance-7F3D1A2B";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Equalizer Helper is already running. Check your system tray for its icon.",
                "Equalizer Helper",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

