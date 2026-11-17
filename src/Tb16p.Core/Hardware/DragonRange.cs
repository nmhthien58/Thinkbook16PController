using System.Runtime.Intrinsics.X86;

namespace Tb16p.Core.Hardware;

/// <summary>
/// SMU layout for Dragon Range (Ryzen 7045HX / 8045HX, e.g. Ryzen 9 8945HX), taken from
/// UXTU's Socket_AM5_V1 table, which is proven to work on the target laptop.
/// </summary>
public static class DragonRange
{
    public static readonly SmuMailbox Mp1 = new("MP1", 0x03B10530, 0x03B1057C, 0x03B109C4);
    public static readonly SmuMailbox Psmu = new("PSMU", 0x03B10524, 0x03B10570, 0x03B10A40);

    // MP1 messages; power values in mW, temperature in degrees C.
    public const uint SetStapmLimit = 0x4F;
    public const uint SetFastLimit = 0x3E;
    public const uint SetSlowLimit = 0x5F;
    public const uint SetTctlTemp = 0x3F;

    // PSMU messages.
    public const uint SetCurveOptimizerPerCore = 0x06;

    public const int Ccds = 2;
    public const int CoresPerCcd = 8;

    /// <summary>
    /// True for AMD family 19h model 61h (Dragon Range), the only layout this tool knows.
    /// </summary>
    public static bool IsSupportedCpu(out string description)
    {
        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        var vendor = string.Concat(Ascii(ebx), Ascii(edx), Ascii(ecx));
        var (eax, _, _, _) = X86Base.CpuId(1, 0);
        var family = ((eax >> 8) & 0xF) + ((eax >> 20) & 0xFF);
        var model = (((eax >> 16) & 0xF) << 4) | ((eax >> 4) & 0xF);
        description = $"{vendor} family {family:X}h model {model:X2}h";
        return vendor == "AuthenticAMD" && family == 0x19 && model == 0x61;

        static string Ascii(int register) => string.Concat(BitConverter.GetBytes(register).Select(b => (char)b));
    }

    /// <summary>
    /// Per-core Curve Optimizer argument, same encoding UXTU uses for Dragon/Fire Range:
    /// bits 20+ select the core as ((ccd &lt;&lt; 4 | ccx) &lt;&lt; 4 | core), bits 0-19 hold the
    /// offset as a 20-bit two's complement number.
    /// </summary>
    public static uint EncodeCurveOptimizer(int ccd, int core, int offset)
    {
        if (ccd is < 0 or >= Ccds)
            throw new ArgumentOutOfRangeException(nameof(ccd));
        if (core is < 0 or >= CoresPerCcd)
            throw new ArgumentOutOfRangeException(nameof(core));

        var magnitude = (uint)Math.Min(Math.Abs(offset), 0xFFFFF);
        var encodedOffset = offset < 0 ? (0x100000 - magnitude) & 0xFFFFF : magnitude & 0xFFFFF;
        var coreSelector = (uint)((((ccd << 4) | (core / 8 & 0xF)) << 4) | (core % 8 & 0xF));
        return (coreSelector << 20) | encodedOffset;
    }
}
