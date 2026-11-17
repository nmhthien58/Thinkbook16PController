using Tb16p.Core.Hardware;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;

// Small test tool for the core library. Examples:
//   tb16p info
//   tb16p limits --fast 70 --slow 55
//   tb16p co --ccd1 -8,-8,-14,-14,-14,-14,-14,-14 --ccd2 -16,-16,-16,-16,-16,-16,-16,-16
//   tb16p battery [get|normal|conservation|rapid]

try
{
    return args.FirstOrDefault()?.ToLowerInvariant() switch
    {
        "info" => Info(),
        "limits" => Limits(args[1..]),
        "co" => CurveOptimizerCommand(args[1..]),
        "battery" => Battery(args.ElementAtOrDefault(1)),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}

static int Usage()
{
    Console.WriteLine("""
        tb16p info
        tb16p limits [--stapm W] [--fast W] [--slow W] [--tctl C]
        tb16p co --ccd1 o1,o2,...,o8 [--ccd2 o1,...,o8]     (use _ to skip a core)
        tb16p battery [get|normal|conservation|rapid]
        """);
    return 2;
}

static int Info()
{
    var supported = DragonRange.IsSupportedCpu(out var cpu);
    Console.WriteLine($"CPU: {cpu} -> {(supported ? "supported (Dragon Range)" : "NOT supported")}");
    Console.WriteLine($"Power source: {PowerSource.Current}");
    try { Console.WriteLine($"Battery charge mode: {BatteryChargeMode.Get()}"); }
    catch (Exception ex) { Console.WriteLine($"Battery charge mode: error - {ex.Message}"); }
    return 0;
}

static int Limits(string[] args)
{
    var limits = new PowerLimits(
        StapmW: IntOption(args, "--stapm"),
        FastW: IntOption(args, "--fast"),
        SlowW: IntOption(args, "--slow"),
        TctlC: IntOption(args, "--tctl"));
    return WithTuner(tuner => tuner.Apply(limits));
}

static int CurveOptimizerCommand(string[] args)
{
    var co = new CurveOptimizer(Offsets(StringOption(args, "--ccd1")), Offsets(StringOption(args, "--ccd2")));
    return WithTuner(tuner => tuner.Apply(co));

    static int?[] Offsets(string? list)
    {
        var result = new int?[DragonRange.CoresPerCcd];
        if (list is null)
            return result;
        var parts = list.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > DragonRange.CoresPerCcd)
            throw new ArgumentException($"At most {DragonRange.CoresPerCcd} offsets per CCD.");
        for (var i = 0; i < parts.Length; i++)
            result[i] = parts[i] == "_" ? null : int.Parse(parts[i]);
        return result;
    }
}

static int Battery(string? action)
{
    switch (action?.ToLowerInvariant())
    {
        case null or "get":
            break;
        case "normal": BatteryChargeMode.Set(ChargeMode.Normal); break;
        case "conservation": BatteryChargeMode.Set(ChargeMode.Conservation); break;
        case "rapid": BatteryChargeMode.Set(ChargeMode.RapidCharge); break;
        default: return Usage();
    }
    Console.WriteLine($"Battery charge mode: {BatteryChargeMode.Get()}");
    return 0;
}

static int WithTuner(Func<CpuTuner, IReadOnlyList<ApplyResult>> apply)
{
    if (!DragonRange.IsSupportedCpu(out var cpu))
        throw new InvalidOperationException($"Refusing to touch the SMU: {cpu} is not Dragon Range.");

    using var access = WinRing0Access.Open();
    var results = apply(new CpuTuner(new Smu(access)));
    foreach (var result in results)
    {
        Console.ForegroundColor = result.Ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(result);
    }
    Console.ResetColor();
    return results.All(r => r.Ok) ? 0 : 3;
}

static string? StringOption(string[] args, string name)
{
    var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int? IntOption(string[] args, string name) => StringOption(args, name) is { } s ? int.Parse(s) : null;
