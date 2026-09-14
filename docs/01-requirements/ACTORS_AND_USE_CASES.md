# Actors, Permissions, and Use Cases

## Actor model

### Human actors

- **Platform Administrator** manages tenant lifecycle and platform operations. This role does not receive routine access to tenant content.
- **Tenant Administrator** manages memberships, roles, devices, groups, licences, content, assignments, and tenant audit history.
- **Content Manager** manages content, playlists, schedules, assignments, and may read device/licence status needed for those tasks.
- **Viewer** has read-only access to authorized tenant dashboards and status information.

In v1, a customer user belongs to exactly one tenant. Platform accounts use a separate platform scope and do not also act as customer accounts. This removes tenant-selection ambiguity; future multi-tenant membership would require an ADR and renewed isolation tests.

### Machine actors

- **Enrolling Device** has only a short-lived one-use enrollment capability.
- **Enrolled Device** authenticates with its own asymmetric credential and accesses only its inventory, heartbeat, lease, manifest, and assigned asset operations.
- **Background Worker** performs narrowly scoped server jobs such as media scanning, cleanup, notifications, and status calculation.
- **Database Migration Principal** owns schema changes and is distinct from the runtime database principal.

## Permission matrix

Legend: `M` manage, `R` read, `S` self/device-scoped, `-` denied.

| Capability | Platform Admin | Tenant Admin | Content Manager | Viewer | Device |
|---|---:|---:|---:|---:|---:|
| Create/suspend tenant | M | - | - | - | - |
| View tenant operational metadata | R | R | - | - | - |
| View tenant media/content | - by default | R | R | R if granted | assigned only |
| Invite/remove tenant members | - | M | - | - | - |
| Assign tenant roles | - | M | - | - | - |
| Manage devices and groups | - | M | R | R | S |
| Create enrollment code | - | M | - | - | - |
| Manage licences and transfers | - | M | R | R | own status only |
| Upload/archive content | - | M | M | R | assigned only |
| Manage playlists/schedules | - | M | M | R | assigned only |
| Publish assignments | - | M | M | R | assigned only |
| View tenant audit events | platform events only | R | own/content events | - | - |
| Rotate own device credential | - | - | - | - | S |
| Send heartbeat/player state | - | - | - | - | S |

Authorization is enforced by backend policies and tenant-aware database access. Frontend visibility is usability, not a security control.

## Primary use cases

### UC-IAM-01 — Accept invitation and establish account

1. A Tenant Administrator creates an invitation for a verified email and role.
2. The system sends a single-use expiring link without exposing whether unrelated accounts exist.
3. The invitee establishes or links an account and verifies the email.
4. Administrative roles enroll TOTP MFA and receive one-time recovery codes.
5. The system records the membership and audit event.

### UC-IAM-02 — Sign in and select tenant

1. The user submits credentials over HTTPS.
2. Rate limits, lockout, and generic failure responses apply.
3. Required MFA completes before an administrative session is issued.
4. The server issues a Secure, HttpOnly, SameSite cookie and rotates the session identifier.
5. If the user has multiple memberships, an authorized tenant is selected.

### UC-DEV-01 — Enroll a Raspberry Pi

1. A Tenant Administrator creates a one-use enrollment code with a short expiry.
2. The agent generates a non-exported private key and certificate signing request locally.
3. The agent sends the code, public-key request, and inventory to the enrollment endpoint over validated TLS.
4. The backend atomically consumes the hashed code, checks conflicts, creates the tenant device, and issues a client certificate.
5. The agent stores the private key/certificate with owner-only permissions and immediately authenticates a heartbeat.
6. Enrollment success and failure are audited without logging secrets.

### UC-DEV-02 — Maintain online state

1. The enrolled device periodically sends a mutually authenticated heartbeat.
2. The heartbeat includes inventory changes, agent/player version, local interfaces, applied manifest, disk capacity, and playback health.
3. The backend records the latest state and server-observed public address.
4. The response includes trusted server time, desired-state version, licence lease, and retry guidance.
5. The dashboard derives Online, Degraded, or Offline from configurable heartbeat thresholds.

### UC-LIC-01 — Create or renew an expiring licence

1. A Tenant Administrator selects an enrolled device and UTC start/end instants.
2. The backend rejects invalid ranges, overlap conflicts, unauthorized devices, and already-used transfer operations.
3. The licence becomes active only within its valid interval and when not suspended or revoked.
4. The action is audited and affects the next device authorization response.

### UC-LIC-02 — Evaluate online/offline playback authorization

1. The backend evaluates tenant, device, certificate, licence state, schedule, and assigned manifest.
2. If allowed, it signs a device- and credential-bound lease whose expiry is the earlier of licence expiry and server time plus 24 hours.
3. The agent verifies signature, key identifier, device/tenant binding, validity, and trusted time.
4. Cached playback is enabled only while the lease and referenced manifest remain valid.
5. Invalid or absent authorization produces the local **Not licensed** state.

