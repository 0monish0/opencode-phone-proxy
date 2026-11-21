#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT="$ROOT/opencode-phone-proxy.sh"
EXPECTED_VERSION="$(cat "$ROOT/VERSION")"

bash -n "$SCRIPT"

ACTUAL_VERSION="$("$SCRIPT" --version)"
if [[ "$ACTUAL_VERSION" != "$EXPECTED_VERSION" ]]; then
  printf 'version mismatch: script=%s VERSION=%s\n' "$ACTUAL_VERSION" "$EXPECTED_VERSION" >&2
  exit 1
fi

HELP="$("$SCRIPT" --help)"
for expected in '--gui' '--interface' '--host' '--port' '--scheme' '--no-nm' '--no-test' '--version'; do
  if ! grep -Fq -- "$expected" <<<"$HELP"; then
    printf 'help output is missing %s\n' "$expected" >&2
    exit 1
  fi
done

printf 'smoke tests passed for v%s\n' "$EXPECTED_VERSION"
