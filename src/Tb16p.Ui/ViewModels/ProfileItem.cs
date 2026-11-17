using CommunityToolkit.Mvvm.ComponentModel;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Tuning;
using Tb16p.Ui.Localization;

namespace Tb16p.Ui.ViewModels;

/// <summary>A number that can be switched off (null = left to the firmware / core left alone).</summary>
public sealed partial class OptionalValue : ObservableObject
{
    private readonly Action _changed;
    private bool _setting;

    public OptionalValue(string? labelKey, int min, int max, int fallback, Action changed, string unit = "")
    {
        LabelKey = labelKey;
        Unit = unit;
        Minimum = min;
        Maximum = max;
        Fallback = Math.Clamp(fallback, min, max);
        _value = Fallback;
        _changed = changed;
    }

    public string? LabelKey { get; }
    public string Unit { get; }
    public string Label => LabelKey is null ? "" : Loc.T(LabelKey);
    public string Description => LabelKey is null ? "" : Loc.T(LabelKey + "_Desc");
    public int Minimum { get; }
    public int Maximum { get; }
    public int Fallback { get; }

    /// <summary>Short label drawn next to the tick box (Curve Optimizer cells).</summary>
    [ObservableProperty] private string _caption = "";

    [ObservableProperty] private bool _isSet;
    [ObservableProperty] private double? _value;

    public int? Result => IsSet ? Math.Clamp((int)Math.Round(Value ?? Fallback), Minimum, Maximum) : null;

    public void Load(int? value)
    {
        _setting = true;
        IsSet = value is not null;
        if (value is not null)
            Value = value;
        _setting = false;
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
    }

    partial void OnIsSetChanged(bool value)
    {
        if (!_setting)
            _changed();
    }

    partial void OnValueChanged(double? value)
    {
        if (!_setting && IsSet && value is not null)
            _changed();
    }
}

/// <summary>Editable view of one profile in the draft.</summary>
public sealed partial class ProfileItem : ObservableObject
{
    private readonly Action _markDirty;
    private bool _loading = true;

    /// <summary>Shown in the editor while the profile leaves the fans to the EC, ready to switch to.</summary>
    private List<FanPoint> _idleCurve;

    public ProfileItem(Profile profile, string? originalName, Action markDirty)
    {
        Profile = profile;
        OriginalName = originalName;
        _markDirty = markDirty;
        _name = profile.Name;

        Stapm = new("Limit_Stapm", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 60, WriteLimits, "W");
        Fast = new("Limit_Fast", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 80, WriteLimits, "W");
        Slow = new("Limit_Slow", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 60, WriteLimits, "W");
        Tctl = new("Limit_Tctl", CpuTuner.MinTctlC, CpuTuner.MaxTctlC, 95, WriteLimits, "°C");
        Ccd1 = new CcdEditor("CCD1", WriteCurveOptimizer);
        Ccd2 = new CcdEditor("CCD2", WriteCurveOptimizer);

        Stapm.Load(profile.StapmW);
        Fast.Load(profile.FastW);
        Slow.Load(profile.SlowW);
        Tctl.Load(profile.TctlC);
        Ccd1.Load(profile.CurveOptimizerCcd1);
        Ccd2.Load(profile.CurveOptimizerCcd2);
        _fanCustom = profile.FanCurve is not null;
        _idleCurve = FanCurve.LenovoStock();
        _loading = false;
    }

    public Profile Profile { get; }

    /// <summary>Name in the saved settings, null for a profile added in this draft.</summary>
    public string? OriginalName { get; }

    public OptionalValue Stapm { get; }
    public OptionalValue Fast { get; }
    public OptionalValue Slow { get; }
    public OptionalValue Tctl { get; }
    public CcdEditor Ccd1 { get; }
    public CcdEditor Ccd2 { get; }
    public IEnumerable<CcdEditor> Ccds => [Ccd1, Ccd2];
    public IEnumerable<OptionalValue> Limits => [Stapm, Fast, Slow, Tctl];

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _usedOnAc;
    [ObservableProperty] private bool _usedOnBattery;
    [ObservableProperty] private bool _fanCustom;

    public string Usage => string.Join(" · ",
        new[] { UsedOnAc ? Loc.T("Profiles_InUseAc") : null, UsedOnBattery ? Loc.T("Profiles_InUseBattery") : null }.Where(s => s is not null));

    /// <summary>Points shown in the fan chart; written back by the chart after every edit.</summary>
    public List<FanPoint> FanPoints
    {
        get => Profile.FanCurve ?? _idleCurve;
        set
        {
            if (FanCustom)
            {
                Profile.FanCurve = value;
                _markDirty();
            }
            else
            {
                _idleCurve = value;
            }
            OnPropertyChanged();
        }
    }

    public void RefreshText()
    {
        foreach (var v in Limits)
            v.RefreshText();
        Ccd1.RefreshText();
        Ccd2.RefreshText();
        OnPropertyChanged(nameof(Usage));
    }

    partial void OnUsedOnAcChanged(bool value) => OnPropertyChanged(nameof(Usage));
    partial void OnUsedOnBatteryChanged(bool value) => OnPropertyChanged(nameof(Usage));

    partial void OnNameChanged(string value)
    {
        var name = value.Trim();
        if (_loading || name.Length == 0 || name == Profile.Name)
            return;
        Profile.Name = name;
        _markDirty();
    }

    partial void OnFanCustomChanged(bool value)
    {
        if (_loading)
            return;
        if (value)
        {
            Profile.FanCurve = _idleCurve.Select(p => p.Clone()).ToList();
        }
        else
        {
            _idleCurve = Profile.FanCurve ?? _idleCurve;
            Profile.FanCurve = null;
        }
        OnPropertyChanged(nameof(FanPoints));
        _markDirty();
    }

    public void ApplyFanPreset(List<FanPoint> preset)
    {
        _idleCurve = preset;
        if (FanCustom)
        {
            Profile.FanCurve = preset.Select(p => p.Clone()).ToList();
            OnPropertyChanged(nameof(FanPoints));
            _markDirty();
        }
        else
        {
            FanCustom = true;
        }
    }

    /// <summary>Copies the fan setting (curve or EC mode) of <paramref name="source"/>.</summary>
    public void CopyFanFrom(ProfileItem source)
    {
        if (source.FanCustom)
            ApplyFanPreset(source.FanPoints.Select(p => p.Clone()).ToList());
        else
            FanCustom = false;
    }

    private void WriteLimits()
    {
        if (_loading)
            return;
        Profile.StapmW = Stapm.Result;
        Profile.FastW = Fast.Result;
        Profile.SlowW = Slow.Result;
        Profile.TctlC = Tctl.Result;
        _markDirty();
    }

    private void WriteCurveOptimizer()
    {
        if (_loading)
            return;
        Profile.CurveOptimizerCcd1 = Collect(Ccd1);
        Profile.CurveOptimizerCcd2 = Collect(Ccd2);
        _markDirty();

        static int?[]? Collect(CcdEditor ccd)
        {
            var values = ccd.Offsets;
            return values.All(v => v is null) ? null : values;
        }
    }
}
