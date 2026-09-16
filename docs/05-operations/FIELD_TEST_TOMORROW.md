# Raspberry Pi field test — quick runbook

This runbook prepares one Windows laptop as the temporary HTTPS server and one Raspberry Pi 4/5 as the screen. Complete the **before leaving** section today. Generate the LAN certificate only after the laptop joins tomorrow's Wi-Fi, because its IPv4 address may change.

## Before leaving

### Laptop checklist

- Docker Desktop is installed and can start Linux containers.
- .NET SDK `10.0.203`, Node.js 24/25, npm 11 and Git are installed.
- The repository and `node_modules` are present on the laptop.
- The laptop power adapter is packed.

Build the self-contained Raspberry Pi archive:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build-PiFieldTestBundle.ps1
```

The command creates the ignored directory `artifacts/pi-field-test` containing:

- `display-control-pi-linux-arm64-0.1.8-final.tar.gz`;
- its `.sha256` file;
- the `deploy-pi` installer directory.

The first build needs Internet access once to download the pinned .NET `linux-arm64` runtime pack.

### Raspberry Pi checklist

- Raspberry Pi 4 or 5 with 64-bit Raspberry Pi OS and a desktop session.
- Chromium, `curl`, `openssl`, `tar` and `ca-certificates` installed.
- SSH enabled temporarily or a USB drive available for copying the bundle.
- Pi power supply, HDMI cable and display packed.
- Know the desktop username created during Raspberry Pi OS setup.

Check the architecture before leaving if the Pi is available:

```bash
uname -m
command -v chromium || command -v chromium-browser
```

The architecture must be `aarch64`.

## At the internship — laptop server

### 1. Join the network

Connect the laptop and Pi to the same Wi-Fi. Find the laptop's Wi-Fi IPv4 address:

```powershell
Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object { $_.IPAddress -notlike '127.*' -and $_.AddressState -eq 'Preferred' } |
  Select-Object InterfaceAlias,IPAddress
```

Use the address belonging to Wi-Fi, for example `192.168.1.25`. If the company Wi-Fi isolates clients, use an approved phone hotspot or private test router; the Pi must be able to reach the laptop.

### 2. Generate the field-test environment

Run once, replacing the example address:

The existing `.data/field-test` directory contains older IP-bound TLS material and must not be reused.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/New-FieldTestEnvironment.ps1 `
  -ServerHost 192.168.1.25 `
  -OutputDirectory .data/field-test-final
```

Copy the one-time platform bootstrap token printed by the command. It is intentionally not written to a normal project file.

Trust the generated local CA for the current Windows user:

```powershell
Import-Certificate `
  -FilePath .data/field-test-final/field-test-server-ca.crt `
  -CertStoreLocation Cert:\CurrentUser\Root
```

Allow inbound TCP port `7443` on the laptop's **Private** firewall profile from an elevated PowerShell window:

```powershell
New-NetFirewallRule -DisplayName 'Display Control field test' `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7443 -Profile Private
```

### 3. Start the server

Start Docker Desktop. Stop the older auto-restarting development stack first so its loopback SQL Server and ClamAV ports do not conflict, then start the fresh server:

```powershell
docker compose down
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .data/field-test-final/start-field-test-server.ps1
```

The launcher starts SQL Server 2022 and ClamAV, applies the SQL Server migrations, builds the administration UI, and serves the API over HTTPS. The generated Development environment uses email-and-password sign-in without an MFA prompt. Keep this terminal open. Verify from the laptop and Pi:

```text
https://192.168.1.25:7443/_health/live
```

### 4. Create the first administrator

In another laptop terminal:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/New-PlatformAdministrator.ps1 `
  -ApiBaseUri https://192.168.1.25:7443 `
  -Email you@example.com `
  -DisplayName 'Your name'
```

Enter a strong password and the copied bootstrap token when prompted. Then open `https://192.168.1.25:7443`, sign in, create a customer, copy its one-use invitation URL, sign out, accept the invitation, and sign in as the customer administrator. The generated Development field-test profile deliberately uses password-only sign-in; production keeps MFA required.

## At the internship — Raspberry Pi

### 1. Create and copy the enrollment material

