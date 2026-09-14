# ADR-005 — Immutable authorized content and an agent-gated local player

- Status: Accepted
- Date: 2026-08-15

## Context

Large remote media must survive interrupted downloads and must never be rendered when corrupt, partial, cross-tenant or unlicensed. Chromium should not hold backend/device credentials.

## Decision

- Allow only UTF-8 plain text, JPEG, PNG, WebP and Pi-tested MP4/H.264/AAC in v1.
- Quarantine, validate, scan and inspect every upload before creating an immutable approved version.
- Publish immutable playlist revisions and per-device manifests containing ordered object hashes/sizes.
- Download to staging, verify all bytes/hashes, then atomically switch active manifests; retain the prior known-good version safely.
- Agent owns device credential, signed lease, trusted time and content gating.
- Agent serves a narrow loopback-only player API; Chromium runs sandboxed, unprivileged and in kiosk mode.
- A valid licence with no assignment shows **No content assigned**. Invalid/absent authorization shows **Not licensed**.

## Consequences

- Content edits create new versions and use additional storage.
- Scanner/parser outages keep uploads quarantined.
- Cache pressure requires explicit retention and low-disk recovery tests.
- Arbitrary HTML/SVG/scripts/external URLs are not supported.
