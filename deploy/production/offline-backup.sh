#!/usr/bin/env bash
set -euo pipefail

umask 077

: "${DISPLAYCONTROL_BACKUP_DATABASE_URL:?Set DISPLAYCONTROL_BACKUP_DATABASE_URL}"
: "${DISPLAYCONTROL_CONTENT_ROOT:?Set DISPLAYCONTROL_CONTENT_ROOT}"
: "${DISPLAYCONTROL_DATA_PROTECTION_ROOT:?Set DISPLAYCONTROL_DATA_PROTECTION_ROOT}"
: "${DISPLAYCONTROL_BACKUP_OUTPUT:?Set DISPLAYCONTROL_BACKUP_OUTPUT}"

if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet display-control-api.service; then
  echo 'Refusing an inconsistent backup while display-control-api.service is running.' >&2
  exit 1
fi

for directory in "$DISPLAYCONTROL_CONTENT_ROOT" "$DISPLAYCONTROL_DATA_PROTECTION_ROOT"; do
  if [[ ! -d "$directory" ]]; then
    echo "Required backup directory does not exist: $directory" >&2
    exit 1
  fi
done

mkdir -p "$DISPLAYCONTROL_BACKUP_OUTPUT"
staging_directory="$(mktemp -d)"
cleanup() {
  rm -rf -- "$staging_directory"
}
trap cleanup EXIT

timestamp="$(date -u +'%Y%m%dT%H%M%SZ')"
archive_name="display-control-backup-${timestamp}.tar.gz"

PGDATABASE="$DISPLAYCONTROL_BACKUP_DATABASE_URL" pg_dump \
  --format=custom \
  --no-owner \
  --no-acl \
  --file="$staging_directory/database.dump"

tar -C "$DISPLAYCONTROL_CONTENT_ROOT" -czf "$staging_directory/content.tar.gz" .
tar -C "$DISPLAYCONTROL_DATA_PROTECTION_ROOT" -czf "$staging_directory/data-protection.tar.gz" .
(
  cd "$staging_directory"
  sha256sum database.dump content.tar.gz data-protection.tar.gz > manifest.sha256
)
tar -C "$staging_directory" -czf "$DISPLAYCONTROL_BACKUP_OUTPUT/$archive_name" \
  database.dump content.tar.gz data-protection.tar.gz manifest.sha256
sha256sum "$DISPLAYCONTROL_BACKUP_OUTPUT/$archive_name" > \
  "$DISPLAYCONTROL_BACKUP_OUTPUT/$archive_name.sha256"

echo "Backup archive: $DISPLAYCONTROL_BACKUP_OUTPUT/$archive_name"
echo 'Back up the CA, signing keys, Data Protection encryption certificate, and database credentials separately through the secret manager.'
