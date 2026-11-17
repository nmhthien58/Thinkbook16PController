using Microsoft.Win32;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;

namespace Tb16p.Service;

/// <summary>
/// Keeps the active profile applied. The EC silently restores its own power limits
/// (and AC plug/unplug wipes limits and Curve Optimizer), and the PM table cannot be read
/// on Dragon Range to detect that, so limits are simply re-sent every few seconds and
/// everything is re-sent on power source changes and resume.
/// </summary>
public sealed class TuningEngine : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ChargeModeCheck = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DriverRetry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FanResend = TimeSpan.FromSeconds(30);
    private const int FanSensorFailureLimit = 3;
    private const string SmuErrorPrefix = "SMU: ";

    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private readonly FanCurveRunner _fanRunner = new();
    private GameZoneSensors? _gameZone;
    private bool _fansPinned;
    private bool _fanMax;
    private string? _fanSource;
    private int _fanSensorFailures;
    private DateTime _nextFanResend;

    private WinRing0Access? _access;
    private CpuTuner? _tuner;
    private PowerLine _lastPowerLine = PowerLine.Unknown;
    private DateTime _nextLimits, _nextCurveOptimizer, _nextChargeCheck, _nextDriverAttempt, _fullApplyAt;
    private bool _paused;
    private bool _disposed;

    public TuningEngine(AppSettings settings)
    {
        Settings = settings;
        _fullApplyAt = DateTime.MinValue;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _timer = new System.Threading.Timer(_ => OnTick(), null, TimeSpan.Zero, Tick);
    }

    /// <summary>Speed the fans are pinned to, or null when the EC runs them.</summary>
    public int? FanTargetRpm { get; private set; }

    /// <summary>Temperature the fan curve last reacted to.</summary>
    public int? FanControlTempC { get; private set; }

    public AppSettings Settings { get; private set; }
    public PowerLine PowerLine => _lastPowerLine;
    public Profile? ActiveProfile => Settings.Find(_lastPowerLine == PowerLine.Battery ? Settings.BatteryProfile : Settings.AcProfile);
    public string? LastError { get; private set; }
    public DateTime? LastApplied { get; private set; }
    public bool Paused => _paused;

    /// <summary>Raised from a worker thread whenever the visible state changes.</summary>
    public event Action? StateChanged;

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_paused == paused)
                return;
            _paused = paused;
            Log.Info(paused ? "Paused" : "Resumed");
            if (!paused)
                _fullApplyAt = DateTime.MinValue;
        }
        StateChanged?.Invoke();
    }

    public void ReplaceSettings(AppSettings settings)
    {
        lock (_gate)
        {
            Settings = settings;
            _fullApplyAt = DateTime.MinValue;
            _nextChargeCheck = DateTime.MinValue;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Re-sends everything on the next tick.</summary>
    public void RequestFullApply()
    {
        lock (_gate)
            _fullApplyAt = DateTime.MinValue;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        lock (_gate)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                // The app is frozen while asleep; never leave the fans pinned through that.
                ReleaseFans("suspend");
            }
            else if (e.Mode == PowerModes.Resume)
            {
                Log.Info("Resume from sleep");
                _fullApplyAt = DateTime.Now + ResumeDelay;
            }
            else if (e.Mode == PowerModes.StatusChange)
            {
                // Power source changes are picked up by polling on the next tick.
                _fullApplyAt = DateTime.MinValue;
            }
        }
    }

    private void OnTick()
    {
        if (!Monitor.TryEnter(_gate))
            return;
        var changed = false;
        try
        {
            if (_disposed)
                return;

            var now = DateTime.Now;
            var powerLine = PowerSource.Current;
            if (powerLine != _lastPowerLine)
            {
                Log.Info($"Power source: {_lastPowerLine} -> {powerLine}");
                _lastPowerLine = powerLine;
                _fullApplyAt = DateTime.MinValue;
                changed = true;
            }

            if (now >= _nextChargeCheck)
            {
                _nextChargeCheck = now + ChargeModeCheck;
                changed |= EnforceChargeMode();
            }

            // Fans go through WMI, independent of the SMU driver, so they run even if WinRing0 fails.
            changed |= FanStep(now);

            if (_paused || !EnsureDriver(now))
                return;

            var profile = ActiveProfile;
            if (profile is null)
                return;

            var full = now >= _fullApplyAt;
            if (full)
                _fullApplyAt = DateTime.MaxValue;

            if (profile.HasLimits && (full || now >= _nextLimits))
            {
                _nextLimits = now + TimeSpan.FromSeconds(Settings.ReapplyLimitsSeconds);
                changed |= Report(_tuner!.Apply(profile.Limits), full ? $"Applied profile \"{profile.Name}\"" : null);
            }

            if (profile.CurveOptimizer is { } co && (full || now >= _nextCurveOptimizer))
            {
                _nextCurveOptimizer = now + TimeSpan.FromSeconds(Settings.ReapplyCurveOptimizerSeconds);
                changed |= Report(_tuner!.Apply(co), full ? "Applied Curve Optimizer" : null);
            }
        }
        catch (Exception ex)
        {
            changed |= SetError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        if (changed)
            StateChanged?.Invoke();
    }

    private bool EnsureDriver(DateTime now)
    {
        if (_tuner is not null)
            return true;
        if (now < _nextDriverAttempt)
            return false;
        _nextDriverAttempt = now + DriverRetry;

        if (!DragonRange.IsSupportedCpu(out var cpu))
        {
            SetError($"Unsupported CPU ({cpu}); tuning disabled.");
            _nextDriverAttempt = DateTime.MaxValue;
            return false;
        }

        try
        {
            _access = WinRing0Access.Open();
            _tuner = new CpuTuner(new Smu(_access));
            Log.Info($"WinRing0 ready ({cpu})");
            return true;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Runs the active profile's fan curve. The EC holds a pinned speed indefinitely, so
    /// every path that stops following the curve must hand the fans back with speed 0.
    /// </summary>
    private bool FanStep(DateTime now)
    {
        var curve = _paused ? null : ActiveProfile?.FanCurve;
        var max = _fanMax && !_paused;
        if (curve is null && !max)
            return _fansPinned && ReleaseFans("no fan curve");

        _gameZone ??= GameZoneSensors.TryOpen();
        if (_gameZone is null)
            return SetError("Fan control: Lenovo GameZone WMI is not available.");

        var temp = _gameZone.Read().FanControlTempC;
        if (max)
            return PinFans(now, FanCurve.MaxRpm, temp, "Max fan");

        if (temp is null)
        {
            if (++_fanSensorFailures >= FanSensorFailureLimit && _fansPinned)
            {
                ReleaseFans("temperature unreadable");
                return SetError("Fan control: cannot read the temperature, fans handed back to the EC.");
            }
            return false;
        }
        _fanSensorFailures = 0;

        return PinFans(now, _fanRunner.Next(curve!, temp.Value), temp, "Fan curve");
    }

    private bool PinFans(DateTime now, int target, int? temp, string source)
    {
        var changed = target != FanTargetRpm || temp != FanControlTempC;
        FanControlTempC = temp;
        if (target == FanTargetRpm && now < _nextFanResend)
            return changed;

        if (!_gameZone!.SetFanSpeed(target))
            return SetError("Fan control: setting the fan speed failed.");

        if (!_fansPinned || source != _fanSource)
            Log.Info($"{source} active ({target} rpm at {temp} C)");
        _fanSource = source;
        _fansPinned = true;
        FanTargetRpm = target;
        _nextFanResend = now + FanResend;
        return true;
    }

    /// <summary>Test switch: both fans flat out regardless of the profile. Not saved.</summary>
    public bool FanMax => _fanMax;

    public void SetFanMax(bool on)
    {
        lock (_gate)
        {
            if (_fanMax == on)
                return;
            _fanMax = on;
            Log.Info($"Max fan {(on ? "ON" : "OFF")}");
            // Start the curve from scratch, and resend at once rather than in up to 30 s.
            _fanRunner.Reset();
            FanTargetRpm = null;
            _nextFanResend = DateTime.MinValue;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Gives the fans back to the EC. Caller holds <see cref="_gate"/> or is shutting down.</summary>
    private bool ReleaseFans(string reason)
    {
        if (!_fansPinned)
            return false;
        var released = _gameZone?.ReleaseFans() ?? false;
        Log.Info($"Fans handed back to the EC ({reason}){(released ? "" : " - FAILED")}");
        _fansPinned = !released;
        _fanSource = null;
        FanTargetRpm = null;
        _fanRunner.Reset();
        return true;
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        lock (_gate)
            ReleaseFans("session ending");
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        lock (_gate)
            ReleaseFans("process exit");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // The process is about to die; do not wait on the gate a stuck tick might hold.
        if (Monitor.TryEnter(_gate, TimeSpan.FromSeconds(2)))
        {
            try { ReleaseFans("crash"); }
            finally { Monitor.Exit(_gate); }
        }
        else
        {
            _gameZone?.ReleaseFans();
        }
    }

    private bool EnforceChargeMode()
    {
        if (Settings.ChargeMode is not { } wanted)
            return false;
        try
        {
            var current = BatteryChargeMode.Get();
            if (current == wanted)
                return false;
            BatteryChargeMode.Set(wanted);
            Log.Info($"Charge mode {current} -> {wanted}");
            return true;
        }
        catch (Exception ex)
        {
            return SetError($"Charge mode: {ex.Message}");
        }
    }

    private bool Report(IReadOnlyList<ApplyResult> results, string? successMessage)
    {
        var failures = results.Where(r => !r.Ok).ToList();
        if (failures.Count > 0)
            return SetError(SmuErrorPrefix + string.Join(", ", failures));

        LastApplied = DateTime.Now;
        if (successMessage is null)
        {
            // A one-off SMU hiccup (busy, or another tool using the mailbox) is fixed by the next
            // periodic re-send; do not leave its error on screen until the next full apply.
            if (LastError?.StartsWith(SmuErrorPrefix, StringComparison.Ordinal) != true)
                return false;
            Log.Info("SMU writes succeed again");
            LastError = null;
            return true;
        }

        Log.Info($"{successMessage}: {string.Join(", ", results)}");
        LastError = null;
        return true;
    }

    private bool SetError(string message)
    {
        if (message == LastError)
            return false;
        LastError = message;
        Log.Error(message);
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            ReleaseFans("exit");
        }
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _timer.Dispose();
        _access?.Dispose();
        _gameZone?.Dispose();
    }
}
