# DisplayPi

This repository contains a multi-tenant platform for managing Raspberry Pi display devices, expiring per-device licences, and centrally assigned video, image, and text content.

The application includes invitation/MFA administration, tenant data isolation, device mTLS enrollment and rotation, expiring licences and bounded signed leases, private scanned media, direct content publication, immutable playlists/manifests, verified Pi caching, local kiosk playback, and automated browser checks.

## Confirmed outcome

- Customer organizations and their users are isolated from one another.
- Administrators manage users, roles, Raspberry Pis, device groups, expiring licences, playlists, schedules, assignments, and content.
- Raspberry Pi 4/5 devices run a restricted C# agent and a React player in Chromium kiosk mode.
- A device connects to the public backend using outbound HTTPS; no inbound customer-network access is required.
- A device reports its name, serial number, network information, agent version, heartbeat, and playback state.
- A licensed device displays only its authorized content.
- An unlicensed device displays **Not licensed**.
- A signed offline lease may authorize cached playback for at most 24 hours and never beyond the actual licence expiry.
- Security, automated tests, real-device testing, and operational documentation are part of the delivery baseline.

## Approved technology direction

- React 19.2, TypeScript, and Vite 8
- ASP.NET Core / .NET 10 LTS and Entity Framework Core
- C# .NET Worker Service on Raspberry Pi OS 64-bit
- SQL Server 2022 with tenant-isolation security policies for tenant-owned data
- Chromium kiosk mode on Raspberry Pi 4/5
- Private media storage behind an abstraction; the implemented filesystem profile is single-writer and a multi-node provider remains a deployment decision
- Containerized development and production deployment where appropriate

Exact patch versions are pinned in the package lockfiles, central package configuration, and `global.json`.

## Documentation map

- [Project charter](docs/00-project/PROJECT_CHARTER.md)
- [Requirements](docs/01-requirements/REQUIREMENTS.md)
- [Detailed security requirements](docs/01-requirements/SECURITY_REQUIREMENTS.md)
- [Actors, permissions, and use cases](docs/01-requirements/ACTORS_AND_USE_CASES.md)
- [Acceptance criteria](docs/01-requirements/ACCEPTANCE_CRITERIA.md)
- [Architecture](docs/02-architecture/ARCHITECTURE.md)
- [Data model](docs/02-architecture/DATA_MODEL.md)
- [API outline](docs/02-architecture/API_CONTRACT.md)
- [Threat model](docs/02-architecture/THREAT_MODEL.md)
- [Test strategy](docs/03-testing/TEST_STRATEGY.md)
- [Development environment](docs/05-operations/DEVELOPMENT.md)
- [Database security baseline](docs/05-operations/DATABASE_SECURITY.md)
- [Raspberry Pi provisioning](docs/05-operations/PI_PROVISIONING.md)
- [Single-node production deployment baseline](deploy/production/README.md)
- [Dependency register](docs/00-project/DEPENDENCIES.md)

## Repository layout

| Path | Responsibility |
|---|---|
| `apps/admin-web` | Central React administration application |
| `apps/player-web` | Fail-closed React kiosk player |
| `src/DisplayControl.Api` | ASP.NET Core human/device control-plane host |
| `src/DisplayControl.Application` | Use cases and ports |
| `src/DisplayControl.Domain` | Security-critical domain rules and state |
| `src/DisplayControl.Infrastructure` | SQL Server, storage, messaging and external adapters |
| `src/DisplayControl.DeviceAgent` | Restricted Raspberry Pi worker and local player authority |
| `tests` | .NET unit/integration/agent tests and Playwright browser/accessibility tests |
| `docs` | Requirements, architecture, testing strategy, and operations |

See the [development guide](docs/05-operations/DEVELOPMENT.md) for local setup. Never commit `.env`, credentials, device keys, real identifiers, or customer media.

## Universal definition of done

A release is complete only when its requirements are implemented, relevant tests pass, security and failure cases are verified, and the living technical documentation is updated. A successful build alone is never sufficient proof of completion.
