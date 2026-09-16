# Detailed Security Requirements

This document refines the cross-cutting requirements in `REQUIREMENTS.md`. Its controls are normative for the relevant feature. Verification will map them to OWASP ASVS 5.0.0 Level 2 controls, code/configuration, and retained evidence.

## 1. Security baseline

| ID | Requirement |
|---|---|
| SEC-BASE-001 | Applicable web/API controls MUST meet OWASP ASVS 5.0.0 Level 2; each non-applicable control requires a written reason. |
| SEC-BASE-002 | Uncertain authentication, authorization, tenant, device, licence, certificate, time, scan, manifest, or integrity state MUST fail closed. |
| SEC-BASE-003 | Human sessions, device identities, internal services, database roles, deployment identities, and signing functions MUST use purpose-separated credentials and policies. |
| SEC-BASE-004 | Production errors MUST exclude stack traces, SQL, filesystem/storage paths, secrets, certificate internals, and cross-tenant existence; a safe correlation ID MUST be returned. |
| SEC-BASE-005 | Cryptography MUST use maintained platform/library implementations with fixed algorithm allowlists; custom encryption or signature primitives are forbidden. |
| SEC-BASE-006 | Security controls MUST map to at least one verification method and retained evidence before final acceptance. |

## 2. Human authentication, MFA, and sessions

| ID | Requirement |
|---|---|
| SEC-HUM-001 | Public registration MUST be disabled; tenant users originate from tenant- and role-bound invitations issued by an authorized administrator. |
| SEC-HUM-002 | Invitation/reset tokens MUST contain at least 128 bits of CSPRNG entropy, be stored as hashes, single-use, expiring, atomically consumed, and absent from URLs/logs except where the email link necessarily carries the opaque token. |
| SEC-HUM-003 | Provisional validity is 24 hours for invitations and 30 minutes for password reset; final values MUST be configuration-tested and documented. |
| SEC-HUM-004 | Passwords MUST require at least 15 characters, allow at least 128 characters plus spaces/Unicode, not be silently truncated, reject compromised/common values, use no composition rule, and not expire periodically without compromise evidence. |
| SEC-HUM-005 | Password hashing MUST use Argon2id with current OWASP parameters or an ADR-approved ASP.NET Identity PBKDF2 configuration at least equivalent to current OWASP guidance, benchmarked against denial-of-service and rehashed on parameter upgrade. |
| SEC-HUM-006 | Login, reset, invitation, and MFA responses/timing MUST materially limit account enumeration. |
| SEC-HUM-007 | Authentication rate limits MUST combine source, normalized account, and global protection. Initial limits are recorded as configuration and MUST be abuse/load-tested rather than hard-coded assumptions. |
| SEC-HUM-008 | Platform Administrators, Tenant Administrators, and Content Managers MUST complete MFA before privileged permissions activate. |
| SEC-HUM-009 | TOTP seeds MUST be encrypted at rest, shown only during enrollment, confirmed before activation, never logged, and recoverable only through the approved process. |
| SEC-HUM-010 | Recovery codes MUST be high entropy, individually hashed, single-use, shown once, and replaced when regenerated; email alone cannot reset privileged MFA. |
| SEC-HUM-011 | MFA reset MUST require recent step-up and an authorized recovery/second-administrator process, revoke sessions, notify the user, and create an audit event. A last-platform-admin break-glass procedure MUST be documented. |
| SEC-HUM-012 | TOTP is not phishing-resistant; passkeys/WebAuthn SHOULD be evaluated as a later strengthening, without falsely describing TOTP as phishing-resistant. |
| SEC-HUM-013 | The human session cookie MUST use a `__Host-` prefix, `Secure`, `HttpOnly`, `Path=/`, no `Domain`, and `SameSite=Strict` unless an ADR proves `Lax` is required and CSRF regression tests pass. |
| SEC-HUM-014 | Session/authentication tokens MUST NOT be stored in `localStorage`, `sessionStorage`, IndexedDB, URLs, or JavaScript-readable cookies. |
| SEC-HUM-015 | Every session MUST have server-side revocation metadata including opaque identifier/hash, user, authentication/MFA time, creation, last activity, absolute/idle expiry, and revocation state. |
| SEC-HUM-016 | Initial privileged expiry is 30-minute idle/12-hour absolute; viewer expiry is 60-minute idle/24-hour absolute. Privileged persistent “remember me” is disabled. Values MUST be tested/documented. |
| SEC-HUM-017 | Session identifiers/security stamps MUST rotate or invalidate after login, MFA, password/MFA/role change, account disable, and other privilege transitions. |
| SEC-HUM-018 | Users MUST be able to inspect/revoke sessions; password reset/change and account disable MUST revoke applicable sessions. |
| SEC-HUM-019 | ASP.NET Data Protection keys MUST persist beyond container life, be purpose-scoped, encrypted at rest, least-privileged, backed up, and verified through rotation/restore tests. |
| SEC-HUM-020 | Role changes, MFA reset, licence transfer, device-certificate revocation, and security/key configuration MUST require step-up MFA no older than ten minutes. |
| SEC-HUM-021 | Sensitive/authenticated responses MUST use `Cache-Control: no-store`; logout is a CSRF-protected unsafe action that revokes and clears the session. |

