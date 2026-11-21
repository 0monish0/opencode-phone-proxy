# Changelog

All notable changes to this project are documented here. This project follows [Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-11-22

Windows tray app only. The Linux `opencode-phone-proxy.sh` remains at 1.0.0 and is
unchanged.

### Added

- Native Windows 11 tray app (`OpenCodePhoneProxy.exe`, C# .NET 8 WinForms,
  self-contained `win-x64`, no runtime install).
- First-run setup wizard with Phone, OpenCode, and Options tabs.
- Android USB-tether adapter detection by RNDIS/Android/USB description scoring,
  with virtual adapters (Hyper-V, VMware, VirtualBox, WSL, VPN, Tailscale) excluded.
- Phone address auto-fill from the adapter gateway with manual override.
- OpenCode auto-detection from known install folders, a bounded search, `where.exe`,
  and a manual Browse fallback.
- Route protection via `Get-NetIPInterface` / `Set-NetIPInterface` interface metrics,
  using one elevated helper session per launch so OpenCode stays non-admin and restore
  does not need a second UAC prompt.
- Crash-safe helper session: closing the pipe restores the metric if the tray app
  disappears, and a pending snapshot is reused instead of saving the guarded metric as
  the baseline when an earlier restore was declined.
- Persisted route snapshot with restore on OpenCode exit, app exit, next start after
  a crash, and uninstall.
- Proxy probe through `curl.exe` for HTTP, HTTPS, SOCKS5, and SOCKS5H. The
  direct-IP comparison binds to a non-USB Wi-Fi/Ethernet adapter, so a USB
  default route cannot produce a false "same IP" warning.
- Process-scoped proxy environment (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`,
  loopback-aware `NO_PROXY`) without `--proxy-server`, keeping OpenCode
  Desktop's local sidecar connection off the phone proxy.
- Tray menu: status, Launch, Stop, Test, Settings, View log, Start with Windows,
  Uninstall, Exit.
- Opt-in autostart via the `HKCU` Run key.
- Clean uninstall via the tray menu or `uninstall.ps1`, with `-KeepSettings`.
- `install.ps1` that copies the app to `%LOCALAPPDATA%\Programs\OpenCodePhoneProxy`
  and creates a Start Menu shortcut.
- Double-click `install.cmd` / `uninstall.cmd` wrappers for users who do not want
  to run PowerShell manually.
- SHA-256 checksum alongside the release zip.
- `--selftest` mode covering settings, proxy URL validation, argument parsing,
  adapter enumeration, OpenCode discovery, and route-capture parsing.
- GitHub Actions `windows-latest` workflow that builds, self-tests, publishes, and
  uploads `OpenCodePhoneProxy-v1.1.0-win-x64.zip`.
- Windows setup, troubleshooting, and build documentation.

### Known limitations

- The exe is unsigned, so SmartScreen may warn on first run.
- Route protection needs one UAC approval per launch; declining it still launches
  OpenCode, but USB tethering may become the default route.
- Windows x64 only; ARM64 is not built yet.
- Every Proxy must accept connections on the tethered interface.
- A new mobile IP is not guaranteed after airplane mode; the app reports the egress
  IP but does not rotate it.
- Functional validation is on GitHub Actions `windows-latest`; the Linux cross-build
  proves compilation and packaging only.

## [1.0.0] - 2026-11-21

### Added

- Initial `opencode-phone-proxy.sh` launcher for sending OpenCode traffic through a phone-hosted proxy over USB tethering.
- Automatic USB/tether interface detection for common Linux names, including `usb*`, `enx*`, and `enp...u...` paths.
- Automatic phone/proxy host detection from the USB connection gateway.
- NetworkManager `ipv4.never-default` / `ipv6.never-default` handling so the existing Wi-Fi connection remains the default route.
- Proxy connectivity test using `curl` before launch.
- HTTP, HTTPS, SOCKS5, and SOCKS5H proxy scheme support.
- CLI mode and desktop GUI mode.
- GUI mode guards against an already-running OpenCode instance.
- GUI mode restarts the OpenCode background service so it inherits the proxy environment.
- Safe cleanup on normal exit, `SIGINT`, and `SIGTERM`, including restoration of NetworkManager settings and starting a clean OpenCode service.
- `--no-nm` and `--no-test` escape hatches.
- `--version` and `--help` output.
- Makefile targets for checking, smoke testing, packaging, installing, and cleaning.
- Release packaging as `dist/opencode-phone-proxy-v1.0.0.tar.gz` with a SHA-256 checksum.
- README, architecture notes, troubleshooting notes, smoke test, and GitHub Actions CI/release workflows.

### Known limitations

- Linux with NetworkManager only.
- Phone-side USB tethering and proxy app setup is manual.
- The released artifact is a Bash script archive, not a compiled native binary.
- Account-based provider rate limits are not bypassed by changing the phone's public IP.
- Tested against Fedora Linux 45, OpenCode Desktop 2.2.7, and Every Proxy on Android.
