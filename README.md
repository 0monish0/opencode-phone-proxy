# OpenCode Phone Proxy

Keep your Linux laptop on campus Wi-Fi and route **only OpenCode** through an HTTP/SOCKS proxy running on your Android phone over USB tethering.

This is for the common campus situation where the laptop must stay on the college network, the phone has usable mobile data, and USB tethering works but you do not want every application on the laptop to switch to the phone connection.

## What it does

- Detects the Android USB-tether interface (for example `usb0`, `enx...`, or `enp0s20f0u4`).
- Detects the phone/proxy address on that USB link.
- Keeps the USB connection from becoming the laptop's default route, so the normal Wi-Fi connection continues to handle ordinary traffic.
- Tests the proxy connection before launching.
- Starts OpenCode with `HTTP_PROXY`, `HTTPS_PROXY`, and `ALL_PROXY` scoped to that process only.
- In GUI mode, restarts the OpenCode background service so it inherits the proxy environment, and refuses to launch if an existing GUI instance is already running.
- On exit, restores any NetworkManager settings it changed and starts a clean non-proxied OpenCode service for normal use.

The result is:

```text
OpenCode GUI/CLI
      |
      |  HTTP(S)_PROXY scoped to this process
      v
Local OpenCode background service
      |
      |  http://<phone-ip>:8080
      v
Every Proxy (or another proxy app) on the phone
      |
      |  USB tethering
      v
Phone mobile data
```

The laptop can remain connected to college Wi-Fi the whole time. The phone proxy is only used by the OpenCode process launched by this script.

## Requirements

- Linux with NetworkManager (`nmcli`) for the automatic route-priority handling.
- Android phone with **USB tethering**.
- A proxy app on the phone; this project was tested with **Every Proxy** in **Server → HTTP Proxy** mode.
- OpenCode installed as either:
  - the desktop GUI at `/opt/OpenCode/ai.opencode`, or
  - a CLI command discoverable as `opencode`.
- `bash`, `ip`, `awk`, and `grep`. `curl` and `nmcli` are used when available.

This has been tested with Fedora Linux 45 (KDE Plasma), OpenCode Desktop 2.2.7, and Every Proxy on Android. Other NetworkManager-based distributions should work, but are not verified.

## Quick start

### 1. Prepare the phone

1. Turn on **mobile data**.
2. Connect the phone to the laptop with a USB cable.
3. Enable **USB tethering**.
4. Start **Every Proxy → Server → HTTP Proxy** on port `8080` (or your chosen port).
5. Keep Every Proxy allowed in the background and disable battery restrictions for it if Android keeps pausing it.

### 2. Find the USB interface and phone address

On the laptop:

```bash
ip -br addr
ip -4 route show dev <usb-interface>
nmcli -g IP4.GATEWAY dev show <usb-interface> 2>/dev/null
```

Example values:

```text
USB interface: enp0s20f0u4
Phone address: 10.203.63.144
Port:          8080
```

The phone address is usually the gateway of the USB subnet, and it is also the address shown by Every Proxy.

### 3. Launch OpenCode

GUI:

```bash
chmod +x opencode-phone-proxy.sh
./opencode-phone-proxy.sh --gui \
  --interface enp0s20f0u4 \
  --host 10.203.63.144 \
  --port 8080
```

CLI:

```bash
./opencode-phone-proxy.sh \
  --interface enp0s20f0u4 \
  --host 10.203.63.144 \
  --port 8080 \
  -- run "Explain this repository"
```

Close OpenCode normally when you are finished. The script will restore its temporary network changes and restart a clean OpenCode service.

## Options

| Option | Description |
| --- | --- |
| `-i, --interface IFACE` | USB/tether interface. Auto-detected if omitted. |
| `-H, --host HOST` | Phone/proxy address. Auto-detected from the interface gateway if omitted. |
| `-p, --port PORT` | Proxy port. Default: `8080`. |
| `-s, --scheme SCHEME` | `http`, `https`, `socks5`, or `socks5h`. Default: `http`. |
| `-c, --cmd CMD` | Command to launch instead of `opencode`. |
| `--gui` | Launch the desktop GUI at `/opt/OpenCode/ai.opencode`. |
| `--no-nm` | Do not modify NetworkManager settings. |
| `--no-test` | Skip the `curl` proxy connectivity test. |
| `-V, --version` | Print the version. |
| `-h, --help` | Print help. |
| `--` | Pass the remaining arguments to the launched command. |

Environment equivalents:

| Variable | Purpose |
| --- | --- |
| `PHONE_PROXY_HOST` | Default proxy host. |
| `PHONE_PROXY_PORT` | Default proxy port. |
| `PHONE_PROXY_SCHEME` | Default proxy scheme. |
| `OPENCODE_CMD` | Default command to launch. |

## Install

User-local install:

```bash
make test
make install
```

This installs `opencode-phone-proxy` into `~/.local/bin`.

System-wide install:

```bash
sudo make install PREFIX=/usr/local
```