## 3. Browser, CSRF, and UI security

| ID | Requirement |
|---|---|
| SEC-WEB-001 | Administration SPA and human API SHOULD remain same-origin; production CORS is deny-by-default and credentialed wildcard origins are forbidden. |
| SEC-WEB-002 | Every cookie-authenticated unsafe method, including multipart upload, login, logout, MFA, and reset completion, MUST validate ASP.NET antiforgery protection globally. |
| SEC-WEB-003 | The antiforgery request token MAY be returned by a bootstrap response and held only in React memory; it MUST rotate with authentication/session rotation. |
| SEC-WEB-004 | `GET`, `HEAD`, and `OPTIONS` MUST be safe. Exact Origin/Fetch Metadata/content-type checks are defense in depth, not antiforgery replacements. |
| SEC-WEB-005 | React contextual escaping MUST be retained; arbitrary `dangerouslySetInnerHTML`, executable Markdown/HTML, dynamic code, and uncontrolled URLs are forbidden. |
| SEC-WEB-006 | Admin CSP MUST deny by default, framing, plugins, base rewriting, eval/dynamic scripts, arbitrary connections, and unapproved media. UI libraries MUST support CSP nonce/hash configuration rather than weakening policy. |
| SEC-WEB-007 | Production headers MUST include validated HSTS, `nosniff`, restrictive referrer and permissions policies, and `frame-ancestors 'none'`. |
| SEC-WEB-008 | Any realtime transport is notification-only by default; a command path requires the same authorization, origin/CSRF, rate-limit, validation, and audit controls as HTTP mutations. |

## 4. Authorization and tenant enforcement

