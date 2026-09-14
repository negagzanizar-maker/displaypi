# ADR-006 — Deterministic v1 assignment and scheduling rules

- Status: Accepted
- Date: 2026-08-15

## Context

Devices may belong to several groups and receive direct assignments. Unspecified precedence or recurring timezone rules could produce different content on server and device.

## Decision

- Devices may belong to multiple same-tenant groups.
- Direct-device assignment overrides group assignment.
- Within the same target class, higher numeric priority wins.
- Equal-priority simultaneous candidates are rejected rather than broken by database order.
- V1 scheduling supports an optional one-time UTC start and exclusive end. Tenant IANA timezone is input/presentation metadata only.
- Recurring calendar rules and media transcoding are outside the core v1 scope and require later ADRs.

## Consequences

- Resolution always yields zero or one desired playlist at an instant.
- Admin UI can explain conflicts before publication.
- Daylight-saving recurrence complexity is deferred without removing basic scheduled control.
