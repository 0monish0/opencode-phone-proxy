# Windows 11 Tray App Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. Each task ends with a compile, a `--selftest` run where possible, and a commit. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a self-contained Windows 11 tray app that reproduces v1's USB-tethered, OpenCode-only phone proxy, with one-time setup, auto-detection, manual fallback, and a clean uninstall.

**Architecture:** One WinForms executable. A tray `ApplicationContext` owns the UI and lifecycle; focused service classes handle detection, route metrics, proxy probing, OpenCode discovery/launch, autostart, and uninstall. The same exe has hidden `--selftest` and `--elevated-route-*` modes.

**Tech Stack:** C# 12, .NET 8, WinForms, `System.Text.Json`, PowerShell for `Get/Set-NetIPInterface`, `curl.exe` for proxy tests, GitHub Actions `windows-latest`.

**Spec:** `docs/superpowers/specs/2026-11-22-windows-tray-app-design.md`

## Global Constraints

- Branch `windows`, worktree `/home/monish/Documents/PROJECTS/proxy-windows`; never modify `/home/monish/Documents/PROJECTS/proxy` or `/home/monish/Documents/PROJECTS/proxy-v2`.
- Windows app version `1.1.0`; Linux `opencode-phone-proxy.sh` stays `1.0.0`.
- Target `net8.0-windows`, `win-x64`, self-contained single-file, no runtime install.
- App runs as the normal user; only the momentary route-metric helper elevates.
- Never change the Windows system proxy or the parent process environment.
- All settings live under `%APPDATA%\OpenCodePhoneProxy`; all logs under `logs\app.log`.
- No paid dependencies, no code signing, no telemetry.
- Every user-facing error names the failed step and the next action.
- Uninstall must restore pending route changes before deleting app data.

## Review Focus

1. **No USB adapter present:** setup must still open, allow a manually typed phone address, and explain what to connect; it must not crash or block saving.
2. **UAC declined:** launch continues without route protection, with a visible warning; OpenCode still starts with the proxy environment.
3. **OpenCode moved or missing:** the launcher opens Settings with a clear message, Browse works, and no partial process kill occurs before a valid path is confirmed.
4. **Proxy unreachable or wrong egress:** the probe distinguishes TCP failure, proxy rejection, and "proxy IP equals direct IP"; launch asks before continuing.
5. **Crash or uninstall mid-session:** a persisted route snapshot is restored on next start and before uninstall deletes settings.

---

### Task 1: Scaffold the WinForms project

**Files:**
- Create: `windows/OpenCodePhoneProxy.sln`
- Create: `windows/src/OpenCodePhoneProxy/OpenCodePhoneProxy.csproj`
- Create: `windows/src/OpenCodePhoneProxy/app.manifest`
- Create: `windows/src/OpenCodePhoneProxy/Program.cs`
- Create: `windows/src/OpenCodePhoneProxy/Resources/app.ico`
- Create: `windows/build.sh`
- Create: `windows/build.ps1`

**Interfaces:**
- Produces: `Program.Main(string[] args)`, `--selftest`, `--version`, `--elevated-route-set`, `--elevated-route-restore`.
- Produces: `OpenCodePhoneProxy.exe` at `windows/dist/win-x64/`.

- [ ] **Step 1:** Create the solution and csproj with `UseWindowsForms`, `EnableWindowsTargeting`, `ApplicationManifest`, `ApplicationIcon`, `Version=1.1.0`, `RuntimeIdentifier=win-x64`, `PublishSingleFile`, compression.
- [ ] **Step 2:** Add a 256px multi-size icon generated with Pillow (blue rounded square, white phone, green status dot).
- [ ] **Step 3:** Add a `Program.cs` that handles `--version`, `--selftest` (write `%TEMP%\OpenCodePhoneProxy-selftest.log`, return 0/1), and otherwise starts the tray context.
- [ ] **Step 4:** Add `build.sh` (Linux cross-build) and `build.ps1` (Windows build) that publish and copy `install.ps1`, `uninstall.ps1`, `README.txt`.
- [ ] **Step 5:** Verify `bash windows/build.sh` produces a PE32+ `.exe`; run `file` and record size.
- [ ] **Step 6:** Commit `feat(windows): scaffold tray app`.

