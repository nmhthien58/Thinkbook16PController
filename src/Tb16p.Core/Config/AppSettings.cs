using System.Text.Json;
using System.Text.Json.Serialization;
using Tb16p.Core.Lenovo;
using Tb16p.Core.Tuning;

namespace Tb16p.Core.Config;

/// <summary>
/// One tuning preset. Null fields are left to the firmware. Curve Optimizer arrays hold
/// one offset per core (8 per CCD); null entries leave that core alone.
/// </summary>
public sealed class Profile
{
    public required string Name { get; set; }
    public int? StapmW { get; set; }
    public int? FastW { get; set; }
    public int? SlowW { get; set; }
    public int? TctlC { get; set; }
    public int?[]? CurveOptimizerCcd1 { get; set; }
    public int?[]? CurveOptimizerCcd2 { get; set; }

    [JsonIgnore]
    public PowerLimits Limits => new(StapmW, FastW, SlowW, TctlC);

    [JsonIgnore]
    public bool HasLimits => StapmW is not null || FastW is not null || SlowW is not null || TctlC is not null;

    [JsonIgnore]
    public CurveOptimizer? CurveOptimizer =>
        CurveOptimizerCcd1 is null && CurveOptimizerCcd2 is null
            ? null
            : new CurveOptimizer(Pad(CurveOptimizerCcd1), Pad(CurveOptimizerCcd2));

    private static int?[] Pad(int?[]? offsets)
    {
        var result = new int?[Hardware.DragonRange.CoresPerCcd];
        offsets?.Take(result.Length).ToArray().CopyTo(result, 0);
        return result;
    }
}

public sealed class AppSettings
{
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Profile used while the charger is plugged in.</summary>
    public string AcProfile { get; set; } = "";

    /// <summary>Profile used on battery.</summary>
    public string BatteryProfile { get; set; } = "";

    /// <summary>Charge mode enforced at startup and checked periodically; null = do not manage.</summary>
    public ChargeMode? ChargeMode { get; set; }

    /// <summary>Writes EC temperatures and fan speeds to a daily CSV, to learn the stock fan curve.</summary>
    public bool SensorLogEnabled { get; set; }

    /// <summary>How often power limits are re-sent, because the EC silently restores its own.</summary>
    public int ReapplyLimitsSeconds { get; set; } = 2;

    /// <summary>How often Curve Optimizer is re-sent (it is also re-sent on every power or resume event).</summary>
    public int ReapplyCurveOptimizerSeconds { get; set; } = 30;

    public Profile? Find(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Thinkbook16PController");

    public static string FilePath { get; } = Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        // Keep Vietnamese profile names readable in settings.json instead of \uXXXX escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = CreateDefaults();
            defaults.Save();
            return defaults;
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
            ?? throw new InvalidDataException("settings.json is empty.");
        settings.Validate();
        return settings;
    }

    /// <summary>Deep copy, so an editor can change settings without touching the live ones.</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Validate()
    {
        if (Profiles.Count == 0)
            throw new InvalidDataException("settings.json has no profiles.");
        if (Profiles.Any(p => string.IsNullOrWhiteSpace(p.Name)))
            throw new InvalidDataException("Every profile needs a name.");
        if (Profiles.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Profile names must be unique.");
        foreach (var p in Profiles)
        {
            foreach (var (label, watts) in new[] { ("STAPM", p.StapmW), ("Fast", p.FastW), ("Slow", p.SlowW) })
            {
                if (watts is < CpuTuner.MinPowerW or > CpuTuner.MaxPowerW)
                    throw new InvalidDataException($"\"{p.Name}\": {label} must be {CpuTuner.MinPowerW}-{CpuTuner.MaxPowerW} W.");
            }
            if (p.TctlC is < CpuTuner.MinTctlC or > CpuTuner.MaxTctlC)
                throw new InvalidDataException($"\"{p.Name}\": Tctl must be {CpuTuner.MinTctlC}-{CpuTuner.MaxTctlC} C.");
            if (new[] { p.CurveOptimizerCcd1, p.CurveOptimizerCcd2 }.Where(a => a is not null).SelectMany(a => a!)
                .Any(o => o is < CpuTuner.MinCurveOffset or > CpuTuner.MaxCurveOffset))
                throw new InvalidDataException($"\"{p.Name}\": Curve Optimizer offsets must be {CpuTuner.MinCurveOffset}..{CpuTuner.MaxCurveOffset}.");
        }
        if (Find(AcProfile) is null)
            throw new InvalidDataException($"acProfile \"{AcProfile}\" does not match any profile.");
        if (Find(BatteryProfile) is null)
            throw new InvalidDataException($"batteryProfile \"{BatteryProfile}\" does not match any profile.");
        ReapplyLimitsSeconds = Math.Clamp(ReapplyLimitsSeconds, 1, 60);
        ReapplyCurveOptimizerSeconds = Math.Clamp(ReapplyCurveOptimizerSeconds, 5, 600);
    }

    private static AppSettings CreateDefaults()
    {
        // The user's per-core Curve Optimizer, stable for months under UXTU.
        int?[] ccd1 = [-8, -8, -14, -14, -14, -14, -14, -14];
        int?[] ccd2 = [-16, -16, -16, -16, -16, -16, -16, -16];

        return new AppSettings
        {
            Profiles =
            [
                new() { Name = "Yên tĩnh 45W", StapmW = 45, SlowW = 45, FastW = 55, CurveOptimizerCcd1 = ccd1, CurveOptimizerCcd2 = ccd2 },
                new() { Name = "Cân bằng 60W", StapmW = 60, SlowW = 60, FastW = 80, CurveOptimizerCcd1 = ccd1, CurveOptimizerCcd2 = ccd2 },
                new() { Name = "Mạnh 75W", StapmW = 75, SlowW = 75, FastW = 90, CurveOptimizerCcd1 = ccd1, CurveOptimizerCcd2 = ccd2 },
                new() { Name = "Mặc định Lenovo" },
            ],
            AcProfile = "Cân bằng 60W",
            BatteryProfile = "Yên tĩnh 45W",
            ChargeMode = Lenovo.ChargeMode.Conservation,
        };
    }
}
