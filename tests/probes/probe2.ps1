# Read-only probe #2: show errors, try several ways to reach GameZone instances. Never calls Set* methods.
$out = Join-Path $PSScriptRoot 'probe2-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$ns = 'root\WMI'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
"Admin: $isAdmin"

$classes = 'LENOVO_GAMEZONE_DATA','LENOVO_OTHER_METHOD','LENOVO_FAN_METHOD','LENOVO_CAPABILITY_DATA_01','LENOVO_FAN_TABLE_DATA','LENOVO_UTILITY_DATA','LENOVO_CPU_METHOD'

"`n=== Get-WmiObject (errors shown) ==="
foreach ($c in $classes) {
    try {
        $r = @(Get-WmiObject -Namespace $ns -Class $c -ErrorAction Stop)
        "[$c] count=$($r.Count) names=" + (($r | ForEach-Object { $_.InstanceName }) -join ', ')
    } catch { "[$c] ERROR: $($_.Exception.GetType().Name): $($_.Exception.Message)" }
}

"`n=== Get-CimInstance (errors shown) ==="
foreach ($c in $classes) {
    try {
        $r = @(Get-CimInstance -Namespace $ns -ClassName $c -ErrorAction Stop)
        "[$c] count=$($r.Count) names=" + (($r | ForEach-Object { $_.InstanceName }) -join ', ')
    } catch { "[$c] ERROR: $($_.Exception.GetType().Name): $($_.Exception.Message)" }
}

"`n=== Direct instance path ACPI\PNP0C14\GMZN_0 ==="
foreach ($c in 'LENOVO_GAMEZONE_DATA','LENOVO_OTHER_METHOD','LENOVO_FAN_METHOD') {
    try {
        $o = [wmi]"root\WMI:$c.InstanceName='ACPI\\PNP0C14\\GMZN_0'"
        "[$c] OK, Active=$($o.Active)"
    } catch { "[$c] ERROR: $($_.Exception.Message)" }
}

"`n=== Getter calls via WMI (read-only) ==="
function Call($cls, $method, $argsHash) {
    try {
        $inst = Get-WmiObject -Namespace $ns -Class $cls -ErrorAction Stop | Select-Object -First 1
        if (-not $inst) { return "  $cls.$method -> no instance" }
        if ($argsHash) {
            $inParams = $inst.GetMethodParameters($method)
            foreach ($k in $argsHash.Keys) { $inParams[$k] = $argsHash[$k] }
            $r = $inst.InvokeMethod($method, $inParams, $null)
        } else { $r = $inst.InvokeMethod($method, $null, $null) }
        $vals = $r.Properties | Where-Object { $_.Name -ne 'ReturnValue' } | ForEach-Object {
            $v = $_.Value
            if ($v -is [array]) { $v = ($v | ForEach-Object { $_ }) -join ',' }
            "$($_.Name)=$v" }
        "  $cls.$method -> " + ($vals -join ' | ')
    } catch { "  $cls.$method -> ERROR: $($_.Exception.Message)" }
}
foreach ($g in 'GetVersion','IsSupportSmartFan','GetSmartFanMode','GetSmartFanSetting','GetThermalMode','GetCPUTemp','GetGPUTemp',
               'IsSupportFanCooling','GetFanCoolingStatus','IsSupportCpuOC','IsBIOSSupportOC','GetProductInfo','GetHardwareInfoSupportVersion','IsSupportOD','IsSupportIGPUMode') {
    Call 'LENOVO_GAMEZONE_DATA' $g $null
}
"--- GetFeatureValue (power-limit IDs, current mode FF) ---"
foreach ($id in 0x0101FF00,0x0102FF00,0x0103FF00,0x0104FF00,0x0105FF00,0x0106FF00,0x0107FF00,0x0201FF00,0x0202FF00,0x0203FF00,0x04020000) {
    "  ID 0x{0:X8}:" -f $id
    Call 'LENOVO_OTHER_METHOD' 'GetFeatureValue' @{ IDs = [uint32]$id }
}
"--- GetFeatureValue per mode (01=quiet 02=balanced 03=perf FF=custom) for CPU PL IDs ---"
foreach ($base in 0x01010000,0x01020000,0x01030000,0x01050000) {
    foreach ($mode in 0x01,0x02,0x03,0xFF) {
        $id = $base -bor ($mode -shl 8)
        "  ID 0x{0:X8}:" -f $id
        Call 'LENOVO_OTHER_METHOD' 'GetFeatureValue' @{ IDs = [uint32]$id }
    }
}
"--- Fan_Get_Table ---"
foreach ($fan in 0,1,2) { foreach ($sensor in 0,1,2,3,4) {
    "  FanID=$fan SensorID=$sensor"
    Call 'LENOVO_FAN_METHOD' 'Fan_Get_Table' @{ FanID = [byte]$fan; SensorID = [byte]$sensor }
} }

"`n=== WMI namespace security (root\WMI) ==="
try { (Invoke-WmiMethod -Namespace root\WMI -Path __SystemSecurity -Name GetSecurityDescriptor).Descriptor.DACL | ForEach-Object { "$($_.Trustee.Domain)\$($_.Trustee.Name) mask=0x{0:X}" -f $_.AccessMask } } catch { "ERROR: $($_.Exception.Message)" }

Stop-Transcript | Out-Null