### Task 2: Settings, logging, and proxy model

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/AppSettings.cs`
- Create: `windows/src/OpenCodePhoneProxy/Models/ProxyConfig.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/SettingsStore.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/Logger.cs`

**Interfaces:**
- `AppSettings` properties: `ProxyHost`, `ProxyPort=8080`, `ProxyScheme="http"`, `AutoDetectProxyHost=true`, `OpenCodePath`, `OpenCodeArguments`, `ProtectDefaultRoute=true`, `TestProxyBeforeLaunch=true`, `CloseExistingOpenCode=true`, `StartWithWindows`, `MinimizeToTray=true`, `FirstRunCompleted`, `PendingRouteSnapshot`.
- `ProxyConfig.FromSettings(AppSettings)` → `ProxyConfig`; `ProxyConfig.Url` → `http://host:port`; `ProxyConfig.Validate()` → `string?` error.
- `SettingsStore(string root)` with `Load()`, `Save(AppSettings)`, `BackupCorrupt()`, `SettingsPath`, `DataDirectory`.
- `Logger(string root)` with `Info/Warn/Error`, `LogPath`, `Tail(int lines)`.

- [ ] **Step 1:** Add selftest assertions: default settings round-trip, corrupt JSON is backed up and replaced, proxy URL cases for http/https/socks5, invalid port rejected.
- [ ] **Step 2:** Implement atomic JSON save (temp + `File.Move(overwrite)`), `SchemaVersion`, and a `PendingRouteSnapshot` string.
- [ ] **Step 3:** Implement a thread-safe rolling log (1 MB cap) and `Tail`.
- [ ] **Step 4:** Run `--selftest`; expect `settings-roundtrip PASS`, `settings-corrupt PASS`, `proxy-url PASS`.
- [ ] **Step 5:** Commit `feat(windows): add settings and logging`.

### Task 3: Phone USB link detection

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/PhoneLink.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/PhoneLinkDetector.cs`

**Interfaces:**
- `PhoneLink(string AdapterId, string AdapterName, string Description, int InterfaceIndex, string LocalAddress, string GatewayAddress, int Score, bool LooksLikeUsbTether)`.
- `PhoneLinkDetector.Detect()` → `IReadOnlyList<PhoneLink>` sorted by score desc.
- `PhoneLinkDetector.Best()` → `PhoneLink?`.
- `PhoneLinkDetector.ProbeCommonHosts()` → candidates from `192.168.42.129`, `192.168.137.1`, `192.168.43.1`.

- [ ] **Step 1:** Enumerate operational interfaces with IPv4 gateways; exclude loopback, tunnel, VPN, Hyper-V, VMware, VirtualBox, WSL, Tailscale.
- [ ] **Step 2:** Score RNDIS/Remote NDIS +100, Android +80, USB/Tether +60, Ethernet +10, `192.168.42.x` +5; expose `LooksLikeUsbTether`.
- [ ] **Step 3:** Selftest logs every adapter and the chosen candidate; asserts no exception and stable sort.
- [ ] **Step 4:** Build and run `--selftest`; expect `detect PASS` with at least the loopback exclusion.
- [ ] **Step 5:** Commit `feat(windows): detect USB tether link`.

### Task 4: Proxy probe

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/ProxyTestResult.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/ProxyProbe.cs`

**Interfaces:**
- `ProxyTestResult(bool TcpReachable, bool Success, string? PublicIp, string? DirectIp, long LatencyMs, string? Error)`.
- `ProxyProbe.TestAsync(ProxyConfig, CancellationToken)` → `ProxyTestResult`.
- `ProxyProbe.DirectIpAsync(CancellationToken)` → `string?`.

