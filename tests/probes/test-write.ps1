# First write test for ThinkBook 16p G6 ADR GameZone WMI.
# Step 0: read-only mapping of Fn+Q modes.  Step 1: switch mode via SetSmartFanMode.
# Step 2: Custom mode + CPU power limits.  Never touches OC methods or fan tables.
param(
    [int]$LongTermW  = 60,   # 0x0102 sustained (PL1-like), allowed 50-90
    [int]$ShortTermW = 70,   # 0x0101 allowed 60-145
    [int]$PeakW      = 80,   # 0x0103 allowed 70-155
    [int]$TempLimitC = 95    # 0x0104 allowed 85-100
)
$out = Join-Path $PSScriptRoot 'test-write-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$ns = 'root\WMI'
$gz = Get-WmiObject -Namespace $ns -Class LENOVO_GAMEZONE_DATA | Select-Object -First 1
$om = Get-WmiObject -Namespace $ns -Class LENOVO_OTHER_METHOD | Select-Object -First 1
if (-not $gz -or -not $om) { "GameZone instances not found - aborting"; Stop-Transcript | Out-Null; exit 1 }

function GetF([uint32]$id) {
    try { $p = $om.GetMethodParameters('GetFeatureValue'); $p['IDs'] = $id; return $om.InvokeMethod('GetFeatureValue', $p, $null).value }
    catch { return "ERR: $($_.Exception.Message)" }
}
function SetF([uint32]$id, [uint32]$v) {
    try { $p = $om.GetMethodParameters('SetFeatureValue'); $p['IDs'] = $id; $p['value'] = $v; $null = $om.InvokeMethod('SetFeatureValue', $p, $null); return 'OK' }
    catch { return "ERR: $($_.Exception.Message)" }
}
function SetMode([uint32]$m) {
    try { $p = $gz.GetMethodParameters('SetSmartFanMode'); $p['Data'] = $m; $null = $gz.InvokeMethod('SetSmartFanMode', $p, $null); return 'OK' }
    catch { return "ERR: $($_.Exception.Message)" }
}
function Snapshot($label) {
    "[{0}] mode(0x210000)={1} | fan1={2} fan2={3} rpm | temps 0x50100={4} 0x50400={5} 0x50500={6}" -f $label,
        (GetF 0x00210000), (GetF 0x04030001), (GetF 0x04030002), (GetF 0x05010000), (GetF 0x05040000), (GetF 0x05050000)
}
function CustomValues($label) {
    "[{0}] custom: long={1} short={2} peak={3} templimit={4}" -f $label,
        (GetF 0x0102FF00), (GetF 0x0101FF00), (GetF 0x0103FF00), (GetF 0x0104FF00)
}

foreach ($v in @($LongTermW,50,90), @($ShortTermW,60,145), @($PeakW,70,155), @($TempLimitC,85,100)) {
    if ($v[0] -lt $v[1] -or $v[0] -gt $v[2]) { "Value $($v[0]) outside allowed $($v[1])-$($v[2]) - aborting"; Stop-Transcript | Out-Null; exit 1 }
}

Write-Host "`n=== STEP 0: map Fn+Q modes (read-only) ===" -ForegroundColor Cyan
Snapshot 'start'
for ($i = 1; $i -le 3; $i++) {
    Read-Host "Bam Fn+Q 1 lan (doi sang mode tiep theo), doi 2 giay, roi nhan Enter. Ghi nho ten mode hien len ($i/3)"
    Snapshot "after Fn+Q #$i"
}
$modeName = Read-Host "Go ten 3 mode vua hien len theo thu tu (vd: eco, auto, perf)"
"User-reported Fn+Q order: $modeName"

Write-Host "`n=== STEP 1: switch to mode 1 via SetSmartFanMode ===" -ForegroundColor Cyan
"SetSmartFanMode(1) -> $(SetMode 1)"
Start-Sleep 2
Snapshot 'after SetSmartFanMode(1)'
$saw = Read-Host "Man hinh co hien thong bao doi mode khong? Neu co thi la mode gi? (Enter neu khong thay gi)"
"User saw: $saw"

$m1 = GetF 0x00210000
if ("$m1" -ne '1') {
    Write-Host "Mode khong doi thanh 1 -> dung lai, khong lam Step 2." -ForegroundColor Yellow
    "STOP: mode read back = $m1, expected 1"
    Stop-Transcript | Out-Null; Read-Host "Nhan Enter de dong"; exit 0
}

Write-Host "`n=== STEP 2: Custom mode + power limits ===" -ForegroundColor Cyan
CustomValues 'before custom'
"SetSmartFanMode(255) -> $(SetMode 255)"
Start-Sleep 2
Snapshot 'after SetSmartFanMode(255)'
CustomValues 'in custom, before set'
"Set long-term  $LongTermW W -> $(SetF 0x0102FF00 $LongTermW)"
"Set short-term $ShortTermW W -> $(SetF 0x0101FF00 $ShortTermW)"
"Set peak       $PeakW W -> $(SetF 0x0103FF00 $PeakW)"
"Set temp limit $TempLimitC C -> $(SetF 0x0104FF00 $TempLimitC)"
Start-Sleep 1
CustomValues 'after set'
Snapshot 'end'

Write-Host "`nXong. Gio chay Cinebench/tai nang ~10 phut, xem CPU Package Power trong HWiNFO." -ForegroundColor Green
Write-Host "Muon tro ve nhu cu: bam Fn+Q hoac khoi dong lai." -ForegroundColor Green
Stop-Transcript | Out-Null
Read-Host "Nhan Enter de dong"
