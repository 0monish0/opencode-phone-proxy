# Troubleshooting

## First checks

Run these on the laptop while USB tethering and the phone proxy are active:

```bash
ip -br addr
ip route show
nmcli -t -f NAME,TYPE,DEVICE,STATE con show
```

You want to see:

- A USB-style interface with an IPv4 address.
- A default route on your normal Wi-Fi interface, not the USB interface, while the script is running.
- A direct route to the USB subnet.

Example:

```text
default via 10.3.0.1 dev wlp45s0
10.203.63.0/24 dev enp0s20f0u4
```

The exact addresses will differ.

## `Could not auto-detect USB tether interface`

The phone link is not visible, or its name did not match a known USB pattern.

1. Re-enable USB tethering on the phone.
2. Run `ip -br addr`.
3. Look for `usb0`, `enx...`, `enp...u...`, or another interface that appeared when you connected the phone.
4. Pass it explicitly:

```bash
./opencode-phone-proxy.sh --gui --interface <iface> --host <phone-ip>
```

Replace the placeholders. The angle brackets are not part of the value.

## `bash: syntax error near unexpected token 'newline'`

This usually means a placeholder was pasted literally:

```bash
--interface <iface>
```

Use a real interface name instead:

```bash
--interface enp0s20f0u4
```

## `Interface ... has no IPv4 address`

USB tethering is off, the cable is not carrying data, or the USB network has not finished configuring.

1. Toggle USB tethering off and on.
2. Try another USB cable or port.
3. Run `ip -br addr` again.

## `Proxy test failed`

The laptop cannot make an HTTP request through the phone proxy.

Check:

1. Mobile data is on.
2. USB tethering is on.
3. Every Proxy is running in **Server** mode.
4. The host and port match the values shown by Every Proxy.
5. The proxy app is not being paused by Android battery optimization.
6. The phone is not on a captive portal or out of data.

Direct test:

```bash
curl -x http://<phone-ip>:<port> --max-time 20 -v https://api.ipify.org
```

Try SOCKS5 if the app provides it:

```bash
./opencode-phone-proxy.sh --gui -s socks5 -p <socks-port>
```

## GUI exits immediately

Check for a running instance:

```bash
pgrep -a ai.opencode
```

Quit OpenCode completely and retry. Starting a second GUI instance normally hands off to the first one and exits, so the second process cannot introduce a new proxy environment.

## OpenCode is “working” but nothing happens

Check whether the background service is actually connected to the proxy:

```bash
ss -tnp | grep <phone-ip>:<port>
pgrep -a -f 'opencode-cli|ai.opencode'
```

If there are no proxy connections, the service was probably started before the proxy environment existed. Quit OpenCode and relaunch through the script.

Inspect the service environment:

```bash
pid=$(pgrep -f 'opencode-cli serve --service' | head -n1)
tr '\0' '\n' < "/proc/$pid/environ" | grep -i proxy
```

Expected values include:

```text
HTTPS_PROXY=http://<phone-ip>:<port>
HTTP_PROXY=http://<phone-ip>:<port>
NO_PROXY=localhost,127.0.0.1,::1
```

If the service has no proxy variables, restart it through the script.

## It works with Wi-Fi off but not with Wi-Fi on

This usually means an old OpenCode service or GUI was still running without the proxy environment, or some traffic is taking the Wi-Fi default route instead of the proxy.

1. Quit everything OpenCode-related.
2. Confirm no processes remain:

```bash
pgrep -a ai.opencode
pgrep -a opencode-cli
```

3. Relaunch through the script.
4. Confirm the service has proxy variables and established connections to the phone proxy.

If it still fails, use `--no-test` and inspect the app logs, or test with a simple CLI request:

```bash
./opencode-phone-proxy.sh -- run "Reply with exactly: OK"
```

## USB tethering drops after airplane mode or sleep

Android may turn USB tethering off when the USB link resets.

1. Re-enable USB tethering.
2. Restart Every Proxy if it stopped listening.
3. Relaunch the script.
4. If the phone's USB address changed, pass the new `--host`.

## Checking and refreshing the phone IP

Check the IP used by proxied requests:

```bash
curl -x http://<phone-ip>:<port> https://api.ipify.org; echo
```

Compare with direct traffic:

```bash
curl https://api.ipify.org; echo
```

To try for a new mobile IP:

1. Airplane mode on for 30–60 seconds.
2. Airplane mode off.
3. Mobile data on.
4. USB tethering on.
5. Restart Every Proxy if needed.
6. Check the proxy IP again.

Carrier CGNAT may keep the same public IP. Rebooting the phone or toggling mobile data can help, but there is no guarantee.

## Rate limits

Provider rate limits are often attached to the account or API key, not the IP. A new mobile IP will not necessarily reset a free-tier or account quota. Check the provider's usage dashboard, wait for the reset window, switch models/providers, reduce concurrent requests, or top up.

Do not use IP rotation to evade service limits or campus policy.
