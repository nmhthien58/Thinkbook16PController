using System.Windows;
using System.Windows.Threading;
using Tb16p.Core.Config;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui;

public partial class App : Application
{
    private Mutex? _single;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new Mutex(true, AppInfo.SingleInstanceMutex, out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        AppSettings settings;
        try
        {
            settings = AppSettings.Load();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Loc.F("Dlg_LoadFailed", AppSettings.FilePath, ex.Message), AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        Loc.Instance.Language = settings.Language;

        // A copy from before the rename has its own mutex; two copies would fight over the SMU and fans.
        if (LegacyAppRunning())
        {
            System.Windows.MessageBox.Show(Loc.T("Dlg_OldAppRunning"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        Log.Info($"Start {AppInfo.Name} {AppInfo.Version}, settings: {AppSettings.FilePath}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            StartupTask.RefreshIfEnabled();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not refresh the logon task: {ex.Message}");
        }

        _controller = new AppController(settings);
        AppController.Current = _controller;
        if (!e.Args.Contains(AppInfo.MinimizedArgument, StringComparer.OrdinalIgnoreCase))
            _controller.ShowMainWindow();
    }

    private static bool LegacyAppRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(AppInfo.LegacySingleInstanceMutex, out var legacy))
                return false;
            legacy.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // exists, owned by an elevated process
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep running: the engine works on its own timer and must keep the fans safe.
        Log.Error($"UI error: {e.Exception}");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
