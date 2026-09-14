# Dependency and Tool Register

## Policy

Dependencies are pinned in `Directory.Packages.props`, project lock files, workspace `package.json` files, `package-lock.json`, `global.json`, and container tags. New frameworks, hosted services, MCPs, scanners, or generators must have a recorded purpose, licence, trust/permission review, and removal path before adoption.

## Approved foundation

| Component | Version/bound | Purpose | Licence/source | Security note |
|---|---:|---|---|---|
| .NET SDK | 10.0.203 | Build API, libraries, agent and tests | MIT; Microsoft | Exact SDK pin in `global.json` |
| ASP.NET Core runtime packages | 10.0.7 | Web API/hosting/OpenAPI/test host | MIT; Microsoft | Patch update reviewed as one set |
| Microsoft.OpenApi | 2.11.0 | OpenAPI object model used transitively by ASP.NET | MIT; Microsoft | Centrally pinned above vulnerable 2.0.0; patched line begins at 2.7.5 |
| Entity Framework Core / `dotnet-ef` | 10.0.7 | Data model and reviewed SQL Server migrations | MIT; Microsoft | Tool manifest and central NuGet pins |
| Microsoft EF Core SQL Server / SqlClient | 10.0.7 / 6.1.3 | SQL Server 2022 persistence and connectivity | MIT; Microsoft | Runtime provider; encrypted production transport required |
| React / React DOM | 19.2.8 | Admin and player UI | MIT; npm/Meta | No auth tokens in browser storage |
| Vite | 8.2.1 | Web build/dev server | MIT; npm/Vite | Dev server is not production exposure |
| TypeScript | 6.0.2 | Static web typing | Apache-2.0; npm/Microsoft | Strict project configuration |
| oxlint | 1.75.0 | Fast web linting | MIT; npm/Oxc | Development-only executable |
| Vitest | 4.1.10 | Web unit/component tests | MIT; npm/Vitest | jsdom is not browser E2E evidence |
| jsdom | 29.1.1 | DOM implementation for component tests | MIT; npm/jsdom | Version 30 excludes installed odd-numbered Node 25; CI remains on Node 24 LTS |
| Testing Library | 16.3.2 | React behavior tests | MIT; npm | Tests observable behavior |
| Playwright Test | 1.61.1 | Real-browser administration and kiosk tests | Apache-2.0; Microsoft | Uses installed Chrome locally when available; CI installs managed Chromium explicitly |
| axe-core Playwright | 4.12.1 | Automated WCAG A/AA checks in browser scenarios | MPL-2.0; Deque Systems | Automation covers only rendered states under test and does not replace manual accessibility review |
| xUnit | 2.9.3 | .NET tests | Apache-2.0; NuGet | Test-only |
| Testcontainers MsSql | 4.14.0 | Real database boundary tests | MIT; NuGet | Docker access only in test environment |
| SQL Server image | 2022-CU16-ubuntu-22.04 | Local/integration database | Microsoft container EULA | Loopback port; local password required |
| ClamAV image | 1.4.5-debian13-slim | Fail-closed private media malware scan | GPL-2.0; official ClamAV image | Exact LTS tag; signatures persist in a private volume; scanner outage quarantines content |

The register records the baseline, not a vulnerability guarantee. CI will add lock verification, SBOM, dependency and container scanning before release gates.
