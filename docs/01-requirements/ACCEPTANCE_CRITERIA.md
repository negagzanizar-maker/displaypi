# Acceptance Criteria

## 1. Purpose

These scenarios define the observable Goal 1 acceptance baseline. They complement, but do not replace, the normative requirements in `REQUIREMENTS.md` and `SECURITY_REQUIREMENTS.md`. Each scenario must later be implemented as one or more automated or controlled manual tests and linked to immutable evidence in `TRACEABILITY.csv`.

The terms **Given**, **When**, and **Then** describe preconditions, action, and required result. Security-sensitive results must be proved at the API, database, storage, or physical-device boundary; a hidden UI control is not sufficient evidence.

## 2. Tenancy and authorization

### AC-TEN-001 — Cross-tenant access is denied

- **Given** two active tenants containing users, devices, licences, content, playlists, assignments, audit records, and stored objects,
- **when** any Tenant A role attempts every supported list/read/create/update/delete/export/download operation using Tenant B identifiers or manipulated tenant context,
- **then** the operation reveals and changes no Tenant B data, returns a safe denial, records an appropriate security event, and the restricted SQL Server runtime login plus RLS security policies independently prevent cross-tenant row access.

### AC-TEN-002 — Missing tenant context defaults to denial

- **Given** the normal restricted runtime database role,
- **when** a tenant-owned query or mutation executes without valid transaction-local tenant context,
- **then** Row-Level Security returns no protected rows and rejects protected mutations.

### AC-TEN-003 — Platform administration has no implicit content bypass

- **Given** a Platform Administrator and an unrelated active tenant,
- **when** the platform identity attempts to fetch that tenant's media, playlist contents, signed downloads, or device content,
- **then** access is denied unless a separately designed, explicitly approved, time-bounded support-access mechanism exists; no such mechanism is part of v1.

### AC-TEN-004 — Tenant suspension propagates safely

- **Given** an active tenant with signed-in users and online devices,
- **when** a Platform Administrator suspends it,
- **then** new human and device authorization is denied, sessions and new leases cease within the documented propagation target, cached playback cannot exceed an already issued valid lease, and audit/recovery records remain intact.

## 3. Human identity and access

### AC-IAM-001 — Invitation-only onboarding

- **Given** no existing customer account,
- **when** a visitor tries to register publicly,
- **then** no account is created; only a valid, unexpired, single-use invitation bound to the expected tenant, email, and role can complete onboarding.

### AC-IAM-002 — Privileged MFA is mandatory

- **Given** a Platform Administrator, Tenant Administrator, or Content Manager with correct primary credentials,
- **when** MFA enrollment or verification is incomplete,
- **then** privileged permissions remain unavailable; valid TOTP completes the flow and invalid, replayed, or rate-limited attempts do not.

### AC-IAM-003 — Browser session protections are effective

- **Given** an authenticated administration session,
- **when** cookie attributes, client storage, cross-site unsafe requests, session rotation, idle/absolute expiry, logout, and server-side revocation are tested,
- **then** authentication exists only in a `Secure`, `HttpOnly`, `__Host-` cookie, no bearer/refresh token is JavaScript-readable, antiforgery validation blocks forged mutations, and expired or revoked sessions cannot be reused.

### AC-IAM-004 — Role boundaries are enforced server-side

- **Given** Platform Administrator, Tenant Administrator, Content Manager, and Viewer accounts,
- **when** each account directly invokes every protected API operation regardless of UI visibility,
- **then** only actions permitted by the approved permission matrix succeed and every privilege change invalidates stale authorization within the documented window.

### AC-IAM-005 — Account recovery does not create a bypass

- **Given** a privileged user who lost a password or MFA factor,
- **when** reset, recovery-code, MFA-reset, replay, enumeration, and last-administrator cases are exercised,
- **then** tokens/codes are expiring and single-use, responses do not materially enumerate accounts, email alone cannot reset privileged MFA, applicable sessions are revoked, and every sensitive event is audited without secret material.

## 4. Raspberry Pi identity and connectivity

### AC-DEV-001 — One-time enrollment creates a unique device identity

- **Given** a pending device and a 15-minute enrollment secret of at least 128 bits,
- **when** a Pi generates an ECDSA P-256 key locally and submits a valid CSR over TLS,
- **then** the secret is consumed atomically once, the private key never leaves the Pi, and the issued client-auth certificate maps to exactly one tenant and device.

### AC-DEV-002 — Enrollment attacks fail closed

- **Given** expired, replayed, guessed, wrong-device, wrong-tenant, or concurrently used enrollment material,
- **when** enrollment is attempted,
- **then** no usable certificate is issued, attempts are rate-limited and safely audited, and an already consumed secret cannot succeed again.

