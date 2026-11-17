# Read-only probe of Lenovo GameZone WMI interface. Never calls Set* methods.
$out = Join-Path $PSScriptRoot 'probe-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$ns = 'root\WMI'

function Fmt($v) {
    if ($null -eq $v) { return '<null>' }
    if ($v -is [array]) { return ($v | ForEach-Object { '0x{0:X}' -f $_ }) -join ' ' }
    if ($v -is [int] -or $v -is [uint32] -or $v -is [long] -or $v -is [uint64] -or $v -is [byte]) { return ('{0} (0x{0:X})' -f $v) }
    return "$v"
}

"=== Method signatures ==="
foreach ($c in 'LENOVO_GAMEZONE_DATA','LENOVO_OTHER_METHOD','LENOVO_FAN_METHOD','LENOVO_CPU_METHOD','LENOVO_GPU_METHOD') {
    $cls = Get-CimClass -Namespace $ns -ClassName $c -ErrorAction SilentlyContinue
    if (-not $cls) { continue }
    "[$c]"
    foreach ($m in $cls.CimClassMethods) {
        $p = ($m.Parameters | ForEach-Object {
            $dir = if ($_.Qualifiers.Name -contains 'In') { 'in' } else { 'out' }
            "$dir $($_.CimType) $($_.Name)" }) -join ', '
        "  $($m.Name)($p)"
    }
}

"`n=== Data class instances ==="
$dataClasses = Get-CimClass -Namespace $ns | Where-Object { $_.CimClassName -match '^LENOVO_.*(DATA|DATA_\d+)$' } | Select-Object -ExpandProperty CimClassName
foreach ($c in $dataClasses) {
    $inst = @(Get-CimInstance -Namespace $ns -ClassName $c -ErrorAction SilentlyContinue)
    "[$c] instances: $($inst.Count)"
    foreach ($i in $inst) {
        $props = $i.CimInstanceProperties | Where-Object { $_.Name -notin 'InstanceName','Active' } |
            ForEach-Object { "$($_.Name)=$(Fmt $_.Value)" }
        "  " + ($props -join ' | ')
    }
}

"`n=== GameZone read-only getters ==="
$gz = Get-CimInstance -Namespace $ns -ClassName LENOVO_GAMEZONE_DATA -ErrorAction SilentlyContinue | Select-Object -First 1
$getters = 'GetVersion','IsSupportSmartFan','GetSmartFanMode','GetSmartFanSetting','GetThermalMode',
           'GetCPUTemp','GetGPUTemp','IsSupportFanCooling','GetFanCoolingStatus','IsSupportCpuOC',
           'IsBIOSSupportOC','GetBIOSOCMode','GetProductInfo','GetPowerChargeMode','GetCpuFrequency',
           'IsSupportOD','GetODStatus','IsSupportIGPUMode','GetIGPUModeStatus','GetHardwareInfoSupportVersion',
           'IsSupportGpuOC','IsSupportWaterCooling','IsSupportLightingFeature','GetKeyboardfeaturelist'
if ($gz) {
    foreach ($g in $getters) {
        try {
            $r = Invoke-CimMethod -InputObject $gz -MethodName $g -ErrorAction Stop
            $vals = $r.PSObject.Properties | Where-Object { $_.Name -notin 'ReturnValue','PSComputerName' } |
                ForEach-Object { "$($_.Name)=$(Fmt $_.Value)" }
            "  $g -> " + ($vals -join ' | ')
        } catch { "  $g -> ERROR $($_.Exception.Message)" }
    }
} else { "  LENOVO_GAMEZONE_DATA instance not found" }

"`n=== GetFeatureValue for every ID listed in capability data ==="
$om = Get-CimInstance -Namespace $ns -ClassName LENOVO_OTHER_METHOD -ErrorAction SilentlyContinue | Select-Object -First 1
$ids = @()
foreach ($c in 'LENOVO_CAPABILITY_DATA_00','LENOVO_CAPABILITY_DATA_01','LENOVO_CAPABILITY_DATA_02') {
    Get-CimInstance -Namespace $ns -ClassName $c -ErrorAction SilentlyContinue | ForEach-Object { if ($_.IDs) { $ids += [uint32]$_.IDs } }
}
# Well-known Legion feature IDs, in case they are not listed (FF = current mode)
$ids += 0x0101FF00,0x0102FF00,0x0103FF00,0x0104FF00,0x0105FF00,0x0106FF00,0x0107FF00,0x0201FF00,0x0202FF00,0x0203FF00,0x04020000
if ($om) {
    foreach ($id in ($ids | Sort-Object -Unique)) {
        try {
            $r = Invoke-CimMethod -InputObject $om -MethodName GetFeatureValue -Arguments @{ IDs = [uint32]$id } -ErrorAction Stop
            "  0x{0:X8} -> {1}" -f $id, (Fmt $r.Value)
        } catch { "  0x{0:X8} -> ERROR {1}" -f $id, $_.Exception.Message }
    }
} else { "  LENOVO_OTHER_METHOD instance not found" }

"`n=== Fan_Get_Table ==="
$fm = Get-CimInstance -Namespace $ns -ClassName LENOVO_FAN_METHOD -ErrorAction SilentlyContinue | Select-Object -First 1
if ($fm) {
    try {
        $r = Invoke-CimMethod -InputObject $fm -MethodName Fan_Get_Table -ErrorAction Stop
        $r.PSObject.Properties | Where-Object { $_.Name -ne 'PSComputerName' } | ForEach-Object { "  $($_.Name)=$(Fmt $_.Value)" }
    } catch { "  ERROR $($_.Exception.Message)" }
}

"`n=== Power/thermal related devices & services ==="
Get-PnpDevice -PresentOnly | Where-Object FriendlyName -match 'AMD|PMF|Lenovo|Thermal|Power' | Select-Object Status, Class, FriendlyName | Format-Table -AutoSize | Out-String -Width 200
Get-Process | Where-Object { $_.ProcessName -match 'Lenovo|Legion|UXTU|Universal|Ryzen|PCManager|LeMgr|Smart' } | Select-Object ProcessName, Path | Format-Table -AutoSize | Out-String -Width 200

Stop-Transcript | Out-Null
