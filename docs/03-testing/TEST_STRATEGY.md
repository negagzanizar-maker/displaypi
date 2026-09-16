# Verification and Test Strategy

## 1. Purpose

Testing is part of implementation, not a final demonstration step. The strategy must prove functional behavior, tenant isolation, authentication/authorization, device identity, licence enforcement, content integrity, resilience, deployment, and real Raspberry Pi operation at the scope of each requirement.

Passing a mocked unit test cannot prove a real trust boundary such as SQL Server RLS, TLS client authentication, object-storage privacy, backup recovery, or physical kiosk operation.

## 2. Test environments

| ID | Environment | Required evidence |
|---|---|---|
| ENV-DEV | Developer workstation, Docker Compose, deterministic fake clock/hardware adapters | Fast developer tests and reproducible setup |
| ENV-CI | Clean runner, pinned SDKs/images, SQL Server 2022 and real dependency containers | Build, static, unit, component, integration reports |
| ENV-INT | Complete isolated stack with test CA, TLS path, private storage, scanner and database | Real protocol/security boundary tests |
| ENV-STG | Production-equivalent public deployment with real DNS/TLS and synthetic data | E2E, DAST, load, resilience and release acceptance |
| ENV-PI | Physical Pi 4 and Pi 5, 64-bit Raspberry Pi OS, monitors, Ethernet and Wi-Fi | Hardware, playback, reboot, network and soak records |
| ENV-DR | Isolated recovery environment | Backup integrity and restoration evidence |
| ENV-PROD | Production | Non-destructive smoke/health checks only |

Test data is synthetic and includes at least two tenants, every role, multiple devices, cross-tenant identifiers, multiple groups, approved/rejected content, and valid/future/expired/suspended/revoked licences.

## 3. Verification layers

### 3.1 Static and supply-chain checks

- Clean locked builds
- .NET format/analyzers and nullable warnings
- TypeScript type checking, ESLint, and formatting
- Migration/schema/RLS invariant checks
- Secret scanning
- Dependency and container vulnerability scanning
- SBOM generation
- Licence inventory
- Documentation links/requirement-ID validation

### 3.2 Unit and state-machine tests

- Effective licence state and exact boundary instants
- Lease bound calculation and validation
- Trusted-time/clock rollback state transitions through an injected clock
- Assignment precedence and conflict rejection
- Desired-state/manifest validation
- Cache staging/atomic activation state machine
- Permission decisions and safe domain errors
- Retry/backoff and idempotency logic
- Hardware collectors behind interfaces with representative OS fixtures

### 3.3 Frontend component tests

- React administration states and role-appropriate actions
- Login, MFA, invitations, validation and error recovery
- Device/licence/content/playlist/assignment views
- Kiosk licensed, offline, synchronizing, no-content, not-licensed and playback-fault states
- Keyboard operation, focus, semantics, contrast and automated accessibility checks

### 3.4 Backend and SQL Server integration tests

Use the real SQL Server 2022 major version, ASP.NET test hosting, and real middleware configuration:

- migrations from clean and previous schema;
- RLS default deny and `FORCE ROW LEVEL SECURITY`;
- connection-pool context reuse across Tenant A, Tenant B, and missing context;
- tenant-safe foreign keys, counts, searches, jobs, raw queries and downloads;
- cookie, CSRF, Identity, MFA, session revocation and rate limiting;
- endpoint policies and direct API access matrix;
- audit/outbox transaction behavior; and
- private storage authorization.

Any cross-tenant disclosure or mutation is a release-blocking defect.

### 3.5 Device protocol and cryptographic contract tests

- Enrollment token expiry, single use, replay, rate limit and wrong tenant/device
- CSR issuance with locally generated key
- Actual TLS client-certificate handshake through the trusted proxy path
- Rejection of spoofed forwarding headers, unknown/expired/revoked/wrong-device certificates
- Rotation proof-of-possession and bounded certificate overlap
- Heartbeat sequence, stale/replay/out-of-order and idempotency behavior
- Signed lease and manifest golden vectors, tampering, binding, algorithm and `kid` handling
- Protocol compatibility across backend/agent/player versions