From the customer dashboard, create an enrollment code. For the first field test, the expected serial may be left empty; bind it explicitly after confirming the Pi serial.

Create `enrollment-code.txt` on the Pi containing only that code. Copy these items to one Pi directory using SSH or USB:

- the `.tar.gz` release archive;
- its `.sha256` file;
- `deploy-pi`;
- `.data/field-test-final/field-test-server-ca.crt`;
- `enrollment-code.txt`.

### 2. Install

On the Pi, replace the server address and desktop username. Read the digest from the `.sha256` file on the laptop.

```bash
sudo ./deploy-pi/install.sh \
  --artifact ./display-control-pi-linux-arm64-0.1.8-final.tar.gz \
  --sha256 'PASTE_THE_64_CHARACTER_SHA256' \
  --version '0.1.8-final' \
  --server 'https://192.168.1.25:7443' \
  --server-ca './field-test-server-ca.crt' \
  --enrollment-code-file './enrollment-code.txt' \
  --kiosk-user "$(whoami)"
```

The installer trusts only the supplied field-test CA, installs the agent, attaches Chromium to the current Wayland/X11 desktop user, starts both services, and deletes the copied enrollment secret after successful enrollment.

### 3. Verify

```bash
systemctl status display-control-agent display-control-kiosk --no-pager
curl --fail http://127.0.0.1:8787/player/v1/health
journalctl -u display-control-agent -u display-control-kiosk -n 100 --no-pager
test ! -e /var/lib/display-control/enrollment-code
```

If the kiosk starts before the desktop session, log into the Pi desktop and run:

```bash
sudo systemctl restart display-control-kiosk
```

## Functional demonstration

1. Confirm that the Pi appears online with serial, network and disk inventory.
2. Create a licence for the Pi.
3. Upload a small PNG/JPEG, short H.264/AAC MP4, or UTF-8 text file.
4. Confirm the clean scan made the content usable automatically, create and publish a playlist, then assign it to the Pi.
5. Confirm synchronization starts essentially immediately and playback changes without refreshing Chromium.
6. Suspend or revoke the licence and confirm an online Pi promptly displays **Not licensed**.
7. Reactivate it and confirm playback resumes.
8. Disconnect Wi-Fi briefly and confirm cached playback remains bounded by the signed offline lease.
9. Reboot the Pi and confirm the services return automatically.

Save screenshots and the two service journals as internship evidence.

## Fast diagnosis

| Symptom | Check |
|---|---|
| Pi cannot open laptop health URL | Same subnet, Windows network set to Private, firewall rule present, no Wi-Fi client isolation |
| Agent says TLS/certificate error | Correct laptop IP used when generating the environment; correct CA supplied to installer; laptop clock and Pi clock synchronized |
| Enrollment stays pending | Enrollment code copied without spaces/newline corruption; code not expired; server terminal and agent journal |
| Player health works but no Chromium window | Desktop user passed with `--kiosk-user`; log into desktop; restart kiosk; inspect Wayland/X11 variables in the journal |
| Content stays synchronizing | ClamAV healthy, content usable, playlist published and assigned, sufficient free disk; inspect the agent journal for the state-change stream connection |
| Video is black or slow | First prove PNG/text; then test H.264/AAC and inspect Chromium/GPU support on the actual Pi image |

This is a controlled field-test setup, not a production deployment. Remove the temporary Windows firewall rule and local CA after the test if they are no longer needed.

## Optional virtual test without a Raspberry Pi

The real Windows-hosted agent can represent a virtual device before the physical test. It reports the laptop hostname, IP and MAC addresses and uses an explicit development serial number. Create a normal one-use enrollment code in the customer dashboard, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Start-VirtualDevice.ps1 `
  -ApiBaseUri https://192.168.1.25:7443 `
  -SerialNumber VIRTUALPI0001 `
  -DisableServerCertificateRevocationCheck
```

Enter the enrollment code when prompted. The last switch is only for a private development CA without an online revocation service; certificate-chain and hostname validation remain enabled. The administration dashboard then exercises real enrollment, inventory, mTLS heartbeats, licensing, content synchronization and playback at `http://localhost:8787`. This does not validate ARM64, systemd, HDMI, Chromium kiosk attachment or Raspberry Pi hardware acceleration.
