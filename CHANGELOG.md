# Changelog

All notable changes to this project are documented here. This project follows [Semantic Versioning](https://semver.org/).

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
