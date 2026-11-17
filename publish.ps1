# Builds the tray app into .\app, the copy that is actually run (and that the logon task points to).
# Close the running app first (tray icon -> Thoát), otherwise its files are locked.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$target = Join-Path $root 'app'

# Match by name only: the app runs elevated, so a non-elevated shell cannot read its path.
if (Get-Process Thinkbook16PController -ErrorAction SilentlyContinue) {
    throw "Thinkbook16PController.exe is still running. Exit it from the tray icon (Thoát) first."
}

# Separate build output so publishing never collides with a dev build that is running from bin\.
$buildOut = Join-Path $root 'build\bin\'
dotnet publish (Join-Path $root 'src\Tb16p.App\Tb16p.App.csproj') -c Release -o $target --nologo "-p:BaseOutputPath=$buildOut"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Write-Host "Published to $target\Thinkbook16PController.exe" -ForegroundColor Green
