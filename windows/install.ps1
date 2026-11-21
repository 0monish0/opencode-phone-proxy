param(
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$source = $PSScriptRoot
$destination = Join-Path $env:LOCALAPPDATA "Programs\OpenCodePhoneProxy"

Write-Host ""
Write-Host "OpenCode Phone Proxy - install"
Write-Host ""

if (-not (Test-Path -LiteralPath (Join-Path $source "OpenCodePhoneProxy.exe"))) {
    Write-Host "OpenCodePhoneProxy.exe was not found next to this script."
    Write-Host "Extract the whole zip, then run install.ps1 again."
    exit 1
}

Write-Host "Copying to $destination ..."
Get-Process -Name "OpenCodePhoneProxy" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source "OpenCodePhoneProxy.exe") -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $source "uninstall.ps1") -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $source "README.txt") -Destination $destination -Force
if (Test-Path -LiteralPath (Join-Path $source "uninstall.cmd")) {
    Copy-Item -LiteralPath (Join-Path $source "uninstall.cmd") -Destination $destination -Force
}

Write-Host "Creating the Start Menu shortcut..."
$startMenu = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $startMenu "OpenCode Phone Proxy.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $destination "OpenCodePhoneProxy.exe"
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = Join-Path $destination "OpenCodePhoneProxy.exe"
$shortcut.Description = "Route only OpenCode through your phone"
$shortcut.Save()

Write-Host ""
Write-Host "Installed. Start it from the Start Menu or the shortcut."
Write-Host "Uninstall later from the tray menu, or run uninstall.ps1 in $destination."
Write-Host ""

if (-not $NoLaunch) {
    Start-Process -FilePath (Join-Path $destination "OpenCodePhoneProxy.exe")
}
