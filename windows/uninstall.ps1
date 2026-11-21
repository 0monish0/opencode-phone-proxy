param(
    [string]$InstallDir = $PSScriptRoot,
    [switch]$KeepSettings,
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

$standardInstall = Join-Path $env:LOCALAPPDATA "Programs\OpenCodePhoneProxy"

function Write-Step([string]$Message) {
    Write-Host "  $Message"
}

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-Writable([string]$Directory) {
    try {
        $probe = Join-Path $Directory (".write-test-" + [guid]::NewGuid().ToString("N"))
        Set-Content -Path $probe -Value "test" -ErrorAction Stop
        Remove-Item -Path $probe -Force -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Quote([string]$Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

# Find every folder this app may have been installed into. Removing both makes a
# zip-folder uninstall clean up a previous install.ps1 copy too.
$installDirs = New-Object System.Collections.Generic.List[string]
foreach ($dir in @($InstallDir, $standardInstall)) {
    if (-not [string]::IsNullOrWhiteSpace($dir) -and
        $dir.Length -gt 3 -and
        (Test-Path -LiteralPath $dir) -and
        -not ($installDirs -contains $dir)) {
        $installDirs.Add($dir)
    }
}

if ($installDirs.Count -eq 0) {
    Write-Host "OpenCode Phone Proxy is not installed in a known folder."
    Write-Host "Settings, logs, autostart, and shortcuts will still be removed."
}

$needsElevation = $false
foreach ($dir in $installDirs) {
    if (-not (Test-Writable $dir)) {
        $needsElevation = $true
    }
}

if ($needsElevation -and -not (Test-Admin) -and -not $Elevated) {
    Write-Host "Administrator approval is needed to remove the install folder..."
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File " + (Quote $PSCommandPath) +
        " -InstallDir " + (Quote $InstallDir) + " -Elevated"
    if ($KeepSettings) { $argumentLine += " -KeepSettings" }
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argumentLine
    exit 0
}

Write-Host ""
Write-Host "OpenCode Phone Proxy - uninstall"
Write-Host ""

$settingsPath = Join-Path $env:APPDATA "OpenCodePhoneProxy\settings.json"
if (Test-Path -LiteralPath $settingsPath) {
    Write-Step "Restoring any network setting the app changed..."
    $restored = $false
    foreach ($dir in $installDirs) {
        $exePath = Join-Path $dir "OpenCodePhoneProxy.exe"
        if (Test-Path -LiteralPath $exePath) {
            try {
                & $exePath --restore-route | Out-Null
                $restored = $true
                break
            }
            catch {
                # Try the next copy.
            }
        }
    }

    if (-not $restored) {
        Write-Step "No restore helper was found; continuing."
    }
}
else {
    Write-Step "No saved settings; nothing to restore."
}

Write-Step "Stopping the tray app..."
Get-Process -Name "OpenCodePhoneProxy" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Write-Step "Removing autostart entry..."
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "OpenCodePhoneProxy" -ErrorAction SilentlyContinue

Write-Step "Removing shortcuts..."
$shortcuts = @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\OpenCode Phone Proxy.lnk"),
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "OpenCode Phone Proxy.lnk")
)
foreach ($shortcut in $shortcuts) {
    Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
}

if (-not $KeepSettings) {
    Write-Step "Deleting settings and logs..."
    Remove-Item -LiteralPath (Join-Path $env:APPDATA "OpenCodePhoneProxy") `
        -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA "OpenCodePhoneProxy") `
        -Recurse -Force -ErrorAction SilentlyContinue
}
else {
    Write-Step "Keeping settings and logs (-KeepSettings)."
}

if ($installDirs.Count -gt 0) {
    foreach ($dir in $installDirs) {
        Write-Step "Scheduling removal of $dir ..."
    }

    $batch = Join-Path $env:TEMP ("ocpp-uninstall-" + [guid]::NewGuid().ToString("N") + ".cmd")
    $batchLines = @(
        "@echo off",
        "timeout /t 2 /nobreak >nul"
    )
    foreach ($dir in $installDirs) {
        $batchLines += ('rmdir /s /q "' + $dir + '"')
    }
    $batchLines += 'del /f /q "%~f0" >nul 2>&1'

    Set-Content -Path $batch -Value $batchLines -Encoding ASCII
    Start-Process -FilePath "cmd.exe" -ArgumentList ('/c "' + $batch + '"') -WindowStyle Hidden
}

Write-Host ""
Write-Host "Done. OpenCode itself was not changed."
Write-Host ""
