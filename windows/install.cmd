@echo off
setlocal
echo.
echo OpenCode Phone Proxy - install
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
