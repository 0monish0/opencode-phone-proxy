#!/usr/bin/env bash
set -euo pipefail

VERSION="1.0.0"

IFACE=""
HOST="${PHONE_PROXY_HOST:-}"
PORT="${PHONE_PROXY_PORT:-8080}"
SCHEME="${PHONE_PROXY_SCHEME:-http}"
CMD="${OPENCODE_CMD:-opencode}"
DO_NM=1
DO_TEST=1
OPENCODE_ARGS=()

usage() {
  cat <<'EOF'
Run OpenCode through an HTTP/SOCKS proxy hosted on your phone (e.g. Every Proxy)
over USB tethering, while keeping your normal default route (college Wi-Fi) intact.

Usage:
  ./opencode-phone-proxy.sh [options] [-- [app args...]]

Options:
  -i, --interface IFACE   USB/tether network interface (default: auto-detect)
  -H, --host HOST         Phone/proxy host IP (default: auto-detect from interface)
  -p, --port PORT         Proxy port on phone (default: 8080)
  -s, --scheme SCHEME     Proxy scheme: http, https, socks5, socks5h (default: http)
  -c, --cmd CMD           Command to run instead of "opencode"
      --gui               Launch GUI OpenCode (/opt/OpenCode/ai.opencode) instead of CLI
      --no-nm             Do not modify NetworkManager connection settings
      --no-test           Skip proxy connectivity test
  -V, --version           Print version and exit
  -h, --help              Show this help

Environment:
  PHONE_PROXY_PORT        Default port if --port not set
  PHONE_PROXY_HOST        Default host if --host not set
  PHONE_PROXY_SCHEME      Default scheme if --scheme not set
  OPENCODE_CMD            Default command if --cmd not set

Phone-side checklist (not automated):
  1) Mobile data ON, USB cable connected, USB tethering ON
  2) Proxy app running (e.g. Every Proxy HTTP proxy on chosen port)
EOF
}

log() { printf '[opencode-phone-proxy] %s\n' "$*" >&2; }
warn() { printf '[opencode-phone-proxy] WARNING: %s\n' "$*" >&2; }
die() { printf '[opencode-phone-proxy] ERROR: %s\n' "$*" >&2; exit 1; }

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "Required command not found: $1"
}