- [ ] **Step 1:** TCP-connect to `host:port` with a 3s timeout; return a distinct "phone not reachable" error.
- [ ] **Step 2:** Run `curl.exe -x <url> --max-time 15 -fsS https://api.ipify.org`; parse IPv4/IPv6; measure latency.
- [ ] **Step 3:** Fetch the direct IP without a proxy; set `SameEgress = PublicIp == DirectIp`.
- [ ] **Step 4:** Selftest bad-port case (expect `TcpReachable=false`, clear error) and direct-IP case (no proxy).
- [ ] **Step 5:** Commit `feat(windows): probe proxy egress`.

### Task 5: OpenCode discovery

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/OpenCodeCandidate.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/OpenCodeLocator.cs`

**Interfaces:**
- `OpenCodeCandidate(string Path, string Version, string Source)`.
- `OpenCodeLocator.FindAll()` → `IReadOnlyList<OpenCodeCandidate>` (deduped, desktop first).
- `OpenCodeLocator.FindBundledCli()` → `string?`.

- [ ] **Step 1:** Check known paths under `%LOCALAPPDATA%\Programs`, `%PROGRAMFILES%`, and `%LOCALAPPDATA%\opencode`.
- [ ] **Step 2:** Bounded recursive search (depth 3) for `*opencode*.exe`, excluding uninstall/crash/helper/updater names.
- [ ] **Step 3:** Add `where.exe opencode` and `where.exe opencode-desktop`; read `FileVersionInfo`.
- [ ] **Step 4:** Find `opencode-cli.exe` under `%APPDATA%\ai.opencode.desktop\cli\*\` and `%LOCALAPPDATA%\ai.opencode.desktop\cli\*\`, newest first.
- [ ] **Step 5:** Selftest logs candidates and asserts no exception; manual checklist covers missing path + Browse.
- [ ] **Step 6:** Commit `feat(windows): locate OpenCode install`.

### Task 6: Route guard and elevation helper

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/RouteSnapshot.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/NetworkRouteGuard.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/Elevation.cs`
- Modify: `windows/src/OpenCodePhoneProxy/Program.cs`

**Interfaces:**
- `RouteSnapshot(int InterfaceIndex, int? V4Metric, bool V4Automatic, int? V6Metric, bool V6Automatic)`.
- `NetworkRouteGuard.Capture(int ifIndex)` → `RouteSnapshot?`.
- `NetworkRouteGuard.ApplyHighMetric(RouteSnapshot)` → `RouteChangeResult` (`Applied`, `Declined`, `Failed`).
- `NetworkRouteGuard.Restore(RouteSnapshot)` → `RouteChangeResult`.
- `Elevation.IsAdministrator` → `bool`; `Elevation.RunElevated(string args)` → `int?` (null = declined).

- [ ] **Step 1:** Read metrics with `Get-NetIPInterface -InterfaceIndex <n> -AddressFamily IPv4/IPv6`; parse `InterfaceMetric|AutomaticMetric`.
- [ ] **Step 2:** Set IPv4/IPv6 metric to 9999; on restore, use `-AutomaticMetric Enabled` when it was automatic, else the saved number.
- [ ] **Step 3:** Add hidden `--elevated-route-set` / `--elevated-route-restore` modes that perform one change, write a result file, and exit without UI.
- [ ] **Step 4:** Persist the snapshot in settings before applying; clear it only after a confirmed restore.
- [ ] **Step 5:** Selftest captures the current adapter snapshot read-only and asserts parsing; never changes metrics in selftest.
- [ ] **Step 6:** Commit `feat(windows): guard default route`.

