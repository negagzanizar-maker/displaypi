# Development Environment

## Prerequisites

- .NET SDK 10.0.203 exactly
- Node.js 24 or 25 and npm 11
- Docker Engine/Desktop with Compose v2
- Git
- PowerShell 5.1 or later for local scripts; CI uses PowerShell 7

## First-time setup

1. Copy `.env.example` to `.env` and replace both SQL Server passwords with different long, random, local-only values.
2. Run `npm ci`; the repository includes its lockfile.
3. Run `dotnet restore --locked-mode`; project lockfiles are included.
4. Run `./scripts/Initialize-SqlServerDemo.ps1`. It starts SQL Server 2022, reuses or starts ClamAV, applies SQL Server migrations, and provisions the non-owner `display_app` login. It is safe to run again.
5. Provide `ConnectionStrings__Database` and `Database__Provider=SqlServer`; absolute dedicated paths for `Security__DataProtectionKeyDirectory` and `ContentStorage__RootDirectory`; `Security__TokenDigestPepperBase64` containing at least 32 random bytes; an ECDSA device-CA PFX path/password; and a separate encrypted ECDSA P-256 licence-signing key path/password. The API deliberately refuses to start outside `Testing` when a required security value is missing or invalid.
6. For the prepared local demonstration, run `./scripts/Start-SqlServerDemo.ps1`. For another development profile, run `dotnet run --project src/DisplayControl.Api` after supplying the same settings.
7. Run the administration UI with `npm run dev:admin`.
8. Run the kiosk UI with `npm run dev:player`; development rendering defaults to the safe **Not licensed** state.

The dependency-free filesystem object-store adapter requires a dedicated absolute directory with permissions limited to the API identity. It is also suitable only for an explicitly approved single-writer deployment on an encrypted private volume. See [Object storage](OBJECT_STORAGE.md) before selecting a production topology.

The required environment keys are:

```text
ConnectionStrings__Database
Database__Provider=SqlServer
Security__DataProtectionKeyDirectory
Security__TokenDigestPepperBase64
Security__DeviceCertificateAuthority__PfxPath
Security__DeviceCertificateAuthority__PfxPassword
Security__DeviceCertificateAuthority__IssuedLifetimeDays=90
Security__LicenseSigningKey__PrivateKeyPath
Security__LicenseSigningKey__Password
ContentStorage__RootDirectory=C:\DisplayControl\Content
ContentStorage__MaximumObjectBytes=268435456
ContentScanning__ClamAv__Host=127.0.0.1
ContentScanning__ClamAv__Port=3310
DeviceProtocol__OfflineAllowanceHours=24
```

`scripts/New-DevelopmentSecurityMaterial.ps1` creates local-only CA/signing material and an ignored launcher beneath `.data`; it does not create production keys. The generated Development launcher uses password-only human sign-in for a simpler local demonstration. MFA remains the default, and password-only mode is rejected outside Development or Testing.

The checked-in Windows launch profiles use `C:\DisplayControl\Content` for local media. Override `ContentStorage__RootDirectory` with another absolute path on Linux, Raspberry Pi, containers, or deployments; only storage keys and metadata are stored in SQL Server.

Do not commit `.env`, private keys, certificates, tokens, real customer data, or real device identifiers.

## Verification

Run `./scripts/Publish-Applications.ps1` to build both web applications, publish both .NET hosts, and verify their web entry points under `artifacts/publish`. Before manually publishing either .NET host, run `npm run build`. Publishing rejects a missing frontend entry point. CI also verifies `wwwroot/index.html` in each publish directory. Rebuild the frontend after every web source change to avoid packaging stale assets.

For signing-key rotation, set the optional indexed environment values `Security__LicenseSigningKey__VerificationPublicKeyPaths__0` through `__2` to absolute public SPKI PEM paths for retiring or upcoming P-256 keys. The active signing key is always included automatically; additional keys must be distinct and must contain public material only. Deploy updated agents before rotating the active signer. Offline leases retain their original expiry and devices learn the new current key at their next authenticated heartbeat.

Retention now drains up to `Operations__Retention__MaximumBatchesPerRun` batches per table per run (default 100, range 1–1000), each in its own transaction. Size this with `BatchSize` and `IntervalMinutes` against observed row creation rates and monitor the logged removed counts. Repeated full runs indicate possible backlog; these limits do not establish a measured fleet capacity.

```powershell
dotnet build DisplayControl.slnx --locked-mode
dotnet test DisplayControl.slnx --no-build --locked-mode
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npx playwright install chromium
npm run test:e2e
& ./scripts/verify-docs.ps1
```

On a Windows development machine, the Playwright configuration automatically uses an installed Google Chrome when available. Other local environments install the pinned managed Chromium with the command above. CI always installs managed Chromium explicitly. The current browser suite exercises real rendering, CSRF-bearing requests, fail-closed player behavior and axe WCAG A/AA checks with deterministic intercepted API responses; it does not replace the live-backend staging E2E gate.

The SQL Server and later mTLS/storage integration suites require Docker. A passing jsdom or mocked unit test does not replace those boundary tests.