find_bundled_opencode_cli() {
  local cli
  cli=$(ls -1 "$HOME"/.config/ai.opencode.desktop/cli/*/opencode-cli 2>/dev/null | sort -V | tail -n1 || true)
  if [[ -n "$cli" && -x "$cli" ]]; then
    echo "$cli"
    return 0
  fi
  if command -v opencode >/dev/null 2>&1; then
    command -v opencode
    return 0
  fi
  return 1
}

gui_already_running() {
  # Main Electron process has no --type= argument; child/helper processes do.
  pgrep -f '^/opt/OpenCode/ai\.opencode($| )' >/dev/null 2>&1 || return 1
  # If only helper processes somehow matched, try to be stricter
  if pgrep -a -f '/opt/OpenCode/ai\.opencode' 2>/dev/null | grep -v -- '--type=' >/dev/null 2>&1; then
    return 0
  fi
  return 1
}

stop_opencode_service() {
  local cli="$1"
  [[ -n "$cli" && -x "$cli" ]] || return 0
  log "Stopping OpenCode background service (so it can restart with proxy settings)..."
  "$cli" service stop >/dev/null 2>&1 || true
}

start_opencode_service_clean() {
  local cli="$1"
  [[ -n "$cli" && -x "$cli" ]] || return 0
  log "Starting OpenCode background service without proxy (normal mode)..."
  (
    unset HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY http_proxy https_proxy all_proxy no_proxy
    "$cli" service start >/dev/null 2>&1 || true
  )
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -i|--interface) IFACE="${2:-}"; shift 2 ;;
    -H|--host) HOST="${2:-}"; shift 2 ;;
    -p|--port) PORT="${2:-}"; shift 2 ;;
    -s|--scheme) SCHEME="${2:-}"; shift 2 ;;
    -c|--cmd) CMD="${2:-}"; shift 2 ;;
    --gui) CMD="/opt/OpenCode/ai.opencode"; shift ;;
    --no-nm) DO_NM=0; shift ;;
    --no-test) DO_TEST=0; shift ;;
    -V|--version) printf '%s\n' "$VERSION"; exit 0 ;;
    -h|--help) usage; exit 0 ;;
    --) shift; OPENCODE_ARGS=("$@"); break ;;
    *) die "Unknown argument: $1 (use --help)" ;;
  esac
done

need_cmd ip
need_cmd awk
need_cmd grep

NM_CON=""
NM_ORIG_IPV4_NEVER=""
NM_ORIG_IPV6_NEVER=""
NM_CHANGED=0
OPENCODE_CLI=""
SERVICE_STOPPED_FOR_PROXY=0

cleanup() {
  local status=$?
  if [[ $SERVICE_STOPPED_FOR_PROXY -eq 1 && -n "$OPENCODE_CLI" ]]; then
    # Stop proxied service started during this session, then restore a clean service for normal use.
    "$OPENCODE_CLI" service stop >/dev/null 2>&1 || true
    start_opencode_service_clean "$OPENCODE_CLI"
  fi
  if [[ $NM_CHANGED -eq 1 && -n "$NM_CON" ]] && command -v nmcli >/dev/null 2>&1; then
    log "Restoring NetworkManager settings for connection: $NM_CON"
    # Restore previous values (empty means unset/default behavior in some cases; use original if known)
    nmcli con mod "$NM_CON" ipv4.never-default "$NM_ORIG_IPV4_NEVER" >/dev/null 2>&1 || \
      warn "Could not restore ipv4.never-default for $NM_CON"
    nmcli con mod "$NM_CON" ipv6.never-default "$NM_ORIG_IPV6_NEVER" >/dev/null 2>&1 || \
      warn "Could not restore ipv6.never-default for $NM_CON"
    # Bring changes into effect for active connection when possible
    nmcli con up "$NM_CON" >/dev/null 2>&1 || true
  fi
  exit $status
}
trap cleanup EXIT INT TERM

detect_iface() {
  local candidates=()
  local line dev
  while IFS= read -r line; do
    dev=$(awk '{print $2}' <<<"$line")
    [[ -z "$dev" || "$dev" == "lo" ]] && continue
    candidates+=("$dev")
  done < <(ip -o -4 addr show 2>/dev/null | awk '$3=="inet" {print}' || true)

  # de-duplicate while preserving order
  local uniq=()
  local d seen
  for d in "${candidates[@]:-}"; do
    [[ -z "$d" ]] && continue
    seen=0
    for u in "${uniq[@]:-}"; do [[ "$u" == "$d" ]] && seen=1 && break; done
    [[ $seen -eq 0 ]] && uniq+=("$d")
  done
  candidates=("${uniq[@]:-}")

  local preferred=()
  for d in "${candidates[@]:-}"; do
    [[ -z "$d" ]] && continue
    # Common USB/tether naming: usb*, enx* (MAC-based), or PCI USB paths like enp0s20f0u4
    if [[ "$d" == usb* || "$d" == enx* || "$d" == enp*u* || "$d" == enp*s*f*u* ]]; then
      preferred+=("$d")
    fi
  done

  # Exclude known primary wifi/ethernet if they slipped in (unlikely given patterns)
  local filtered=()
  for d in "${preferred[@]:-}"; do
    case "$d" in
      wlp*|wlan*) ;;
      *) filtered+=("$d") ;;
    esac
  done
  preferred=("${filtered[@]:-}")

  if [[ ${#preferred[@]} -eq 1 ]]; then
    echo "${preferred[0]}"
    return 0
  elif [[ ${#preferred[@]} -gt 1 ]]; then
    warn "Multiple USB-like interfaces found: ${preferred[*]}"
    printf '%s\n' "${preferred[0]}"
    return 0
  fi

  # Fallback: interfaces with IPv4 that aren't common primary/system names
  local other=()
  for d in "${candidates[@]:-}"; do
    [[ -z "$d" ]] && continue
    case "$d" in
      lo|wlp*|wlan*|enp[0-9]*s[0-9]|enp[0-9]*s[0-9][0-9]|eth[0-9]*|docker*|br-*|virbr*|veth*|tailscale*|tun*|wg*|zt*) ;;
      *) other+=("$d") ;;
    esac
  done
  if [[ ${#other[@]} -eq 1 ]]; then
    echo "${other[0]}"
    return 0
  fi

  return 1
}

detect_host() {
  local iface="$1"
  local gw
  gw=$(ip -4 route show default dev "$iface" 2>/dev/null | awk '{for(i=1;i<=NF;i++) if($i=="via"){print $(i+1); exit}}' || true)
  if [[ -n "$gw" ]]; then
    echo "$gw"
    return 0
  fi
  if command -v nmcli >/dev/null 2>&1; then
    gw=$(nmcli -g IP4.GATEWAY dev show "$iface" 2>/dev/null | head -n1 || true)
    if [[ -n "$gw" && "$gw" != "--" ]]; then
      echo "$gw"
      return 0
    fi
  fi
  # Common Android USB tether gateway
  local ipcidr ipbase
  ipcidr=$(ip -o -4 addr show dev "$iface" 2>/dev/null | awk '{print $4; exit}' || true)
  if [[ "$ipcidr" =~ ^192\.168\.42\. ]]; then
    echo "192.168.42.129"
    return 0
  fi
  return 1
}

find_nm_connection_for_device() {
  local iface="$1"
  command -v nmcli >/dev/null 2>&1 || return 1
  nmcli -t -f NAME,DEVICE con show --active 2>/dev/null | awk -F: -v d="$iface" '$2==d {print $1; exit}'
}

maybe_set_nm_never_default() {
  local iface="$1"
  [[ $DO_NM -eq 1 ]] || return 0
  command -v nmcli >/dev/null 2>&1 || { warn "nmcli not found; cannot adjust NetworkManager route priority"; return 0; }

  NM_CON=$(find_nm_connection_for_device "$iface" || true)
  if [[ -z "$NM_CON" ]]; then
    warn "No active NetworkManager connection found for $iface; skipping route-priority adjustment"
    return 0
  fi

  NM_ORIG_IPV4_NEVER=$(nmcli -g ipv4.never-default con show "$NM_CON" 2>/dev/null || echo "no")
  NM_ORIG_IPV6_NEVER=$(nmcli -g ipv6.never-default con show "$NM_CON" 2>/dev/null || echo "no")
  [[ -z "$NM_ORIG_IPV4_NEVER" ]] && NM_ORIG_IPV4_NEVER="no"
  [[ -z "$NM_ORIG_IPV6_NEVER" ]] && NM_ORIG_IPV6_NEVER="no"

  log "Ensuring '$NM_CON' ($iface) is not used as default route (was ipv4.never-default=$NM_ORIG_IPV4_NEVER, ipv6.never-default=$NM_ORIG_IPV6_NEVER)"
  if nmcli con mod "$NM_CON" ipv4.never-default yes ipv6.never-default yes >/dev/null 2>&1; then
    NM_CHANGED=1
    nmcli con up "$NM_CON" >/dev/null 2>&1 || true
  else
    warn "Failed to modify NetworkManager connection '$NM_CON' (may need permissions). Continuing without changing route priority."
    NM_CON=""
  fi
}

# --- main ---
if [[ "$CMD" == "/opt/OpenCode/ai.opencode" || "$CMD" == *"ai.opencode"* ]]; then
  OPENCODE_CLI=$(find_bundled_opencode_cli || true)
  if gui_already_running; then
    die "OpenCode GUI is already running. Quit it completely (and wait a few seconds), then run this script again. Launching again while it runs will attach to the existing non-proxied instance and exit immediately."
  fi
fi

if [[ -z "$IFACE" ]]; then
  IFACE=$(detect_iface || true)
fi
[[ -n "$IFACE" ]] || die "Could not auto-detect USB tether interface. Connect phone, enable USB tethering, then retry or pass --interface <iface> (see: ip -br addr)."

ip link show "$IFACE" >/dev/null 2>&1 || die "Interface not found: $IFACE"
ip -4 addr show dev "$IFACE" 2>/dev/null | grep -q 'inet ' || \
  die "Interface $IFACE has no IPv4 address. Is USB tethering enabled on the phone?"

if [[ -z "$HOST" ]]; then
  HOST=$(detect_host "$IFACE" || true)
fi
[[ -n "$HOST" ]] || die "Could not auto-detect phone/proxy host for $IFACE. Pass --host <phone-ip> (often shown as gateway in: ip route show dev $IFACE)."

PROXY_URL="${SCHEME}://${HOST}:${PORT}"

log "Interface: $IFACE"
log "Proxy:     $PROXY_URL"
log "Command:   $CMD ${OPENCODE_ARGS[*]:-}"
log "Default route before launch:"
ip route show default >&2 || true

maybe_set_nm_never_default "$IFACE"

if [[ $DO_TEST -eq 1 ]]; then
  if command -v curl >/dev/null 2>&1; then
    log "Testing proxy connectivity (curl https://api.ipify.org via proxy, 10s timeout)..."
    if curl --max-time 10 --proxy "$PROXY_URL" -fsS https://api.ipify.org >/dev/null 2>&1; then
      log "Proxy test: OK"
    else
      warn "Proxy test failed. Make sure mobile data is on and the proxy app is running on the phone ($PROXY_URL). Continuing anyway."
    fi
  else
    warn "curl not found; skipping proxy test"
  fi
fi

if ! command -v "$CMD" >/dev/null 2>&1; then
  if [[ $CMD == */* && -x $CMD ]]; then
    : # absolute/relative executable path is fine
  else
    die "Command not found: $CMD. Install opencode, or pass --cmd <path-or-command>."
  fi
