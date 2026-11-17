# Builds the downloadable release: a self-contained single-file TB16PTuner.exe (no .NET install
# needed) with the WinRing0 driver next to it, zipped as build\release\TB16PTuner-v<version>-win-x64.zip.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Tb16p.Ui\Tb16p.Ui.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "TB16PTuner-v$version-win-x64"
$out = Join-Path $root "build\release\$name"
$zip = Join-Path $root "build\release\$name.zip"

Remove-Item $out, $zip -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $project -c Release -r win-x64 --self-contained true -o $out --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    "-p:BaseOutputPath=$(Join-Path $root 'build\release-bin\')"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $root 'LICENSE') $out
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Portable zip: $zip" -ForegroundColor Green

# Installer (Inno Setup 6, per-user install via: winget install JRSoftware.InnoSetup --scope user).
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found.' }
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$out" "/O$(Join-Path $root 'build\release')" (Join-Path $root 'installer\TB16PTuner.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }
Write-Host "Installer: $(Join-Path $root "build\release\TB16PTuner-v$version-setup.exe")" -ForegroundColor Green
