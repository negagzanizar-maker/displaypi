#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo 'Usage: verify-backup.sh /path/to/display-control-backup-*.tar.gz' >&2
  exit 2
fi

archive="$(realpath "$1")"
if [[ ! -f "$archive" ]]; then
  echo "Backup archive does not exist: $archive" >&2
  exit 1
fi

staging_directory="$(mktemp -d)"
cleanup() {
  rm -rf -- "$staging_directory"
}
trap cleanup EXIT

tar -C "$staging_directory" -xzf "$archive"
(
  cd "$staging_directory"
  sha256sum --check manifest.sha256
  pg_restore --list database.dump >/dev/null
  tar -tzf content.tar.gz >/dev/null
  tar -tzf data-protection.tar.gz >/dev/null
)

echo 'Backup structure and checksums are valid. This is not a substitute for restoring into an isolated database and testing the application.'
