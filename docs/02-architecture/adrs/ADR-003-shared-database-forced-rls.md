# ADR-003 — Shared PostgreSQL with forced Row-Level Security

- Status: Superseded by ADR-007
- Date: 2026-08-15

## Context

Multiple customer organizations require strict data isolation while the platform remains minimal and operable. Application query filters alone can be bypassed by omissions, raw SQL or background jobs.

## Decision

- Use one PostgreSQL 18 database for the initial platform.
- Put an immutable non-null `tenant_id` on every tenant-owned row and enforce same-tenant relationships.
- Enforce authorization in ASP.NET policies and enforce data isolation again through PostgreSQL `ENABLE` plus `FORCE ROW LEVEL SECURITY` default-deny policies.
- Set tenant context transaction-locally from a verified human membership or certificate mapping.
- Separate schema owner/migration, restricted runtime, backup and monitoring roles. Runtime is not owner/superuser/`BYPASSRLS`.
- In v1, a customer account belongs to no more than one tenant. Platform identities are a separate scope.

## Consequences

- Migrations and CI must inspect policies, owners, grants and tenant constraints automatically.
- Pooled-connection reuse, background jobs, backups, constraint error leakage and every resource receive hostile cross-tenant tests.
- A future database-per-tenant model or multi-tenant customer account requires an ADR and migration plan.
