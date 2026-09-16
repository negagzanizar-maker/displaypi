#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: sudo ./install.sh --artifact FILE --sha256 HEX --version VERSION --server HTTPS_URL [--enrollment-code-file FILE] [--server-ca FILE] [--kiosk-user USER]" >&2
}

artifact=""
expected_sha256=""
release_version=""
server_url=""
enrollment_source=""
server_ca=""
kiosk_user="display-control-kiosk"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --artifact) artifact="${2:-}"; shift 2 ;;
    --sha256) expected_sha256="${2:-}"; shift 2 ;;
    --version) release_version="${2:-}"; shift 2 ;;
    --server) server_url="${2:-}"; shift 2 ;;
    --enrollment-code-file) enrollment_source="${2:-}"; shift 2 ;;
    --server-ca) server_ca="${2:-}"; shift 2 ;;
    --kiosk-user) kiosk_user="${2:-}"; shift 2 ;;
    *) usage; exit 2 ;;
  esac
done

if [[ ${EUID} -ne 0 ]]; then
  echo "This installer must run as root." >&2
  exit 1
fi

if [[ ! -f "${artifact}" ||
      ( -n "${enrollment_source}" && ! -f "${enrollment_source}" ) ||
      ! "${expected_sha256}" =~ ^[0-9a-fA-F]{64}$ ||
      ! "${release_version}" =~ ^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$ ||
      ! "${server_url}" =~ ^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?/?$ ||
      ! "${kiosk_user}" =~ ^[a-z_][a-z0-9_-]*[$]?$ ]]; then
  usage
  exit 2
fi

artifact="$(readlink -f -- "${artifact}")"
if [[ -n "${enrollment_source}" ]]; then
  enrollment_source="$(readlink -f -- "${enrollment_source}")"
fi
for required_command in curl openssl sha256sum tar update-ca-certificates; do
  if ! command -v "${required_command}" >/dev/null 2>&1; then
    echo "Required command is not installed: ${required_command}" >&2
    exit 1
  fi
done

if [[ -n "${server_ca}" ]]; then
  if [[ ! -f "${server_ca}" || -L "${server_ca}" ]]; then
    echo "The server CA certificate is missing or unsafe." >&2
    exit 2
  fi
  server_ca="$(readlink -f -- "${server_ca}")"
  openssl x509 -in "${server_ca}" -noout >/dev/null
fi
if [[ "$(uname -m)" != "aarch64" ]]; then
  echo "This artifact requires 64-bit Raspberry Pi OS (aarch64)." >&2
  exit 1
fi

actual_sha256="$(sha256sum -- "${artifact}" | awk '{print $1}')"
if [[ "${actual_sha256,,}" != "${expected_sha256,,}" ]]; then
  echo "Artifact SHA-256 verification failed." >&2
  exit 1
fi

if ! id display-control-agent >/dev/null 2>&1; then
  useradd --system --user-group --home-dir /var/lib/display-control --shell /usr/sbin/nologin display-control-agent
fi
if ! id "${kiosk_user}" >/dev/null 2>&1; then
  if [[ "${kiosk_user}" != "display-control-kiosk" ]]; then
    echo "The requested graphical kiosk user does not exist: ${kiosk_user}" >&2
    exit 1
  fi
  useradd --system --user-group --home-dir /var/lib/display-control-kiosk --create-home --shell /usr/sbin/nologin "${kiosk_user}"
fi
kiosk_group="$(id -gn "${kiosk_user}")"
kiosk_uid="$(id -u "${kiosk_user}")"
kiosk_home="$(getent passwd "${kiosk_user}" | cut -d: -f6)"

install -d -o root -g root -m 0755 /opt/display-control/releases /etc/display-control /usr/local/lib/display-control
install -d -o display-control-agent -g display-control-agent -m 0700 /var/lib/display-control
install -d -o "${kiosk_user}" -g "${kiosk_group}" -m 0700 /var/lib/display-control-kiosk

if [[ -z "${enrollment_source}" &&
      ( ! -f /var/lib/display-control/agent-state.json ||
        ! -f /var/lib/display-control/device-private-key.pem ||
        -L /var/lib/display-control/agent-state.json ||
        -L /var/lib/display-control/device-private-key.pem ) ]]; then
  echo "An enrollment code file is required for a device without existing protected enrollment state." >&2
  exit 2
fi

if [[ -n "${server_ca}" ]]; then
  install -o root -g root -m 0644 "${server_ca}" /usr/local/share/ca-certificates/display-control-field-test.crt
  update-ca-certificates >/dev/null
fi

previous_release="$(readlink -f /opt/display-control/current 2>/dev/null || true)"
release_directory="/opt/display-control/releases/${release_version}"
if [[ -e "${release_directory}" ]]; then
  echo "Release directory already exists; refusing to overwrite it." >&2
  exit 1
