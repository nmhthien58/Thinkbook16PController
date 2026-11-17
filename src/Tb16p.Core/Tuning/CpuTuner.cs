using Tb16p.Core.Hardware;

namespace Tb16p.Core.Tuning;

/// <summary>Power limits in watts and temperature in degrees C; null leaves a value untouched.</summary>
public sealed record PowerLimits(int? StapmW = null, int? FastW = null, int? SlowW = null, int? TctlC = null);

/// <summary>Per-core Curve Optimizer offsets; index = core within the CCD, null = leave alone.</summary>
public sealed record CurveOptimizer(int?[] Ccd1, int?[] Ccd2)
{
    public static CurveOptimizer None { get; } = new(new int?[DragonRange.CoresPerCcd], new int?[DragonRange.CoresPerCcd]);
}

public sealed record ApplyResult(string Setting, SmuStatus Status)
{
    public bool Ok => Status == SmuStatus.Ok;
    public override string ToString() => $"{Setting}: {Status}";
}

public sealed class CpuTuner(Smu smu)
{
    // Guard rails: well inside what the firmware itself allows for this laptop
    // (long-term 50-90 W, peak 70-155 W, CPU temperature limit 85-100 C).
    public const int MinPowerW = 15;
    public const int MaxPowerW = 155;
    public const int MinTctlC = 60;
    public const int MaxTctlC = 100;
    public const int MinCurveOffset = -30;
    public const int MaxCurveOffset = 10;

    public IReadOnlyList<ApplyResult> Apply(PowerLimits limits)
    {
        var results = new List<ApplyResult>();
        Power("STAPM", DragonRange.SetStapmLimit, limits.StapmW);
        Power("Fast", DragonRange.SetFastLimit, limits.FastW);
        Power("Slow", DragonRange.SetSlowLimit, limits.SlowW);

        if (limits.TctlC is { } tctl)
        {
            CheckRange(tctl, MinTctlC, MaxTctlC, "Tctl");
            results.Add(new($"Tctl {tctl} C", smu.Send(DragonRange.Mp1, DragonRange.SetTctlTemp, (uint)tctl)));
        }

        return results;

        void Power(string name, uint message, int? watts)
        {
            if (watts is not { } w)
                return;
            CheckRange(w, MinPowerW, MaxPowerW, name);
            results.Add(new($"{name} {w} W", smu.Send(DragonRange.Mp1, message, (uint)w * 1000)));
        }
    }

    public IReadOnlyList<ApplyResult> Apply(CurveOptimizer co)
    {
        var results = new List<ApplyResult>();
        Ccd(0, co.Ccd1);
        Ccd(1, co.Ccd2);
        return results;

        void Ccd(int ccd, int?[] offsets)
        {
            for (var core = 0; core < Math.Min(offsets.Length, DragonRange.CoresPerCcd); core++)
            {
                if (offsets[core] is not { } offset)
                    continue;
                CheckRange(offset, MinCurveOffset, MaxCurveOffset, $"CCD{ccd + 1} core {core + 1}");
                var argument = DragonRange.EncodeCurveOptimizer(ccd, core, offset);
                results.Add(new($"CO CCD{ccd + 1} core {core + 1} = {offset}",
                    smu.Send(DragonRange.Psmu, DragonRange.SetCurveOptimizerPerCore, argument)));
            }
        }
    }

    private static void CheckRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be between {min} and {max}.");
    }
}
