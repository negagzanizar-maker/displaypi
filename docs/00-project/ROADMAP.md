# Gated Project Roadmap

Dates are intentionally omitted until the internship dates and delivery deadline are supplied. Dependencies and proof gates, not invented calendar claims, determine completion.

## Cross-goal rule

Every goal updates requirements, ADRs, threat model where relevant, tests, traceability, technical documentation, changelog, and French-report working notes. A goal is not complete with undocumented code or unexecuted planned tests.

| Goal | Outcome | Required exit evidence | Status |
|---|---|---|---|
| 1. Specification and security design | Approved scope, roles, states, requirements, architecture, data/API outlines, threat model, tests, report plan | Goal 1 checklist and document consistency audit | Complete — 2026-08-15 |
| 2. Engineering foundation | Git/monorepo, pinned tools, React apps, API, agent, test projects, SQL Server dev stack, CI and doc skeleton | Locked clean builds/tests in dev and CI | Implemented locally; remote CI evidence open |
| 3. Secure backend/data core | Domain core, migrations, SQL Server RLS, media abstraction, validation/errors, audit/outbox, logging/health | SQL Server integration and isolation gate | Complete locally; SQL Server migration revalidated 2026-09-11; remote CI evidence open |
| 4. Human identity and access | Invitations, verification, passwords/reset, MFA, sessions, CSRF, rate limits, RBAC | Identity/authorization matrix and E2E evidence | Core, recent-MFA step-up and deterministic browser identity checks complete locally; external SMTP and live-backend E2E open |
| 5. Administration modules | Tenants/members, devices/groups, licences/transfers, content, playlists, assignments, audit UI/API | Module integration and role acceptance evidence | Tenant and platform workflows, groups, bounded schedules, precedence and transfer UI complete locally |
| 6. Trusted Pi identity | Enrollment, local keys, mTLS, rotation/revocation, inventory, heartbeats, versions | Real TLS protocol tests and Pi enrollment evidence | Local real-Kestrel mTLS and enrollment/rotation replay safety pass; physical Pi and production edge evidence open |
| 7. Licence/content synchronization | Signed lease, trusted time, offline bound, manifest/download/cache state machines | Deterministic protocol, corruption, time and outage evidence | Signed protocol, verified cache and interrupted-transfer resume pass locally; outage/reboot/disk-pressure matrix open |
| 8. Pi kiosk | Loopback player, Chromium/systemd, media loop, safe states, recovery | Pi 4/5 playback and reboot evidence | In progress — software and hardened service definitions implemented; physical Pi acceptance open |
| 9. Administration dashboard | Complete responsive/accessible tenant and platform workflows | Component, accessibility and Playwright evidence | In progress — tenant/platform UI connected and five Playwright/axe checks pass; full live-backend journeys and manual accessibility open |
| 10. Production operations | TLS, secrets, storage, scanning, containers, migration, backup, monitoring, provisioning/update/recovery | Staging deployment and DR restore evidence | In progress — local scanning, secrets helper and Pi provisioning baseline exist; production topology, monitoring, signed update and DR open |
| 11. Full verification | ASVS, E2E, cross-tenant, malicious input, resilience/load and physical soak campaign | Release-candidate and hardware gate bundle | Pending |
| 12. Final delivery | Reconciled docs/manuals, French DOCX/PDF, evidence audit | 100% mandatory requirements verified; clean install review | Pending |

## Goal 1 exit checklist

- [x] Master objective and twelve gated goals recorded.
- [x] Workspace baseline inspected.
- [x] Reference French report inspected and adaptation rules recorded.
- [x] Project charter and explicit scope/exclusions drafted.
- [x] Stable normative requirement IDs drafted.
- [x] Actors, permissions, use cases, state models and invariants drafted.
- [x] Architecture, deployment/trust boundaries and critical flows drafted.
- [x] Logical data model and SQL Server RLS baseline implemented and tested.
- [x] Human/device API and local player protocol outline drafted.
- [x] Threat model reconciled and risk register complete.
- [x] Individual requirement traceability baseline complete.
- [x] Verification environments, layers, critical scenarios and gates drafted.
- [x] French report blueprint and evidence rules drafted.
- [x] ADRs for approved foundational decisions recorded.
- [x] Cross-document consistency and link/ID audit passed on 2026-08-15.
- [x] User accepted implementation start on 2026-08-15; deployment/report metadata decisions remain visibly deferred to their recorded blocking gates.

## Provisional sequencing inside implementation

Backend/data/identity foundations precede full UI polish. Device protocol domain logic is designed with injectable clocks and hardware adapters before real-Pi integration. Administration and Pi experiences then integrate against stable contracts. Production hardening and real hardware tests are not postponed until after documentation claims completion; evidence is captured as each boundary becomes executable.

## Current implementation note — 2026-08-15

The repository now contains a locally verified secure vertical slice. It includes invitation-only human identity with TOTP MFA and revocable sessions; SQL Server 2022 RLS filter/block policies; tenant member, device, licence, content, playlist, assignment and audit APIs; one-time device enrollment; ECDSA client certificates and rotation; mTLS-bound heartbeats; ES256 offline leases capped at 24 hours and at the real licence/certificate expiry; streamed content inspection and fail-closed ClamAV scanning; immutable private content; canonical desired-state manifests; verified content-addressed Pi caching; a loopback-only player; connected React administration workflows; and hardened agent/kiosk systemd units.

The implementation does not yet claim production readiness. Recent-authentication enforcement, platform administration, group and bounded-schedule precedence, replay-safe device enrollment/rotation, interrupted-download resume, a real Kestrel mTLS listener test and an initial Playwright/axe browser gate now pass locally. Remaining gates include remote CI, real SMTP invitation/recovery delivery, live-backend browser journeys, manual accessibility, general human-command idempotency, Pi 4/5 media/reboot/clock/outage/disk-pressure tests, multi-node private object storage if that topology is selected, production edge TLS/secrets/monitoring/backup and restore, signed agent updates, load/ASVS testing, and the final French DOCX/PDF report.

The latest evidence bundle is recorded in [secure vertical slice local evidence](../03-testing/evidence/2026-08-15-secure-vertical-slice-local.md).
