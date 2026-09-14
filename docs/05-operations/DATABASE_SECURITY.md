# SQL Server Security Baseline

The supported runtime database is SQL Server 2022. EF Core migrations live in `DisplayControl.SqlServerMigrations`; the migration owner is never used by the running API.

## Runtime identities

- The migration identity creates the database schema and security policies, then is removed from normal runtime configuration.
- The API uses a restricted login that is neither `sysadmin` nor `db_owner`. Production grants are defined by `deploy/sqlserver/runtime-user.template.sql`.
- The optional notification worker uses a separate login with only `SELECT` and `UPDATE` on `app.identity_notifications`.
- The optional retention worker uses the exact login `display_control_maintenance` with only `SELECT` and `DELETE` on heartbeat and audit rows.

The API readiness probe rejects a runtime identity that is `sysadmin` or `db_owner`. `deploy/sqlserver/verify-runtime-security.sql` additionally checks the sensitive-table permissions and confirms that every tenant-owned table has an enabled security policy.

## Tenant context and row-level security

Each trusted tenant transaction calls `sys.sp_set_session_context` with `tenant_id`. SQL Server filter predicates hide rows belonging to other tenants; block predicates reject cross-tenant inserts and updates. With no tenant context, tenant tables are fail-closed. EF query filters provide a second application-layer boundary.

Platform catalog transactions use the separate `platform_catalog` context. Notification delivery and retention use `notification_delivery` and `data_retention`; only their narrowly granted database users can perform the corresponding table operations. Connection-pool reuse and missing/cross-tenant contexts are covered by SQL Server Testcontainers integration tests.

## Migration rule

Generate and review SQL Server migrations before execution. Apply them through `dotnet ef database update --project src/DisplayControl.SqlServerMigrations --startup-project src/DisplayControl.Api` with a temporary migration-owner connection in `DISPLAYCONTROL_MIGRATION_CONNECTION`. Production application startup never performs owner-level migration automatically.
