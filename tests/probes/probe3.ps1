# Read-only probe #3: dump every property of the GameZone data instances, then GetFeatureValue for each listed ID.
$out = Join-Path $PSScriptRoot 'probe3-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$ns = 'root\WMI'

function Fmt($v) {
    if ($null -eq $v) { return '<null>' }
    if ($v -is [array]) { return ($v | ForEach-Object { $_ }) -join ',' }
    if ($v -is [ValueType] -and $v -isnot [bool]) { return ('{0} (0x{0:X})' -f $v) }
    return "$v"
}

$classes = Get-WmiObject -Namespace $ns -List | Where-Object { $_.Name -match '^LENOVO_' -and $_.Name -notmatch '_EVENT$|_METHOD$' } | Select-Object -ExpandProperty Name
foreach ($c in $classes) {
    $inst = @(Get-WmiObject -Namespace $ns -Class $c -ErrorAction SilentlyContinue)
    if ($inst.Count -eq 0) { continue }
    "`n=== $c ($($inst.Count)) ==="
    foreach ($i in $inst) {
        $props = $i.Properties | Where-Object { $_.Name -notin 'InstanceName','Active' } | ForEach-Object { "$($_.Name)=$(Fmt $_.Value)" }
        "  [$($i.InstanceName -replace '.*\\','')] " + ($props -join ' | ')
    }
}

"`n=== GetFeatureValue for IDs listed in capability data ==="
$om = Get-WmiObject -Namespace $ns -Class LENOVO_OTHER_METHOD | Select-Object -First 1
$ids = foreach ($c in 'LENOVO_CAPABILITY_DATA_00','LENOVO_CAPABILITY_DATA_01','LENOVO_CAPABILITY_DATA_02') {
    Get-WmiObject -Namespace $ns -Class $c -ErrorAction SilentlyContinue | ForEach-Object { $_.IDs }
}
foreach ($id in ($ids | Where-Object { $_ -ne $null } | Sort-Object -Unique)) {
    try {
        $p = $om.GetMethodParameters('GetFeatureValue'); $p['IDs'] = [uint32]$id
        $r = $om.InvokeMethod('GetFeatureValue', $p, $null)
        "  0x{0:X8} -> {1}" -f [uint32]$id, $r.value
    } catch { "  0x{0:X8} -> ERROR {1}" -f [uint32]$id, $_.Exception.Message }
}

Stop-Transcript | Out-Null