### AC-DEV-003 — Device routes require real mTLS identity

- **Given** unknown, expired, revoked, wrongly purposed, self-signed, wrong-device, or spoofed-forwarding certificates,
- **when** the device API is called through the production-equivalent TLS path,
- **then** TLS/application validation rejects the request; human cookies cannot authenticate device routes and device certificates cannot authenticate human routes.

### AC-DEV-004 — Rotation and revocation are bounded

- **Given** an enrolled online Pi,
- **when** it rotates its certificate or an administrator revokes its credential,
- **then** rotation proves possession of the old and new keys, any overlap remains within 24 hours, revoked credentials receive no new application authorization, and recovery requires the audited flow rather than weakened validation.

### AC-DEV-005 — Inventory and heartbeat are trustworthy enough for operations

- **Given** a Pi 4 or Pi 5 on Ethernet or Wi-Fi behind an unrelated NAT,
- **when** it boots and sends sequenced outbound HTTPS heartbeats,
- **then** the dashboard shows sanitized hostname, Pi serial, active local IP/MAC data, server-observed public source, OS/architecture, versions, disk, desired/applied state and health; stale/replayed heartbeats cannot roll state backward.

### AC-DEV-006 — No inbound device exposure is required

- **Given** a provisioned Pi on a customer network,
- **when** listening ports and firewall behavior are inspected,
- **then** the platform operates without router port forwarding, the player service is loopback-only, remote debugging is disabled, and no application secret is exposed to Chromium.

## 5. Licence and trusted time

### AC-LIC-001 — Backend licence evaluation is authoritative

- **Given** future, active, expired, suspended, revoked, transferred, wrong-tenant, wrong-device, or disabled-device licence inputs,
- **when** authorization is requested,
- **then** the backend alone derives the effective state using `[validFromUtc, expiresAtUtc)` and signs a lease only for the eligible active case.

### AC-LIC-002 — Offline authorization is strictly bounded

- **Given** an eligible device whose licence expires at `L` and server time `S`,
- **when** a lease is issued,
- **then** its expiry is no later than `min(L, S + 24 hours, any earlier authorization boundary)`, contains the fixed signed bindings, and contains no hidden grace period.

### AC-LIC-003 — Lease tampering and replay fail closed

- **Given** a valid lease and variants with altered signature, algorithm, key ID, schema, issuer, audience, device, tenant, licence, time, manifest, duplicate/conflicting claims, or replay on another device,
- **when** the agent validates them,
- **then** only the untouched correctly bound lease can authorize playback and every invalid variant results in **Not licensed**.

### AC-LIC-004 — Offline expiry stops protected playback

- **Given** valid cached content and a valid lease while the network is unavailable,
- **when** trusted effective time reaches the exclusive lease expiry,
- **then** protected playback is paused/unloaded and the screen changes to exactly **Not licensed** within five seconds.

### AC-LIC-005 — Clock rollback cannot extend playback

- **Given** authenticated server-time history and an active or expired lease,
- **when** wall time moves backward, forward, stored time state is corrupted/removed/replayed, or the Pi reboots with insufficient trusted state,
- **then** effective trusted time never moves backward; uncertainty fails closed and cannot extend entitlement.

### AC-LIC-006 — Online revocation propagates within target

- **Given** an online device currently playing under a valid lease,
- **when** its licence, device, or tenant is suspended or revoked,
- **then** playback stops within the tested heartbeat/propagation target, provisionally 60 seconds, and the event is observable and audited.

### AC-LIC-007 — Licence transfer prevents unintended simultaneous use

- **Given** a licensed source Pi and a same-tenant replacement Pi,
- **when** an authorized administrator performs a step-up-protected transfer with a reason,
- **then** no new source lease is issued, destination activation waits for source surrender or latest possible source-lease expiry, history is immutable, and the destination then becomes the sole eligible device atomically.

## 6. Content, assignment, synchronization, and playback

### AC-CNT-001 — Upload pipeline rejects unsafe content

- **Given** valid supported files and malicious, oversized, mismatched, polyglot, malformed, unsupported, or traversal-oriented inputs,
- **when** an authorized writer uploads them,
- **then** every upload is streamed to private quarantine under a random key, limits/signatures/metadata/codecs are checked, scanning is mandatory and bounded, failures remain quarantined, and only valid UTF-8 text, JPEG, PNG, WebP, or hardware-validated MP4 H.264/AAC can be approved.

### AC-CNT-002 — Published assets and manifests are immutable