fi

# For GUI mode, restart background service so it inherits proxy env vars for this session.
if [[ -n "$OPENCODE_CLI" && "$CMD" == "/opt/OpenCode/ai.opencode" ]]; then
  stop_opencode_service "$OPENCODE_CLI"
  SERVICE_STOPPED_FOR_PROXY=1
fi

log "Launching with proxy env vars set for this process only..."
log "(Press Ctrl+C or exit OpenCode normally; network settings changed by this script will be restored.)"

export HTTP_PROXY="$PROXY_URL"
export HTTPS_PROXY="$PROXY_URL"
export ALL_PROXY="$PROXY_URL"
export http_proxy="$PROXY_URL"
export https_proxy="$PROXY_URL"
export all_proxy="$PROXY_URL"
# Preserve local access; append common local ranges if NO_PROXY already set
LOCAL_NO_PROXY="localhost,127.0.0.1,::1"
if [[ -n "${NO_PROXY:-}" ]]; then
  export NO_PROXY="${NO_PROXY},${LOCAL_NO_PROXY}"
  export no_proxy="${NO_PROXY}"
else
  export NO_PROXY="$LOCAL_NO_PROXY"
  export no_proxy="$LOCAL_NO_PROXY"
fi

set +e
"$CMD" "${OPENCODE_ARGS[@]}"
status=$?
set -e

log "OpenCode exited with status $status"
exit $status
