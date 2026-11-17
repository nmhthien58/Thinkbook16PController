using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tb16p.Core.Config;
using Tb16p.Core.Tuning;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Tb16p.Ui.Services;

namespace Tb16p.Ui.ViewModels;

/// <summary>
/// Profiles page. Edits a draft of the profile list; nothing reaches the engine until
/// "Save &amp; apply". The draft survives switching pages and hiding the window.
/// Settings changed elsewhere in the meantime (General page, tray) are kept on save.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly AppController _app;
    private bool _saving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(DeleteCommand))]
    private ProfileItem? _selected;

    [ObservableProperty] private int? _liveTempC;
    [ObservableProperty] private int? _liveRpm;
    [ObservableProperty] private bool _fanMax;

    public ProfilesViewModel(AppController app)
    {
        _app = app;
        app.SettingsChanged += OnSettingsChanged;
        app.StateChanged += () => FanMax = _app.Engine.FanMax && !_app.Engine.Paused;
        app.Sensors.Updated += () =>
        {
            LiveTempC = _app.Sensors.Latest?.FanControlTempC;
            LiveRpm = _app.Sensors.Latest?.Fan1Rpm;
        };
        Loc.Instance.LanguageChanged += () =>
        {
            foreach (var item in Items)
                item.RefreshText();
            OnPropertyChanged(nameof(FanHint));
        };
        Load(null);
        FanMax = app.Engine.FanMax;
    }

    public ObservableCollection<ProfileItem> Items { get; } = [];
    public string FanHint => Loc.F("Fan_Hint", FanCurve.EmergencyTempC);    private void Load(string? select)
    {
        var previous = select ?? Selected?.Name;
        Items.Clear();
        foreach (var p in _app.Settings.Clone().Profiles)
            Items.Add(new ProfileItem(p, p.Name, MarkDirty));
        RefreshUsage();
        Selected = Items.FirstOrDefault(i => i.Name == previous) ?? Items.FirstOrDefault();
        IsDirty = false;
    }

    private void OnSettingsChanged()
    {
        if (_saving)
            return;
        if (IsDirty)
            RefreshUsage();
        else
            Load(null);
    }

    private void MarkDirty() => IsDirty = true;

    /// <summary>Which draft profile the saved AC / battery choice points at, following renames.</summary>
    private ProfileItem? ItemFor(string savedName) =>
        Items.FirstOrDefault(i => string.Equals(i.OriginalName, savedName, StringComparison.OrdinalIgnoreCase));

    private void RefreshUsage()
    {
        var ac = ItemFor(_app.Settings.AcProfile);
        var battery = ItemFor(_app.Settings.BatteryProfile);
        foreach (var item in Items)
        {
            item.UsedOnAc = item == ac;
            item.UsedOnBattery = item == battery;
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Items.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} {i}";
        return name;
    }

    private void AddItem(Profile profile)
    {
        var item = new ProfileItem(profile, null, MarkDirty);
        Items.Add(item);
        Selected = item;
        MarkDirty();
    }

    [RelayCommand]
    private void Add()
    {
        // Start from the AC profile's Curve Optimizer: it is tuned per chip, not per profile.
        var ac = ItemFor(_app.Settings.AcProfile)?.Profile;
        AddItem(new Profile
        {
            Name = UniqueName(Loc.T("Profile_NewName")),
            StapmW = 60,
            SlowW = 60,
            FastW = 80,
            CurveOptimizerCcd1 = (int?[]?)ac?.CurveOptimizerCcd1?.Clone(),
            CurveOptimizerCcd2 = (int?[]?)ac?.CurveOptimizerCcd2?.Clone(),
        });
    }

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selected is not { Profile: var source })
            return;
        AddItem(new Profile
        {
            Name = UniqueName(source.Name + Loc.T("Profile_CopySuffix")),
            StapmW = source.StapmW,
            FastW = source.FastW,
            SlowW = source.SlowW,
            TctlC = source.TctlC,
            CurveOptimizerCcd1 = (int?[]?)source.CurveOptimizerCcd1?.Clone(),
            CurveOptimizerCcd2 = (int?[]?)source.CurveOptimizerCcd2?.Clone(),
            FanCurve = source.FanCurve?.Select(point => point.Clone()).ToList(),
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (Selected is not { } item)
            return;
        if (Items.Count == 1)
        {
            await Dialogs.AlertAsync(Loc.T("Dlg_LastProfile"));
            return;
        }
        if (item.UsedOnAc || item.UsedOnBattery)
        {
            await Dialogs.AlertAsync(Loc.F("Dlg_ProfileInUse", item.Name));
            return;
        }
        if (!await Dialogs.ConfirmAsync(Loc.F("Dlg_DeleteProfile", item.Name)))
            return;
        var index = Items.IndexOf(item);
        Items.Remove(item);
        Selected = Items[Math.Min(index, Items.Count - 1)];
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync() => await Save();

    /// <summary>Saves the draft; false (after telling the user) when it is not valid.</summary>
    public async Task<bool> Save()
    {
        var live = _app.Settings.Clone();
        live.AcProfile = ItemFor(live.AcProfile)?.Name ?? live.AcProfile;
        live.BatteryProfile = ItemFor(live.BatteryProfile)?.Name ?? live.BatteryProfile;
        live.Profiles = Items.Select(i => i.Profile).ToList();

        _saving = true;
        bool ok;
        string? error;
        try
        {
            ok = _app.Commit(live, out error);
        }
        finally
        {
            _saving = false;
        }
        if (!ok)
        {
            await Dialogs.AlertAsync(Loc.F("Dlg_SaveFailed", error));
            return false;
        }
        Log.Info("Profiles saved from the management window");
        Load(Selected?.Name);
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert() => Load(null);

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        Selected?.ApplyFanPreset(name switch
        {
            "Stock" => FanCurve.LenovoStock(),
            "Quiet" => FanCurve.Quiet(),
            "Cool" => FanCurve.Cool(),
            _ => FanCurve.Balanced(),
        });
    }

    [RelayCommand]
    private async Task CopyFanToAllAsync()
    {
        if (Selected is not { } source || !await Dialogs.ConfirmAsync(Loc.T("Dlg_CopyFan")))
            return;
        foreach (var item in Items.Where(i => i != source))
            item.CopyFanFrom(source);
        MarkDirty();
    }

    [RelayCommand]
    private void ClearCurveOptimizer()
    {
        if (Selected is not { } item)
            return;
        item.Ccd1.SetAll(null);
        item.Ccd2.SetAll(null);
    }

    [RelayCommand]
    private void ToggleFanMax() => _app.Engine.SetFanMax(!_app.Engine.FanMax);
}
