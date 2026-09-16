# ADR-007 — SQL Server 2022 persistence and row-level security

- Status: Accepted
- Date: 2026-09-11
- Supersedes: ADR-003 database-provider and RLS implementation details

## Context

The deployment target requires SQL Server. The platform still needs fail-closed tenant isolation, restricted runtime credentials, reviewed migrations, and real-database integration proof.

## Decision

- Use SQL Server 2022 and `Microsoft.EntityFrameworkCore.SqlServer` for the supported runtime.
- Keep SQL Server migrations in the separate `DisplayControl.SqlServerMigrations` assembly.
- Set trusted tenant identity with `sys.sp_set_session_context` inside each tenant transaction.
- Apply enabled security policies with filter and block predicates to every tenant-owned table.
- Use separate migration-owner, application, notification-delivery, and retention logins. The API login is never `sa`, `sysadmin`, or `db_owner`.
- Run integration boundaries on the pinned SQL Server 2022 container image.

## Consequences

- PostgreSQL migrations and dated PostgreSQL evidence remain historical only; they are not the supported deployment path.
- Current startup, production configuration, tests, and runbooks must select `Database:Provider=SqlServer`.
- SQL Server backup/restore evidence is required before production acceptance.
