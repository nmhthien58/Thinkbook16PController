using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tb16p.Core.Config;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui.ViewModels;

/// <summary>One of the four charge mode cards.</summary>
public sealed partial class ChargeOption : ObservableObject
{
    private readonly GeneralViewModel _owner;
    private readonly string _key;

    public ChargeOption(GeneralViewModel owner, ChargeMode? mode, string key)
    {
        _owner = owner;
        _key = key;
        Mode = mode;
        Loc.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
        };
    }

    public ChargeMode? Mode { get; }
    public string Title => Loc.T(_key);
    public string Description => Loc.T(_key + "_Desc");

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _owner.SelectChargeMode(Mode);
    }
}

public sealed record SummaryLine(string Label, string Value);

/// <summary>
/// General page: which profile runs on AC and on battery, the charge mode, quick actions and
/// a summary of the profile running now. Everything here applies at once.
/// </summary>
public sealed partial class GeneralViewModel : ObservableObject
{
    private readonly AppController _app;
    private bool _loading;

    [ObservableProperty] private string? _acProfile;
    [ObservableProperty] private string? _batteryProfile;
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private bool _fanMax;
    [ObservableProperty] private string _activeProfileName = "";

    public GeneralViewModel(AppController app)
    {
        _app = app;
        ChargeOptions =
        [
            new(this, ChargeMode.Conservation, "Charge_Conservation"),
            new(this, ChargeMode.Normal, "Charge_Normal"),
            new(this, ChargeMode.RapidCharge, "Charge_Rapid"),
            new(this, null, "Charge_Unmanaged"),
        ];
        app.SettingsChanged += Load;
        app.StateChanged += RefreshState;
        Loc.Instance.LanguageChanged += RefreshState;
        Load();
    }

    public ObservableCollection<string> ProfileNames { get; } = [];
    public IReadOnlyList<ChargeOption> ChargeOptions { get; }
    public ObservableCollection<SummaryLine> Summary { get; } = [];
    public string FanMaxDescription => Loc.F("Action_FanMax_Desc", FanCurve.MaxRpm);

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _app.Settings;
            var names = settings.Profiles.Select(p => p.Name).ToList();
            if (!names.SequenceEqual(ProfileNames))
            {
                ProfileNames.Clear();
                foreach (var name in names)
                    ProfileNames.Add(name);
            }
            AcProfile = settings.Find(settings.AcProfile)?.Name;
            BatteryProfile = settings.Find(settings.BatteryProfile)?.Name;
            foreach (var option in ChargeOptions)
                option.IsSelected = option.Mode == settings.ChargeMode;
        }
        finally
        {
            _loading = false;
        }
        RefreshState();
    }

    private void RefreshState()
    {
        var engine = _app.Engine;
        _loading = true;
        Paused = engine.Paused;
        FanMax = engine.FanMax;
        _loading = false;
        OnPropertyChanged(nameof(FanMaxDescription));

        var p = engine.ActiveProfile;
        ActiveProfileName = p?.Name ?? "?";
        Summary.Clear();
        if (p is null)
            return;
        Summary.Add(new(Loc.T("Summary_Limits"), p.StapmW is null && p.FastW is null && p.SlowW is null
            ? Loc.T("Summary_Firmware")
            : Loc.F("Summary_LimitsValue", Watts(p.StapmW), Watts(p.FastW), Watts(p.SlowW))));
        Summary.Add(new(Loc.T("Summary_Tctl"), p.TctlC is { } t ? $"{t} °C" : Loc.T("Summary_Firmware")));
        var cores = new[] { p.CurveOptimizerCcd1, p.CurveOptimizerCcd2 }.Where(a => a is not null).SelectMany(a => a!).Count(o => o is not null);
        Summary.Add(new(Loc.T("Summary_Co"), cores > 0 ? Loc.F("Summary_CoOn", cores) : Loc.T("Summary_Off")));
        Summary.Add(new(Loc.T("Summary_Fan"), p.FanCurve is { } curve ? Loc.F("Summary_FanCurve", curve.Count) : Loc.T("Summary_FanEc")));

        string Watts(int? w) => w is { } v ? $"{v} W" : "–";
    }

    partial void OnAcProfileChanged(string? value)
    {
        if (!_loading && value is not null)
            _app.UpdateSettings(s => s.AcProfile = value);
    }

    partial void OnBatteryProfileChanged(string? value)
    {
        if (!_loading && value is not null)
            _app.UpdateSettings(s => s.BatteryProfile = value);
    }

    partial void OnPausedChanged(bool value)
    {
        if (!_loading)
            _app.Engine.SetPaused(value);
    }

    partial void OnFanMaxChanged(bool value)
    {
        if (!_loading)
            _app.Engine.SetFanMax(value);
    }

    internal void SelectChargeMode(ChargeMode? mode)
    {
        foreach (var option in ChargeOptions.Where(o => o.Mode != mode))
            option.IsSelected = false;
        if (!_loading && _app.Settings.ChargeMode != mode)
            _app.UpdateSettings(s => s.ChargeMode = mode);
    }

    [RelayCommand]
    private void Reapply() => _app.Engine.RequestFullApply();
}
