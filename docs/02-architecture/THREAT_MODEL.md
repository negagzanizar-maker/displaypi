# Threat Model and Residual Risk Register

## 1. Scope and method

This threat model covers the administration browser/API, tenant database access, private media pipeline, device enrollment/mTLS API, licence signing and offline enforcement, C# Pi agent, local React/Chromium player, deployment pipeline, monitoring and backups. STRIDE categories are used as an analysis aid; risk treatment follows the detailed security requirements.

The model is reviewed whenever a trust boundary, privileged role, authentication scheme, external provider, media type, offline policy, cryptographic key, deployment topology, or update mechanism changes.

## 2. Security objectives

1. Prevent cross-tenant disclosure or mutation.
2. Ensure only authenticated active devices receive authorized manifests/content.
3. Display protected content only while a valid device-bound signed lease permits it.
4. Protect privileged human accounts, device keys, signing/CA/update keys, sessions and backups.
5. Preserve licence, role, assignment, content, audit and update integrity.
6. Fail closed when security state is uncertain.
7. Remain recoverable and provide attributable evidence.
8. Minimize collected and exposed personal/device data.

## 3. Assumptions

- Tenants, the Internet, customer LANs, user input and uploaded media are untrusted.
- Serial, hostname, IP and MAC are inventory, not authenticators.
- The managed Pi OS, restricted agent account and Chromium sandbox are assumed uncompromised in the baseline profile.
- Infrastructure operators are privileged but are not ordinary application users.
- Development, test, staging and production are separate security domains.
- The system is a licensing/content-control platform, not DRM.

## 4. Asset classification

| Classification | Examples | Required properties |
|---|---|---|
| Restricted secrets | Password hashes, MFA seeds/recovery, sessions, reset/invitation/enrollment tokens, Data Protection/DB/storage credentials, CA/lease/update keys, device private keys | Confidentiality, integrity, least privilege, rotation/recovery |
| Confidential tenant data | Users/emails, media/text, playlists, assignments, device identifiers/history, licences, audit, backups | Tenant isolation, authorized access, retention, encryption |
| Integrity-critical state | Roles, tenant context, certificate bindings, licence state, leases, trusted time, manifests/hashes, audit/update metadata | Strong authentication, authorization, signing/versioning, tamper detection |
| Availability-critical services | API, SQL Server, storage/scanner, signing, Pi agent/player, email | Limits, monitoring, recovery, safe offline behavior |
| Public | Login assets and explicitly published documentation | Integrity and safe caching |

No restricted secret or human authentication token may be exposed to React, Chromium, URLs, analytics, or logs.

## 5. Trust boundaries

1. Browser -> public edge -> human API.
2. Unenrolled Pi -> server-authenticated enrollment endpoint.
3. Enrolled Pi agent -> Internet -> mTLS device endpoint.
4. ASP.NET runtime -> restricted SQL Server login/RLS security policies.
5. API/worker -> quarantine, scanner/parser and private object storage.
6. Agent credential/authorization process -> loopback local player -> Chromium.
7. Source/CI -> registries -> signed release/deployment/update.
8. Production systems -> logs, monitoring, backups and recovery environment.

## 6. Principal threat register

