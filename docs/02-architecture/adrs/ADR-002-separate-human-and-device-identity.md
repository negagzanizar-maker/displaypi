# ADR-002 — Separate human cookie sessions from asymmetric device identity

- Status: Accepted
- Date: 2026-08-15

## Context

Human administrators use browsers; unattended Pis operate on unrelated networks. A shared JWT/API-key model would expose bearer credentials to different attack surfaces and make replay/device revocation harder.

## Decision

- Serve the React administration UI and human API same-origin using server-managed Secure HttpOnly cookie sessions plus global antiforgery validation.
- Do not place authentication tokens in browser storage.
- Enroll each Pi with a short-lived one-use code. The Pi creates its private key and CSR locally.
- Authenticate enrolled device routes using an individual mTLS client certificate bound to exactly one tenant/device.
- Prefer separate admin and device API hostnames so client-certificate policy is explicit.
- Do not accept human cookies on device routes or device certificates on human routes.

## Consequences

- Reverse proxy certificate-header trust must be configured and tested carefully.
- Device certificate issuance, rotation, revocation and re-enrollment become required operational workflows.
- Human and device authorization tests remain separate and easier to reason about.
