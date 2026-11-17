using System.Runtime.InteropServices;

namespace Tb16p.Core.Hardware;

/// <summary>
/// SMU register access through WinRing0 (OpenLibSys). The SMU registers are reached
/// indirectly through the host bridge (bus 0, device 0, function 0): write the SMU
/// address to config offset 0xB8, then read or write the data at 0xBC.
/// WinRing0x64.dll and WinRing0x64.sys must sit next to the executable.
/// </summary>
public sealed class WinRing0Access : ISmuRegisterAccess
{
    private const uint HostBridgePciAddress = 0x00000000;
    private const uint SmuIndexRegister = 0xB8;
    private const uint SmuDataRegister = 0xBC;

    // Same global mutex HWiNFO, UXTU and LibreHardwareMonitor take around SMU and PCI
    // access, so two tools never interleave their index/data writes or mailbox commands.
    private const string PciMutexName = @"Global\Access_PCI";

    private readonly Mutex _pciMutex;
    private bool _disposed;

    private WinRing0Access(Mutex pciMutex) => _pciMutex = pciMutex;

    public static WinRing0Access Open()
    {
        bool initialised;
        try
        {
            initialised = Native.InitializeOls();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("WinRing0x64.dll not found next to the executable.", ex);
        }

        var status = Native.GetDllStatus();
        if (!initialised || status != 0)
        {
            Native.DeinitializeOls();
            throw new InvalidOperationException(
                $"WinRing0 could not load its driver (status {status}). Run as administrator and make sure " +
                "Defender does not block WinRing0x64.sys (the app folder must be in the exclusions).");
        }

        return new WinRing0Access(new Mutex(false, PciMutexName));
    }

    public bool TryEnter(TimeSpan timeout)
    {
        try
        {
            return _pciMutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // Another process died while holding it; ownership passed to us.
            return true;
        }
    }

    public void Exit() => _pciMutex.ReleaseMutex();

    public bool TryRead(uint smuAddress, out uint value)
    {
        value = 0;
        return Native.WritePciConfigDwordEx(HostBridgePciAddress, SmuIndexRegister, smuAddress)
            && Native.ReadPciConfigDwordEx(HostBridgePciAddress, SmuDataRegister, out value);
    }

    public bool TryWrite(uint smuAddress, uint value) =>
        Native.WritePciConfigDwordEx(HostBridgePciAddress, SmuIndexRegister, smuAddress)
        && Native.WritePciConfigDwordEx(HostBridgePciAddress, SmuDataRegister, value);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pciMutex.Dispose();
        Native.DeinitializeOls();
    }

    private static class Native
    {
        private const string Dll = "WinRing0x64.dll";

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeOls();

        [DllImport(Dll)]
        public static extern void DeinitializeOls();

        [DllImport(Dll)]
        public static extern uint GetDllStatus();

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadPciConfigDwordEx(uint pciAddress, uint regAddress, out uint value);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WritePciConfigDwordEx(uint pciAddress, uint regAddress, uint value);
    }
}