- **Given** approved assets and a playlist,
- **when** it is published and later edited,
- **then** the first asset/manifest bytes, sizes, SHA-256 values and playback parameters remain immutable and the edit creates a new version with audit history.

### AC-CNT-003 — Private content authorization is enforced

- **Given** a human or device requesting a stored object,
- **when** the requester belongs to another tenant, lacks the relevant permission, or the object is not assigned to that authenticated device,
- **then** storage remains private and no content bytes or reusable broad storage credential are disclosed.

### AC-ASN-001 — Assignment resolution is deterministic

- **Given** active group and direct assignments,
- **when** the server compiles desired state,
- **then** a direct-device assignment outranks a group assignment, higher priority wins within the same class, equal-priority collisions are rejected, and one unambiguous immutable desired state results.

### AC-ASN-002 — One-time schedule boundaries are exact

- **Given** a tenant IANA timezone and optional start/end entered through the administration UI,
- **when** desired state is evaluated before, at, and after the corresponding UTC boundaries including DST transitions,
- **then** the same UTC result is obtained on server and device; recurring calendar rules are not silently inferred in v1.

### AC-SYN-001 — Content activation is integrity protected and atomic

- **Given** a new authorized manifest,
- **when** downloads succeed, fail, resume, exceed quota, are interrupted, or contain the wrong length/hash,
- **then** only a complete hash-verified manifest and all its assets can become active atomically, while the previous valid authorized cache remains intact until safe replacement.

### AC-PLY-001 — Licensed content plays correctly

- **Given** a valid lease, complete assigned cache, and active schedule,
- **when** the kiosk runs on supported Pi 4/5 hardware,
- **then** approved images, text and 1080p H.264/AAC video render full-screen in playlist order with configured durations/looping and without browser chrome or navigation.

### AC-PLY-002 — Empty and unlicensed states are distinct

- **Given** first a valid licence with no active assignment and then absent/invalid authorization,
- **when** each state is applied,
- **then** the first screen shows exactly **No content assigned** and the second shows exactly **Not licensed**, without cached protected content leakage.

### AC-PLY-003 — Kiosk recovers without bypassing authorization

- **Given** reboot, browser/agent crash, monitor reconnect, power loss during sync, corrupt cache, low disk, or network outage,
- **when** services recover,
- **then** bounded restart policies restore only a complete still-authorized state, defaulting to **Not licensed** whenever authorization or trusted time is uncertain.

## 7. Operations, evidence, and delivery

### AC-OPS-001 — Deployment is reproducible and least-privileged

- **Given** a clean supported server and a clean supported Pi,
- **when** the documented deployment/provisioning procedures are followed,
- **then** pinned artifacts start with validated configuration, least-privileged identities, only intended network exposure, health checks, migrations and rollback/recovery paths.

### AC-OPS-002 — Backup restoration proves recoverability

- **Given** an encrypted production-equivalent backup set,
- **when** it is restored into the isolated recovery environment,
- **then** database, private media, manifests, audit/configuration and required keys are coherent, tenant isolation remains enforced, and measured RPO/RTO meet the approved targets.

### AC-OPS-003 — Observability is useful without leaking secrets

- **Given** authentication abuse, device outage, licence failure, scan failure, clock anomaly, key/security change, capacity pressure, or worker failure,
- **when** the event occurs,
- **then** structured logs/metrics/alerts identify it with safe correlation and audit data while excluding credentials, cookies, tokens, keys, MFA material, signed URLs, and media bodies.

### AC-VER-001 — Release evidence covers the actual boundaries

- **Given** a release candidate,
- **when** quality gates are evaluated,
- **then** locked builds, unit/component tests, real SQL Server 2022 RLS tests, real TLS/mTLS tests, browser E2E, ASVS mapping, resilience/load/restore tests and Pi 4/5 hardware tests are attributable to the same release; skipped/flaky/planned tests do not count as passes.

### AC-DOC-001 — Documentation and report contain only verified claims

- **Given** the final release and evidence bundle,
- **when** OpenAPI, ERD, ADRs, manuals, traceability, DOCX and PDF are audited,
- **then** they describe the same release, every mandatory requirement has evidence, automatic numbering and references are refreshed, sensitive values are redacted, and planned or unavailable results are labeled honestly.

## 8. Goal 1 approval rule

Goal 1 is accepted when:

1. all normative IDs are unique and present in the traceability ledger;
2. architecture, API, data, threat, test, and acceptance documents contain no known contradictory rule;
3. every unresolved decision has an explicit later blocking gate;
4. no planned test or feature is described as implemented; and
5. the user accepts this baseline or requests recorded amendments.
