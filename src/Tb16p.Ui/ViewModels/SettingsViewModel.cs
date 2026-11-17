using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tb16p.Core.Config;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui.ViewModels;

/// <summary>A combo box entry whose text follows the UI language.</summary>
public sealed class LocalizedOption<T> : ObservableObject
{
    private readonly string _key;

    public LocalizedOption(T value, string key)
    {
        Value = value;
        _key = key;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Title));
    }

    public T Value { get; }
    public string Title => Loc.T(_key);
    public override string ToString() => Title;
}

public sealed record LanguageOption(string Code, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Settings page: language, theme, startup and window behaviour, re-apply timers, logging.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppController _app;
    private bool _loading;

    [ObservableProperty] private LanguageOption? _language;
    [ObservableProperty] private LocalizedOption<AppTheme>? _theme;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private double? _reapplyLimitsSeconds;
    [ObservableProperty] private double? _reapplyCurveOptimizerSeconds;
    [ObservableProperty] private bool _sensorLog;

    public SettingsViewModel(AppController app)
    {
        _app = app;
        Themes =
        [
            new(AppTheme.System, "Theme_System"),
            new(AppTheme.Light, "Theme_Light"),
            new(AppTheme.Dark, "Theme_Dark"),
        ];
        app.SettingsChanged += Load;
        Load();
        _loading = true;
        try { StartWithWindows = StartupTask.IsEnabled(); }
        catch { StartWithWindows = false; }
        _loading = false;
    }

    public IReadOnlyList<LanguageOption> Languages { get; } = [new("en", "English"), new("vi", "Tiếng Việt")];
    public IReadOnlyList<LocalizedOption<AppTheme>> Themes { get; }
    public string DataFolder => AppSettings.Directory;

    private void Load()
    {
        var s = _app.Settings;
        _loading = true;
        try
        {
            Language = Languages.FirstOrDefault(l => l.Code == s.Language) ?? Languages[0];
            Theme = Themes.First(t => t.Value == s.Theme);
            CloseToTray = s.CloseToTray;
            ReapplyLimitsSeconds = s.ReapplyLimitsSeconds;
            ReapplyCurveOptimizerSeconds = s.ReapplyCurveOptimizerSeconds;
            SensorLog = s.SensorLogEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnLanguageChanged(LanguageOption? value)
    {
        if (_loading || value is null)
            return;
        Loc.Instance.Language = value.Code;
        _app.UpdateSettings(s => s.Language = value.Code);
    }

    partial void OnThemeChanged(LocalizedOption<AppTheme>? value)
    {
        if (_loading || value is null)
            return;
        _app.ApplyTheme(value.Value);
        _app.UpdateSettings(s => s.Theme = value.Value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loading)
            _app.UpdateSettings(s => s.CloseToTray = value);
    }

    partial void OnSensorLogChanged(bool value)
    {
        if (_loading)
            return;
        Log.Info($"Sensor log: {value}");
        _app.UpdateSettings(s => s.SensorLogEnabled = value);
    }

    partial void OnReapplyLimitsSecondsChanged(double? value)
    {
        if (!_loading && value is { } v)
            _app.UpdateSettings(s => s.ReapplyLimitsSeconds = (int)Math.Round(v));
    }

    partial void OnReapplyCurveOptimizerSecondsChanged(double? value)
    {
        if (!_loading && value is { } v)
            _app.UpdateSettings(s => s.ReapplyCurveOptimizerSeconds = (int)Math.Round(v));
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
            return;
        try
        {
            if (value) StartupTask.Enable(); else StartupTask.Disable();
            Log.Info($"Start with Windows: {value}");
        }
        catch (Exception ex)
        {
            _ = Dialogs.AlertAsync(Loc.F("Dlg_StartupFailed", ex.Message));
            _loading = true;
            StartWithWindows = !value;
            _loading = false;
        }
    }

    [RelayCommand]
    private static void OpenDataFolder() => AppController.OpenDataFolder();

    [RelayCommand]
    private static void OpenLog() => AppController.OpenLog();
}
