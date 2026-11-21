# Windows 11 Tray App — Design

Date: 2026-11-22
Status: Approved in conversation; implementation on branch `windows`
Base: tag `v1.0.0` (`f9bc727`)

## Intent

Give Windows 11 users the same capability as the Linux v1 launcher: keep the PC on
its normal network, but route **only OpenCode** through an HTTP/SOCKS proxy running
on an Android phone over USB tethering.

The Linux script is a CLI wrapper. The Windows version must be approachable for a
non-technical user: install once, answer a short setup form, then use a tray icon.

## Goals

- Detect the Android USB-tether adapter and phone proxy address automatically.
- Keep the USB adapter from becoming the Windows default route.
- Launch OpenCode with proxy settings scoped to that process tree.
- Test the proxy and show the phone's egress IP before launching.
- Recover cleanly after cable unplug, airplane mode, reboot, or a crash.
- Provide a clean uninstall that removes app data, autostart, shortcuts, and
  restores any network setting the app changed.
- Work on a stock Windows 11 x64 machine with no .NET runtime install.

## Non-goals

- Internet/remote access (that is v2).
- Wi-Fi hotspot transport; this port keeps v1's USB tethering model.
- Transparent proxying or routing all Windows traffic.
- Installing or configuring Every Proxy on the phone.
- Bypassing provider quotas or campus policy.

## Constraints

- v1 and the v2 worktree must not be modified.
- No paid services or code-signing certificate.
- The exe will be unsigned; SmartScreen may warn on first run.
- The app must not require OpenCode to run elevated.
- Windows 11 x64 is the primary target; ARM64 is a possible later build.

## Approaches considered

### Electron

Familiar UI and easy tray support, but 150–250 MB, its own runtime, slower startup,
and a larger attack surface. v1 already depends on Electron for OpenCode; adding a
second Electron app for a launcher is disproportionate.

### Rust + Tauri / native GUI

Small binaries and strong correctness, but tray/menu/UAC/installer work is slower to
build and harder to iterate on. Cross-compiling a polished Windows GUI from Linux is
also more fragile.

### Python + PyInstaller

Fast to write, but PyInstaller cannot reliably cross-build a Windows exe from Linux,
and the resulting bundle is large and prone to antivirus false positives.

### C# .NET 8 WinForms (chosen)

- Native Windows tray, forms, registry, UAC, and process APIs.
- Self-contained single-file publish needs no runtime install.
- Cross-builds from Linux with `EnableWindowsTargeting`.
- Small, predictable codebase; easy to review.
- GitHub Actions `windows-latest` can build and smoke-test it on a real Windows host.

## Architecture

```text
OpenCodePhoneProxy.exe (tray, normal user)
  ├─ SetupForm / StatusForm
  ├─ SettingsStore        %APPDATA%\OpenCodePhoneProxy\settings.json
  ├─ Logger               %APPDATA%\OpenCodePhoneProxy\logs\app.log
  ├─ PhoneLinkDetector    NetworkInterface + gateway discovery
  ├─ NetworkRouteGuard    PowerShell Get/Set-NetIPInterface
  │    └─ --elevated-route-session helper (one UAC, held on a named pipe)
  ├─ RouteSession         apply/restore over the helper pipe; crash-safe keep/restore
  ├─ ProxyProbe           curl.exe (http/https/socks5) + direct-IP comparison
  ├─ OpenCodeLocator      known paths + bounded recursive search + manual browse
  ├─ OpenCodeLauncher     close existing, start with scoped env, monitor exit
  ├─ AutostartManager     HKCU Run key
  └─ Uninstaller          uninstall.ps1 + in-app fallback
```

### Detection

`NetworkInterface.GetAllNetworkInterfaces()` is filtered to operational interfaces
with an IPv4 gateway. Candidates are scored by adapter description: RNDIS / Remote
NDIS highest, then Android/USB/Tether, then generic Ethernet. Known virtual adapters
(VirtualBox, VMware, Hyper-V, WSL, Tailscale, VPN, loopback) are excluded. The phone
address is the adapter gateway; `192.168.42.129` is a fallback. The user can always
override the adapter, host, port, and scheme.

### Route protection

Windows picks a default route by interface metric. Before launch, the app records the
USB adapter's IPv4/IPv6 metric and automatic-metric state, then sets the metric to
9999. This keeps Wi-Fi as the default route while the USB subnet route remains usable.
Restoration happens when OpenCode exits, when the app exits, and after a crash on the
next start. The change needs administrator rights, but OpenCode itself stays unelevated.

A normal session uses one UAC prompt. The tray app starts a tiny
`--elevated-route-session` helper of the same exe with `Verb=runas` and talks to it over
a random named pipe:

- helper applies the metric, writes `A`, and stays alive;
- parent sends `R` to restore (helper replies `D` on success or `X` on failure);
- parent sends `K` when the tray app exits while OpenCode is still running (helper
  acknowledges and exits without restoring);
