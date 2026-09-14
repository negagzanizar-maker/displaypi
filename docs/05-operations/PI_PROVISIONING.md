# Provisioning Raspberry Pi

## Supported baseline

The release target is Raspberry Pi OS 64-bit on Raspberry Pi 4 or 5. The device needs outbound HTTPS/DNS/NTP, Chromium, `curl`, `systemd`, and the native libraries required by the self-contained .NET arm64 publication. No inbound customer-router rule is needed: the backend connection is initiated by the agent and the player listens only on `127.0.0.1:8787`.

The kiosk launcher detects an available Wayland socket and otherwise uses X11. The installer can bind it to an existing graphical user with `--kiosk-user`; this is the reliable field-test profile for current Raspberry Pi OS desktop images. The separate non-login kiosk identity remains the hardened baseline for an image whose graphical seat permissions are provisioned explicitly.

## Release artifact

For a laptop-and-Pi test on the same Wi-Fi, build the bundle with `scripts/Build-PiFieldTestBundle.ps1` and provision private-LAN HTTPS trust with `scripts/New-FieldTestEnvironment.ps1`.

Build the player first so it is embedded into the agent publication, then create an immutable archive:

```bash
npm --prefix apps/player-web ci
npm --prefix apps/player-web run build
dotnet publish src/DisplayControl.DeviceAgent/DisplayControl.DeviceAgent.csproj \
  --configuration Release --runtime linux-arm64 --self-contained true \
  --output artifacts/pi/linux-arm64
tar -C artifacts/pi/linux-arm64 -czf artifacts/display-control-pi-linux-arm64.tar.gz .
sha256sum artifacts/display-control-pi-linux-arm64.tar.gz
```

The SHA-256 must be transported through an independently trusted release channel. This installer verifies a supplied digest; a production release still needs the separately signed update-metadata workflow recorded in the security requirements.

Publishing fails with an explicit error if the built player `dist/index.html` is absent. Ordinary .NET builds and unit tests do not require that web build.

## First enrollment

1. Create an enrollment code for the expected serial in the tenant dashboard.
2. Place only that code in a root-readable temporary file on the Pi.
3. Copy `deploy/pi` and the verified release archive to the Pi.
4. Run:

```bash
sudo deploy/pi/install.sh \
  --artifact ./display-control-pi-linux-arm64.tar.gz \
  --sha256 '<trusted 64-character digest>' \
  --version '1.0.0' \
  --server 'https://control.example.com' \
  --enrollment-code-file './enrollment-code.txt'
```

For a Raspberry Pi OS desktop field test, add `--kiosk-user "$(whoami)"`. For a private LAN server signed by the generated field-test CA, also add `--server-ca ./field-test-server-ca.crt`. Production uses a publicly trusted server certificate and does not install this local CA.

The installer always creates a non-login `display-control-agent` account and, unless a graphical user is supplied, a separate `display-control-kiosk` account. It creates owner-only state directories, immutable root-owned releases, hardened service units, and an atomic `current` symlink. The agent reads the copied one-time enrollment file and deletes it only after the certificate and pinned licence-verification key have been validated and persisted.

For an already-enrolled Pi, install a new immutable version without creating or copying another enrollment secret. The installer permits omission of `--enrollment-code-file` only when the protected `agent-state.json` and device private key already exist:

```bash
sudo deploy/pi/install.sh \
  --artifact ./display-control-pi-linux-arm64-0.1.7-realtime.tar.gz \
  --sha256 '<trusted 64-character digest>' \
  --version '0.1.7-realtime' \
  --server 'https://control.example.com' \
  --kiosk-user "$(whoami)"
```

Add `--server-ca ./field-test-server-ca.crt` when updating a private-CA field-test installation.

## Validation

Installation explicitly restarts the agent, including when its service was already running. Each immutable release contains a `release-version` marker. Activation waits for HTTP 200 from the health endpoint with the expected `releaseVersion`, checking up to 45 times with two-second pauses and a two-second request timeout. This verifies that the new process is serving requests and its worker has completed a cycle. It does not certify successful content playback or cloud reachability: an authorized offline cycle also counts as worker progress.

If activation fails, the installer restores the previous release symlink and `agent.env`, restarts the prior agent, and exits unsuccessfully. On a first installation with no prior release it stops the failed agent. The kiosk is restarted only after successful agent activation. Rollback does not restore device state, installed service units, or the operating-system trust store; incompatible state migrations require a separate migration and rollback plan.

```bash
systemctl status display-control-agent display-control-kiosk
curl --fail http://127.0.0.1:8787/player/v1/health
journalctl -u display-control-agent -u display-control-kiosk --since today
test ! -e /var/lib/display-control/enrollment-code
ss -lntp | grep 8787
```

The port check must show loopback only. Chromium must run without `--no-sandbox` and without a remote-debugging port. Reboot, network loss/recovery, licence expiry, corrupt-cache, browser restart, Pi 4, and Pi 5 evidence remain physical acceptance gates and cannot be claimed from a workstation test.

## Playback, health, and signing-key rotation

`GET /player/v1/health` returns HTTP 503 before the first successful worker cycle, after a failed cycle, or when progress is older than the configured heartbeat interval plus 120 seconds. Its JSON includes the player state, assembly `version`, and installer `releaseVersion` (null for a development run). Inspect the player state separately when diagnosing missing content.

The browser reports playback through same-origin `POST /player/v1/playback-report`. Reports must match the currently authorized manifest, version, and content identifier. Accepted error codes are `media_error` and `media_stalled`; a successful `playing` report clears the error. The agent includes this safe reason code in its next heartbeat. These reports indicate browser observations, not proof that a physical display is working.

An unchanged desired-state identity and manifest hash renew authorization without resetting playback or rehashing all cached assets. While replacement content downloads, the old manifest can continue only until its original authorization expires. A synchronization attempt is bounded to 30 seconds, including response-body reads; partial downloads can resume on the next cycle.

After enrollment, the agent maintains an outbound mTLS-authenticated server-sent event stream at `GET /device/v1/state-changes`. Its messages contain no content or licence authority: they only wake the existing heartbeat, signed-lease, manifest-validation, and cache pipeline. Notifications are coalesced while synchronization is active, and the normal heartbeat remains enabled for reconnects, dropped events, backend restarts, and offline devices. The local kiosk checks the agent's authoritative player state every second, so a completed synchronization switches presentation without refreshing Chromium.

The authenticated HTTPS heartbeat can supply a `LicenseVerificationKeys` trust set containing at most four ES256/P-256 public keys. The agent rejects duplicate identifiers, invalid curves, and identifiers that do not match the first 16 bytes of SHA-256 over SPKI. It selects the key named by the lease, validates the lease, and persists that key with the authorized state. A legacy response without this field retains the existing pin. Offline operation cannot change trust. On the server, configure retiring public keys through `Security:LicenseSigningKey:VerificationPublicKeyPaths` alongside the current signing key; retain old verification keys through the outstanding lease window. The authenticated server TLS connection is the trust boundary for this rotation.

Workstation unit tests cover playback continuity, authorization expiry, real-time wake-up coalescing, health freshness, report validation, and malformed rotation trust sets. Installer syntax has been checked, but service activation, rollback, and the hardware acceptance scenarios above still require execution on the target Pi.

## Remote-access baseline

Disable VNC and unused remote services. If SSH is operationally required, use key-only authentication, an allowlisted management path/firewall, a separate named administrator account, and retained authentication logs. Application users and device certificates must never be reused as operating-system credentials.
