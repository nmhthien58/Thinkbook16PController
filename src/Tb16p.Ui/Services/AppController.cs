using System.Diagnostics;
using System.Windows;
using Tb16p.Core.Config;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Tb16p.Ui.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Tb16p.Ui.Services;

/// <summary>
/// Owns the running pieces: the tuning engine, the sensor logger and monitor, the tray icon
/// and the main window. Settings that apply at once (everything outside the profile editor)
/// go through <see cref="UpdateSettings"/>.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly TrayIcon _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _watchingTheme;

    public AppController(AppSettings settings)
    {
        Engine = new TuningEngine(settings);
        SensorLogger = new SensorLogger(Engine);
        Sensors = new SensorMonitor();
        Engine.StateChanged += () => Application.Current.Dispatcher.BeginInvoke(() => StateChanged?.Invoke());

        Header = new HeaderViewModel(this);
        General = new GeneralViewModel(this);
        Profiles = new ProfilesViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        _tray = new TrayIcon(this);
        Loc.Instance.LanguageChanged += () => StateChanged?.Invoke();
    }

    public static AppController Current { get; set; } = null!;

    public TuningEngine Engine { get; }
    public SensorLogger SensorLogger { get; }
    public SensorMonitor Sensors { get; }

    public HeaderViewModel Header { get; }
    public GeneralViewModel General { get; }
    public ProfilesViewModel Profiles { get; }
    public SettingsViewModel SettingsPage { get; }

    /// <summary>Engine state changed (power source, errors, fans, pause); raised on the UI thread.</summary>
    public event Action? StateChanged;

    /// <summary>The live settings were replaced; raised on the UI thread.</summary>
    public event Action? SettingsChanged;

    public AppSettings Settings => Engine.Settings;

    /// <summary>Changes a copy of the live settings, saves and applies it. Returns false (after telling the user) on failure.</summary>
    public bool UpdateSettings(Action<AppSettings> change)
    {
        var updated = Engine.Settings.Clone();
        change(updated);
        if (!Commit(updated, out var error))
        {
            _ = Dialogs.AlertAsync(Loc.F("Dlg_SaveFailed", error));
            return false;
        }
        return true;
    }

    /// <summary>Validates, saves and applies a full settings object.</summary>
    public bool Commit(AppSettings settings, out string? error)
    {
        try
        {
            settings.Validate();
            settings.Save();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        Engine.ReplaceSettings(settings.Clone());
        error = null;
        SettingsChanged?.Invoke();
        return true;
    }

    public void ApplyTheme(AppTheme theme)
    {
        var applied = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };
        ApplicationThemeManager.Apply(applied, WindowBackdropType.Mica, true);
        // The watcher only accepts a loaded window; MainWindow calls this again from Loaded.
        if (_window is not { IsLoaded: true })
            return;
        // Following Windows only makes sense for "System"; a fixed choice must not be overridden.
        var watch = theme == AppTheme.System;
        if (watch == _watchingTheme)
            return;
        if (watch)
            SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
        else
            SystemThemeWatcher.UnWatch(_window);
        _watchingTheme = watch;
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow(this);
            Application.Current.MainWindow = _window;
            ApplyTheme(Settings.Theme);
            _window.Loaded += (_, _) => ApplyTheme(Settings.Theme);
        }
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Called when the window closes; it is hidden instead when "close to tray" is on.</summary>
    public bool ShouldHideOnClose() => !_exiting && Settings.CloseToTray;

    public bool IsExiting => _exiting;

    public void OnWindowHidden() => Sensors.Running = false;
    public void OnWindowShown() => Sensors.Running = true;

    /// <summary>Exits the app, offering to save unsaved profile edits first.</summary>
    public async Task ExitAsync()
    {
        if (_exiting)
            return;
        if (Profiles.IsDirty)
        {
            ShowMainWindow();
            switch (await Dialogs.AskSaveAsync(Loc.T("Dlg_UnsavedExit")))
            {
                case Choice.Cancel:
                    return;
                case Choice.Primary when !await Profiles.Save():
                    return;
            }
        }
        _exiting = true;
        Log.Info("Exit");
        _window?.Close();
        Dispose();
        Application.Current.Shutdown();
    }

    public static void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(AppSettings.Directory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Directory}\"") { UseShellExecute = true });
    }

    public static void OpenLog()
    {
        if (System.IO.File.Exists(Log.FilePath))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Log.FilePath}\"") { UseShellExecute = true });
    }

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void Dispose()
    {
        _tray.Dispose();
        Sensors.Dispose();
        SensorLogger.Dispose();
        Engine.Dispose();
    }
}
