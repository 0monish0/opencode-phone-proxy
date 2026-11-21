$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = "1.1.0"
$dist = "dist/win-x64"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish src/OpenCodePhoneProxy/OpenCodePhoneProxy.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $dist

Copy-Item install.ps1, install.cmd, uninstall.ps1, uninstall.cmd, README.txt, README.md $dist -Force

$zip = "dist/OpenCodePhoneProxy-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$dist/*" -DestinationPath $zip

Write-Host "Built $zip"
