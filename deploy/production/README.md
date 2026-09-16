# Single-node production deployment baseline

The supported baseline is one ASP.NET Core node, SQL Server 2022, private filesystem media storage, ClamAV, and separately deployed Pi agents/players. The API rejects `Deployment__InstanceCount` values other than `1`; multiple API writers require shared object storage, distributed rate limiting, and coordinated workers.

## Required external services

- SQL Server 2022 reachable with `Encrypt=True` and `TrustServerCertificate=False` using a trusted server certificate.
- ClamAV reachable only from the API network.
- Encrypted persistent volumes for content and Data Protection keys.
- A secret manager for database credentials, token pepper, TLS certificates, the device CA, Data Protection encryption certificate, and licence-signing key.
- Public DNS/TLS, centralized logs and alerts, tested off-host backups, and SMTP when notifications are enabled.

The reverse proxy must preserve client-certificate handling for `/device/v1` and allow unbuffered, long-lived `text/event-stream` responses on `/device/v1/state-changes`. Heartbeats remain the fallback when the SSE stream reconnects.

## Database provisioning

1. Create an empty SQL Server database with a migration-owner identity.
2. Set `DISPLAYCONTROL_MIGRATION_CONNECTION` only for the migration process.
3. Run `dotnet ef database update --project src/DisplayControl.SqlServerMigrations --startup-project src/DisplayControl.Api`.
4. Create the application login through the deployment secret manager and apply `deploy/sqlserver/runtime-user.template.sql` with `sqlcmd`.
5. When enabled, provision the separate notification and maintenance logins with their templates.
6. Connect as the exact API runtime login and run `deploy/sqlserver/verify-runtime-security.sql`. Do not start the API if it fails.

The running API identity must never be `sa`, `sysadmin`, `db_owner`, a table owner, or able to read protected notification payloads. Production startup rejects wildcard hosts and unverified SQL Server transport; readiness independently rejects privileged database identities.

## Host installation

1. Publish the tested release and install it read-only under `/opt/display-control/api` using the non-login `display-control-api` service account.
2. Create `/var/lib/display-control/content` and `/var/lib/display-control/data-protection` with owner-only permissions on encrypted storage.
3. Install secret-manager material read-only under `/etc/display-control/secrets`.
4. Copy `api.env.example` to `/etc/display-control/api.env`, replace every placeholder, and set mode `0600`.
5. Install `display-control-api.service`, run `systemd-analyze verify`, reload systemd, and enable the service.

Human MFA remains mandatory in production. Password-only sign-in is rejected outside Development and Testing.

## Backup and release gate

The checked-in legacy PostgreSQL backup shell scripts are not valid for SQL Server and must not be used. Production acceptance requires a documented SQL Server full/differential/log backup policy and an isolated restore drill that restores the database, media, Data Protection keys, CA/signing material, and then proves tenant isolation and playback.

Before public use, retain evidence for SMTP, live browser/API/SQL Server E2E, expected-fleet load, network/storage/scanner faults, backup restoration, vulnerability scanning, and independent security review. Physical Raspberry Pi acceptance remains a separate hardware gate.