### UC-LIC-03 — Transfer a licence

1. A Tenant Administrator selects the old and replacement enrolled devices.
2. The backend validates both devices belong to the same tenant and requires a reason.
3. The source stops receiving new leases. The destination remains pending until the source explicitly relinquishes authorization or its latest outstanding lease expires.
4. The replacement binding then activates atomically and both transitions plus the actor are preserved in append-only audit events.

### UC-CNT-01 — Upload and approve content

1. An authorized user declares the intended type and uploads within configured limits.
2. The server streams to quarantine using a random internal identifier.
3. Extension, media signature, MIME, size, and safe decoding checks run; malware scanning must pass.
4. A SHA-256 digest and media metadata are calculated.
5. Only an approved immutable asset version enters private active storage.
6. Rejection reasons are safe for users and detailed only in protected logs.

### UC-CNT-02 — Publish a playlist assignment

1. An authorized user creates an ordered playlist using approved assets.
2. Durations for images/text, video playback rules, optional UTC start/end window, and target device/group are validated.
3. A direct-device assignment overrides a group assignment; within one target class the higher priority wins, and equal-priority ambiguity is rejected.
4. Publishing creates an immutable desired-state version and audit event.
5. Devices receive the new manifest summary on heartbeat and synchronize missing assets.

### UC-PLY-01 — Activate synchronized content

1. The agent downloads missing assets through authenticated, device-scoped endpoints.
2. Every object is size- and hash-verified in a staging location.
3. The complete manifest is validated before an atomic active-version switch.
4. The player reads only the active local manifest over loopback.
5. An interrupted or corrupt update leaves the last valid authorized version intact.

### UC-PLY-02 — Boot and recover

1. `systemd` starts the restricted agent.
2. The agent exposes the packaged player only on loopback.
3. Chromium starts in kiosk mode after the local health endpoint is ready.
4. Cached authorized content or **Not licensed** is visible without an administrative desktop.
5. Agent/player crashes trigger bounded automatic restarts and health reporting.

### UC-PLY-03 — Handle a licensed device without assigned content

1. The agent verifies a valid lease but resolves no active desired-state assignment.
2. It exposes a distinct `LicensedNoContent` state to the local player.
3. The player displays **No content assigned**, not **Not licensed**, so licensing and content configuration remain distinguishable.

## Critical state models

### Device operational state

`PendingEnrollment -> Active -> Suspended -> Retired`

Online health is orthogonal: `Online`, `Degraded`, or `Offline`. A retired or suspended device cannot obtain a valid lease even if it still presents an otherwise valid certificate.

### Licence state

The effective state is derived from timestamps and explicit controls:

- `Scheduled`: valid start is in the future.
- `Active`: current trusted time is inside the interval and no suspension/revocation exists.
- `Expired`: current trusted time is at or after the exclusive end instant.
- `Suspended`: temporarily disabled by an authorized action.
- `Revoked`: permanently disabled.
- `Transferred`: binding closed as part of an audited replacement workflow.

### Content state

`Quarantined -> Scanning -> Approved -> Published -> Archived`, with `Rejected` reachable from validation/scanning. Rejected or quarantined assets are never returned to devices.

### Desired-state version

`Draft -> Published -> Superseded`. Published versions are immutable; editing produces a new version.

## Business invariants

- Tenant-owned references cannot cross tenant boundaries, including indirect joins and object-storage keys.
- A membership role is scoped to its tenant.
- A v1 customer user has no more than one active customer membership.
- An enrollment code is stored as a hash, expires, is single-use, and cannot be recovered after creation.
- Every enrolled device has a unique active certificate identity; fleet-wide shared secrets are forbidden.
- Reported serial/MAC/IP information cannot authenticate a device.
- One active device record may claim a given Pi serial; a conflict enters an administrative review path.
- Licence intervals use UTC and an exclusive end instant.
- No lease outlives the licence, device authorization, or 24-hour offline maximum.
- A licence transfer does not authorize the destination until the source has relinquished its lease or the latest source lease has expired.
- The local React player cannot override an agent authorization decision.
- Published manifests and asset versions are immutable.
- Direct-device assignments override group assignments; higher priority wins within a class and an equal-priority collision is rejected.
- Content is private and authorization is checked before every metadata or binary response.
- Arbitrary HTML, SVG script, JavaScript, and executable uploads are forbidden.
- Audit events record actor, tenant, action, target, timestamp, outcome, correlation identifier, and relevant reason without secrets.
- Destructive administrative actions require explicit intent and preserve required audit/history records.