### 3.6 Browser end-to-end tests

Playwright covers the critical journeys of each role:

- invitation, email verification fixture, login, mandatory MFA and session management;
- tenant user/role lifecycle;
- enrollment creation and dashboard device status;
- licence issue, renewal, suspension, revocation and safe transfer;
- malicious/valid upload states;
- playlist edit/publication and assignment conflict behavior;
- audit visibility; and
- full kiosk state changes driven by the real test backend.

Hidden buttons are not evidence; E2E/API tests invoke forbidden routes directly.

The fast browser suite in `tests/e2e` uses deterministic Playwright route interception for UI state, request-shape, CSRF and fail-closed player checks. It also runs axe against the covered administration and player views. These tests are a browser boundary gate, not evidence for the complete live API/SQL Server journey listed above; that journey runs separately in staging with real services.

### 3.7 Security, resilience, load, and recovery tests

- OWASP ASVS 5.0 Level 2 requirement mapping
- IDOR, injection, XSS, CSRF, open redirect, CORS and secure-header tests
- Authentication/enrollment/upload/download abuse and rate-limit behavior
- Malformed/polyglot/oversized media and scanner/parser failures
- DNS/TLS/network latency/loss/reset and retry-storm prevention
- API, database, object-storage, scanner and worker restarts
- Disk pressure, corrupted cache, partial download, concurrent publication
- Secret/key/certificate rotation
- Expected fleet heartbeat and administrative load plus agreed safety margin
- Encrypted backup restoration and integrity in ENV-DR

### 3.8 Physical Raspberry Pi acceptance

Run on both Pi 4 and Pi 5 unless hardware availability is explicitly recorded as an unresolved final blocker:

- clean, repeatable provisioning and update/rollback;
- hostname, serial, active-interface MAC/IP compared with OS evidence;
- enrollment, real mTLS heartbeat, certificate rotation and revocation;
- Ethernet/Wi-Fi and genuinely different NAT/network operation;
- actual 1080p H.264/AAC video, JPEG/PNG/WebP and UTF-8 text playback;
- playlist order, durations, continuous loop and monitor reconnect;
- valid-to-expired transition during playback and exact **Not licensed** result;
- offline operation and fail-closed cold reboot/time uncertainty;
- power interruption during synchronization, browser/agent termination, disk-full and cache corruption;
- restricted account/files, loopback listener, absence of unexpected inbound port; and
- 24–72 hour soak with CPU, memory, disk, temperature and error evidence.

Short non-production leases test physical expiry quickly. Deterministic clock tests prove the full 24-hour policy without weakening production validation.

## 4. Tooling policy

Approved baseline test frameworks:

- xUnit and ASP.NET Core test hosting
- Testcontainers for SQL Server 2022 and other real dependencies
- Vitest and React Testing Library
- Playwright
- axe-core for accessibility automation

Candidates to announce and record before installation when their milestone begins:

- Gitleaks for secret scanning
- Trivy for dependency/container/SBOM analysis
- OWASP ZAP for dynamic security testing
- k6 for load and heartbeat testing
- ClamAV for upload scanning
- `ffprobe`/FFmpeg tooling for media compatibility validation

A tool's green result is evidence only for the controls it actually exercises.

## 5. Critical acceptance scenarios

### Tenant isolation

Every tenant-owned list/read/create/update/delete/export/download operation is attempted with Tenant A credentials and Tenant B identifiers. Missing tenant context default-denies. Cross-tenant inserts/updates are rejected, not merely hidden later. Runtime database role ownership/`BYPASSRLS`, RLS forcing, pooled connections, background jobs, aggregates, storage URLs, backup/restore, and device endpoints are all checked.

