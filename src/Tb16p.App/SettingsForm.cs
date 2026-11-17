using Tb16p.Service;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;

namespace Tb16p.App;

/// <summary>
/// Management window: profile list, power limits, per-core Curve Optimizer, general
/// settings and a live status bar. Edits a copy of the settings; nothing reaches the
/// engine until "Lưu &amp; áp dụng".
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly TuningEngine _engine;
    private AppSettings _draft;
    private bool _dirty;
    private bool _loading;

    private readonly ListBox _profiles = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly OptionalNumber _stapm = new("STAPM", "W", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 60);
    private readonly OptionalNumber _fast = new("Fast (PPT ngắn hạn)", "W", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 80);
    private readonly OptionalNumber _slow = new("Slow (PPT dài hạn)", "W", CpuTuner.MinPowerW, CpuTuner.MaxPowerW, 60);
    private readonly OptionalNumber _tctl = new("Ngưỡng nhiệt CPU", "°C", CpuTuner.MinTctlC, CpuTuner.MaxTctlC, 95);
    private readonly OptionalNumber[] _ccd1 = CoreEditors();
    private readonly OptionalNumber[] _ccd2 = CoreEditors();
    private readonly RadioButton _fanAuto = new() { Text = "Để EC tự quyết (đường cong gốc của Lenovo)", AutoSize = true, Checked = true };
    private readonly RadioButton _fanCustom = new() { Text = "Đường cong tùy chỉnh", AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
    private readonly FanCurveEditor _fanEditor = new() { Enabled = false };
    private readonly CheckBox _fanMax = new()
    {
        Text = "Quạt tối đa (thử)",
        Appearance = Appearance.Button,
        AutoSize = true,
        Padding = new Padding(8, 0, 8, 0),
    };

    private readonly ComboBox _acProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _batteryProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _chargeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _limitsInterval = new() { Minimum = 1, Maximum = 60, Width = 60 };
    private readonly NumericUpDown _coInterval = new() { Minimum = 5, Maximum = 600, Width = 60 };
    private readonly CheckBox _startup = new() { Text = "Chạy cùng Windows", AutoSize = true };
    private readonly CheckBox _sensorLog = new() { Text = "Ghi nhật ký cảm biến (nhiệt độ + RPM mỗi 2 giây)", AutoSize = true };

    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly Label _sensors = new() { AutoSize = true, Padding = new Padding(4) };
    private readonly Button _save = new() { Text = "Lưu && áp dụng", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly System.Windows.Forms.Timer _sensorTimer = new() { Interval = 2000 };
    private GameZoneSensors? _gameZone;
    private bool _readingSensors;

    private static readonly (string Text, ChargeMode? Mode)[] ChargeModes =
    [
        ("Conservation (giữ pin ~60-80%)", ChargeMode.Conservation),
        ("Normal", ChargeMode.Normal),
        ("Rapid Charge", ChargeMode.RapidCharge),
        ("Không quản lý", null),
    ];

    public SettingsForm(TuningEngine engine)
    {
        _engine = engine;
        _draft = engine.Settings.Clone();

        Text = "Thinkbook16PController";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(980, 660);
        Size = new Size(1040, 720);

        BuildLayout();
        LoadDraft();

        _engine.StateChanged += OnEngineStateChanged;
        _sensorTimer.Tick += async (_, _) => await RefreshSensorsAsync();
        Shown += async (_, _) =>
        {
            _gameZone = await Task.Run(GameZoneSensors.TryOpen);
            await RefreshSensorsAsync();
            _sensorTimer.Start();
        };
        RefreshStatus();
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var statusBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = SystemColors.ControlLight };
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(_sensors);
        root.Controls.Add(statusBar, 0, 0);
        root.SetColumnSpan(statusBar, 2);

        root.Controls.Add(BuildProfileList(), 0, 1);
        root.Controls.Add(BuildEditor(), 1, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(BuildGeneral(), 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        var revert = new Button { Text = "Hủy thay đổi", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        revert.Click += (_, _) => { _draft = _engine.Settings.Clone(); LoadDraft(); };
        _save.Click += (_, _) => Save();
        buttons.Controls.Add(_save);
        buttons.Controls.Add(revert);
        bottom.Controls.Add(buttons, 1, 0);
        root.Controls.Add(bottom, 0, 2);
        root.SetColumnSpan(bottom, 2);

        Controls.Add(root);
    }

    private Control BuildProfileList()
    {
        var box = new GroupBox { Text = "Profile", Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Skipped while loading or renaming: refreshing the list item text re-fires this event,
        // and reloading the editor would reset the name box caret mid-typing.
        _profiles.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
                ShowSelectedProfile();
        };
        layout.Controls.Add(_profiles, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(SmallButton("Thêm", AddProfile));
        buttons.Controls.Add(SmallButton("Nhân bản", DuplicateProfile));
        buttons.Controls.Add(SmallButton("Xóa", DeleteProfile));
        layout.Controls.Add(buttons, 0, 1);
        box.Controls.Add(layout);
        return box;
    }

    private Control BuildEditor()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var nameRow = new FlowLayoutPanel { AutoSize = true };
        nameRow.Controls.Add(new Label { Text = "Tên profile", AutoSize = true, Margin = new Padding(3, 7, 8, 3) });
        nameRow.Controls.Add(_name);
        _name.TextChanged += (_, _) => RenameSelected();

        var limits = new GroupBox { Text = "Giới hạn điện (bỏ tích = để firmware tự quyết)", AutoSize = true, Padding = new Padding(8) };
        var limitsFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill };
        foreach (var editor in new[] { _stapm, _fast, _slow, _tctl })
        {
            editor.Changed += () => WriteLimits();
            limitsFlow.Controls.Add(editor);
        }
        limitsFlow.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Gợi ý: STAPM = Slow. Firmware cho phép dài hạn 50-90 W, đỉnh 70-155 W, ngưỡng nhiệt 85-100 °C.",
        });
        limits.Controls.Add(limitsFlow);
        panel.Controls.Add(limits);

        var co = new GroupBox { Text = "Curve Optimizer từng nhân (bỏ tích = không đụng nhân đó)", AutoSize = true, Padding = new Padding(8) };
        var coLayout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = DragonRange.CoresPerCcd + 1 };
        grid.Controls.Add(new Label(), 0, 0);
        for (var core = 0; core < DragonRange.CoresPerCcd; core++)
            grid.Controls.Add(new Label { Text = $"Nhân {core + 1}", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.None }, core + 1, 0);
        AddCcdRow(grid, 1, "CCD1", _ccd1);
        AddCcdRow(grid, 2, "CCD2", _ccd2);
        coLayout.Controls.Add(grid);

        // Fill helpers sit under the grid so the eight core columns fit the window width.
        var fill = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        AddFillHelper(fill, "CCD1", _ccd1);
        AddFillHelper(fill, "CCD2", _ccd2);
        AddClearHelper(fill);
        coLayout.Controls.Add(fill);

        co.Controls.Add(coLayout);
        panel.Controls.Add(co);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var powerTab = new TabPage("Điện && Curve Optimizer") { UseVisualStyleBackColor = true };
        powerTab.Controls.Add(panel);
        var fanTab = new TabPage("Quạt") { UseVisualStyleBackColor = true };
        fanTab.Controls.Add(BuildFanTab());
        tabs.TabPages.Add(powerTab);
        tabs.TabPages.Add(fanTab);

        var profileEditor = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        profileEditor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileEditor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profileEditor.Controls.Add(nameRow, 0, 0);
        profileEditor.Controls.Add(tabs, 0, 1);
        return profileEditor;
    }

    private Control BuildFanTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var mode = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        mode.Controls.Add(_fanAuto);
        mode.Controls.Add(_fanCustom);
        layout.Controls.Add(mode, 0, 0);

        var presets = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        presets.Controls.Add(new Label { Text = "Mẫu:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        presets.Controls.Add(SmallButton("Êm", () => ApplyFanPreset(FanCurve.Quiet())));
        presets.Controls.Add(SmallButton("Cân bằng", () => ApplyFanPreset(FanCurve.Balanced())));
        presets.Controls.Add(SmallButton("Mát", () => ApplyFanPreset(FanCurve.Cool())));
        presets.Controls.Add(new Label { Width = 16 });
        presets.Controls.Add(SmallButton("Chép sang mọi profile", CopyFanCurveToAll));
        presets.Controls.Add(new Label { Width = 16 });
        presets.Controls.Add(_fanMax);
        // Applies immediately and is not part of the saved settings.
        _fanMax.CheckedChanged += (_, _) =>
        {
            if (_fanMax.Checked != _engine.FanMax)
                _engine.SetFanMax(_fanMax.Checked);
            ShowFanMax();
        };
        layout.Controls.Add(presets, 0, 1);

        _fanEditor.Dock = DockStyle.Fill;
        layout.Controls.Add(_fanEditor, 0, 2);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Kéo điểm để chỉnh  •  Nhấp đúp vào chỗ trống để thêm điểm  •  Chuột phải vào điểm để xóa\n"
                + $"Nhiệt độ điều khiển = cao nhất của CPU và CB5  •  Từ {FanCurve.EmergencyTempC} °C quạt chạy tối đa  •  "
                + "Quạt chỉ giảm tốc khi nhiệt đã hạ 3 °C  •  Thoát / tạm dừng / sleep: quạt trả về cho EC",
        }, 0, 3);

        _fanAuto.CheckedChanged += (_, _) => OnFanModeChanged();
        _fanEditor.CurveChanged += () =>
        {
            if (!_loading && SelectedProfile is { FanCurve: not null } p)
            {
                p.FanCurve = _fanEditor.GetPoints();
                MarkDirty();
            }
        };
        return layout;
    }

    /// <summary>Mirrors the engine's max-fan switch, which the tray menu can also flip.</summary>
    private void ShowFanMax()
    {
        if (_fanMax.Checked != _engine.FanMax)
            _fanMax.Checked = _engine.FanMax;
        _fanMax.BackColor = _engine.FanMax ? Color.Firebrick : SystemColors.Control;
        _fanMax.ForeColor = _engine.FanMax ? Color.White : SystemColors.ControlText;
        _fanEditor.MaxOverride = _engine.FanMax;
    }

    private void OnFanModeChanged()
    {
        _fanEditor.Enabled = _fanCustom.Checked;
        if (_loading || SelectedProfile is not { } p)
            return;
        p.FanCurve = _fanCustom.Checked ? _fanEditor.GetPoints() : null;
        MarkDirty();
    }

    private void ApplyFanPreset(List<FanPoint> preset)
    {
        _fanEditor.SetPoints(preset);
        if (!_fanCustom.Checked)
            _fanCustom.Checked = true; // also stores the curve through OnFanModeChanged
        else if (SelectedProfile is { } p)
        {
            p.FanCurve = _fanEditor.GetPoints();
            MarkDirty();
        }
    }

    private void CopyFanCurveToAll()
    {
        if (SelectedProfile is not { } source)
            return;
        var what = source.FanCurve is null ? "chế độ \"Để EC tự quyết\"" : "đường cong quạt này";
        if (MessageBox.Show(this, $"Áp {what} cho tất cả profile?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        foreach (var p in _draft.Profiles.Where(p => p != source))
            p.FanCurve = source.FanCurve?.Select(point => point.Clone()).ToList();
        MarkDirty();
    }

    private void AddCcdRow(TableLayoutPanel grid, int row, string label, OptionalNumber[] editors)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) }, 0, row);
        for (var core = 0; core < editors.Length; core++)
        {
            editors[core].Changed += () => WriteCurveOptimizer();
            grid.Controls.Add(editors[core], core + 1, row);
        }
    }

    private void AddFillHelper(FlowLayoutPanel host, string label, OptionalNumber[] editors)
    {
        var all = new NumericUpDown { Minimum = CpuTuner.MinCurveOffset, Maximum = CpuTuner.MaxCurveOffset, Width = 50, Value = -10 };
        host.Controls.Add(new Label { Text = $"Đặt cả {label} =", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        host.Controls.Add(all);
        host.Controls.Add(SmallButton("Áp", () =>
        {
            foreach (var editor in editors)
                editor.Value = (int)all.Value;
            WriteCurveOptimizer();
        }));
        host.Controls.Add(new Label { Width = 16 });
    }

    private void AddClearHelper(FlowLayoutPanel host) =>
        host.Controls.Add(SmallButton("Bỏ CO (không đụng nhân nào)", () =>
        {
            foreach (var editor in _ccd1.Concat(_ccd2))
                editor.Value = null;
            WriteCurveOptimizer();
        }));

    private Control BuildGeneral()
    {
        var box = new GroupBox { Text = "Chung", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Fill };

        foreach (var (text, mode) in ChargeModes)
            _chargeMode.Items.Add(text);

        AddRow(0, 0, "Profile khi cắm sạc", _acProfile);
        AddRow(1, 0, "Profile khi dùng pin", _batteryProfile);
        AddRow(2, 0, "Chế độ sạc pin", _chargeMode);
        AddRow(0, 2, "Ghi lại giới hạn mỗi (giây)", _limitsInterval);
        AddRow(1, 2, "Ghi lại Curve Optimizer mỗi (giây)", _coInterval);
        grid.Controls.Add(_startup, 3, 2);
        grid.Controls.Add(_sensorLog, 0, 3);
        grid.SetColumnSpan(_sensorLog, 2);
        grid.Controls.Add(SmallButton("Mở thư mục dữ liệu", TrayApp.OpenDataFolder), 3, 3);
        _sensorLog.CheckedChanged += (_, _) => { if (!_loading) { _draft.SensorLogEnabled = _sensorLog.Checked; MarkDirty(); } };

        _acProfile.SelectedIndexChanged += (_, _) => { if (!_loading && _acProfile.SelectedItem is string s) { _draft.AcProfile = s; MarkDirty(); } };
        _batteryProfile.SelectedIndexChanged += (_, _) => { if (!_loading && _batteryProfile.SelectedItem is string s) { _draft.BatteryProfile = s; MarkDirty(); } };
        _chargeMode.SelectedIndexChanged += (_, _) => { if (!_loading && _chargeMode.SelectedIndex >= 0) { _draft.ChargeMode = ChargeModes[_chargeMode.SelectedIndex].Mode; MarkDirty(); } };
        _limitsInterval.ValueChanged += (_, _) => { if (!_loading) { _draft.ReapplyLimitsSeconds = (int)_limitsInterval.Value; MarkDirty(); } };
        _coInterval.ValueChanged += (_, _) => { if (!_loading) { _draft.ReapplyCurveOptimizerSeconds = (int)_coInterval.Value; MarkDirty(); } };
        _startup.CheckedChanged += (_, _) => { if (!_loading) ToggleStartup(); };

        box.Controls.Add(grid);
        return box;

        void AddRow(int row, int column, string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, column, row);
            grid.Controls.Add(control, column + 1, row);
        }
    }

    // ------------------------------------------------------------------ data binding

    private void LoadDraft(string? select = null)
    {
        _loading = true;
        try
        {
            var previous = select ?? (_profiles.SelectedItem as string);
            _profiles.Items.Clear();
            foreach (var p in _draft.Profiles)
                _profiles.Items.Add(p.Name);

            RefreshProfileCombos();
            _chargeMode.SelectedIndex = Array.FindIndex(ChargeModes, m => m.Mode == _draft.ChargeMode);
            _limitsInterval.Value = Math.Clamp(_draft.ReapplyLimitsSeconds, 1, 60);
            _coInterval.Value = Math.Clamp(_draft.ReapplyCurveOptimizerSeconds, 5, 600);
            try { _startup.Checked = StartupTask.IsEnabled(); } catch { _startup.Checked = false; }
            _sensorLog.Checked = _draft.SensorLogEnabled;

            var index = previous is null ? -1 : _profiles.Items.IndexOf(previous);
            _profiles.SelectedIndex = index >= 0 ? index : _profiles.Items.Count > 0 ? 0 : -1;
        }
        finally
        {
            _loading = false;
        }
        ShowSelectedProfile();
        SetDirty(false);
    }

    private void RefreshProfileCombos()
    {
        var wasLoading = _loading;
        _loading = true;
        foreach (var (combo, selected) in new[] { (_acProfile, _draft.AcProfile), (_batteryProfile, _draft.BatteryProfile) })
        {
            combo.Items.Clear();
            foreach (var p in _draft.Profiles)
                combo.Items.Add(p.Name);
            combo.SelectedItem = _draft.Find(selected)?.Name;
        }
        _loading = wasLoading;
    }

    private Profile? SelectedProfile =>
        _profiles.SelectedIndex >= 0 && _profiles.SelectedIndex < _draft.Profiles.Count ? _draft.Profiles[_profiles.SelectedIndex] : null;

    private void ShowSelectedProfile()
    {
        var p = SelectedProfile;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _name.Text = p?.Name ?? "";
            _stapm.Value = p?.StapmW;
            _fast.Value = p?.FastW;
            _slow.Value = p?.SlowW;
            _tctl.Value = p?.TctlC;
            for (var core = 0; core < DragonRange.CoresPerCcd; core++)
            {
                _ccd1[core].Value = p?.CurveOptimizerCcd1?.ElementAtOrDefault(core);
                _ccd2[core].Value = p?.CurveOptimizerCcd2?.ElementAtOrDefault(core);
            }
            // Without a custom curve the editor still shows a starting point to switch to.
            _fanEditor.SetPoints(p?.FanCurve ?? FanCurve.Balanced());
            _fanCustom.Checked = p?.FanCurve is not null;
            _fanAuto.Checked = p?.FanCurve is null;
            _fanEditor.Enabled = _fanCustom.Checked;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void WriteLimits()
    {
        if (_loading || SelectedProfile is not { } p)
            return;
        p.StapmW = _stapm.Value;
        p.FastW = _fast.Value;
        p.SlowW = _slow.Value;
        p.TctlC = _tctl.Value;
        MarkDirty();
    }

    private void WriteCurveOptimizer()
    {
        if (_loading || SelectedProfile is not { } p)
            return;
        p.CurveOptimizerCcd1 = Collect(_ccd1);
        p.CurveOptimizerCcd2 = Collect(_ccd2);
        MarkDirty();

        static int?[]? Collect(OptionalNumber[] editors)
        {
            var values = editors.Select(e => e.Value).ToArray();
            return values.All(v => v is null) ? null : values;
        }
    }

    private void RenameSelected()
    {
        if (_loading || SelectedProfile is not { } p)
            return;
        var newName = _name.Text.Trim();
        if (newName.Length == 0 || newName == p.Name)
            return;

        if (string.Equals(_draft.AcProfile, p.Name, StringComparison.OrdinalIgnoreCase))
            _draft.AcProfile = newName;
        if (string.Equals(_draft.BatteryProfile, p.Name, StringComparison.OrdinalIgnoreCase))
            _draft.BatteryProfile = newName;
        p.Name = newName;

        _loading = true;
        _profiles.Items[_profiles.SelectedIndex] = newName;
        _loading = false;
        RefreshProfileCombos();
        MarkDirty();
    }

    private void AddProfile()
    {
        // Start from the AC profile's Curve Optimizer: the user's CO is per-chip, not per-profile.
        var ac = _draft.Find(_draft.AcProfile);
        var profile = new Profile
        {
            Name = UniqueName("Profile mới"),
            StapmW = 60,
            SlowW = 60,
            FastW = 80,
            CurveOptimizerCcd1 = (int?[]?)ac?.CurveOptimizerCcd1?.Clone(),
            CurveOptimizerCcd2 = (int?[]?)ac?.CurveOptimizerCcd2?.Clone(),
        };
        _draft.Profiles.Add(profile);
        LoadDraftKeepingDirty(profile.Name);
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is not { } source)
            return;
        var copy = new Profile
        {
            Name = UniqueName(source.Name + " (bản sao)"),
            StapmW = source.StapmW,
            FastW = source.FastW,
            SlowW = source.SlowW,
            TctlC = source.TctlC,
            CurveOptimizerCcd1 = (int?[]?)source.CurveOptimizerCcd1?.Clone(),
            CurveOptimizerCcd2 = (int?[]?)source.CurveOptimizerCcd2?.Clone(),
            FanCurve = source.FanCurve?.Select(point => point.Clone()).ToList(),
        };
        _draft.Profiles.Add(copy);
        LoadDraftKeepingDirty(copy.Name);
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { } p)
            return;
        if (_draft.Profiles.Count == 1)
        {
            Warn("Phải còn ít nhất một profile.");
            return;
        }
        if (string.Equals(_draft.AcProfile, p.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_draft.BatteryProfile, p.Name, StringComparison.OrdinalIgnoreCase))
        {
            Warn($"\"{p.Name}\" đang được dùng cho lúc cắm sạc hoặc dùng pin. Chọn profile khác ở phần Chung trước khi xóa.");
            return;
        }
        if (MessageBox.Show(this, $"Xóa profile \"{p.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _draft.Profiles.Remove(p);
        LoadDraftKeepingDirty(null);
    }

    private void LoadDraftKeepingDirty(string? select)
    {
        LoadDraft(select);
        MarkDirty();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; _draft.Find(name) is not null; i++)
            name = $"{baseName} {i}";
        return name;
    }

    private void Save()
    {
        try
        {
            _draft.Validate();
            _draft.Save();
            _engine.ReplaceSettings(_draft.Clone());
            Log.Info("Settings saved from the management window");
            SetDirty(false);
        }
        catch (Exception ex)
        {
            Warn($"Không lưu được:\n\n{ex.Message}");
        }
    }

    private void ToggleStartup()
    {
        try
        {
            if (_startup.Checked) StartupTask.Enable(); else StartupTask.Disable();
            Log.Info($"Start with Windows: {_startup.Checked}");
        }
        catch (Exception ex)
        {
            Warn($"Không đổi được chế độ chạy cùng Windows: {ex.Message}");
            _loading = true;
            _startup.Checked = !_startup.Checked;
            _loading = false;
        }
    }

    private void MarkDirty()
    {
        if (!_loading)
            SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _save.Enabled = dirty;
        Text = dirty ? "Thinkbook16PController *" : "Thinkbook16PController";
    }

    // ------------------------------------------------------------------ live status

    private void OnEngineStateChanged()
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(RefreshStatus);
    }

    private void RefreshStatus()
    {
        var profile = _engine.ActiveProfile;
        var source = _engine.PowerLine == PowerLine.Battery ? "Dùng pin" : "Cắm sạc";
        var state = _engine.Paused ? "TẠM DỪNG"
            : _engine.LastError is { } error ? $"LỖI: {error}"
            : _engine.LastApplied is { } at ? $"áp lần cuối {at:HH:mm:ss}" : "đang khởi động";
        var fan = _engine.FanMax && !_engine.Paused ? $"quạt: TỐI ĐA ({FanCurve.MaxRpm} rpm)"
            : _engine.FanTargetRpm is { } rpm ? $"quạt theo đường cong: {rpm} rpm"
            : "quạt: EC tự quyết";
        ShowFanMax();
        _status.Text = $"{source}  •  {profile?.Name ?? "?"}  •  {state}  •  {fan}";
        _status.ForeColor = _engine.LastError is not null ? Color.Firebrick : SystemColors.ControlText;
    }

    private async Task RefreshSensorsAsync()
    {
        RefreshStatus();
        if (_gameZone is null || _readingSensors)
        {
            if (_gameZone is null)
                _sensors.Text = "  |  Cảm biến: không đọc được";
            return;
        }
        _readingSensors = true;
        try
        {
            var r = await Task.Run(_gameZone.Read);
            _fanEditor.SetLive(r.FanControlTempC, r.Fan1Rpm);
            _sensors.Text = $"  |  CPU {Show(r.CpuC, "°C")}  •  CB1 {Show(r.Sensor1C, "°C")}  •  CB5 {Show(r.Sensor5C, "°C")}"
                + $"  •  Quạt 1 {Show(r.Fan1Rpm, " rpm")}  •  Quạt 2 {Show(r.Fan2Rpm, " rpm")}";
        }
        finally
        {
            _readingSensors = false;
        }

        static string Show(int? value, string unit) => value is { } v ? $"{v}{unit}" : "-";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this, "Có thay đổi chưa lưu. Lưu trước khi đóng?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes)
            {
                Save();
                if (_dirty)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.StateChanged -= OnEngineStateChanged;
        _sensorTimer.Dispose();
        _gameZone?.Dispose();
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------ helpers

    private void Warn(string message) => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static Button SmallButton(string text, Action onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static OptionalNumber[] CoreEditors() =>
        Enumerable.Range(0, DragonRange.CoresPerCcd)
            .Select(_ => new OptionalNumber(null, null, CpuTuner.MinCurveOffset, CpuTuner.MaxCurveOffset, -10))
            .ToArray();
}

/// <summary>A checkbox plus a number box; unchecked means "no value" (null).</summary>
internal sealed class OptionalNumber : FlowLayoutPanel
{
    private readonly CheckBox _enabled = new() { AutoSize = true, Margin = new Padding(3, 5, 0, 3) };
    private readonly NumericUpDown _number = new() { Width = 56 };
    private readonly int _fallback;
    private bool _setting;

    public OptionalNumber(string? label, string? unit, int min, int max, int fallback)
    {
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0);
        _fallback = Math.Clamp(fallback, min, max);
        _number.Minimum = min;
        _number.Maximum = max;
        _number.Value = _fallback;
        _number.Enabled = false;

        Controls.Add(_enabled);
        if (label is not null)
        {
            _enabled.Text = label;
            _enabled.MinimumSize = new Size(170, 0);
        }
        Controls.Add(_number);
        if (unit is not null)
            Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(3, 6, 3, 3) });

        _enabled.CheckedChanged += (_, _) =>
        {
            _number.Enabled = _enabled.Checked;
            if (!_setting)
                Changed?.Invoke();
        };
        _number.ValueChanged += (_, _) =>
        {
            if (!_setting && _enabled.Checked)
                Changed?.Invoke();
        };
    }

    public event Action? Changed;

    public int? Value
    {
        get => _enabled.Checked ? (int)_number.Value : null;
        set
        {
            _setting = true;
            _enabled.Checked = value is not null;
            _number.Value = Math.Clamp(value ?? (int)_number.Value, (int)_number.Minimum, (int)_number.Maximum);
            _number.Enabled = value is not null;
            _setting = false;
        }
    }
}
