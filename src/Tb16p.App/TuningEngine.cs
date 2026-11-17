using Microsoft.Win32;
using Tb16p.Core.Config;
using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;

namespace Tb16p.App;

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

    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;

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
        _timer = new System.Threading.Timer(_ => OnTick(), null, TimeSpan.Zero, Tick);
    }

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
            if (e.Mode == PowerModes.Resume)
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
            return SetError("SMU: " + string.Join(", ", failures));

        LastApplied = DateTime.Now;
        if (successMessage is null)
            return false;

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
        }
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Dispose();
        _access?.Dispose();
    }
}
