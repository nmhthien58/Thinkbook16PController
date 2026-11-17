using CommunityToolkit.Mvvm.ComponentModel;
using Tb16p.Core.Hardware;
using Tb16p.Core.Tuning;
using Tb16p.Ui.Localization;

namespace Tb16p.Ui.ViewModels;

/// <summary>
/// Curve Optimizer for one CCD, edited either as one value for all eight cores or per core.
/// The profile always stores per-core offsets; "all cores" just writes the same one to each.
/// </summary>
public sealed partial class CcdEditor : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    public CcdEditor(string name, Action changed)
    {
        Name = name;
        _changed = changed;
        Cores = Enumerable.Range(0, DragonRange.CoresPerCcd)
            .Select(_ => new OptionalValue(null, CpuTuner.MinCurveOffset, CpuTuner.MaxCurveOffset, -10, OnCoreChanged))
            .ToArray();
        All = new OptionalValue(null, CpuTuner.MinCurveOffset, CpuTuner.MaxCurveOffset, -10, OnAllChanged);
        RefreshText();
    }

    public void RefreshText()
    {
        for (var i = 0; i < Cores.Length; i++)
            Cores[i].Caption = Loc.F("Co_Core", i + 1);
        All.Caption = Loc.T("Co_AllCores_Value");
    }

    public string Name { get; }
    public OptionalValue[] Cores { get; }

    /// <summary>The single value used in "all cores" mode.</summary>
    public OptionalValue All { get; }

    [ObservableProperty] private bool _useAll;

    public int?[] Offsets => Cores.Select(c => c.Result).ToArray();

    /// <summary>Shows saved offsets; eight equal ones (or none at all) open in "all cores" mode.</summary>
    public void Load(int?[]? offsets)
    {
        _loading = true;
        var values = Enumerable.Range(0, Cores.Length).Select(i => offsets?.ElementAtOrDefault(i)).ToArray();
        for (var i = 0; i < Cores.Length; i++)
            Cores[i].Load(values[i]);
        UseAll = values.Distinct().Count() == 1;
        All.Load(values[0]);
        _loading = false;
    }

    /// <summary>Sets every core (null = leave them all alone), keeping the current mode.</summary>
    public void SetAll(int? offset)
    {
        _loading = true;
        All.Load(offset);
        foreach (var core in Cores)
            core.Load(offset);
        _loading = false;
        _changed();
    }

    partial void OnUseAllChanged(bool value)
    {
        if (_loading || !value)
            return; // switching to per core keeps the cores as they are
        // Start "all cores" from the first offset in use, ticked if any core was.
        var first = Cores.Select(c => c.Result).FirstOrDefault(v => v is not null);
        SetAll(first ?? (Cores.Any(c => c.IsSet) ? All.Fallback : null));
    }

    private void OnAllChanged()
    {
        if (_loading)
            return;
        _loading = true;
        foreach (var core in Cores)
            core.Load(All.Result);
        _loading = false;
        _changed();
    }

    private void OnCoreChanged()
    {
        if (!_loading)
            _changed();
    }
}