- if the pipe closes because the tray app crashed, the helper restores immediately.

If the helper cannot start, the app falls back to the existing one-shot
`--elevated-route-apply` path. If the user declines UAC, the app continues without
route protection and says so.

The snapshot is persisted before the metric is changed. If a restore is declined, the
next launch reuses that snapshot for the same interface instead of capturing the
already-guarded 9999 metric as the new baseline. A read-only `IsRestored` check clears
a stale snapshot without another UAC prompt when the adapter metric already matches the
saved value.

### Proxy scoping

The child process receives:

```text
HTTP_PROXY, HTTPS_PROXY, ALL_PROXY
http_proxy, https_proxy, all_proxy
NO_PROXY=localhost,127.0.0.1,::1
no_proxy=localhost,127.0.0.1,::1
NODE_USE_ENV_PROXY=1
```

The Electron UI also receives `--proxy-server=<proxy-url>`. The parent environment and
Windows system proxy are never changed.

### OpenCode lifecycle

1. Locate the desktop exe (known paths, bounded recursive search, `where.exe`, or
   manual Browse).
2. Close running OpenCode processes gracefully, then forcefully after a timeout.
3. Stop a bundled `opencode-cli` background service if one is found.
4. Start OpenCode with the scoped environment and Chromium proxy flag.
5. On exit, restore the route metric and start a clean non-proxied service if the app
   stopped one.

### Proxy test

`curl.exe` is used so HTTP, HTTPS, SOCKS5, and SOCKS5H all work. The test fetches
`https://api.ipify.org` through the proxy and directly. A different proxy IP proves
the phone is the egress; an identical IP warns that the phone may be on Wi-Fi or the
proxy is not active. TCP reachability to `host:port` is checked separately so the UI
can distinguish "phone unreachable" from "proxy rejected the request".

## UX

### First run

A three-tab setup form:

1. **Phone** — detected adapter dropdown + Refresh, phone address (auto/manual), port,
   scheme, Test connection.
2. **OpenCode** — auto-detected path dropdown, Browse, version label, close-existing
   option.
3. **Options** — protect default route, test before launch, start with Windows,
   minimize to tray.

Save validates the inputs, writes settings atomically, updates autostart, and shows the
status window.

### Tray

- Status header: phone connected / not detected, phone IP:port.
- Launch OpenCode (default, bold).
- Stop OpenCode.
- Test connection.
- Settings.
- View log.
- Start with Windows (checkable).
- Uninstall.
- Exit.

Double-click opens the status window. Balloon notifications report detection,
launch failures, and proxy-test results. Closing the status window hides it to tray.

### Visual system

Windows-native Segoe UI, white cards on a pale blue-grey background, one blue accent
(`#0B6BCB`), green/red status dots, generous spacing, flat buttons, and DPI-aware
layout via `TableLayoutPanel`. No custom chrome or animation.

## Uninstall

The release zip contains `OpenCodePhoneProxy.exe`, `install.ps1`, `uninstall.ps1`, and
`README.txt`.

The in-app **Uninstall** action:

1. Confirms with the user.
2. Restores any pending route-metric change (elevating if needed).
3. Removes the `HKCU` Run value and Start Menu/Desktop shortcuts.
4. Launches `uninstall.ps1`, which stops the app, deletes
   `%APPDATA%\OpenCodePhoneProxy`, and removes the install folder after exit.
5. Falls back to a built-in self-delete batch if the script is missing.

`uninstall.ps1 -KeepSettings` keeps configuration and logs. It never touches OpenCode
or the user's OpenCode data.

## Testing

- `--selftest` runs without UI: settings round-trip, proxy URL cases, detector
  enumeration, locator enumeration, PowerShell/curl availability, and a log write.
- GitHub Actions `windows-latest` builds, runs `--selftest`, publishes the
  self-contained exe, and uploads the zip.
- Manual Windows 11 checklist: first run, detect phone, test through Every Proxy,
  verify Wi-Fi remains default, launch OpenCode, unplug/replug, exit, uninstall.
- Local Linux verification is limited to compilation, packaging, and static checks;
  the Windows runner is the functional gate.

## Risks

- Every Proxy may bind only to Wi-Fi/mobile interfaces; if so, the UI reports the
  failure and the user can switch to a Termux-hosted proxy later.
- Unsigned exe triggers SmartScreen once.
- OpenCode update paths change; the locator re-searches and the user can Browse.
- UAC cancellation disables route protection but does not block OpenCode.
- Windows may keep the same public IP after airplane mode; the app reports the IP, it
  does not promise a new one.

## Versioning

Windows app version `1.1.0` (the Windows port of v1). The Linux script remains
`1.0.0` and is not modified. Release tagging is decided after Windows validation.