### Task 7: OpenCode lifecycle

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Models/LaunchResult.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/OpenCodeLauncher.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/ProcessRunner.cs`

**Interfaces:**
- `LaunchResult(bool Started, int? ProcessId, string? Error)`.
- `OpenCodeLauncher.Prepare(OpenCodeCandidate, AppSettings)` → closes existing processes and stops the bundled service.
- `OpenCodeLauncher.Start(OpenCodeCandidate, ProxyConfig, AppSettings)` → `(Process?, LaunchResult)`.
- `OpenCodeLauncher.StopAll()` → `int` count.
- `OpenCodeLauncher.StartCleanService()` → `void`.
- `OpenCodeLauncher.IsRunning` → `bool`.

- [ ] **Step 1:** Close processes by known names (`opencode-desktop`, `OpenCode`, `opencode`, `ai.opencode`, `opencode-cli`) with `CloseMainWindow`, 5s wait, then `Kill(entireProcessTree:true)`.
- [ ] **Step 2:** Stop the bundled CLI service (`service stop`) if found, recording that it was stopped.
- [ ] **Step 3:** Start the selected exe with `UseShellExecute=false`, the six proxy variables, `NO_PROXY`, `NODE_USE_ENV_PROXY=1`, and `--proxy-server=<url>`; preserve user arguments.
- [ ] **Step 4:** Monitor `Exited`; restore the route snapshot; start a clean service without proxy variables.
- [ ] **Step 5:** Selftest validates the environment dictionary builder and argument quoting without starting OpenCode.
- [ ] **Step 6:** Commit `feat(windows): launch OpenCode proxied`.

### Task 8: UI theme and reusable controls

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/UI/UiTheme.cs`
- Create: `windows/src/OpenCodePhoneProxy/UI/CardPanel.cs`
- Create: `windows/src/OpenCodePhoneProxy/UI/StatusDot.cs`
- Create: `windows/src/OpenCodePhoneProxy/UI/PrimaryButton.cs`

**Interfaces:**
- `UiTheme` colors/fonts: `Background`, `Surface`, `Accent`, `Success`, `Danger`, `Text`, `Muted`, `Border`, `TitleFont`, `BodyFont`, `SmallFont`.
- `CardPanel : Panel` with 1px border and optional title.
- `StatusDot : Control` with `Color` property, 12px circle.
- `PrimaryButton : Button` with flat accent styling; `SecondaryButton`.

- [ ] **Step 1:** Implement DPI-aware, flat, accessible styles (contrast ≥ 4.5:1, focus cues preserved).
- [ ] **Step 2:** Build; no designer files, all layout in code.
- [ ] **Step 3:** Commit `feat(windows): add UI theme`.

### Task 9: First-run setup form

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/UI/SetupForm.cs`
- Modify: `windows/src/OpenCodePhoneProxy/Program.cs`

**Interfaces:**
- `SetupForm(AppSettings, SettingsStore, Logger)`; `DialogResult` OK on save.
- `SetupForm.SavedSettings` → `AppSettings`.

- [ ] **Step 1:** Build three tabs (Phone, OpenCode, Options) with the exact fields from the spec.
- [ ] **Step 2:** Wire adapter Refresh and auto-fill host from the selected gateway; manual host always editable.
- [ ] **Step 3:** Wire OpenCode auto-detect, candidate dropdown, Browse, and version label.
- [ ] **Step 4:** Wire async Test Connection with disabled button, result text, and direct-vs-proxy IP warning.
- [ ] **Step 5:** Validate on Save; show actionable inline errors; persist atomically; update autostart.
- [ ] **Step 6:** Manual checklist: no adapter, manual host, missing OpenCode, UAC not involved here.
- [ ] **Step 7:** Commit `feat(windows): add setup wizard`.

### Task 10: Status window, tray, and orchestration

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/UI/StatusForm.cs`
- Create: `windows/src/OpenCodePhoneProxy/UI/TrayApp.cs`
- Modify: `windows/src/OpenCodePhoneProxy/Program.cs`

**Interfaces:**
- `TrayApp : ApplicationContext`; `LaunchAsync()`, `Stop()`, `ShowStatus()`, `ShowSettings()`, `ExitApp()`.
- `StatusForm` events: `LaunchRequested`, `StopRequested`, `TestRequested`, `SettingsRequested`.

