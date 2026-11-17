using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;
using Tb16p.Service;
using Tb16p.Ui.Localization;
using Forms = System.Windows.Forms;

namespace Tb16p.Ui.Services;

/// <summary>
/// Tray icon: a coloured disc with the active sustained power limit, and a menu for the
/// things worth changing without opening the window.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly Forms.NotifyIcon _icon;
    private Icon? _current;

    public TrayIcon(AppController app)
    {
        _app = app;
        _icon = new Forms.NotifyIcon { Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _app.ShowMainWindow();
        };
        _app.StateChanged += Refresh;
        _app.SettingsChanged += Refresh;
        Refresh();
    }

    private TuningEngine Engine => _app.Engine;

    private void Refresh()
    {
        var profile = Engine.ActiveProfile;
        var source = Loc.T(Engine.PowerLine == PowerLine.Battery ? "Power_Battery" : "Power_Ac");
        var state = Engine.Paused ? Loc.T("Status_Paused")
            : Engine.LastError is not null ? Loc.T("Status_ErrorShort")
            : profile?.Name ?? "?";
        var text = $"{AppInfo.Name}\n{state} · {source}";
        _icon.Text = text.Length > 127 ? text[..127] : text;

        var color = Engine.Paused ? Color.Gray
            : Engine.LastError is not null ? Color.Firebrick
            : Engine.FanMax ? Color.DarkOrange
            : Color.SeaGreen;
        SetIcon(DrawIcon(profile?.SlowW?.ToString() ?? "L", color));
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        var settings = _app.Settings;
        var profile = Engine.ActiveProfile;
        var source = Loc.T(Engine.PowerLine == PowerLine.Battery ? "Power_Battery" : "Power_Ac");

        menu.Items.Add(new Forms.ToolStripMenuItem($"{profile?.Name ?? "?"} · {source}") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new Forms.ToolStripMenuItem(_app.Header.StatusText) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("Tray_Open"), null, (_, _) => _app.ShowMainWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(ProfileMenu(Loc.T("Tray_AcProfile"), settings.AcProfile, (s, name) => s.AcProfile = name));
        menu.Items.Add(ProfileMenu(Loc.T("Tray_BatteryProfile"), settings.BatteryProfile, (s, name) => s.BatteryProfile = name));
        menu.Items.Add(ChargeModeMenu(settings.ChargeMode));
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(Loc.T("Action_Reapply"), null, (_, _) => Engine.RequestFullApply());
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("Action_Pause"), null, (_, _) => Engine.SetPaused(!Engine.Paused)) { Checked = Engine.Paused });
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("Action_FanMax"), null, (_, _) => Engine.SetFanMax(!Engine.FanMax)) { Checked = Engine.FanMax });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Settings_OpenFolder"), null, (_, _) => AppController.OpenDataFolder());
        menu.Items.Add(Loc.T("Tray_Exit"), null, async (_, _) => await _app.ExitAsync());
    }

    private Forms.ToolStripMenuItem ProfileMenu(string title, string selected, Action<AppSettings, string> select)
    {
        var item = new Forms.ToolStripMenuItem(title);
        foreach (var p in _app.Settings.Profiles)
        {
            var name = p.Name;
            item.DropDownItems.Add(new Forms.ToolStripMenuItem(Describe(p), null, (_, _) => _app.UpdateSettings(s => select(s, name)))
            {
                Checked = string.Equals(name, selected, StringComparison.OrdinalIgnoreCase),
            });
        }
        return item;

        static string Describe(Profile p) => p.HasLimits
            ? $"{p.Name}   (Slow {p.SlowW?.ToString() ?? "-"} / Fast {p.FastW?.ToString() ?? "-"} W{(p.CurveOptimizer is null ? "" : ", CO")})"
            : $"{p.Name}   ({Loc.T("Tray_NoTuning")})";
    }

    private Forms.ToolStripMenuItem ChargeModeMenu(ChargeMode? selected)
    {
        var item = new Forms.ToolStripMenuItem(Loc.T("General_Charge_Title"));
        Add("Charge_Conservation", ChargeMode.Conservation);
        Add("Charge_Normal", ChargeMode.Normal);
        Add("Charge_Rapid", ChargeMode.RapidCharge);
        item.DropDownItems.Add(new Forms.ToolStripSeparator());
        Add("Charge_Unmanaged", null);
        return item;

        void Add(string key, ChargeMode? mode) =>
            item.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T(key), null, (_, _) => _app.UpdateSettings(s => s.ChargeMode = mode))
            {
                Checked = selected == mode,
            });
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
        var old = _current;
        _icon.Icon = icon;
        _current = icon;
        old?.Dispose();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