| ID | Category | Threat | Initial risk | Primary controls | Required verification | Residual risk |
|---|---|---|---|---|---|---|
| TM-S-001 | Spoofing | Stolen/guessed human credential or session impersonates a privileged user | Critical | MFA, password policy/hash, secure cookie, rotation/revocation, rate limits, step-up | SEC-ACC-005/006/013 | TOTP phishing; endpoint compromise |
| TM-S-002 | Spoofing | Serial/MAC/IP or forged body claims impersonate a Pi | Critical | Unique mTLS identity, CSR/local key, cert-device mapping | SEC-ACC-007 | Software key copy by root |
| TM-S-003 | Spoofing | Stolen enrollment code registers attacker first | High | 128-bit token, 15-minute expiry, hash, one use, binding, rate limit | enrollment replay/race tests | Possessor within short window can race |
| TM-S-004 | Spoofing | Proxy certificate headers are forged from Internet | Critical | Dedicated host, strip external headers, trusted proxy, backend chain/mapping | real proxy/mTLS spoof tests | Proxy/operator compromise |
| TM-T-001 | Tampering | Tenant/resource IDs or mass assignment alter another tenant | Critical | backend policies, allowlist DTOs, composite tenant constraints, FORCE RLS | SEC-ACC-003/004/006 | Privileged DB owner |
| TM-T-002 | Tampering | Lease is forged, algorithm-confused, rebound or replayed | Critical | fixed ES256 JWS, issuer/audience/key/device/tenant/manifest validation | SEC-ACC-008 | Lease signing-key compromise |
| TM-T-003 | Tampering | Clock/state rollback extends offline authorization | Critical | signed server time, monotonic progression, high-water integrity, fail closed | deterministic + real Pi clock tests | Root/SD snapshot defeats software state |
| TM-T-004 | Tampering | Media or manifest is replaced/partially downloaded | High | immutable versions, exact hash/size, lease-manifest binding, atomic activation | SEC-ACC-010/011 | Root can alter official software |
| TM-T-005 | Tampering | Malicious update compromises agent/player | Critical | separate signing key, version/expiry metadata, artifact promotion, rollback protection | update tamper/rollback tests | Trusted publisher/key compromise |
| TM-R-001 | Repudiation | Privileged actor denies role, licence, transfer, publication or cert action | High | append-only structured audit, step-up, correlation, durable export | audit completeness/immutability tests | Infrastructure log admin |
| TM-I-001 | Disclosure | IDOR/query/RLS error leaks tenant data | Critical | policies + RLS + storage auth + generic errors | exhaustive two-tenant matrix | Constraint/timing covert channels |
| TM-I-002 | Disclosure | Secrets leak through source, frontend, logs, URLs, builds or crash reports | Critical | secret manager, redaction, scans, no browser token storage | SEC-ACC-013 | Infrastructure/key store compromise |
| TM-I-003 | Disclosure | Object URL/storage key exposes media across tenants/devices | High | private storage, server auth, short object-scoped URLs if used | storage/download/cross-tenant tests | Authorized recipient can copy visible media |
| TM-I-004 | Disclosure | Cached content/device key is extracted physically | Critical | managed appliance, permissions, optional hardware profile | physical inspection | Accepted baseline limitation |
| TM-D-001 | Denial | Login/MFA/enrollment guessing exhausts service or locks users | High | layered rate limits, progressive delay, monitoring | abuse/load tests | Upstream DDoS still needed |
| TM-D-002 | Denial | Upload/parser/media bomb exhausts CPU/memory/disk | High | streaming quotas, sandbox/no network, resource/time/output limits | hostile corpus/quota tests | Parser zero-day/resource edge cases |
| TM-D-003 | Denial | Fleet heartbeat retry storm overloads API/database | High | jitter/backoff, stateless validation, aggregation, capacity monitoring | load/fault tests | Deployment capacity limits |
| TM-D-004 | Denial | Backup/restore is absent, filtered, inconsistent or lacks keys | Critical | coherent encrypted PITR/media/key backup and isolated restore | SEC-ACC-016 | RPO/RTO depend on resources |
| TM-E-001 | Elevation | Tenant user grants platform/admin privilege | Critical | named policies, issuer grant ceiling, last-admin/role invariants | endpoint-role matrix | Compromised legitimate tenant admin |
| TM-E-002 | Elevation | Runtime/migration/background principal bypasses RLS | Critical | separated roles, not owner/BYPASSRLS, transaction-local tenant, catalog CI | SEC-ACC-003/004 | Infrastructure DB owner |
| TM-E-003 | Elevation | Browser/player escapes to local agent, filesystem or shell | High | loopback narrow API, CSP, Chromium sandbox/non-root, no remote debug/URL fetch | kiosk escape/port/files tests | Browser/OS zero-day or root |
| TM-SC-001 | Supply chain | Dependency/container/build credential compromise | Critical | lockfiles, scans/SBOM, protected CI, signed promoted artifact | SEC-ACC-013/014 and provenance | Trusted upstream compromise |
| TM-A-001 | Abuse | Platform/support or tenant administrator intentionally misuses access | High | least privilege, no implicit content access, step-up, reason/audit/alerts | permission/audit review | Authorized malicious actor |

## 7. Physical/root-access limitation

> The baseline platform cannot enforce licensing or media confidentiality against a person with physical access or root control of a Raspberry Pi. Such an attacker can copy software-stored device keys and cached content, clone an SD-card image, alter the agent/player or Chromium, restore trusted-time state, manipulate the operating system clock, or display cached media outside the official player. Serial numbers and MAC addresses do not prevent this. Backend revocation and anomaly detection take effect only when a device communicates, while a legitimate unmodified offline player may use its already-issued lease for at most 24 hours and never beyond the licence expiry. A root attacker can ignore the player entirely, so this system must not be described as DRM or tamper-proof.

An optional stronger hardware profile would require managed physical controls, a TPM/secure element for non-exportable identity and monotonic state, protected RTC, measured/verified boot, signed read-only OS and remote attestation. This changes hardware cost, provisioning, recovery, and support scope. Cache encryption with a key stored on the same untrusted Pi is not a complete solution.

## 8. Offline revocation limitation

An already issued valid lease is an intentional bounded authorization capability. If a legitimate device goes offline before suspension/revocation, the backend cannot contact it; the unmodified player may continue only until the signed lease expires, at most 24 hours and never beyond actual licence expiry. Online devices observe denial at the next successful heartbeat. Licence transfer therefore waits for old-device relinquishment or the latest source lease expiry before activating the destination.

## 9. Risk acceptance rules

- Cross-tenant disclosure/write, authentication/licence bypass, signing/private-key disclosure, unrecoverable data loss, or an uncontained critical/high dependency vulnerability blocks release.
- Medium residual risk requires named owner, rationale, compensating controls, expiry/review date, and explicit approval.
- Low risk is tracked; it is not silently discarded.
- The physical/root limitation is accepted only for the managed-appliance baseline and must remain visible in user, operations, security, and French-report documentation.