| ID | Requirement |
|---|---|
| SEC-AUT-001 | A deny-by-default backend authorization policy MUST protect every non-public endpoint; frontend visibility is never authorization. |
| SEC-AUT-002 | Named policies and resource checks MUST implement the permission matrix without accepting client role claims. |
| SEC-AUT-003 | Tenant, user, device, ownership, and permission context MUST be derived or validated server-side; route/body/query/header/cookie values are untrusted selectors. |
| SEC-AUT-004 | Request DTOs MUST explicitly allowlist mutable fields; tenant, identity, audit, ownership, and immutable version fields are not mass-assignable. |
| SEC-AUT-005 | Opaque IDs reduce guessing but MUST NOT replace authorization; inaccessible cross-tenant resources normally return a generic `404`/denial. |
| SEC-AUT-006 | Platform roles cannot be assigned by tenant roles. Platform administration has no silent tenant-content bypass. |
| SEC-AUT-007 | Background jobs MUST process one authenticated/recorded tenant context per transaction and cannot use an ordinary all-tenant runtime bypass. |
| SEC-TEN-001 | Every tenant-owned row, including audit and credential metadata, MUST contain an immutable non-null `tenant_id`. |
| SEC-TEN-002 | Tenant relationships MUST use composite or equivalent constraints that prevent a child from referencing another tenant's parent. |
| SEC-TEN-003 | EF Core tenant filters MAY provide defense in depth; SQL Server RLS security policies plus application authorization remain authoritative. |
| SEC-TEN-004 | Every tenant table MUST use `ENABLE ROW LEVEL SECURITY`, `FORCE ROW LEVEL SECURITY`, explicit `USING`/`WITH CHECK`, and default-deny when context is absent. |
| SEC-TEN-005 | Separate schema owner/migration, runtime, backup, and monitoring roles MUST exist. Runtime is not owner/superuser/`BYPASSRLS`, cannot alter policy, `TRUNCATE`, create in trusted schemas, or `SET ROLE` to privilege. |
| SEC-TEN-006 | Each request/job MUST begin a transaction and set tenant context using parameterized transaction-local `set_config`; session-scoped pooled tenant state is forbidden. |
| SEC-TEN-007 | Runtime `search_path` MUST contain only trusted schemas where untrusted roles cannot create objects. |
| SEC-TEN-008 | CI MUST inspect SQL Server catalogs for protected tables, enabled security policies/predicates, owners, grants, roles, and tenant constraints. |
| SEC-TEN-009 | Cross-tenant CRUD/query/download tests MUST use the real restricted runtime role and include missing/tampered context plus pooled-connection reuse. |
| SEC-TEN-010 | Backups MUST use a deliberate audited role/configuration that fails when RLS would silently filter data. |
| SEC-TEN-011 | API errors MUST avoid exposing cross-tenant values through SQL Server uniqueness/referential errors, which can bypass row visibility checks. |

## 5. Device enrollment and certificate lifecycle

| ID | Requirement |
|---|---|
| SEC-DEV-001 | Device inventory fields are sanitized untrusted metadata; `lastSeenUtc` and public source address come from server observation. |
| SEC-DEV-002 | Authentication uses an immutable random device identifier and asymmetric credential, never serial/MAC/IP/hostname. |
| SEC-DEV-003 | Enrollment secrets MUST have at least 128 bits entropy, bind to tenant/pending device, be shown once, hashed, valid initially for 15 minutes, and consumed atomically once. |
| SEC-DEV-004 | Enrollment secrets appear only in a TLS request body, never query/log; endpoint attempts are bounded and audited safely. |
| SEC-DEV-005 | Agent MUST generate an ECDSA P-256 private key and signed CSR locally; backend never receives/logs/backs up the private key. |
| SEC-DEV-006 | Production PKI SHOULD use a protected issuing intermediate and offline root; CA, lease, update, TLS, and Data Protection keys MUST be separate. |
| SEC-DEV-007 | Device certificates MUST have client-auth EKU, digital-signature use, immutable device ID in SAN, random serial, no secret/PII, and no server-auth purpose; self-signed device certs are rejected. |
| SEC-DEV-008 | Initial device certificate validity is 90 days; rotation starts with 30 days remaining using current mTLS plus proof of new-key possession. Planned overlap is bounded to 24 hours. |
| SEC-DEV-009 | Device API validates chain, dates, EKU, issuer, SAN, certificate lifecycle and active device/tenant mapping on every request. Body values cannot override identity. |
| SEC-DEV-010 | Device/certificate suspension or revocation denies new application requests immediately; any authorization cache is bounded and invalidated. |
| SEC-DEV-011 | TLS proxying may forward certificate identity only from a known protected proxy that strips Internet headers and validates the client certificate; backend mapping remains mandatory. |
| SEC-DEV-012 | Failed renewal requires audited re-enrollment, never relaxed certificate validation. |
| SEC-DEV-013 | Heartbeats include a random boot ID and monotonically increasing per-boot sequence; stale/out-of-order mutations are rejected and processing is idempotent. |
| SEC-DEV-014 | Conflicting concurrent boot/location use of a credential generates an alert/review; detection does not falsely claim software-key cloning is impossible. |
| SEC-DEV-015 | Human cookies are not accepted on device routes and device certificates cannot access human/platform APIs. |
| SEC-DEV-016 | Agent key/state directory permissions MUST be owner-only (`0700` directory, `0600` files), dedicated non-login user, inaccessible to Chromium. |
| SEC-DEV-017 | Hostname/chain validation is mandatory. “Accept any certificate” hooks cannot compile or activate in production configuration. |