## Safety and cleanup

The script is designed to avoid leaving your normal network setup altered:

- Proxy environment variables are exported only for the launched process and its children. They are not written to your shell profile or system proxy settings.
- NetworkManager's `ipv4.never-default` and `ipv6.never-default` values on the USB connection are saved before changing and restored on `EXIT`, `SIGINT`, or `SIGTERM`.
- GUI mode stops the OpenCode background service before launch so it can inherit the proxy environment, and starts a clean non-proxied service on exit.
- GUI mode refuses to run if an OpenCode GUI instance is already running, because launching a second instance normally attaches to the existing non-proxied instance and exits immediately.
- If NetworkManager cannot be modified, the script warns and continues; use `--no-nm` if you manage routes yourself.

The script does **not** automate the phone-side steps. USB tethering and the proxy app must already be running.

## Troubleshooting

### `Could not auto-detect USB tether interface`

Check that USB tethering is enabled:

```bash
ip -br addr
```

Look for an interface such as `usb0`, `enx...`, or `enp0s20f0u4`. Pass it explicitly:

```bash
./opencode-phone-proxy.sh --gui --interface <iface> --host <phone-ip>
```

Do not type the angle brackets. Replace `<iface>` and `<phone-ip>` with real values.

### `bash: syntax error near unexpected token 'newline'`

You copied a placeholder literally, for example `--interface <iface>`. Replace the whole placeholder with the real interface name.

### GUI exits immediately after launch

An OpenCode GUI instance is already running. Quit it completely, verify it is gone, and retry:

```bash
pgrep -a ai.opencode
```

### Proxy test fails

- Confirm mobile data is on.
- Confirm USB tethering is on.
- Confirm Every Proxy is running in **Server** mode.
- Confirm the host and port match what Every Proxy shows.
- Temporarily disable battery optimization for Every Proxy.
- Try the SOCKS server instead:

```bash
./opencode-phone-proxy.sh --gui -s socks5 -p <socks-port>
```

### OpenCode appears stuck on “working”

Check whether the OpenCode service is actually connected to the phone proxy:

```bash
ss -tnp | grep <phone-ip>:<port>
pgrep -a -f 'opencode-cli|ai.opencode'
```

If there are no proxy connections, quit OpenCode and relaunch through the script. The script's GUI mode must start the backend service with the proxy environment; an already-running service from a normal launch will not use the proxy.

If connections are present but responses are slow, the phone's mobile network or the free model's queue may be the bottleneck. Test the proxy path directly:

```bash
curl -x http://<phone-ip>:<port> --max-time 20 https://api.ipify.org; echo
```

### USB tethering dropped

Re-enable USB tethering on the phone, restart Every Proxy if needed, then relaunch the script. If the phone's USB address changed, update `--host`.

## Refreshing the phone's public IP

The IP used by the proxy is the phone's **mobile-data public IP**, assigned by the carrier. Neither USB tethering nor Every Proxy creates that IP.

To try to get a new one:

1. Turn on airplane mode for 30–60 seconds.
2. Turn airplane mode off and ensure mobile data is on.
3. Re-enable USB tethering if Android disabled it.
4. Restart Every Proxy if it is no longer listening.
5. Check the public IP through the proxy:

```bash
curl -x http://<phone-ip>:<port> https://api.ipify.org; echo
```

Many carriers use CGNAT and may hand back the same address. Rebooting the phone or toggling mobile data can also help, but nothing guarantees a new IP.

## Rate limits and terms of use

Provider and model rate limits are commonly tied to the **account/API key**, not the source IP. A new mobile IP will not reset account-based quotas. Use the provider's normal reset window, switch models/providers, reduce concurrent requests, or top up the account.

Do not use IP rotation to evade service limits or your campus network's acceptable-use policy. Check the terms of the services you use.

## Development

```bash
make check     # bash syntax check, plus shellcheck when available
make test      # smoke tests
make package   # build dist/opencode-phone-proxy-v1.0.0.tar.gz + checksum
make install   # install to ~/.local/bin
make clean     # remove dist/
```

## Project structure

```text
.
├── opencode-phone-proxy.sh   # the launcher
├── VERSION                   # current version
├── Makefile                  # check, test, package, install
├── README.md
├── CHANGELOG.md
├── ROADMAP.md
├── docs/
│   ├── ARCHITECTURE.md
│   └── TROUBLESHOOTING.md
├── tests/
│   └── smoke.sh
└── .github/workflows/
    ├── ci.yml
    └── release.yml
```

## Versioning

This repository starts at `v1.0.0`. See [CHANGELOG.md](CHANGELOG.md). Version 2 work can build on the same launcher and packaging flow.

## License

No license is granted. This is a private repository. Add a license if you want to allow reuse or redistribution.

## Acknowledgements

- [OpenCode](https://opencode.ai/) for the coding agent.
- [Every Proxy](https://everyproxy.com/) for the Android proxy app used during testing.

This project is independent and is not affiliated with OpenCode or Every Proxy.
