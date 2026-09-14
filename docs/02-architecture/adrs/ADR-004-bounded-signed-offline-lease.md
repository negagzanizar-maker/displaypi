# ADR-004 — Device-bound signed lease with bounded offline operation

- Status: Accepted
- Date: 2026-08-15

## Context

Pis must continue during temporary internet loss, while licences expire and may be suspended/revoked. The local browser and wall clock are not trustworthy entitlement authorities.

## Decision

- Backend evaluates tenant, device, certificate, serial anomaly, licence and desired state using server UTC.
- It issues a fixed-schema ES256 JWS lease bound to tenant, device, credential, licence and exact manifest hash.
- Lease expiry is the earlier of actual licence expiry and issuance plus 24 hours. There is no hidden grace period.
- The C# agent verifies the lease and maintains authenticated high-water/monotonic trusted time; React cannot override it.
- Invalid/uncertain authorization fails closed to **Not licensed**.
- A legitimate disconnected device may retain authorization only through its already-signed lease.
- Licence transfer stops new source leases and delays destination activation until source relinquishment or latest source-lease expiry.

## Consequences

- Online revocation propagates at heartbeat; offline revocation cannot invalidate a previously issued lease immediately.
- Root/physical control of a normal Pi can bypass software enforcement. The product is not DRM or tamper-proof.
- Lease/signing key rotation, golden test vectors, clock injection and real-Pi expiry tests are mandatory.
