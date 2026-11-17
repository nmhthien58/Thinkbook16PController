using System.Globalization;
using Microsoft.Win32;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;

namespace Tb16p.Service;

/// <summary>
/// Appends a CSV row every two seconds with the EC temperatures, fan speeds and the state
/// that might drive the stock fan curve (Fn+Q mode, power source, profile). Used to learn
/// the curve the EC really runs, which does not fully match the tables the BIOS publishes.
/// One file per day in the settings folder.
/// </summary>
public sealed class SensorLogger : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private const string ItsKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider";
    private const string Header = "time,power,profile,fnq_mode,sensor1_c,cpu_c,sensor5_c,fan1_rpm,fan2_rpm";

    private readonly TuningEngine _engine;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private GameZoneSensors? _sensors;
    private bool _disposed;

    public SensorLogger(TuningEngine engine)
    {
        _engine = engine;
        _timer = new System.Threading.Timer(_ => Tick(), null, Interval, Interval);
    }

    public static string FilePathFor(DateTime day) =>
        Path.Combine(AppSettings.Directory, $"sensor-log-{day:yyyy-MM-dd}.csv");

    private void Tick()
    {
        if (!Monitor.TryEnter(_gate))
            return;
        try
        {
            if (_disposed || !_engine.Settings.SensorLogEnabled)
                return;

            _sensors ??= GameZoneSensors.TryOpen();
            if (_sensors is null)
                return;

            var now = DateTime.Now;
            var r = _sensors.Read();
            var row = string.Join(",",
                now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                _engine.PowerLine == PowerLine.Battery ? "battery" : "ac",
                Csv(_engine.Paused ? "(paused)" : _engine.ActiveProfile?.Name),
                FnQMode(),
                r.Sensor1C, r.CpuC, r.Sensor5C, r.Fan1Rpm, r.Fan2Rpm);

            var path = FilePathFor(now);
            var isNew = !File.Exists(path);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(isNew));
            if (isNew)
                writer.WriteLine(Header);
            writer.WriteLine(row);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file (e.g. open in Excel) only costs this sample.
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private static string FnQMode() => Registry.GetValue(ItsKey, "ITS_CurrentSetting", null) switch
    {
        0 => "auto",
        1 => "eco",
        3 => "performance",
        int other => other.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string Csv(string? value) =>
        value is null ? "" : value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _timer.Dispose();
        _sensors?.Dispose();
    }
}
