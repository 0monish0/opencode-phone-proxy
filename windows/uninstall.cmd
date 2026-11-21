@echo off
setlocal
echo.
echo OpenCode Phone Proxy - uninstall
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
echo.
pause
