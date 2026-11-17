# First test of the app core (tb16p.exe). Run elevated with UXTU closed.
# Applies slow 55 W / fast 70 W (clearly different from the UXTU preset) and reads the battery mode.
$out = Join-Path $PSScriptRoot 'core-test-report.txt'
Start-Transcript -Path $out -Force | Out-Null
$exe = Join-Path $PSScriptRoot '..\src\Tb16p.Cli\bin\Release\net8.0-windows\tb16p.exe'

"=== info ==="
& $exe info; "exit=$LASTEXITCODE"
"`n=== limits: slow 55 W, fast 70 W ==="
& $exe limits --slow 55 --fast 70; "exit=$LASTEXITCODE"

Stop-Transcript | Out-Null
Write-Host "`nGio chay Cinebench ~30s va xem HWiNFO: CPU Package Power phai quanh 55 W." -ForegroundColor Green
Read-Host 'Nhan Enter de dong'