### Human identity

Test invitation expiry/replay/binding, verification, password/reset expiry/replay, mandatory administrator MFA, one-use recovery codes, generic errors, lockout/rate limits, session fixation/idle/absolute expiry/revocation, role changes, last-admin invariant, CSRF on every unsafe route, and backend permission policies for every role/action.

### Device identity

Test one-use enrollment, local private key, actual mTLS, wrong tenant/device, certificate expiry/revocation/rotation, duplicate/changing serial anomaly, heartbeat replay/order, device denial from human APIs, trusted proxy headers, restricted service/file permissions, loopback-only player, and outbound-only network operation.

### Licence and trusted time

Use interval semantics `[validFromUtc, expiresAtUtc)`. Test valid/future/expired/suspended/revoked/transferred states; exact boundary; signed lease algorithm/key/binding; tampered/stale/replayed/other-device values; maximum 24 hours and actual-expiry bound; signing key rotation; online propagation; offline expiry during video; restart/corruption; timezone/DST independence; backward/forward clock changes; and safe pending transfer while a source lease remains outstanding.

### Content and playback

Test allowlisted and forbidden types, extension/MIME/signature/codec mismatch, traversal/double extension/polyglot/size/malware, scanner outage, text injection, storage privacy, device assignment authorization, digest mismatch, partial/resumed downloads, atomic switch, cache pressure, unsupported decode, empty assignment, concurrent publication, crashes, power loss, and licence priority over cached playback.

## 6. Quality gates

### G0 — Specification freeze

- Stable requirement IDs and measurable acceptance criteria
- Approved roles, state models, trust boundaries and threat model
- Media/time/capacity/recovery decisions frozen or visibly open with a blocking milestone
- Initial traceability rows for every normative requirement

### G1 — Change/pull request

- Clean build, format, lint and typecheck
- Relevant unit/component/integration tests pass
- No critical test skipped or hidden by retry
- No committed secret
- No unaccepted critical/high dependency finding
- Documentation and traceability updated with the change

### G2 — Integrated system

- Clean and upgrade migration paths pass
- Database role/RLS invariants pass
- Real cookie/CSRF/mTLS/storage boundary tests pass
- Test reports retained and attributable to a commit

### G3 — Release candidate

- E2E, ASVS/security, resilience, deployment, load and backup/restore pass
- Zero open P0/P1 and zero unaccepted critical/high security finding
- Frozen performance/recovery targets met
- Quarantined/flaky/skipped tests do not count as proof

### G4 — Hardware release

- Both Pi targets pass provisioning, playback, network, licence, certificate, failure and soak acceptance
- Hardware evidence belongs to the same release candidate

### G5 — Final delivery

- Every approved mandatory requirement is `Verified`
- Clean-machine and clean-Pi instructions succeed
- Code, OpenAPI, ERD, diagrams, manuals, test records, DOCX and PDF describe the same verified release
- Word fields/cross-references refreshed and final PDF visually inspected

## 7. Evidence record

Every result record contains:

- test ID and requirement IDs;
- exact command or manual procedure;
- expected and actual result;
- commit/release;
- environment and relevant dependency/image/OS versions;
- UTC timestamp;
- pass/fail/skipped counts;
- evidence path and SHA-256;
- related defect; and
- tester/reviewer for manual/hardware work.

Results are immutable per release. A rerun creates a new dated evidence set. Planned tests are never written as executed tests.

## 8. Defect policy

- **P0:** active compromise, cross-tenant write/read, irrecoverable data loss — immediate stop/release block.
- **P1:** authentication/licence bypass, secret exposure, broken restore, widespread unusability — release block.
- **P2:** major functional failure with bounded workaround — must be resolved or explicitly accepted before release.
- **P3/P4:** limited defect/cosmetic/documentation issue — tracked and prioritized honestly.

Flaky tests are defects. Disabling or retrying a test does not turn it into completion evidence.
