# System Requirements Specification

## 1. Purpose and interpretation

This document is the authoritative Goal 1 product baseline. Requirement keywords use the following meanings:

- **MUST**: required for final project completion.
- **SHOULD**: expected unless an Architecture Decision Record approves a justified alternative.
- **MAY**: optional and not completion-critical.

Every `MUST` requirement requires authoritative implementation and verification evidence in `TRACEABILITY.md`. Provisional numeric defaults are explicitly identified and must be load- or hardware-tested before final acceptance.

## 2. Tenancy and customer administration

| ID | Requirement |
|---|---|
| TEN-001 | The system MUST isolate each tenant's users, memberships, devices, device groups, licences, content, playlists, assignments, audit events, and stored objects from every other tenant. |
| TEN-002 | Tenant context MUST be derived from an authenticated active membership and MUST NOT be trusted from an arbitrary client-supplied identifier. |
| TEN-003 | A Platform Administrator MUST be able to create, suspend, reactivate, and view operational metadata for tenants. |
| TEN-004 | A Platform Administrator MUST NOT receive routine access to tenant content or device media merely because of the platform role. |
| TEN-005 | A Tenant Administrator MUST be able to invite, view, change the role of, and deactivate members within that tenant only. |
| TEN-006 | In v1, a customer user MUST belong to at most one customer tenant; platform identities are separate. Supporting multi-tenant customer membership later requires an ADR and renewed isolation/session testing. |
| TEN-007 | Suspending a tenant MUST prevent its human users and devices from obtaining new authorized access while preserving required audit and recovery data. |
| TEN-008 | All tenant-owned database relations and private-storage object keys MUST carry an unambiguous tenant boundary. |

## 3. Human identity, authentication, and sessions

| ID | Requirement |
|---|---|
| IAM-001 | User onboarding MUST be invitation-based; public self-registration MUST be disabled. |
| IAM-002 | Invitation tokens MUST be random, single-use, stored non-recoverably, expire, and be bound to the intended tenant, email, and role. |
| IAM-003 | Email ownership MUST be verified before protected tenant access is granted. |
| IAM-004 | Password creation and reset MUST use ASP.NET Core Identity's supported password hashing and token mechanisms with current secure configuration. |
| IAM-005 | Platform Administrators, Tenant Administrators, and Content Managers MUST enroll and use TOTP MFA; recovery codes MUST be one-time, protected, and regenerable with reauthentication. |
| IAM-006 | Authentication state in the administration browser MUST use `Secure`, `HttpOnly`, appropriately scoped `SameSite` cookies; bearer or refresh tokens MUST NOT be stored in `localStorage` or `sessionStorage`. |
| IAM-007 | Every state-changing browser request MUST enforce anti-forgery protection in addition to SameSite cookie policy. |
| IAM-008 | Successful authentication and privilege changes MUST rotate or invalidate prior session material as appropriate. |
| IAM-009 | Users MUST be able to sign out the current session; administrators and users MUST be able to revoke all applicable sessions after credential compromise. |
| IAM-010 | Sessions MUST implement idle and absolute expiry, server-side revocation, and security-stamp validation; final durations MUST be documented. |
| IAM-011 | Login, invitation, MFA, recovery, and password-reset endpoints MUST apply endpoint-specific rate limits and bounded lockout. |
| IAM-012 | Authentication and recovery responses MUST avoid disclosing whether an unrelated account exists. |
| IAM-013 | Authorization MUST use backend policy/permission checks; hiding frontend controls MUST NOT be treated as authorization. |
| IAM-014 | The platform MUST support the Platform Administrator, Tenant Administrator, Content Manager, and Viewer permission sets defined in `ACTORS_AND_USE_CASES.md`. |
| IAM-015 | Sensitive account, role, MFA, session, and recovery events MUST be audited without recording credentials, tokens, or recovery codes. |
| IAM-016 | Access to a tenant after membership removal, role reduction, tenant suspension, or account deactivation MUST be revoked within the documented propagation window. |
| IAM-017 | Critical actions SHOULD require recent reauthentication; the initial set MUST include MFA reset, administrator-role assignment, and device credential recovery. |

## 4. Device lifecycle and inventory

