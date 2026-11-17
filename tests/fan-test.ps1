# Fan_Set_Table test for ThinkBook 16p G6 ADR. Run elevated, with the laptop IDLE.
# Writes a MORE aggressive curve (every point = level 8) and watches whether fan RPM follows.
# Safety: aborts and restores the ramp 1..10 if CPU temp > 90 C or fans drop below 1000 rpm while warm.
param([int]$Level = 8, [int]$WatchSeconds = 25)

$out = Join-Path $PSScriptRoot 'fan-test-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$ns = 'root\WMI'
$om = Get-WmiObject -Namespace $ns -Class LENOVO_OTHER_METHOD | Select-Object -First 1
$fm = Get-WmiObject -Namespace $ns -Class LENOVO_FAN_METHOD | Select-Object -First 1
$gz = Get-WmiObject -Namespace $ns -Class LENOVO_GAMEZONE_DATA | Select-Object -First 1
if (-not $om -or -not $fm) { "GameZone instances not found - aborting"; Stop-Transcript | Out-Null; exit 1 }

function GetF([uint32]$id) { $p = $om.GetMethodParameters('GetFeatureValue'); $p['IDs'] = $id; $om.InvokeMethod('GetFeatureValue', $p, $null).value }
function Read-Sensors { [pscustomobject]@{ Fan1 = GetF 0x04030001; Fan2 = GetF 0x04030002; T1 = GetF 0x05010000; Cpu = GetF 0x05040000; T3 = GetF 0x05050000 } }

# Layout used by Legion Toolkit: byte FSTM=1, byte FSID=0, uint32 FSTL=0, 10 x uint16 fan levels (little endian)
function New-FanTableBytes([uint16[]]$levels) {
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ms)
    $bw.Write([byte]1); $bw.Write([byte]0); $bw.Write([uint32]0)
    foreach ($l in $levels) { $bw.Write([uint16]$l) }
    $bw.Flush()
    # The MOF declares FanTable as uint8[] with Max=64, so WmiProv only accepts exactly 64 bytes
    $buf = New-Object byte[] 64
    [Array]::Copy($ms.ToArray(), $buf, $ms.Length)
    , $buf
}
function Set-FanTable([uint16[]]$levels) {
    try {
        # Unwrap: PowerShell hands back a PSObject around the array, which WMI cannot cast to System.Array
        [byte[]]$bytes = (New-FanTableBytes $levels).PSObject.BaseObject
        $p = $fm.GetMethodParameters('Fan_Set_Table'); $p['FanTable'] = $bytes
        $null = $fm.InvokeMethod('Fan_Set_Table', $p, $null); 'OK'
    } catch { "ERR: $($_.Exception.Message)" }
}
$Ramp = [uint16[]](1..10)

function Watch($label, [int]$secs) {
    $samples = @()
    for ($i = 0; $i -lt $secs; $i++) {
        $s = Read-Sensors; $samples += $s
        "{0} t+{1,2}s fan1={2,4} fan2={3,4} rpm | cpu={4}C t1={5} t3={6}" -f $label, $i, $s.Fan1, $s.Fan2, $s.Cpu, $s.T1, $s.T3
        if ($s.Cpu -gt 90 -or (($s.Fan1 -lt 1000 -or $s.Fan2 -lt 1000) -and $s.Cpu -gt 65)) {
            Write-Host "!!! Bat thuong - tra ve duong cong goc" -ForegroundColor Red
            "ABORT at $label t+$i : restore ramp -> $(Set-FanTable $Ramp)"
            return $null
        }
        Start-Sleep 1
    }
    [pscustomobject]@{ Fan1 = ($samples.Fan1 | Measure-Object -Average).Average; Fan2 = ($samples.Fan2 | Measure-Object -Average).Average }
}
function Dump-FanTableData($label) {
    "--- LENOVO_FAN_TABLE_DATA ($label) ---"
    Get-WmiObject -Namespace $ns -Class LENOVO_FAN_TABLE_DATA | ForEach-Object {
        "  mode={0,3} fan={1} sensor={2} speeds={3}" -f $_.Mode, $_.Fan_Id, $_.Sensor_ID, (($_.FanTable_Data) -join ',')
    }
}

"ITS mode (registry) = $((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LenovoProcessManagement\Performance\PowerSlider').ITS_CurrentSetting)"
Write-Host "Giu may o trang thai nghi (khong chay tac vu nang) trong suot bai test." -ForegroundColor Cyan
Dump-FanTableData 'before'

Write-Host "`n=== A: baseline ${WatchSeconds}s ===" -ForegroundColor Cyan
$base = Watch 'base' $WatchSeconds
if (-not $base) { Stop-Transcript | Out-Null; Read-Host 'Nhan Enter de dong'; exit 1 }
"baseline avg fan1={0:N0} fan2={1:N0}" -f $base.Fan1, $base.Fan2

Write-Host "`n=== B: write curve all = level $Level (current mode) ===" -ForegroundColor Cyan
"Fan_Set_Table([$Level x10]) -> $(Set-FanTable ([uint16[]](@($Level) * 10)))"
$b = Watch 'B' $WatchSeconds
if (-not $b) { Stop-Transcript | Out-Null; Read-Host 'Nhan Enter de dong'; exit 1 }
"B avg fan1={0:N0} fan2={1:N0}  (delta {2:+0;-0} / {3:+0;-0})" -f $b.Fan1, $b.Fan2, ($b.Fan1 - $base.Fan1), ($b.Fan2 - $base.Fan2)
Dump-FanTableData 'after B'
$worked = ($b.Fan1 - $base.Fan1) -gt 400 -or ($b.Fan2 - $base.Fan2) -gt 400

if (-not $worked) {
    Write-Host "`n=== C: SetSmartFanMode(255) then write again ===" -ForegroundColor Cyan
    try { $p = $gz.GetMethodParameters('SetSmartFanMode'); $p['Data'] = [uint32]255; $null = $gz.InvokeMethod('SetSmartFanMode', $p, $null); "SetSmartFanMode(255) -> OK" } catch { "SetSmartFanMode(255) -> ERR $($_.Exception.Message)" }
    Start-Sleep 2
    "Fan_Set_Table([$Level x10]) -> $(Set-FanTable ([uint16[]](@($Level) * 10)))"
    $c = Watch 'C' $WatchSeconds
    if ($c) {
        "C avg fan1={0:N0} fan2={1:N0}  (delta {2:+0;-0} / {3:+0;-0})" -f $c.Fan1, $c.Fan2, ($c.Fan1 - $base.Fan1), ($c.Fan2 - $base.Fan2)
        $worked = ($c.Fan1 - $base.Fan1) -gt 400 -or ($c.Fan2 - $base.Fan2) -gt 400
    }
    Dump-FanTableData 'after C'
}
"RESULT: fan table write " + $(if ($worked) { 'CHANGED fan speed' } else { 'had NO visible effect' })

Write-Host "`n=== D: restore ramp 1..10 ===" -ForegroundColor Cyan
"Fan_Set_Table(ramp 1..10) -> $(Set-FanTable $Ramp)"
$d = Watch 'D' 15
if ($d) { "D avg fan1={0:N0} fan2={1:N0}" -f $d.Fan1, $d.Fan2 }

Write-Host "`nXong. Neu quat van chay bat thuong: bam Fn+Q doi mode qua lai hoac khoi dong lai may." -ForegroundColor Green
Stop-Transcript | Out-Null
Read-Host 'Nhan Enter de dong'
