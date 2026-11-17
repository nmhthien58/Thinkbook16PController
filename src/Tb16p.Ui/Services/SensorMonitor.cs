using System.Windows.Threading;
using Tb16p.Core.Lenovo;

namespace Tb16p.Ui.Services;

/// <summary>
/// Reads the EC sensors every two seconds while the window is open, for the status header
/// and the live marker on the fan chart. The WMI calls run off the UI thread.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private GameZoneSensors? _sensors;
    private bool _opened;
    private bool _reading;

    public SensorMonitor() => _timer.Tick += async (_, _) => await ReadAsync();

    /// <summary>Latest reading, or null when the sensors cannot be read.</summary>
    public SensorReading? Latest { get; private set; }

    /// <summary>Raised on the UI thread after every read.</summary>
    public event Action? Updated;

    public bool Running
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled)
                return;
            if (value)
            {
                _timer.Start();
                _ = ReadAsync();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    private async Task ReadAsync()
    {
        if (_reading)
            return;
        _reading = true;
        try
        {
            if (!_opened)
            {
                _sensors = await Task.Run(GameZoneSensors.TryOpen);
                _opened = true;
            }
            Latest = _sensors is null ? null : await Task.Run(_sensors.Read);
        }
        finally
        {
            _reading = false;
        }
        Updated?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _sensors?.Dispose();
    }
}