| ID | Requirement |
|---|---|
| DEV-001 | The supported target MUST include Raspberry Pi 4 and 5 running a supported 64-bit Raspberry Pi OS release. |
| DEV-002 | Each Pi MUST run a C# agent as a dedicated restricted operating-system account and start automatically under `systemd`. |
| DEV-003 | The agent MUST collect and report device name, Raspberry Pi serial number, relevant local IP addresses, normalized MAC addresses, OS/architecture, agent version, player version, disk capacity, applied manifest, and playback health. |
| DEV-004 | The backend MUST record the public source address it observes separately from agent-reported local addresses. |
| DEV-005 | Hardware serial, MAC, IP, hostname, and user-supplied names MUST be treated as inventory, not authentication credentials. |
| DEV-006 | Enrollment MUST use a cryptographically random, short-lived, single-use code generated by an authorized Tenant Administrator and stored as a hash. |
| DEV-007 | The Pi MUST generate its asymmetric private key locally; the raw private key MUST NOT be transmitted to or stored by the backend. |
| DEV-008 | Enrolled device requests MUST authenticate with a unique device identity using mutual TLS or an ADR-approved equivalently sender-constrained asymmetric mechanism. |
| DEV-009 | The backend MUST bind the authenticated certificate identity to exactly one active device and tenant before device authorization runs. |
| DEV-010 | Device credentials MUST support automatic renewal/rotation before expiry and immediate administrative revocation. |
| DEV-011 | Suspending, retiring, or revoking a device MUST prevent issuance of new valid leases even if its former certificate is presented. |
| DEV-012 | The agent MUST send configurable periodic heartbeats over outbound HTTPS and obey bounded server retry guidance with jittered backoff. |
| DEV-013 | The dashboard MUST show device lifecycle state, Online/Degraded/Offline health, last trusted heartbeat, inventory, versions, licence state, desired/applied content version, and relevant errors. |
| DEV-014 | No inbound customer-router port forwarding or remotely accessible Pi management port MUST be required. |
| DEV-015 | The player-facing local service MUST bind only to loopback and MUST NOT expose enrollment credentials or device private keys to browser JavaScript. |
| DEV-016 | Duplicate/conflicting reported serial numbers MUST be detected and quarantined for administrative resolution rather than silently reassigned. |
| DEV-017 | Enrollment, credential rotation/revocation, inventory changes, suspension, retirement, and duplicate-identity events MUST be audited. |

## 5. Licence lifecycle and enforcement

| ID | Requirement |
|---|---|
| LIC-001 | A licence MUST be tenant-owned and bound to one enrolled device for a UTC validity interval with an exclusive end instant. |
| LIC-002 | The backend MUST derive the effective state from start/end timestamps and explicit suspension, revocation, or transfer events. |
| LIC-003 | Only authorized Tenant Administrators MUST create, renew, suspend, revoke, or transfer a licence. |
| LIC-004 | Licence evaluation MUST occur on the backend; React clients and agent-supplied status MUST NOT determine entitlement. |
| LIC-005 | An authorized backend response MUST contain a cryptographically signed, versioned, device- and tenant-bound lease with a key identifier, issued time, validity bounds, licence identifier, and authorized desired-state reference. |
| LIC-006 | The lease end MUST be the earliest of the actual licence end, device/tenant authorization boundary, and server time plus the configured offline allowance. |
| LIC-007 | The offline allowance MUST NOT exceed 24 hours. |
| LIC-008 | The agent MUST verify the lease signature, signing-key trust, device/tenant binding, validity interval, desired-state binding, and trusted time before enabling playback. |
| LIC-009 | Missing, malformed, wrongly bound, expired, suspended, revoked, replayed, or untrusted authorization MUST produce the local **Not licensed** state. |
| LIC-010 | An online expiry, suspension, or revocation MUST stop playback within a provisional target of 60 seconds; the final target MUST be verified under the selected heartbeat configuration. |
| LIC-011 | The agent MUST persist a high-water mark of authenticated server time and use monotonic elapsed time within a boot to detect backward clock movement. |
| LIC-012 | If time trust cannot be established safely after rollback, corruption, or suspicious reboot, the device MUST fail closed until it regains the server or a still-verifiable safe state. |
| LIC-013 | Licence transfer MUST require a reason, stop new source leases, delay destination activation until source relinquishment or latest outstanding source-lease expiry, then activate the same-tenant replacement atomically and retain immutable history. |
| LIC-014 | Licence signing keys MUST be separated from database content, protected through production secret/key management, identified by `kid`, and support overlap during rotation. |
| LIC-015 | A customer with sustained root/physical control is outside the baseline tamper-resistance guarantee; this limitation MUST appear in security and operational documentation. |

