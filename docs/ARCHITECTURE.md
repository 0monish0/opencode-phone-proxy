# Architecture

## Goal

Use a phone-hosted proxy for OpenCode while the laptop remains on its normal network (for example, campus Wi-Fi). Traffic from other applications must continue to use the laptop's Wi-Fi connection.

## Two separate mechanisms

There are two things people often conflate:

1. **USB tethering** creates a network link between the phone and the laptop. The laptop gets an IP on a private USB subnet, and the phone can forward packets to its mobile-data connection.
2. **A proxy** listens on the phone and forwards HTTP/SOCKS requests. The laptop must explicitly send requests to the proxy address.

USB tethering alone does not keep the laptop on Wi-Fi while routing only one application through the phone. If the USB link becomes the default route, all traffic uses it. If it does not, nothing uses it unless an application is configured to use the phone's address directly.

This project uses USB tethering as the transport and an HTTP/SOCKS proxy as the selector. Only the launched OpenCode process receives the proxy environment, so only its requests use the phone path.

```text
┌──────────────────────────── Laptop ────────────────────────────┐
│                                                                │
│  OpenCode GUI / CLI                                             │
│       │                                                        │
│       │ HTTP(S)_PROXY=<phone>:<port> (process-scoped)           │
│       ▼                                                        │
│  OpenCode background service                                    │
│       │                                                        │
│       │ TCP to the USB subnet                                   │
│       ▼                                                        │
│  enp0s20f0u4 / usb0 / enx...  ──────────────┐                   │
│       │                                     │                   │
│       │ default route remains on Wi-Fi      │                   │
│       ▼                                     │                   │
│  wlp45s0 / wlan0  (campus Wi-Fi)            │                   │
│                                             │                   │
└─────────────────────────────────────────────┼───────────────────┘
                                              │ USB
                                              ▼
┌──────────────────────────── Phone ──────────────────────────────┐
│  Every Proxy (Server → HTTP Proxy, port 8080)                   │
│       │                                                        │
│       ▼                                                        │
│  Mobile data                                                    │
└─────────────────────────────────────────────────────────────────┘
```

## Route priority

The script finds the NetworkManager connection attached to the USB interface and sets:

```text
ipv4.never-default = yes
ipv6.never-default = yes
```

That prevents the USB connection from installing a default route. The normal Wi-Fi default route remains in place. The USB subnet route still exists, so the laptop can reach the phone proxy directly at its USB address.

The original values are read before the change and written back during cleanup. The script also brings the connection up again when possible so the restored settings take effect.

## Proxy environment

The script exports the proxy URL in upper- and lowercase forms:

```text
HTTP_PROXY
HTTPS_PROXY
ALL_PROXY
http_proxy
https_proxy
all_proxy
```

`NO_PROXY` includes loopback so the GUI can keep talking to the local OpenCode service:

```text
localhost,127.0.0.1,::1
```

The exports happen immediately before launching the target command. They are inherited by that process and its children, but they are not written to the user's shell profile, systemd environment, or desktop-wide proxy settings.

## GUI mode and the OpenCode background service

OpenCode clients talk to a shared background service. The service, not the GUI window itself, owns provider requests. Setting proxy variables only on the GUI process is therefore not enough if the service is already running without them.

GUI mode does the following:

1. Finds the bundled OpenCode CLI, for example:
   `~/.config/ai.opencode.desktop/cli/<version>/opencode-cli`.
2. Checks for an existing `/opt/OpenCode/ai.opencode` process. If one is running, the script stops with a clear error. A second Electron launch would otherwise hand off to the existing instance and exit, leaving the old non-proxied service in place.
3. Stops the existing background service.
4. Launches `/opt/OpenCode/ai.opencode` with the proxy environment exported.
5. On exit, stops the service created for the proxied session and starts a clean service without proxy variables.

The service then opens connections to the phone proxy, which is visible as established TCP connections from the laptop's USB IP to the phone's USB IP on the proxy port.

## Failure behavior

The script is intentionally conservative:

- Interface detection failure: exits with instructions unless `--interface` is supplied.
- Missing IPv4 address on the USB interface: exits; USB tethering is probably not enabled.
- Host detection failure: exits unless `--host` is supplied.
- Proxy test failure: warns and continues; the user may still want to inspect the app.
- NetworkManager modification failure: warns and continues without the route priority change.
- Missing target command: exits unless it is an executable path.

Use `--no-nm` to avoid touching NetworkManager at all, or `--no-test` to skip the `curl` check.

## What this is not

- It is not a VPN.
- It is not a transparent proxy.
- It does not configure Android or start the phone proxy app.
- It does not bypass account-based provider quotas or campus network policy.
- It does not make all laptop traffic use the phone. That is the point of the process-scoped proxy environment.
