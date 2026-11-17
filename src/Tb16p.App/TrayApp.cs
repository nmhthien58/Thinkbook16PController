using Tb16p.Service;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;

namespace Tb16p.App;

public sealed class TrayApp : ApplicationContext
{
    private readonly TuningEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;
    private Icon? _currentIcon;
    private SettingsForm? _settingsForm;
    private readonly SensorLogger _sensorLogger;

    public TrayApp(AppSettings settings)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _engine = new TuningEngine(settings);
        _sensorLogger = new SensorLogger(_engine);
        _tray = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSettingsWindow();
        };
        _engine.StateChanged += () => _ui.Post(_ => Refresh(), null);
        Refresh();
    }

    private void Refresh()
    {
        var profile = _engine.ActiveProfile;
        var source = _engine.PowerLine == PowerLine.Battery ? "pin" : "cắm sạc";
        var state = _engine.Paused ? "TẠM DỪNG" : _engine.LastError is not null ? "LỖI" : profile?.Name ?? "?";
        var text = $"Thinkbook16PController\n{state} ({source})";
        _tray.Text = text.Length > 127 ? text[..127] : text;

        var color = _engine.Paused ? Color.Gray
            : _engine.LastError is not null ? Color.Firebrick
            : _engine.FanMax ? Color.DarkOrange
            : Color.SeaGreen;
        var label = profile?.SlowW?.ToString() ?? "L";
        SetIcon(DrawIcon(label, color));
    }

    private void BuildMenu()
    {
        var menu = _tray.ContextMenuStrip!;
        menu.Items.Clear();
        var settings = _engine.Settings;
        var profile = _engine.ActiveProfile;
        var source = _engine.PowerLine == PowerLine.Battery ? "đang dùng pin" : "đang cắm sạc";

        menu.Items.Add(new ToolStripMenuItem($"{profile?.Name ?? "?"} — {source}") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
        var status = _engine.Paused ? "Đã tạm dừng"
            : _engine.LastError is { } error ? $"Lỗi: {error}"
            : _engine.LastApplied is { } at ? $"Áp lần cuối lúc {at:HH:mm:ss}" : "Đang khởi động...";
        menu.Items.Add(new ToolStripMenuItem(status) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Mở giao diện quản lý", null, (_, _) => ShowSettingsWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(ProfileMenu("Profile khi cắm sạc", settings.AcProfile, name => settings.AcProfile = name));
        menu.Items.Add(ProfileMenu("Profile khi dùng pin", settings.BatteryProfile, name => settings.BatteryProfile = name));
        menu.Items.Add(ChargeModeMenu(settings));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Áp lại ngay", null, (_, _) => _engine.RequestFullApply());
        menu.Items.Add(new ToolStripMenuItem("Tạm dừng", null, (_, _) => _engine.SetPaused(!_engine.Paused)) { Checked = _engine.Paused });
        menu.Items.Add(new ToolStripMenuItem("Quạt tối đa (thử)", null, (_, _) => _engine.SetFanMax(!_engine.FanMax)) { Checked = _engine.FanMax });
        menu.Items.Add(StartupMenuItem());
        menu.Items.Add(new ToolStripMenuItem("Ghi nhật ký cảm biến", null, (_, _) =>
        {
            settings.SensorLogEnabled = !settings.SensorLogEnabled;
            Log.Info($"Sensor log: {settings.SensorLogEnabled}");
            SaveAndApply();
        })
        { Checked = settings.SensorLogEnabled });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Sửa cài đặt (settings.json)", null, (_, _) => OpenInNotepad(AppSettings.FilePath));
        menu.Items.Add("Tải lại cài đặt", null, (_, _) => ReloadSettings());
        menu.Items.Add("Xem nhật ký", null, (_, _) => OpenInNotepad(Log.FilePath));
        menu.Items.Add("Mở thư mục dữ liệu", null, (_, _) => OpenDataFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitThreadCore());
    }

    private ToolStripMenuItem ProfileMenu(string title, string selected, Action<string> select)
    {
        var item = new ToolStripMenuItem(title);
        foreach (var p in _engine.Settings.Profiles)
        {
            var name = p.Name;
            item.DropDownItems.Add(new ToolStripMenuItem(Describe(p), null, (_, _) =>
            {
                select(name);
                SaveAndApply();
            })
            { Checked = string.Equals(name, selected, StringComparison.OrdinalIgnoreCase) });
        }
        return item;

        static string Describe(Profile p) => p.HasLimits
            ? $"{p.Name}   (Slow {p.SlowW?.ToString() ?? "-"} / Fast {p.FastW?.ToString() ?? "-"} W{(p.CurveOptimizer is null ? "" : ", CO")})"
            : $"{p.Name}   (không can thiệp)";
    }

    private ToolStripMenuItem ChargeModeMenu(AppSettings settings)
    {
        var item = new ToolStripMenuItem("Chế độ sạc pin");
        Add("Conservation (giữ pin ~60-80%)", ChargeMode.Conservation);
        Add("Normal", ChargeMode.Normal);
        Add("Rapid Charge", ChargeMode.RapidCharge);
        item.DropDownItems.Add(new ToolStripSeparator());
        Add("Không quản lý", null);
        return item;

        void Add(string text, ChargeMode? mode) =>
            item.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) =>
            {
                settings.ChargeMode = mode;
                SaveAndApply();
            })
            { Checked = settings.ChargeMode == mode });
    }

    private ToolStripMenuItem StartupMenuItem()
    {
        bool enabled;
        try { enabled = StartupTask.IsEnabled(); }
        catch { enabled = false; }

        return new ToolStripMenuItem("Chạy cùng Windows", null, (_, _) =>
        {
            try
            {
                if (enabled) StartupTask.Disable(); else StartupTask.Enable();
                Log.Info($"Start with Windows: {!enabled}");
            }
            catch (Exception ex)
            {
                ShowError($"Không đổi được chế độ chạy cùng Windows: {ex.Message}");
            }
        })
        { Checked = enabled };
    }

    private void SaveAndApply()
    {
        try
        {
            _engine.Settings.Validate();
            _engine.Settings.Save();
            _engine.ReplaceSettings(_engine.Settings);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ReloadSettings()
    {
        try
        {
            _engine.ReplaceSettings(AppSettings.Load());
            Log.Info("Settings reloaded");
            _tray.ShowBalloonTip(2000, "Thinkbook16PController", "Đã tải lại cài đặt.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"settings.json không hợp lệ, vẫn giữ cài đặt cũ.\n\n{ex.Message}");
        }
    }

    private static void OpenInNotepad(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void OpenDataFolder()
    {
        Directory.CreateDirectory(AppSettings.Directory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Directory}\"") { UseShellExecute = true });
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "Thinkbook16PController", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowSettingsWindow()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_engine);
        _settingsForm.Show();
    }

    private static Icon DrawIcon(string label, Color background)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(background);
            g.FillEllipse(brush, 0, 0, 31, 31);
            using var font = new Font("Segoe UI", label.Length > 2 ? 10 : 14, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(label, font);
            g.DrawString(label, font, Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void SetIcon(Icon icon)
    {
        var old = _currentIcon;
        _tray.Icon = icon;
        _currentIcon = icon;
        old?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        Log.Info("Exit");
        _settingsForm?.Close();
        _tray.Visible = false;
        _sensorLogger.Dispose();
        _engine.Dispose();
        _tray.Dispose();
        _currentIcon?.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