## 6. Content, playlists, schedules, and assignments

| ID | Requirement |
|---|---|
| CNT-001 | Authorized Tenant Administrators and Content Managers MUST be able to upload video, image, and plain-text content within their tenant. |
| CNT-002 | The initial allowlist MUST be explicit and limited to hardware-tested formats; arbitrary HTML, JavaScript, SVG script, archives, and executables MUST be rejected. |
| CNT-003 | Upload processing MUST enforce authenticated authorization, request/body limits, declared extension checks, MIME and magic-signature checks, safe metadata/decoding checks, and configurable per-type size limits. |
| CNT-004 | New uploads MUST enter non-public quarantine under a random internal name and MUST pass the configured malware scanner before approval. |
| CNT-005 | Original client filenames MUST be treated as metadata, normalized for display, and never used as a storage path. |
| CNT-006 | Approved asset versions MUST be immutable and have a recorded byte length, SHA-256 digest, media metadata, creator, tenant, and audit history. |
| CNT-007 | Media and metadata MUST remain private; every download MUST authorize tenant and, for devices, assignment to the authenticated device. |
| CNT-008 | Users MUST be able to create an ordered playlist from approved assets and configure image/text duration plus video playback/looping behavior. |
| CNT-009 | Users MUST be able to target a published desired state to a device or device group and configure an optional one-time start/end window. |
| CNT-010 | Authoritative schedule bounds MUST be UTC. The tenant IANA timezone is used for input/presentation; recurring calendar schedules are outside the v1 core. |
| CNT-011 | Assignment conflicts MUST be rejected or resolved through a documented deterministic precedence rule; the server MUST compile one unambiguous desired state for a device at a time. |
| CNT-012 | Publishing MUST create an immutable, versioned manifest containing ordered asset references, hashes, sizes, playback parameters, and schedule/effective metadata. |
| CNT-013 | Editing published content, playlists, or assignments MUST produce a new version rather than mutate a version already authorized to devices. |
| CNT-014 | Archiving or deleting content MUST preserve required audit/history and MUST be rejected while an active published version depends on it unless a safe replacement is published. |
| CNT-015 | Upload, scan, approve/reject, archive, playlist, schedule, assignment, and publication actions MUST be audited. |
| CNT-016 | Text content MUST be rendered as text or through a narrowly defined sanitizer; it MUST NOT become executable markup. |

## 7. Device synchronization and kiosk playback

| ID | Requirement |
|---|---|
| PLY-001 | The agent MUST learn the desired-state version, trusted server time, and authorization status through an authenticated heartbeat or follow-up response. |
| PLY-002 | The agent MUST download only missing device-authorized assets and MUST support resumable or restart-safe transfer for large media. |
| PLY-003 | Downloads MUST enter a staging area and be verified against declared size and SHA-256 before use. |
| PLY-004 | A manifest MUST become active only after the complete manifest and all required assets pass verification; activation MUST be atomic. |
| PLY-005 | An interrupted, rejected, corrupt, or incomplete update MUST leave the previous valid authorized cache intact. |
| PLY-006 | Cache cleanup MUST preserve the active and rollback-safe versions and MUST operate within configured disk-space limits. |
| PLY-007 | The React player MUST be packaged with and served locally by the agent (or an ADR-approved restricted local component) and MUST not need a remote administration session to render. |
| PLY-008 | Chromium MUST start in full-screen kiosk mode after the local service is healthy and recover automatically after browser failure. |
| PLY-009 | The player MUST render the supported video, image, and text types, preserve playlist order, apply durations/schedules, and loop as configured. |
| PLY-010 | The agent, not browser state, MUST gate whether licensed content routes are available to the player. |
| PLY-011 | When no valid authorization exists, the player MUST show a stable **Not licensed** screen and MUST NOT reveal cached protected content. |
| PLY-012 | Cached content MUST continue during a temporary network outage only while the signed lease remains valid and scheduled content is locally available. |
| PLY-013 | Agent and kiosk services MUST start after reboot, use bounded restart policies, expose local health, and report recoverable failure state on the next heartbeat. |
| PLY-014 | With cached content and valid authorization, the provisional target from OS readiness to visible content MUST be at most 60 seconds and verified on actual Pi 4/5 hardware. |
| PLY-015 | The player MUST avoid exposing administrative controls, browser chrome, local file paths, secrets, or unrestricted navigation. |
| PLY-016 | A licensed device with no active assignment MUST show **No content assigned**; only invalid/absent authorization produces **Not licensed**. |

