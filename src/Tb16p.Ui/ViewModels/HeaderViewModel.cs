using CommunityToolkit.Mvvm.ComponentModel;
using Tb16p.Core.Hardware;
using Tb16p.Core.Tuning;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui.ViewModels;

/// <summary>Status strip shown above every page.</summary>
public sealed partial class HeaderViewModel : ObservableObject
{
    private readonly AppController _app;

    [ObservableProperty] private string _powerText = "";
    [ObservableProperty] private bool _onBattery;
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _fanText = "";
    [ObservableProperty] private bool _fanMax;
    [ObservableProperty] private bool _sensorsAvailable = true;
    [ObservableProperty] private string _cpu = "–";
    [ObservableProperty] private string _cb1 = "–";
    [ObservableProperty] private string _cb5 = "–";
    [ObservableProperty] private string _fan1 = "–";
    [ObservableProperty] private string _fan2 = "–";

    public HeaderViewModel(AppController app)
    {
        _app = app;
        app.StateChanged += Refresh;
        app.SettingsChanged += Refresh;
        app.Sensors.Updated += RefreshSensors;
        Refresh();
    }

    private void Refresh()
    {
        var engine = _app.Engine;
        OnBattery = engine.PowerLine == PowerLine.Battery;
        PowerText = Loc.T(OnBattery ? "Power_Battery" : "Power_Ac");
        ProfileName = engine.ActiveProfile?.Name ?? "?";
        IsPaused = engine.Paused;
        IsError = !engine.Paused && engine.LastError is not null;
        StatusText = engine.Paused ? Loc.T("Status_Paused")
            : engine.LastError is { } error ? Loc.F("Status_Error", error)
            : engine.LastApplied is { } at ? Loc.F("Status_AppliedAt", at)
            : Loc.T("Status_Starting");
        FanMax = engine.FanMax && !engine.Paused;
        FanText = FanMax ? Loc.F("Fan_Max", FanCurve.MaxRpm)
            : engine.FanTargetRpm is { } rpm ? Loc.F("Fan_Curve", rpm)
            : Loc.T("Fan_Ec");
    }

    private void RefreshSensors()
    {
        var r = _app.Sensors.Latest;
        SensorsAvailable = r is not null;
        Cpu = Show(r?.CpuC, " °C");
        Cb1 = Show(r?.Sensor1C, " °C");
        Cb5 = Show(r?.Sensor5C, " °C");
        Fan1 = Show(r?.Fan1Rpm, " rpm");
        Fan2 = Show(r?.Fan2Rpm, " rpm");

        static string Show(int? value, string unit) => value is { } v ? $"{v}{unit}" : "–";
    }
}