- [ ] **Step 1:** Add `NotifyIcon` + menu (status, Launch, Stop, Test, Settings, Log, Autostart, Uninstall, Exit), double-click status.
- [ ] **Step 2:** Poll adapter presence every 5s and TCP reachability every 10s; update dot/text without blocking UI.
- [ ] **Step 3:** Implement the launch flow: validate path → capture/apply route → test (optional, ask on failure) → prepare → start → monitor → restore.
- [ ] **Step 4:** On first run or missing settings, show SetupForm; exit if canceled.
- [ ] **Step 5:** On startup, restore a persisted route snapshot with a warning if UAC is declined.
- [ ] **Step 6:** Manual checklist: launch, stop, hide-to-tray, exit while OpenCode runs, relaunch after cable replug.
- [ ] **Step 7:** Commit `feat(windows): add tray orchestration`.

### Task 11: Autostart and uninstall

**Files:**
- Create: `windows/src/OpenCodePhoneProxy/Services/AutostartManager.cs`
- Create: `windows/src/OpenCodePhoneProxy/Services/Uninstaller.cs`
- Create: `windows/install.ps1`
- Create: `windows/uninstall.ps1`
- Create: `windows/README.txt`

**Interfaces:**
- `AutostartManager.Set(bool, string exePath)`; `IsEnabled`.
- `Uninstaller.Run(Logger, AppSettings?, string installDir)` → `bool`.
- `uninstall.ps1` params: `-InstallDir`, `-KeepSettings`, `-Elevated`.

- [ ] **Step 1:** Add/remove `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OpenCodePhoneProxy` with `--tray`.
- [ ] **Step 2:** In-app Uninstall: confirm, restore route, remove Run key/shortcuts, launch `uninstall.ps1`, exit.
- [ ] **Step 3:** `uninstall.ps1`: stop app, restore snapshot if present, delete `%APPDATA%\OpenCodePhoneProxy`, remove shortcuts, self-delete install dir after exit; support `-KeepSettings`.
- [ ] **Step 4:** `install.ps1`: copy exe/scripts to `%LOCALAPPDATA%\Programs\OpenCodePhoneProxy`, create Start Menu shortcut, optionally launch.
- [ ] **Step 5:** Manual checklist: uninstall from app and standalone; verify no Run key, no app data, network metric restored, OpenCode untouched.
- [ ] **Step 6:** Commit `feat(windows): add uninstaller`.

### Task 12: CI, docs, and release artifact

**Files:**
- Create: `.github/workflows/windows.yml`
- Create: `windows/README.md`
- Modify: `README.md` (add Windows section, Linux untouched)
- Modify: `CHANGELOG.md`
- Create: `windows/.gitignore` if needed

**Interfaces:**
- Artifact: `OpenCodePhoneProxy-v1.1.0-win-x64.zip` containing exe + scripts + README.

- [ ] **Step 1:** Add `windows-latest` workflow: restore, build, `--selftest`, publish, zip, upload artifact.
- [ ] **Step 2:** Add user docs: first run, phone checklist, troubleshooting, uninstall, build.
- [ ] **Step 3:** Run local `dotnet build` and `dotnet publish`; verify zip contents and PE header.
- [ ] **Step 4:** Push branch `windows`, watch CI, download artifact, inspect zip.
- [ ] **Step 5:** Commit any CI fixes and re-run.
- [ ] **Step 6:** Report artifact path and Windows manual test steps.

## Self-Review

- Spec coverage: detection, route guard, proxy scoping, lifecycle, setup, tray, uninstall, testing, docs all have tasks.
- Step scan: each task has a compile/selftest or manual check and a commit.
- Types: `RouteSnapshot`, `ProxyConfig`, `PhoneLink`, `OpenCodeCandidate`, `LaunchResult` names are consistent across tasks.
- Review Focus tests: no-adapter (Task 9 manual + Task 3 fallback), UAC decline (Task 6/10), missing OpenCode (Task 5/9/10), bad proxy (Task 4/10), crash/uninstall restore (Task 6/11).
- Proportion: plan is task-level; bodies live in the implementation, not here.