## 8. API, auditing, and observability

| ID | Requirement |
|---|---|
| API-001 | Human administration and device APIs MUST have separate authentication schemes and independently restricted route groups/hosts. |
| API-002 | APIs MUST be explicitly versioned and documented through generated OpenAPI plus narrative security/flow documentation. |
| API-003 | All external inputs MUST be schema-, length-, range-, format-, and authorization-validated before domain mutation. |
| API-004 | Errors MUST use a consistent problem-details contract, stable machine code, correlation identifier, and safe user message without stack traces or secrets. |
| API-005 | Mutation endpoints vulnerable to retries, including enrollment, publication, transfer, and renewal, MUST implement atomicity and appropriate idempotency/conflict handling. |
| API-006 | Endpoint-specific request limits, timeouts, cancellation, and rate limits MUST protect authentication, enrollment, heartbeat, upload, and download surfaces. |
| API-007 | Audit events MUST be append-oriented and record authenticated actor/device, tenant, action, target, UTC timestamp, outcome, correlation identifier, and approved reason/context. |
| API-008 | Logs and telemetry MUST exclude passwords, cookies, tokens, raw enrollment codes, private keys, MFA secrets/recovery codes, and unnecessary media/body data. |
| API-009 | Health endpoints MUST distinguish liveness from readiness and MUST not disclose sensitive configuration publicly. |
| API-010 | Metrics MUST cover request failures/latency, authentication abuse, device heartbeat health, licence failures, synchronization failures, storage/scan health, and background-job failures. |

## 9. Cross-cutting security and privacy

| ID | Requirement |
|---|---|
| SEC-001 | Production communication MUST use validated TLS; administrative pages MUST use HSTS and appropriate secure headers. |
| SEC-002 | Administration UI and API SHOULD be same-origin; CORS MUST be disabled unless an explicit allowlist and test proves it is necessary. |
| SEC-003 | The administration UI MUST deploy a restrictive Content Security Policy and avoid unsafe dynamic code execution. |
| SEC-004 | Tenant database isolation MUST use application authorization plus SQL Server filter/block security predicates that default-deny tenant-owned tables. |
| SEC-005 | Runtime database roles MUST not own RLS-protected tables, be superusers, or hold `BYPASSRLS`; migration and runtime principals MUST be distinct. |
| SEC-006 | Tenant context applied to the database connection MUST be transaction-scoped, reset safely with pooled connections, and covered by positive and negative integration tests. |
| SEC-007 | Secrets and signing/CA keys MUST be supplied through protected secret/key management, never committed, never placed in frontend bundles, and rotated through documented procedures. |
| SEC-008 | Production services and Pi processes MUST run with least privilege, explicit filesystem/network access, and hardened service/container settings. |
| SEC-009 | Security-relevant dependencies and base images MUST be pinned, inventoried, scanned, patched, and reviewed before release. |
| SEC-010 | Backups MUST be encrypted and access-controlled, and restore procedures MUST be tested without weakening tenant isolation or secret handling. |
| SEC-011 | File parsing/scanning MUST be isolated and resource-bounded to reduce decompression, parser, and denial-of-service risks. |
| SEC-012 | Personal/device data collection and retention MUST be limited to operational need, documented, and removable according to the approved retention policy while preserving required security audit evidence. |
| SEC-013 | Security controls and verification MUST be mapped to OWASP ASVS 5.0 Level 2, with non-applicable controls justified. |
| SEC-014 | The threat model MUST be reviewed when a trust boundary, authentication method, external provider, media type, deployment topology, or privileged role changes. |

## 10. Reliability, performance, accessibility, and operations

