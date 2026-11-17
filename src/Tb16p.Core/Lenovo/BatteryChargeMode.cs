using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tb16p.Core.Lenovo;

public enum ChargeMode
{
    Normal,
    Conservation,
    RapidCharge,
}

/// <summary>
/// Battery charge mode through Lenovo's energy driver (\\.\EnergyDrv, installed with the
/// "Lenovo ACPI-Compliant Virtual Power Controller"). Codes and bit layout follow
/// Lenovo Legion Toolkit's BatteryFeature, which works on this laptop.
/// </summary>
public static class BatteryChargeMode
{
    private const uint IoctlBatteryChargeMode = 0x831020F8;
    private const uint QueryState = 0xFF;

    private const uint ConservationOn = 0x3;
    private const uint ConservationOff = 0x5;
    private const uint RapidChargeOn = 0x7;
    private const uint RapidChargeOff = 0x8;

    // Lenovo Vantage and LLT remember the user's choice here; keep it in sync so they agree.
    private const string VantageKey = @"HKEY_CURRENT_USER\Software\Lenovo\VantageService\AddinData\IdeaNotebookAddin";
    private const string VantageValue = "BatteryChargeMode";

    public static ChargeMode Get()
    {
        var raw = Send(QueryState);
        var state = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(raw);

        if (Bit(state, 17))
            return Bit(state, 26) ? ChargeMode.RapidCharge : ChargeMode.Normal;
        if (Bit(state, 29))
            return ChargeMode.Conservation;

        throw new InvalidOperationException($"Unknown battery state 0x{raw:X8}.");

        static bool Bit(uint value, int n) => ((value >> n) & 1) == 1;
    }

    public static void Set(ChargeMode mode)
    {
        var current = Get();
        if (current == mode)
            return;

        uint[] codes = mode switch
        {
            ChargeMode.Conservation => current == ChargeMode.RapidCharge ? [RapidChargeOff, ConservationOn] : [ConservationOn],
            ChargeMode.Normal => current == ChargeMode.Conservation ? [ConservationOff] : [RapidChargeOff],
            ChargeMode.RapidCharge => current == ChargeMode.Conservation ? [ConservationOff, RapidChargeOn] : [RapidChargeOn],
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        foreach (var code in codes)
            Send(code);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Get() == mode)
            {
                Microsoft.Win32.Registry.SetValue(VantageKey, VantageValue, mode switch
                {
                    ChargeMode.Conservation => "Storage",
                    ChargeMode.RapidCharge => "Quick",
                    _ => "Normal",
                });
                return;
            }
            Thread.Sleep(50);
        }

        throw new InvalidOperationException($"Battery mode did not change to {mode}.");
    }

    private static uint Send(uint input)
    {
        using var handle = Native.CreateFile(@"\\.\EnergyDrv", Native.FileReadData | Native.FileWriteData,
            Native.FileShareRead | Native.FileShareWrite, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open Lenovo energy driver (\\\\.\\EnergyDrv).");

        if (!Native.DeviceIoControl(handle, IoctlBatteryChargeMode, ref input, sizeof(uint), out var output, sizeof(uint), out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Battery charge mode IOCTL failed.");

        return output;
    }

    private static class Native
    {
        public const uint FileReadData = 0x0001;
        public const uint FileWriteData = 0x0002;
        public const uint FileShareRead = 0x1;
        public const uint FileShareWrite = 0x2;
        public const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref uint inBuffer, int inSize,
            out uint outBuffer, int outSize, out int returned, IntPtr overlapped);
    }
}
