# Checks before building the app:
#  1. RyzenAdj init (no args) and one power-limit write (slow 55 W / fast 70 W, clearly different from the UXTU preset)
#  2. Lenovo EnergyDrv battery charge mode read (read-only)
# Close UXTU first so it does not overwrite the test values.
$out = Join-Path $PSScriptRoot 'verify-readonly-report.txt'
Start-Transcript -Path $out -Force | Out-Null

$ra = Join-Path $PSScriptRoot '..\vendor\ryzenadj-0.19.0\ryzenadj.exe'
Push-Location (Split-Path $ra)
"=== RyzenAdj init only ==="
& $ra 2>&1 | Out-String -Width 200
"exit=$LASTEXITCODE"
"`n=== RyzenAdj write: slow 55 W, fast 70 W ==="
& $ra --slow-limit=55000 --fast-limit=70000 2>&1 | Out-String -Width 200
"exit=$LASTEXITCODE"
Pop-Location

"`n=== EnergyDrv battery charge mode (read-only, in=0xFF) ==="
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class Energy {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint inBuf, int inSize, out uint outBuf, int outSize, out int ret, IntPtr ov);
    public const uint IOCTL_BATTERY_CHARGE_MODE = 0x831020F8;
    public static string ReadChargeMode() {
        using (var h = CreateFile(@"\\.\EnergyDrv", 0x0001 | 0x0002, 0x1 | 0x2, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (h.IsInvalid) return "open failed, err=" + Marshal.GetLastWin32Error();
            uint input = 0xFF, o; int r;
            if (!DeviceIoControl(h, IOCTL_BATTERY_CHARGE_MODE, ref input, 4, out o, 4, out r, IntPtr.Zero)) return "ioctl failed, err=" + Marshal.GetLastWin32Error();
            return string.Format("raw=0x{0:X8}", o);
        }
    }
}
'@
$raw = [Energy]::ReadChargeMode()
"charge mode: $raw"
if ($raw -match 'raw=0x([0-9A-F]{8})') {
    $v = [Convert]::ToUInt32($Matches[1], 16)
    $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b)   # reverse endianness, as LLT does
    $r = [BitConverter]::ToUInt32($b, 0)
    $bit = { param($n) (($r -shr $n) -band 1) -eq 1 }
    $state = if (& $bit 17) { if (& $bit 26) { 'RapidCharge' } else { 'Normal' } } elseif (& $bit 29) { 'Conservation' } else { 'Unknown' }
    "reversed=0x{0:X8} bit17={1} bit26={2} bit29={3} -> {4}" -f $r, (& $bit 17), (& $bit 26), (& $bit 29), $state
}

Stop-Transcript | Out-Null
Read-Host 'Nhan Enter de dong'
