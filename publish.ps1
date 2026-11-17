# Builds TB16P Tuner into .\app, the copy that is actually run (and that the logon task points to).
# Exit the running app first (tray icon -> Exit), otherwise its files are locked.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = Join-Path $root 'app'

# Match by name only: the app runs elevated, so a non-elevated shell cannot read its path.
foreach ($name in 'TB16PTuner', 'Thinkbook16PController') {
    if (Get-Process $name -ErrorAction SilentlyContinue) {
        throw "$name.exe is still running. Exit it from the tray icon first."
    }
}

# Separate build output so publishing never collides with a dev build that is running from bin\.
$buildOut = Join-Path $root 'build\bin\'
dotnet publish (Join-Path $root 'src\Tb16p.Ui\Tb16p.Ui.csproj') -c Release -o $target --nologo "-p:BaseOutputPath=$buildOut"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Write-Host "Published to $target\TB16PTuner.exe" -ForegroundColor Green
