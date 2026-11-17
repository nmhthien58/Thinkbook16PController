# Fan control test 2 (laptop idle, run elevated):
#   A. does value 0 really hand control back to the EC (fans settle at the automatic speed)?
#   B. can a target BELOW the automatic speed be set (2000 rpm)?  guarded: stops above 75 C
#   C. does a target hold for 60 s without being re-sent?
# Ends by sending 0 so the EC is back in charge.
$out = Join-Path $PSScriptRoot 'fan-rpm-test2-report.txt'
Start-Transcript -Path $out -Force | Out-Null

$om = Get-WmiObject -Namespace root\WMI -Class LENOVO_OTHER_METHOD | Select-Object -First 1
$Fan1    = [Convert]::ToUInt32('04030001', 16)
$Fan2    = [Convert]::ToUInt32('04030002', 16)
$CpuTemp = [Convert]::ToUInt32('05040000', 16)
$Temp3   = [Convert]::ToUInt32('05050000', 16)

function GetF([uint32]$id) { $p = $om.GetMethodParameters('GetFeatureValue'); $p['IDs'] = $id; [int]$om.InvokeMethod('GetFeatureValue', $p, $null).value }
function SetF([uint32]$id, [uint32]$v) { $p = $om.GetMethodParameters('SetFeatureValue'); $p['IDs'] = $id; $p['value'] = $v; $null = $om.InvokeMethod('SetFeatureValue', $p, $null) }
function SetBoth([uint32]$rpm) { SetF $Fan1 $rpm; SetF $Fan2 $rpm; Write-Host "set both fans -> $rpm" }

function Watch([string]$label, [int]$seconds, [int]$maxTemp = 90) {
    for ($i = 0; $i -lt $seconds; $i++) {
        $t = GetF $CpuTemp
        Write-Host ("{0,-10} t+{1,2}s  fan1={2,4}  fan2={3,4} rpm  cpu={4} C  sensor5050={5} C" -f $label, $i, (GetF $Fan1), (GetF $Fan2), $t, (GetF $Temp3))
        if ($t -gt $maxTemp) {
            Write-Host "!!! CPU $t C > $maxTemp C - tra quat ve tu dong" -ForegroundColor Red
            SetBoth 0
            return
        }
        Start-Sleep 1
    }
}

"=== A: release (0) and watch the EC settle, 45 s ==="
SetBoth 0
Watch 'auto' 45

"`n=== B: 2000 rpm (below automatic), 25 s, abort above 75 C ==="
SetBoth 2000
Watch 'rpm2000' 25 75
SetBoth 0

"`n=== C: 3500 rpm once, NOT re-sent, 60 s ==="
SetBoth 3500
Watch 'hold3500' 60

"`n=== end: release to automatic ==="
SetBoth 0
Watch 'final' 10

Stop-Transcript | Out-Null
Read-Host 'Xong. Nhan Enter de dong'
