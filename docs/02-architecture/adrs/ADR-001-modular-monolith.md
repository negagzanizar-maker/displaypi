# ADR-001 — Modular monolith with a separate Pi runtime

- Status: Accepted
- Date: 2026-08-15

## Context

The platform spans tenant administration, identity, devices, licensing, content, desired state, synchronization and audit. Splitting these into networked microservices would add deployment, consistency, tracing, secret, test and operational complexity without measured scale evidence.

## Decision

Build one ASP.NET Core modular monolith and one relational database, with explicit in-process module boundaries. ADR-007 selects SQL Server 2022 as the current provider. Deploy a separate C# agent plus locally served React/Chromium player to every Pi. Private object storage, scanner, email and protected key services are external infrastructure adapters, not domain microservices.

## Consequences

- Domain changes can be transactional and tested in one process.
- Modules must not bypass each other's application interfaces or reach casually into their tables.
- Background work uses the transactional outbox and tenant-scoped jobs.
- Independent service decomposition requires measured need and a new ADR.

## Rejected alternatives

- Browser-only hardware discovery: browser security does not expose reliable serial/MAC data.
- Initial microservices: unnecessary distributed-system risk.
- One full backend per customer: stronger physical separation but disproportionate operations for the approved project.