| ID | Requirement |
|---|---|
| NFR-001 | The backend MUST remain a modular monolith unless measured evidence and an approved ADR justify decomposition. |
| NFR-002 | All timestamps used for authorization/audit MUST be UTC; user-facing conversions MUST use explicit supported timezones. |
| NFR-003 | Database mutations that change licence, assignment, enrollment, or tenant authorization MUST be transactional. |
| NFR-004 | Heartbeat cadence and offline thresholds MUST be configurable; provisional defaults are 30 seconds and Offline after three missed intervals. |
| NFR-005 | Under the approved target load, non-download API operations SHOULD meet a provisional p95 of 500 ms and error rate below 1%, excluding expected authorization/validation failures. |
| NFR-006 | The administration UI SHOULD meet WCAG 2.2 AA for supported workflows, including keyboard operation, focus visibility, semantics, contrast, and error identification. |
| NFR-007 | The administration UI MUST support current stable Chrome/Edge and a documented responsive viewport range. |
| NFR-008 | The system MUST degrade safely when email, scanner, object storage, database, network, or a background worker is unavailable; authorization MUST fail closed where security state is uncertain. |
| NFR-009 | Deployment MUST provide repeatable configuration validation, schema migration, health checking, rollback/recovery instructions, and environment separation. |
| NFR-010 | Backup frequency, retention, recovery point objective, and recovery time objective MUST be agreed before production acceptance and verified through restore exercises. |
| NFR-011 | Monitoring MUST alert on sustained device offline state, impending/expired licences, repeated authentication/enrollment abuse, failed content synchronization, low device disk, and critical service health. |
| NFR-012 | Agent and server update procedures MUST verify artifact origin/integrity, support rollback, and document compatibility between API, agent, player, manifest, and lease versions. |
| NFR-013 | Expected tenant/device/content scale MUST be documented before final load acceptance; tests MUST exercise at least that scale plus an agreed safety margin. |

## 11. Verification, documentation, and internship report

| ID | Requirement |
|---|---|
| VER-001 | Backend domain logic MUST have unit tests, especially licence boundaries, permission decisions, schedule compilation, and trusted-time behavior. |
| VER-002 | SQL Server 2022-backed integration tests MUST prove RLS and application authorization for allowed, denied, cross-tenant, missing-context, and privileged-operation cases. |
| VER-003 | Authentication tests MUST cover invitation, verification, password/reset, MFA, recovery, lockout, rate limits, CSRF, session rotation, expiry, revocation, and role change. |
| VER-004 | Device tests MUST cover enrollment, replay, expiry, wrong tenant/certificate, rotation, revocation, duplicate identity, heartbeat backoff, and version compatibility. |
| VER-005 | Licence tests MUST cover boundary instants, renewal, suspension, revocation, transfer, offline maximum, actual-expiry bound, bad signatures/bindings, restart, and clock rollback. |
| VER-006 | Content tests MUST cover malicious names/types/signatures, oversize and corrupt files, scanner failure, unauthorized access, hashing, interrupted download, atomic activation, and cache pressure. |
| VER-007 | Frontend and Playwright tests MUST cover each role's critical journey plus forbidden direct navigation/API behavior. |
| VER-008 | Real Pi 4/5 tests MUST cover provisioning, reboot/autostart, kiosk recovery, supported media/audio/video, network change/outage, lease expiry during playback, disk pressure, and corrupted cache. |
| VER-009 | Deployment tests MUST prove clean installation, migration, backup/restore, TLS, secret absence from artifacts/logs, monitoring, and documented recovery. |
| VER-010 | Security testing MUST include ASVS mapping, static/dependency/container scanning, authorization/IDOR tests, CSRF/XSS/injection checks, upload abuse, rate-limit behavior, and manual threat-model review. |
| DOC-001 | Requirements, architecture, data model/ERD, ADRs, threat model, OpenAPI, Pi setup, deployment, operations, security, test plan/results, traceability, user/admin manuals, and changelog MUST be maintained with the implementation. |
| DOC-002 | Test results MUST record environment, version/commit, command or procedure, date, expected/actual result, and retained evidence; planned tests MUST never be presented as executed. |
| DOC-003 | The French report MUST follow the analyzed reference's academic style while correcting its numbering and test-evidence weaknesses. |
| DOC-004 | The French report MUST include front matter, résumé/abstract, acronyms, automatic contents/figure/table lists, host/project context, analysis, design, implementation, security/testing, deployment, conclusion, references, and useful appendices. |
| DOC-005 | Report statements, diagrams, screenshots, metrics, and results MUST match the final current system and traceable evidence. |
| DOC-006 | Final completion MUST include a requirement-by-requirement audit that classifies every requirement as proven or not complete; missing or indirect evidence cannot pass. |

## 12. Baseline exclusions and change control

A proposed change to the licence trust model, tenant boundary, device authentication, offline duration, supported executable content, production topology, or privileged roles requires threat-model review and an ADR before implementation. Scope may grow with user approval, but final completion cannot silently omit or weaken an existing `MUST` requirement.
