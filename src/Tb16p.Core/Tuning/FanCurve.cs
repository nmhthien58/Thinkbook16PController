namespace Tb16p.Core.Tuning;

public sealed class FanPoint
{
    public int TempC { get; set; }
    public int Rpm { get; set; }

    public FanPoint() { }
    public FanPoint(int tempC, int rpm) { TempC = tempC; Rpm = rpm; }

    public FanPoint Clone() => new(TempC, Rpm);
}

/// <summary>
/// Temperature to fan speed curve. Points are sorted by temperature and the speed never
/// drops as the temperature rises; between points the speed is interpolated linearly,
/// outside them it is held at the first or last point.
/// </summary>
public static class FanCurve
{
    // Limits from the EC fan test data (two fans, 1500-5500 rpm) and a sane temperature axis.
    public const int MinRpm = 1500;
    public const int MaxRpm = 5500;
    public const int MinTempC = 30;
    public const int MaxTempC = 100;
    public const int MinPoints = 2;
    public const int MaxPoints = 16;

    /// <summary>At or above this temperature the fans run flat out whatever the curve says.</summary>
    public const int EmergencyTempC = 95;

    public static int Evaluate(IReadOnlyList<FanPoint> points, double tempC)
    {
        if (points.Count == 0)
            throw new ArgumentException("Empty fan curve.", nameof(points));
        if (tempC <= points[0].TempC)
            return points[0].Rpm;
        for (var i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            if (tempC <= b.TempC)
                return (int)Math.Round(a.Rpm + (b.Rpm - a.Rpm) * (tempC - a.TempC) / (b.TempC - a.TempC));
        }
        return points[^1].Rpm;
    }

    /// <summary>Returns an error message, or null when the curve is usable.</summary>
    public static string? Validate(IReadOnlyList<FanPoint> points)
    {
        if (points.Count is < MinPoints or > MaxPoints)
            return $"A fan curve needs {MinPoints}-{MaxPoints} points.";
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.TempC is < MinTempC or > MaxTempC)
                return $"Temperatures must be {MinTempC}-{MaxTempC} °C.";
            if (p.Rpm is < MinRpm or > MaxRpm)
                return $"Fan speeds must be {MinRpm}-{MaxRpm} rpm.";
            if (i > 0 && p.TempC <= points[i - 1].TempC)
                return "Fan curve temperatures must increase.";
            if (i > 0 && p.Rpm < points[i - 1].Rpm)
                return "Fan speed must not drop as the temperature rises.";
        }
        return null;
    }

    /// <summary>
    /// What the EC really runs, measured from ~12,000 sensor log samples (2026-11-17, AC,
    /// Fn+Q performance, 56-89 °C CPU): fixed steps that rise at 76/79/82/85 °C and drop
    /// about 3 °C lower. Much gentler than the BIOS fan tables. Nothing was seen below
    /// 2100 rpm or above 89 °C; the last segment just ramps into the emergency limit.
    /// </summary>
    public static List<FanPoint> LenovoStock() =>
        [new(72, 2100), new(76, 3100), new(79, 3400), new(82, 3700), new(85, 3900), new(89, 3900), new(95, 5500)];

    public static List<FanPoint> Quiet() =>
        [new(50, 1600), new(60, 2000), new(70, 2600), new(80, 3400), new(88, 4500), new(95, 5500)];

    public static List<FanPoint> Balanced() =>
        [new(45, 1800), new(60, 2400), new(70, 3200), new(78, 3900), new(85, 4600), new(92, 5500)];

    public static List<FanPoint> Cool() =>
        [new(40, 2200), new(55, 3000), new(65, 3800), new(75, 4600), new(82, 5200), new(90, 5500)];
}

/// <summary>
/// Turns temperature samples into fan targets with hysteresis: the speed follows a rising
/// temperature at once but only comes down after the temperature has dropped a few degrees,
/// so the fans do not hunt up and down around a curve point.
/// </summary>
public sealed class FanCurveRunner(int hysteresisC = 3)
{
    private double? _effectiveTemp;

    public void Reset() => _effectiveTemp = null;

    public int Next(IReadOnlyList<FanPoint> curve, double tempC)
    {
        if (tempC >= FanCurve.EmergencyTempC)
        {
            _effectiveTemp = tempC;
            return FanCurve.MaxRpm;
        }

        if (_effectiveTemp is not { } effective || tempC >= effective || tempC <= effective - hysteresisC)
            _effectiveTemp = tempC;

        return Math.Clamp(FanCurve.Evaluate(curve, _effectiveTemp!.Value), FanCurve.MinRpm, FanCurve.MaxRpm);
    }
}