fi

staging_directory="/opt/display-control/releases/.staging-${release_version}-$$"
previous_environment="/etc/display-control/.agent.env-previous-$$"
cleanup() {
  if [[ -d "${staging_directory}" ]]; then
    rm -rf --one-file-system -- "${staging_directory}"
  fi
  rm -f -- "${previous_environment}"
}
trap cleanup EXIT
install -d -o root -g root -m 0755 "${staging_directory}"
tar --extract --gzip --file "${artifact}" --directory "${staging_directory}" --no-same-owner --no-same-permissions
if [[ ! -f "${staging_directory}/DisplayControl.DeviceAgent" || -L "${staging_directory}/DisplayControl.DeviceAgent" ]]; then
  echo "Release does not contain the expected agent executable." >&2
  exit 1
fi
chmod 0755 "${staging_directory}/DisplayControl.DeviceAgent"
printf '%s\n' "${release_version}" > "${staging_directory}/release-version"
chown -R root:root "${staging_directory}"
mv -- "${staging_directory}" "${release_directory}"

if [[ -f /etc/display-control/agent.env ]]; then
  cp --preserve=mode,ownership /etc/display-control/agent.env "${previous_environment}"
fi
if [[ -n "${enrollment_source}" ]]; then
  install -o display-control-agent -g display-control-agent -m 0600 "${enrollment_source}" /var/lib/display-control/enrollment-code
fi

{
cat <<EOF
Agent__ServerBaseAddress=${server_url}
Agent__StateDirectory=/var/lib/display-control
Agent__HeartbeatIntervalSeconds=30
Agent__MaximumCacheBytes=4294967296
Agent__MinimumFreeDiskBytes=268435456
EOF
if [[ -n "${enrollment_source}" ]]; then
  echo "Agent__EnrollmentCodeFile=/var/lib/display-control/enrollment-code"
fi
if [[ -n "${server_ca}" ]]; then
  echo "Agent__CheckServerCertificateRevocation=false"
fi
} > /etc/display-control/agent.env
chown root:display-control-agent /etc/display-control/agent.env
chmod 0640 /etc/display-control/agent.env

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install -o root -g root -m 0755 "${script_directory}/launch-kiosk.sh" /usr/local/lib/display-control/launch-kiosk.sh
install -o root -g root -m 0644 "${script_directory}/display-control-agent.service" /etc/systemd/system/display-control-agent.service
escaped_kiosk_home="${kiosk_home//&/\\&}"
sed \
  -e "s|@KIOSK_USER@|${kiosk_user}|g" \
  -e "s|@KIOSK_GROUP@|${kiosk_group}|g" \
  -e "s|@KIOSK_UID@|${kiosk_uid}|g" \
  -e "s|@KIOSK_HOME@|${escaped_kiosk_home}|g" \
  "${script_directory}/display-control-kiosk.service" \
  > /etc/systemd/system/display-control-kiosk.service
chown root:root /etc/systemd/system/display-control-kiosk.service
chmod 0644 /etc/systemd/system/display-control-kiosk.service

systemctl daemon-reload
activation_link="/opt/display-control/.current-new-$$"
ln -s "${release_directory}" "${activation_link}"
mv -Tf "${activation_link}" /opt/display-control/current
systemctl enable display-control-agent.service display-control-kiosk.service
activation_succeeded=false
if systemctl restart display-control-agent.service; then
  for attempt in {1..45}; do
    health="$(curl --fail --silent --max-time 2 http://127.0.0.1:8787/player/v1/health || true)"
    if [[ "${health}" == *"\"releaseVersion\":\"${release_version}\""* ]]; then
      activation_succeeded=true
      break
    fi
    sleep 2
  done
fi
if [[ "${activation_succeeded}" != true ]]; then
  echo "New agent did not become healthy; rolling back activation." >&2
  if [[ -f "${previous_environment}" ]]; then
    mv -f -- "${previous_environment}" /etc/display-control/agent.env
  fi
  if [[ "${previous_release}" == /opt/display-control/releases/* && -d "${previous_release}" ]]; then
    ln -s "${previous_release}" "${activation_link}"
    mv -Tf "${activation_link}" /opt/display-control/current
    systemctl restart display-control-agent.service
  else
    systemctl stop display-control-agent.service
  fi
  exit 1
fi
systemctl restart display-control-kiosk.service

if [[ -n "${enrollment_source}" ]]; then
  echo "Display Control ${release_version} installed. The enrollment secret will be deleted after successful enrollment."
else
  echo "Display Control ${release_version} updated using the existing protected device identity."
fi
