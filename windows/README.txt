OpenCode Phone Proxy - Windows 11
=================================
Developed by Monish.

Routes only OpenCode through an HTTP/SOCKS proxy running on your Android phone
over USB tethering. Windows and your other apps stay on their normal network.

QUICK START
-----------
1. Phone
   - Mobile data ON.
   - USB cable connected.
   - USB tethering ON.
   - Every Proxy running: Server -> HTTP Proxy, port 8080.

2. PC
   - Run OpenCodePhoneProxy.exe.
   - Windows SmartScreen may say "Windows protected your PC" because this
     build is unsigned. Click "More info" -> "Run anyway".

3. First-run setup
   - Phone tab: select the USB tether adapter, click "Test connection".
     The test should show the phone's mobile IP, not your PC's IP.
   - OpenCode tab: the app auto-detects OpenCode. If not found, click
     Browse and select OpenCode.exe.
   - Options tab: leave "Keep USB tethering from becoming the default route"
     checked. Windows asks for administrator approval once when launching;
     the same helper restores the setting when OpenCode closes.

4. Launch
   - Right-click the tray icon -> "Launch OpenCode".
   - OpenCode opens with the proxy applied only to it.
   - Closing OpenCode restores the network setting automatically.

TRAY MENU
---------
Launch OpenCode, Stop OpenCode, Test connection, Settings, View log,
Start with Windows, Uninstall, Exit.

UNINSTALL
---------
Tray icon -> "Uninstall...". This removes:
  - the app and its folder
  - settings and logs (%APPDATA%\OpenCodePhoneProxy)
  - the autostart entry
  - Start Menu / Desktop shortcuts
  - any network metric the app changed

It does NOT touch OpenCode or your OpenCode sessions.

You can also double-click uninstall.cmd in the install folder, or run
uninstall.ps1. Add -KeepSettings to keep your configuration and logs.

FILES
-----
%APPDATA%\OpenCodePhoneProxy\settings.json   configuration
%APPDATA%\OpenCodePhoneProxy\logs\app.log    diagnostics

TROUBLESHOOTING
---------------
"Phone not detected"
  - USB cable must support data, not just charging.
  - Turn USB tethering off and on.
  - Start Every Proxy and keep it allowed in the background.

"Proxy test failed"
  - Confirm mobile data is on.
  - Confirm Every Proxy is in Server -> HTTP Proxy mode.
  - Confirm the port matches (default 8080).
  - Try HTTP instead of SOCKS5, or the other way around.

"Proxy IP matches this PC"
  - The phone may be on Wi-Fi instead of mobile data.
  - Or Every Proxy is not actually intercepting requests.
  - Turn off phone Wi-Fi and test again.

OpenCode not found
  - Settings -> OpenCode -> Browse -> select OpenCode.exe
    (or opencode.exe for the CLI).

Privacy
-------
No telemetry. The app stores only your proxy/OpenCode settings and a local log.
Network requests: only the proxy test to https://api.ipify.org and OpenCode's
own traffic.
