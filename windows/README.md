# OpenCode Phone Proxy — Windows 11

A friendly tray app that routes **only OpenCode** through a proxy running on your
Android phone over USB tethering. Windows and all your other apps keep using your
normal network.

This is the Windows port of the Linux launcher in the parent folder. Linux users
should keep using `opencode-phone-proxy.sh`.

- Windows app version: **1.1.0**
- Target: Windows 11 x64 (Windows 10 should work too)
- No .NET runtime install needed; the exe is self-contained
- Unsigned build: SmartScreen may warn once ("More info" → "Run anyway")

## What it does

1. Detects the Android USB-tether adapter and the phone's gateway address.
2. Temporarily raises that adapter's interface metric so Wi-Fi stays the default
   route (one UAC prompt, OpenCode itself stays non-admin).
3. Tests the phone proxy and shows the phone's public IP next to your PC's direct IP.
4. Launches OpenCode with `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`,
   `NODE_USE_ENV_PROXY=1`, and `--proxy-server`, scoped to that process only.
5. Restores the network metric when OpenCode exits, when the app exits, and after
   a crash on the next start.
6. Uninstalls cleanly: app, settings, logs, autostart, shortcuts, and any network
   change it made. OpenCode is never touched.

## Install

1. Download `OpenCodePhoneProxy-v1.1.0-win-x64.zip` from the Actions artifact or
   a release.
2. Extract the whole zip to a folder.
3. Double-click `install.cmd`, or right-click `install.ps1` → Run with PowerShell,
   or just run `OpenCodePhoneProxy.exe` from the extracted folder.
4. The installer copies the app to `%LOCALAPPDATA%\Programs\OpenCodePhoneProxy`
   and creates a Start Menu shortcut.

If PowerShell blocks the script:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Phone checklist

- Mobile data ON.
- USB cable connected (a data cable, not charge-only).
- USB tethering ON.
- [Every Proxy](https://play.google.com/store/apps/details?id=com.gorillasoftware.everyproxy)
  running in **Server → HTTP Proxy** mode, port **8080**.
- Phone Wi-Fi OFF if you want to be sure traffic uses mobile data.

## First run

The setup window has three tabs:

- **Phone** — pick the detected USB tether adapter, confirm the phone address
  (usually the adapter gateway), port, and proxy type. Click **Test connection**;
  a good result shows a phone egress IP different from your PC's direct IP.
- **OpenCode** — the app auto-detects OpenCode installs. If yours is missing,
  click **Browse** and select `OpenCode.exe` (or `opencode.exe` for the CLI).
- **Options** — leave route protection on. Enable **Start with Windows** if you
  want the tray app available after sign-in.

## Daily use

Right-click the tray icon:

| Item | Action |
| --- | --- |
| Phone: … | Current link status |
| Launch OpenCode | Apply route guard, test, start OpenCode |
| Stop OpenCode | Stop OpenCode and restore the network |
| Test connection | Probe the phone proxy |
| Settings… | Reopen the setup window |
| View log | Open `%APPDATA%\OpenCodePhoneProxy\logs\app.log` |
| Start with Windows | Toggle autostart |
| Uninstall… | Remove the app and all its data |
| Exit | Quit the tray app |

Double-click the tray icon to open the status window. Closing the status window
hides it to the tray.

## Files

| Path | Purpose |
| --- | --- |
| `%APPDATA%\OpenCodePhoneProxy\settings.json` | Configuration |
| `%APPDATA%\OpenCodePhoneProxy\logs\app.log` | Diagnostics (rolling, 1 MB) |
| `%LOCALAPPDATA%\Programs\OpenCodePhoneProxy` | Installed app |

## Uninstall

From the tray menu: **Uninstall…**. Or double-click `uninstall.cmd`, or run
`uninstall.ps1` from the install folder.

```powershell
# keep settings and logs
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -KeepSettings
```

The uninstaller restores any pending interface metric, removes the `HKCU` Run
value, shortcuts, settings, logs, and the install folder. It does not touch
OpenCode or your OpenCode data.

## Troubleshooting

**"Phone not detected"**
- Use a data-capable USB cable.
- Toggle USB tethering off/on.
- Allow Every Proxy to run in the background (disable battery optimization).
- Click **Refresh** in the Phone tab.

**"Cannot reach host:port"**
- Mobile data must be on.
- Every Proxy must be running in HTTP Proxy mode.
- Check the port (default 8080).
- Try `http` instead of `socks5`, or the reverse.

**"Proxy works, but its public IP matches this PC"**
- The phone is probably on Wi-Fi, or the proxy is not active.
- Turn phone Wi-Fi off and test again.
- Note: airplane-mode toggling does not guarantee a new carrier IP.

**OpenCode not found**
- Settings → OpenCode → Browse → select the exe once. The path is remembered.

**UAC prompt every launch**
- Expected once per launch while route protection is on. The prompt comes from a
  tiny helper mode of the same exe and only changes the USB adapter's metric.

**SmartScreen warning**
- The exe is unsigned. Click "More info" → "Run anyway". Verify the SHA-256 if
  you downloaded a release.

## Build from source

Requires the .NET 8 SDK.

**Windows:**

```powershell
cd windows
.\build.ps1
```

**Linux cross-build:**

```bash
cd windows
./build.sh
```

Both produce `windows/dist/OpenCodePhoneProxy-v1.1.0-win-x64.zip`.

CI (`.github/workflows/windows.yml`) builds on `windows-latest`, runs
`OpenCodePhoneProxy.exe --selftest`, publishes the exe, and uploads the zip as an
artifact.

## Privacy

No telemetry. The app stores only local settings and logs. Network requests are
the proxy test (`https://api.ipify.org`) and OpenCode's own traffic.

## License

Same license as the parent repository.
