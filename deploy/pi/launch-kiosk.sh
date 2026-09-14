#!/usr/bin/env bash
set -euo pipefail

readonly player_url="http://127.0.0.1:8787"
readonly health_url="${player_url}/player/v1/health"
readonly profile_directory="/var/lib/display-control-kiosk/profile"

chromium_binary=""
for candidate in /usr/bin/chromium /usr/bin/chromium-browser; do
  if [[ -x "${candidate}" ]]; then
    chromium_binary="${candidate}"
    break
  fi
done

if [[ -z "${chromium_binary}" ]]; then
  echo "A supported Chromium executable is not installed." >&2
  exit 1
fi

for _ in $(seq 1 60); do
  if /usr/bin/curl --fail --silent --show-error --max-time 2 "${health_url}" >/dev/null; then
    break
  fi
  /usr/bin/sleep 1
done

if ! /usr/bin/curl --fail --silent --show-error --max-time 2 "${health_url}" >/dev/null; then
  echo "The loopback player did not become healthy within 60 seconds." >&2
  exit 1
fi

/usr/bin/install -d -m 0700 "${profile_directory}"

platform_arguments=()
if [[ -n "${XDG_RUNTIME_DIR:-}" && -n "${WAYLAND_DISPLAY:-}" &&
      -S "${XDG_RUNTIME_DIR}/${WAYLAND_DISPLAY}" ]]; then
  platform_arguments+=(--ozone-platform=wayland)
elif [[ -n "${DISPLAY:-}" ]]; then
  platform_arguments+=(--ozone-platform=x11)
fi

exec "${chromium_binary}" \
  --kiosk \
  --no-first-run \
  --no-default-browser-check \
  --disable-session-crashed-bubble \
  --disable-features=Translate \
  --disable-pinch \
  --overscroll-history-navigation=0 \
  --password-store=basic \
  --user-data-dir="${profile_directory}" \
  "${platform_arguments[@]}" \
  "${player_url}"
