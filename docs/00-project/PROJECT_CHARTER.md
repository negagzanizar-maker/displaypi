# Project Charter

## Document control

| Field | Value |
|---|---|
| Status | Goal 1 baseline accepted on 2026-08-15; controlled changes require traceability updates |
| Product name | To be supplied; working title: Secure Raspberry Pi Content Platform |
| Project type | Internship project |
| Primary language of technical documentation | English |
| Final internship report language | French |
| Reference report | `IntelliHire_rapport_corrige.docx` |
| Architecture | Secure modular monolith plus a Raspberry Pi device agent/player |

## Problem statement

Organizations need to control what remote Raspberry Pi display devices show while enforcing an expiring per-device licence. Devices may be deployed behind unrelated customer networks and therefore cannot depend on local network discovery or inbound connectivity. Each customer must manage only its own users, devices, licences, and content, without cross-customer disclosure or modification.

## Product vision

Deliver a minimal but production-quality platform in which:

1. a customer administrator securely signs in to a central web application;
2. the administrator manages users, devices, groups, licences, media, playlists, schedules, and assignments within one tenant boundary;
3. a Raspberry Pi is enrolled through a one-time process and receives a unique asymmetric device identity;
4. the Pi agent reports trusted inventory and runtime state over outbound HTTPS;
5. the backend alone evaluates the licence and desired content state;
6. the Pi verifies and caches a signed authorization lease and integrity-protected content;
7. the local kiosk player displays approved video, image, or text while authorization remains valid; and
8. the local kiosk player displays **Not licensed** when authorization is absent, expired, suspended, revoked, invalid, or beyond the permitted offline period.

## Confirmed product principles

- **Backend authority:** React clients, device serial numbers, MAC addresses, and local clocks are inputs, not security authorities.
- **Tenant isolation:** tenant context is derived from an authenticated membership, not accepted from an arbitrary request parameter.
- **Separate identities:** human sessions and device identities use distinct authentication schemes and endpoint surfaces.
- **Outbound-only devices:** Pis initiate HTTPS connections; customer routers require no port forwarding.
- **Least privilege:** users, services, database roles, and Pi processes receive only the permissions they need.
- **Safe offline behavior:** offline playback requires a valid signed lease and stops no later than its bounded expiry.
- **Evidence over claims:** tests, logs, screenshots, and reviewed documents must support completion statements.
- **Living documentation:** design, security, test, and report records change with the implementation.
- **Minimal architecture:** one deployable backend and database are preferred over premature microservices.

## In scope

- Platform and tenant administration
- Invitation-based user lifecycle and role-based access control
- Mandatory MFA for platform and tenant administrators
- Tenant-isolated device, licence, content, playlist, assignment, and audit data
- Device grouping and per-device overrides
- Expiring, suspendable, revocable, and auditable per-device licences
- Audited licence transfer to a replacement device
- Secure content upload, validation, private storage, publication, and delivery
- Ordered looping playlists and time-based scheduling
- Pi enrollment, certificate lifecycle, inventory, heartbeat, status, and desired-state synchronization
- Signed offline licence leases with a maximum 24-hour allowance bounded by the real licence expiry
- Local content caching with integrity checks and atomic activation
- Full-screen React kiosk playback on Raspberry Pi 4/5 running Raspberry Pi OS 64-bit
- TLS, secret management, backup/restore, monitoring, alerting, recovery, and secure update procedures
- Automated and real-hardware verification, including OWASP ASVS 5.0 Level 2 coverage
- Complete technical documentation and an evidence-backed French internship report

## Explicitly out of scope unless later approved

- Public self-registration
- Advertising billing, payment processing, or customer invoicing
- Arbitrary HTML, JavaScript, executables, or remote shell commands as display content
- Browser-based LAN scanning
- Inbound remote administration ports on Raspberry Pis
- A promise of tamper-proof licensing against a person with root and physical control of an ordinary Pi
- Mobile applications
- Microservice decomposition
- Customer-authored plugins running on the device

## Confirmed operating assumptions

- Raspberry Pi 4 and 5 devices use a supported 64-bit Raspberry Pi OS release.
- Devices are managed appliances; customers are not intentionally given root access.
- Devices have periodic internet access and can make outbound TLS connections.
- Offline authorization lasts at most 24 hours and never beyond the licence end timestamp.
- Device licences are primarily associated with the enrolled device and reported Pi serial number; transfer is an explicit audited operation.
- Administrators publish content; devices do not choose unassigned content.
- Production server hosting, email delivery, object storage, malware scanning, final branding, and expected scale remain deployment decisions.

## Success measures

- No tested tenant role can access another tenant's data or assets.
- A revoked or expired online licence stops playback within the defined propagation window.
- An offline device stops playback when its valid lease reaches its bounded expiry.
- Device credentials can be enrolled, rotated, and revoked without using a shared fleet secret.
- Content corruption or partial download never becomes the active playlist.
- A Pi recovers automatically after reboot or player/agent failure.
- A clean environment can be deployed and a clean Pi can be provisioned from documented procedures.
- Every approved requirement has implementation and verification evidence in the traceability matrix.
- The French report contains only functionality and results supported by current evidence.

## Delivery gates

The project follows the twelve goals in the roadmap. Each gate requires implementation, tests, security review, documentation updates, and evidence. Gate approval never follows from elapsed time or code quantity.