## 6. Signed lease and trusted-time enforcement

| ID | Requirement |
|---|---|
| SEC-LIC-001 | Eligibility requires matching active tenant/device/certificate/licence, `startsAtUtc <= serverNow < expiresAtUtc`, and no suspension/revocation/anomaly. |
| SEC-LIC-002 | Authorization lease MUST use a fixed-schema compact JWS signed with ES256 by a dedicated protected key; allowed `alg`, `typ`, issuer, audience and known `kid` are fixed. |
| SEC-LIC-003 | Claims MUST include schema version, issuer, audience, device subject, tenant, licence, unique `jti`, `iat`, `nbf`, `exp`, manifest/version and manifest SHA-256. |
| SEC-LIC-004 | `exp` MUST equal the earliest of actual licence expiry and `iat + 24 hours`; no hidden grace period exists. |
| SEC-LIC-005 | Agent MUST validate signature/algorithm/type/key/issuer/audience/device/tenant/schema/time/manifest binding; malformed, duplicate, unknown, or conflicting claims fail closed. |
| SEC-LIC-006 | Signing private keys SHOULD use KMS/HSM where available. Public verification trust supports current/next overlap and authenticated retirement after all issued leases expire. |
| SEC-LIC-007 | Authenticated server time advances a persistent high-water mark; monotonic/boottime advances effective time within a boot and effective time never moves backward. |
| SEC-LIC-008 | Trusted-time state MUST be versioned, integrity-protected, atomically written, and include high-water UTC plus lease/boot/monotonic references. Missing/corrupt/replayed/incompatible state fails closed until online synchronization. |
| SEC-LIC-009 | Wall time may advance but never reduce trusted time. A large forward jump safely expires playback and raises a diagnostic. |
| SEC-LIC-010 | Agent is the local licence authority; React cannot read signing/device keys or override authorization. |
| SEC-LIC-011 | Online revocation/suspension MUST stop playback within the tested heartbeat target. Offline legitimate playback may continue only through the already-issued lease. |
| SEC-LIC-012 | Player MUST recheck continuously and before each item; within five seconds of lease expiry it pauses/unloads protected media and shows exactly **Not licensed**. |
| SEC-LIC-013 | Transfer requires step-up and audit. Destination authorization waits until source relinquishment or latest outstanding source-lease expiry, preventing unintended simultaneous use. |
| SEC-LIC-014 | State loss, invalid signature/key/binding/time/manifest, disabled device, or corrupted authorization produces **Not licensed**, never stale playback. |

## 7. Content, storage, delivery, and kiosk security

| ID | Requirement |
|---|---|
| SEC-CNT-001 | Initial allowlist is UTF-8 plain text, JPEG, PNG, WebP, and hardware-validated MP4/H.264/AAC. SVG, HTML, script, archive, active document, executable, and arbitrary external URL content are rejected. |
| SEC-CNT-002 | Upload requires tenant writer authorization, CSRF, per-file/request/tenant quotas, concurrency limits, streaming, cancellation, and timeouts. |
| SEC-CNT-003 | Client name/extension/MIME are untrusted; validate decoded name, extension, MIME, signature, parsed structure/dimensions/codecs/duration, and size. |
| SEC-CNT-004 | Quarantine uses random server object keys in non-executable private storage outside the webroot. |
| SEC-CNT-005 | Malware scanning is mandatory. Media inspection/normalization runs non-root, no-network, and under CPU/memory/time/disk/output bounds. |
| SEC-CNT-006 | Scanner/parser/normalizer failure, timeout, ambiguity, or unsupported content remains quarantined; only successful immutable results publish. |
| SEC-CNT-007 | Plain text is valid normalized UTF-8 within a limit and rendered as a React text node with allowlisted presentation values, never executable HTML/Markdown/CSS. |
| SEC-CNT-008 | Immutable manifests contain tenant, desired-state/playlist version, ordered objects, exact type, size, playback data and SHA-256; lease binds the exact manifest hash. |
| SEC-CNT-009 | Agent stages with quota/length enforcement, verifies SHA-256, fsyncs where required, and atomically renames/switches; partial/oversized/corrupt objects never display. |
| SEC-CNT-010 | Device download authorization is recomputed from authenticated device and assignment. Storage credentials are never sent to devices; any signed URL is object-scoped and expires within five minutes. |
| SEC-CNT-011 | Media responses use exact MIME, `nosniff`, bounded byte ranges, no human admin cookies, and no directory listing. |
| SEC-CNT-012 | Cleanup preserves the active known-good version until a complete verified replacement exists; low disk/reboot/interruption cannot create partial playback. |
| SEC-CNT-013 | Local agent/player binds only loopback; no shell, admin, arbitrary path, credential, remote debugging, or unrestricted URL-fetch surface exists. |
| SEC-CNT-014 | Chromium runs as a distinct unprivileged user with sandbox enabled, no human credentials, disabled remote debugging, restricted profile/navigation, and kiosk recovery. |
| SEC-CNT-015 | Player CSP defaults to deny and permits only bundled assets plus gated loopback content/API; no eval, arbitrary frames/plugins/connections. |
| SEC-CNT-016 | Agent/player services restart with bounded backoff; screen defaults to **Not licensed** until valid state is re-established. |

