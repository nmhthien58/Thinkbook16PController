using System.Runtime.InteropServices;

namespace Tb16p.Core.Hardware;

public enum PowerLine
{
    Battery,
    AC,
    Unknown,
}

public static class PowerSource
{
    public static PowerLine Current =>
        GetSystemPowerStatus(out var status)
            ? status.ACLineStatus switch { 0 => PowerLine.Battery, 1 => PowerLine.AC, _ => PowerLine.Unknown }
            : PowerLine.Unknown;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
