# Fan control test through SetFeatureValue, based on the DSDT (\_SB.GZFD.WMAE):
#   0x04020000 = 1/0      -> EC FNST bit (fan full speed on/off)
#   0x04030001 = rpm      -> EC command 0xD1, fan 1, rpm / 100
#   0x04030002 = rpm      -> EC command 0xD1, fan 2, rpm / 100
# Only raises fan speed, then tries value 0 to see whether the EC returns to automatic control.
# Safety: if CPU temperature passes 90 C, full speed is switched on and the test stops.
# Run elevated with the laptop idle.
$out = Join-Path $PSScriptRoot 'fan-rpm-test-report.txt'
Start-Transcript -Path $out -Force | Out-Null

$om = Get-WmiObject -Namespace root\WMI -Class LENOVO_OTHER_METHOD | Select-Object -First 1
if (-not $om) { 'LENOVO_OTHER_METHOD not found'; Stop-Transcript | Out-Null; exit 1 }

$FullSpeed = [Convert]::ToUInt32('04020000', 16)
$Fan1      = [Convert]::ToUInt32('04030001', 16)
$Fan2      = [Convert]::ToUInt32('04030002', 16)
$CpuTemp   = [Convert]::ToUInt32('05040000', 16)

function GetF([uint32]$id) { $p = $om.GetMethodParameters('GetFeatureValue'); $p['IDs'] = $id; [int]$om.InvokeMethod('GetFeatureValue', $p, $null).value }
function SetF([uint32]$id, [uint32]$v) {
    try { $p = $om.GetMethodParameters('SetFeatureValue'); $p['IDs'] = $id; $p['value'] = $v; $null = $om.InvokeMethod('SetFeatureValue', $p, $null); 'OK' }
    catch { "ERR $($_.Exception.Message)" }
}

$script:aborted = $false
function Watch([string]$label, [int]$seconds) {
    $f1 = @(); $f2 = @()
    for ($i = 0; $i -lt $seconds; $i++) {
        $a = GetF $Fan1; $b = GetF $Fan2; $t = GetF $CpuTemp
        $f1 += $a; $f2 += $b
        "{0,-12} t+{1,2}s  fan1={2,4}  fan2={3,4} rpm  cpu={4} C  fullspeed={5}" -f $label, $i, $a, $b, $t, (GetF $FullSpeed)
        if ($t -gt 90) {
            Write-Host '!!! CPU qua 90 C - bat quat toi da va dung test' -ForegroundColor Red
            "ABORT: full speed on -> $(SetF $FullSpeed 1)"
            $script:aborted = $true
            return
        }
        Start-Sleep 1
    }
    "{0} average: fan1={1:N0} fan2={2:N0}" -f $label, ($f1 | Measure-Object -Average).Average, ($f2 | Measure-Object -Average).Average
}

Write-Host 'Giu may nghi trong suot bai test (~2 phut). Quat se to len roi nho lai.' -ForegroundColor Cyan

Write-Host "`n=== A: baseline ===" -ForegroundColor Cyan
Watch 'baseline' 15; if ($aborted) { Stop-Transcript | Out-Null; Read-Host 'Enter'; exit 1 }

Write-Host "`n=== B: full speed ON 10s, then OFF ===" -ForegroundColor Cyan
"SetFeatureValue(0x04020000, 1) -> $(SetF $FullSpeed 1)"
Watch 'fullspeed' 10; if ($aborted) { Stop-Transcript | Out-Null; Read-Host 'Enter'; exit 1 }
"SetFeatureValue(0x04020000, 0) -> $(SetF $FullSpeed 0)"
Watch 'after-full' 12; if ($aborted) { Stop-Transcript | Out-Null; Read-Host 'Enter'; exit 1 }

Write-Host "`n=== C: set both fans to 4500 rpm ===" -ForegroundColor Cyan
"SetFeatureValue(0x04030001, 4500) -> $(SetF $Fan1 4500)"
"SetFeatureValue(0x04030002, 4500) -> $(SetF $Fan2 4500)"
Watch 'rpm4500' 15; if ($aborted) { Stop-Transcript | Out-Null; Read-Host 'Enter'; exit 1 }

Write-Host "`n=== D: set both fans to 0 (does the EC go back to auto?) ===" -ForegroundColor Cyan
"SetFeatureValue(0x04030001, 0) -> $(SetF $Fan1 0)"
"SetFeatureValue(0x04030002, 0) -> $(SetF $Fan2 0)"
Watch 'release0' 20

Write-Host "`nXong. Neu quat van chay bat thuong: bam Fn+Q doi mode qua lai hoac khoi dong lai may." -ForegroundColor Green
Stop-Transcript | Out-Null
Read-Host 'Nhan Enter de dong'