## 8. Infrastructure, secrets, audit, backup, and updates

| ID | Requirement |
|---|---|
| SEC-INF-001 | Production SHOULD use TLS 1.3 per RFC 9846. Any TLS 1.2 compatibility exception uses modern AEAD-only configuration; TLS 1.0/1.1, compression, weak suites, and unsafe mutation 0-RTT are forbidden. |
| SEC-INF-002 | Only intended HTTPS surfaces are public. SQL Server, storage administration, scanner, key/signing services, detailed readiness and metrics remain private. |
| SEC-INF-003 | Secrets MUST NOT be committed, placed in frontend/images/command lines/CI output, or shared across environments; production uses least-privileged secret management or mounted protected files. |
| SEC-INF-004 | CA, lease, update, TLS, Data Protection, TOTP-field encryption, database, storage and backup keys/credentials MUST be separated, inventoried, owned, rotatable and revocable. |
| SEC-INF-005 | Services/containers run non-root, drop capabilities, use read-only filesystems except explicit volumes, apply resource limits, and never mount the Docker socket. |
| SEC-INF-006 | Pi provisioning disables unused SSH/VNC/remote services. Required SSH is key-only, allowlisted/firewalled, audited, and separate from application identity. |
| SEC-INF-007 | Agent `systemd` confinement MUST include dedicated user, `NoNewPrivileges`, empty capability set unless justified, protected system/home/kernel controls, private temp, and explicit writable paths/address families. |
| SEC-INF-008 | OS, Chromium, .NET, parsers/scanner, SQL Server, reverse proxy, and containers MUST remain supported and receive controlled security updates. |
| SEC-INF-009 | Pi application updates MUST be signed with a separate update key, verify hash/size/version/expiry, install atomically, reject rollback/freeze metadata, and retain tested last-known-good recovery. |
| SEC-INF-010 | CI uses lockfiles, dependency/licence inventory, SBOM, secret/SAST/dependency/container scans and protected release controls; untrusted jobs receive no production secrets. |
| SEC-INF-011 | Release artifacts MUST be traceable to source/commit, signed where deployed to Pis, and promoted rather than rebuilt differently after testing. |
| SEC-INF-012 | Structured audit MUST cover authentication/MFA/session, denial, role/user, enrollment/certificate, licence/transfer, scan/publication/assignment, clock, update, backup and security configuration events. |
| SEC-INF-013 | Audit records include UTC, actor type/id, tenant, action, target, outcome, safe source information and trace ID; user-controlled values are log-injection safe. |
| SEC-INF-014 | Logs MUST exclude passwords, MFA material, invitation/reset/enrollment secrets, sessions/cookies, private keys, service credentials, complete signed URLs, and full media. |
| SEC-INF-015 | Runtime cannot update/delete audit events; security events are exported to controlled durable storage with retention/integrity monitoring. |
| SEC-INF-016 | Alerts MUST cover brute force, cross-tenant denials, enrollment replay, duplicate/revoked credentials, clock rollback, malware/scan failure, privilege/key changes, backup failure, capacity, and device concurrency. |
| SEC-INF-017 | Edge/application rate limits, streaming limits, quotas, timeouts, bounded queues, and device jittered backoff MUST be load-tested against abuse and retry storms. |
| SEC-INF-018 | Backups MUST coherently cover SQL Server full/differential/log recovery, immutable media/manifests, audit/configuration and keys required to decrypt/validate; device cache is never authoritative. |
| SEC-INF-019 | Provisional production objectives are RPO 15 minutes, RTO 4 hours, daily encrypted backup, continuous WAL, 30-day retention and separate immutable/offsite copy; business approval is required before Goal 10. |
| SEC-INF-020 | Isolated restore tests MUST prove database/media/key consistency, tenant isolation, account/session behavior, manifest integrity and licence expiry. |
| SEC-INF-021 | Incident runbooks MUST cover account/session breach, tenant isolation failure, device clone/cert theft, signing/CA/update key compromise, malicious media, dependency incident and data loss. |
| SEC-INF-022 | Retention/deletion policy MUST cover emails, IP/MAC/serial history, heartbeats, logs, media, archived tenants and backups, minimizing operationally unnecessary data. |
| SEC-INF-023 | Public health reveals only generic liveness; versions, dependencies, metrics and diagnostics require operator access. |

