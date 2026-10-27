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
    private const string SingleInstanceMutexName = "LocoMic-SingleInstance-7F3D1A2B";

    // A second launch signals this event so the running copy shows its window.
    private const string ShowWindowEventName = "LocoMic-ShowWindow-7F3D1A2B";
    private EventWaitHandle? _showWindowEvent;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Bring over presets/settings saved under the app's old name.
        Services.AppPaths.MigrateLegacyData();
        Services.ThemeService.Apply(Services.ThemeService.Find(Services.AppSettingsStore.Load().Theme));

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            // Already running: bring that copy to the front instead of starting
            // a second one (two copies would overwrite each other's settings).
            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var showExisting))
            {
                showExisting.Set();
                showExisting.Dispose();
            }

            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        var listener = new Thread(() =>
        {
            while (_showWindowEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(() => mainWindow.RestoreFromTray());
            }
        })
        {
            IsBackground = true,
            Name = "LocoMic show-window listener",
        };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A second copy that only showed "already running" never owned the
        // mutex; releasing it anyway throws and crashes the app on exit.
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

