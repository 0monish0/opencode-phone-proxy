# Roadmap

## v1.x

- Keep the launcher focused and reliable for Linux + NetworkManager + Android USB tethering.
- Improve diagnostics when the OpenCode service or GUI is in a stale state.
- Add more smoke-test coverage around option parsing and help/version output.
- Document tested OpenCode, Every Proxy, and distribution versions.

## v2 candidates

- Pass Chromium `--proxy-server` and bypass flags to the desktop GUI so the entire Electron network stack uses the phone proxy, not only the OpenCode background service.
- Optional DNS handling: keep the phone's resolver available while the USB link is not the default route, with explicit save/restore.
- Automated phone-side checks through ADB where authorized, while keeping manual setup as the default.
- A `systemd --user` service or desktop integration for repeatable launches.
- Windows and macOS support, or clearly separated platform-specific launchers.
- A mock HTTP/SOCKS proxy integration test that verifies the launcher's environment and cleanup behavior without requiring a real phone.
- Optional `--phone-default` mode that intentionally makes the USB link the default route for the duration of the session, with the same safe cleanup guarantees.
- Structured release notes and checksums for every tag.

## Non-goals

- Bypassing account-based provider quotas.
- Evading campus network policy or service terms.
- Transparently proxying every application on the machine.