## 9. Security acceptance gates

The following are release-blocking evidence groups:

- `SEC-ACC-001`: completed ASVS 5.0.0 Level 2 mapping;
- `SEC-ACC-002`: reviewed threat/data-flow model including keys, offline behavior, admin abuse and physical limitations;
- `SEC-ACC-003`: automated SQL Server catalog/RLS/role/constraint inspection;
- `SEC-ACC-004`: full cross-tenant resource and storage test matrix;
- `SEC-ACC-005`: human invitation/password/MFA/session/CSRF/rate-limit E2E matrix;
- `SEC-ACC-006`: endpoint-by-role authorization and tampered identifier matrix;
- `SEC-ACC-007`: real mTLS enrollment/certificate/proxy spoofing lifecycle tests;
- `SEC-ACC-008`: real lease signature/binding/algorithm/time/offline/rollback tests;
- `SEC-ACC-009`: transfer test proving no unintended simultaneous device entitlement;
- `SEC-ACC-010`: hostile upload corpus plus scanner/parser/quota/storage isolation tests;
- `SEC-ACC-011`: manifest/hash/atomic cache/kiosk sandbox/loopback/not-licensed tests;
- `SEC-ACC-012`: TLS/header/CSP scan;
- `SEC-ACC-013`: secret/browser storage/source/artifact/log scan;
- `SEC-ACC-014`: dependency/SBOM/container assessment with no unaccepted critical/high issue;
- `SEC-ACC-015`: rate/load/fault tests showing safe behavior;
- `SEC-ACC-016`: isolated restore meeting approved RPO/RTO;
- `SEC-ACC-017`: real Pi boot/network/offline/cert/time/expiry/kiosk evidence;
- `SEC-ACC-018`: no open critical/high security defect; and
- `SEC-ACC-019`: docs/runbooks/report match the verified release.

## 10. Authoritative references

- [OWASP ASVS 5.0.0](https://owasp.org/www-project-application-security-verification-standard/)
- [NIST SP 800-63B-4](https://pages.nist.gov/800-63-4/sp800-63b.html)
- [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [OWASP Session Management](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [OWASP File Upload](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)
- [SQL Server row-level security](https://learn.microsoft.com/sql/relational-databases/security/row-level-security)
- [ASP.NET Core certificate authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0)
- [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
- [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [RFC 9846 — TLS 1.3](https://www.rfc-editor.org/info/rfc9846/)
- [The Update Framework](https://theupdateframework.io/docs/overview/)
- [NIST Secure Software Development Framework](https://csrc.nist.gov/projects/ssdf)
