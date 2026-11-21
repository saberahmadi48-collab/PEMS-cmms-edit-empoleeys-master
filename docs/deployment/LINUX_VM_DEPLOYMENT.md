# PEMS — Linux VM deployment (existing Docker Compose stack)

Target: Ubuntu 24.04 VM `pems-server` (`192.168.1.136`, **DHCP address — reserve it**). The VM runs PEMS as Docker containers; this kit updates **only `pems-web`**
and leaves SQL Server and Caddy alone. The Azure UAT deployment (`.github/workflows/deploy-uat.yml`, `appsettings.UAT.json`) is not touched by this kit.

```
 Windows (VS Code)                         Ubuntu VM  (no internet)
 ───────────────────                       ──────────────────────────────────────────────────────────────
 deploy\deploy.ps1  ──ssh/scp (key)──▶     pems-caddy  :80/:443  (existing, tls internal, host pems.home.arpa)
   Build-Package.ps1  (publish, strip,         │ reverse_proxy
   manifest, sha256)                        pems-web    :8080     (container, managed by deploy/docker/compose.pems-web.yml)
                                              │ network pems-cmms-edit-empoleeys-master_default
                                            pems-sqlserver :1433   (existing, Compose-managed; volume pems-cmms-…_pems-sql-data)
 volumes (external, never removed): pems-uploads · pems-logs · pems-filestore (new) · pems-dpkeys (new, DataProtection keys)
 VM-local state: /opt/pems/deploy-state  (env file 0600, release records, probe, key copies)     /opt/pems/deploy  (the kit)
```

## 0. Supported access method (read this first)

**The only supported URL is `https://pems.home.arpa`** (Caddy, TLS from Caddy's internal CA, port 443).

| Endpoint | Status | Why |
|---|---|---|
| `https://pems.home.arpa` | **Supported (canonical)** | certificate SAN = `DNS:pems.home.arpa`; secure auth cookie works |
| `http://pems.home.arpa/…` and `http://192.168.1.136/…` (port 80) | Redirected (301/308) to the canonical URL | Caddy catch-all in `deploy/caddy/Caddyfile` |
| `https://192.168.1.136` | **Unsupported** | no certificate is issued for the IP (TLS handshake fails); issuing one would need an IP SAN *and* trust on every client |
| `http://192.168.1.136:8080` | **Unsupported – diagnostic only** | plain HTTP. The auth cookie `PEMS.Auth` is `Secure` (by design, outside Development), so a browser never stores it over HTTP ⇒ **login "succeeds" then returns to Login**. This is expected, not a bug; never weaken the cookie to "fix" it. After the loopback change below the port is not reachable from the LAN at all. |
| `127.0.0.1:8080` (on the VM) | Internal / health checks only | used by the deploy kit |

**Login redirect loop – root cause.** Opening the app by `http://<ip>:8080` makes the browser receive a `Secure` cookie over an insecure origin, which it discards; the next request is anonymous and is sent back to `/Account/Login`. Through `https://pems.home.arpa` the cookie is stored (`Secure; HttpOnly; SameSite=Lax`) and the loop does not occur.

**Client prerequisites (per Windows PC – nothing is automatic):**
1. Name resolution: a DNS A record `pems.home.arpa → 192.168.1.136` on the router/DNS server (preferred, covers all clients) **or** the hosts line from step C′ below on each PC.
2. Trust of the Caddy root CA (public certificate only – never copy anything from `/data/caddy/pki/authorities/local/` other than `root.crt`): managed PCs via GPO/Intune (*Trusted Root Certification Authorities*); unmanaged PCs via step D. Leaf certificates are re-issued by Caddy automatically every ~12 h and the intermediate every ~7 days – only the **root (valid to 2036)** must be installed, once.
3. Validate on a client: `nslookup pems.home.arpa` (or `ping`) → `192.168.1.136`; the browser shows a lock without warnings; `curl.exe --ssl-revoke-best-effort https://pems.home.arpa/health` → `200`. (Plain `curl.exe` on Windows reports `CRYPT_E_NO_REVOCATION_CHECK` because Caddy's internal CA publishes no CRL/OCSP endpoint – a schannel strictness, not a trust failure; browsers are unaffected.)
4. Firewall/network boundary: LAN → VM **22, 80, 443 only**. 8080 (after the loopback change) and 1433 (SQL Server) must not be reachable from the LAN; see §6 steps F–G.

## 1. What the kit does

| File | Purpose |
|---|---|
| `deploy/deploy.ps1` | the one Windows entry point (Preflight · Deploy · Status · Smoke · VerifyRuntime · Rollback · Backup · MigrationScript · Migrate · AdoptEnv · PrepareVolumes · SyncKit · Package) |
| `deploy/Build-Package.ps1` | `dotnet publish` (linux-x64) → strips user data, dev/UAT settings, Windows-only DLLs → secret scan → `release-manifest.json` (SHA-256 per file) → archive + `.sha256` |
| `deploy/Convert-EfScript.ps1` | makes EF-generated SQL runnable by `sqlcmd` (raw `UPDATE`/`CHECK` statements wrapped in `EXEC(N'…')`; see §5) |
| `deploy/remote/pems-docker.sh` | VM-side controller (runs as the docker-group user, no root): preflight, adopt-env, prepare-volumes, build, deploy, rollback, db-backup, db-apply … |
| `deploy/docker/Dockerfile.release` | **offline** release image: new publish output laid over the existing base image `pems-web:0fc34ab` (identity-checked by image ID), Linux OCR wiring |
| `deploy/docker/compose.pems-web.yml` | Compose file for `pems-web` only: external volumes/network, env file, healthcheck, `restart: unless-stopped` |
| `deploy/tests/test-pems-docker.sh` + `fakes/` | 55 mock-based checks (fake docker/curl/df); also run by `PEMS.Tests` (`LinuxDeploymentArtifactsTests`) and on the VM itself |
| `tools/PemsLinuxProbe` | diagnostic probe run inside the release image: real OCR extraction and DataProtection decrypt (prints success/failure only) |

Hard guarantees (enforced in code and tests): no `compose down`, no `-v`, no prune/rm/rmi/volume removal, no database change during deploy, no secret printed or placed on a
command line, images are built `--pull=false` from a local base, the package manifest must verify (fails closed), an unhealthy new release is rolled back automatically.

## 2. One-time setup (already done on `pems-server`, 2026-10-09)

1. SSH key `~/.ssh/pems_deploy_ed25519` authorized for `arsin`; host key stored in `known_hosts`; `deploy/targets.local.json` created from `targets.example.json` (git-ignored).
2. `deploy.ps1 -Action AdoptEnv` → `/opt/pems/deploy-state/pems-web.env` (mode 600) created **from the running container's own environment** (secrets never enter the repo or chat).
3. `deploy.ps1 -Action PrepareVolumes -KeysDir <dir>` → created `pems-filestore` and `pems-dpkeys`, seeded `pems-dpkeys` with the **two** DataProtection keys (hash-verified copies of the live key and the key still held by the stopped `pems-web-old` container), recorded their names in `required-dp-keys.txt` (preflight fails if one disappears).
4. `deploy.ps1 -Action Backup` and `-Action Migrate` (see §5) before the first release.

## 3. Routine update

```powershell
.\deploy\deploy.ps1 -Action Package -DryRun     # what would ship (no server contact)
.\deploy\deploy.ps1 -Action Preflight           # read-only checks on the VM
.\deploy\deploy.ps1                             # confirm host → package → upload → verify manifest → offline build → recreate pems-web → health + Caddy → runtime proofs
.\deploy\deploy.ps1 -Action Status
.\deploy\deploy.ps1 -Action Rollback            # recreate pems-web from the recorded previous image (database NOT reverted)
.\deploy\deploy.ps1 -Action Reapply             # recreate pems-web from the SAME image with the kit's Compose file (port/healthcheck changes); auto-restores the old Compose file if unhealthy
.\deploy\deploy.ps1 -Action BootCheck           # read-only reboot-recovery evidence (restart policies, health, SQL, Caddy, ports, obsolete pems.service)
```
Release id = `UTC timestamp-commit[-dirty]`; image `pems-web:rel-<id>`. The previous image/container is always kept: the first release keeps the original hand-started container as
`pems-web-pre-<id>` (stopped), later releases roll back to the previous `pems-web:rel-*` image. Old images are never pruned automatically.

**An update never touches accounts** (owner decision 2026-10-27, SCRUM-410): the kit swaps the container and runs the schema migration only. The application seeds the administrator only on an empty user table, so after any `deploy.ps1` action (also Rollback and Reapply) the administrators keep their passwords. Only a fresh install or an explicit database reset restarts the first-install flow (forced password change). Details and the manual check M-14: [DEPLOYMENT_README.md](DEPLOYMENT_README.md) section 9.

## 4. Persistence (verified)

* `wwwroot/uploads` → volume `pems-uploads`; logs → `pems-logs`; **`filestore` → `pems-filestore`**; **DataProtection keys → `pems-dpkeys`** (`/root/.aspnet/DataProtection-Keys`).
* Verified by recreating the container with marker files: they survived, the 16 existing uploads were intact, and the key ring stayed at 2 keys (none generated, none lost).
* The stored provider credential (`StorageProviders.EncryptedPassword`) was encrypted with key `5cf51e3f-…`, which existed **only** in the stopped `pems-web-old` container — the previously running app could not decrypt it.
  It decrypts now (`verify-runtime` → `DP_DECRYPT=OK`, application name `/app/`). **Do not delete `pems-web-old` until the key copies under `/opt/pems/deploy-state/<timestamp>/` have been backed up off the VM.**
* Back up off-VM (operator): the SQL backups in the SQL container's `/var/opt/mssql/backup`, volumes `pems-uploads`, `pems-filestore`, `pems-dpkeys`, `/opt/pems/deploy-state` (contains secrets, mode 600/700).

## 5. Database migrations (separate and explicit)

Deployment never migrates. Procedure used and verified on 2026-10-09:
1. `deploy.ps1 -Action Backup` — `COPY_ONLY, CHECKSUM` backup + `RESTORE VERIFYONLY`; prove restorability by restoring into a scratch database and running `DBCC CHECKDB` (done: clean, identical row counts), then drop the scratch DB.
2. `deploy.ps1 -Action MigrationScript` → idempotent script plus `…-sqlcmd-safe.sql` (via `Convert-EfScript.ps1`). **Why the converter:** EF puts a migration's raw SQL in the same batch as the `ALTER TABLE … ADD [col]` it depends on, and SQL Server resolves existing-table columns at compile time → `Invalid column name`. `EXEC(N'…')` defers compilation; semantics are identical. Review the diff.
3. `deploy.ps1 -Action Migrate -SqlFile <…-sqlcmd-safe.sql> -ConfirmReviewed` — refuses without a verified backup < 1 h old; runs `sqlcmd -b -I` (abort on error; QUOTED_IDENTIFIER ON for filtered indexes); each migration is its own transaction, so a failure leaves earlier migrations committed and the failing one rolled back.
4. Application rollback does **not** undo schema changes; restore from the verified backup if the schema must be reverted.

## 6. Administrator-only steps (need `sudo` / Windows elevation — not performed by the kit)

| # | Where | Command / action | Why |
|---|---|---|---|
| A | VM | `sudo systemctl disable --now pems.service` | obsolete second deployment: runs the source-tree build as `arsin` with the Windows-style connection string and crash-loops (161 restarts); the application runs in Docker |
| B | VM | `sudo sed -i 's/^192\.168\.1\.109\s\+pems\.home\.arpa/192.168.1.136 pems.home.arpa/' /etc/hosts` | stale mapping to the old VM address |
| C | Router / DNS | DHCP **reservation** for the VM's MAC (or static IP via netplan) + A record `pems.home.arpa → 192.168.1.136` | the VM address is DHCP; clients need the name |
| C′ | Windows client (elevated PowerShell) | `Add-Content C:\Windows\System32\drivers\etc\hosts "192.168.1.136 pems.home.arpa"` | only if the router cannot serve the record |
| D | Windows client (elevated) | `Import-Certificate -FilePath <pems-caddy-root.crt> -CertStoreLocation Cert:\LocalMachine\Root` | Caddy `tls internal` CA (`CN=Caddy Local Authority - 2026 ECC Root`, SHA-256 `23B4A12A…C82540`): without it browsers warn; export with `docker exec pems-caddy cat /data/caddy/pki/authorities/local/root.crt` |
| F | VM (**applied 2026-10-09** with `Reapply`, same image, ~20 s restart) | `.\deploy\deploy.ps1 -Action Reapply` (compose publishes `127.0.0.1:8080:8080`) | stops the LAN from reaching the plain-HTTP app port, removing the "login loop by IP:8080" trap. Rollback = `-Action Rollback` |
| G | VM / owner decision | bind SQL Server's port to loopback (`127.0.0.1:1433:1433` in `/opt/pems/source/PEMS-cmms-edit-empoleeys-master/docker-compose.yml`, `docker compose up -d sqlserver`) **or** firewall 1433 (`sudo ufw allow from <admin-host> to any port 1433`, default deny) | SQL (`sa`) is currently published on all interfaces. Confirm nobody on the LAN needs it (e.g. SSMS) before closing |
| H | VM (no sudo; already applied 2026-10-09) | Caddyfile changes: edit `deploy/caddy/Caddyfile`, copy to `/opt/pems/caddy/Caddyfile` **in place** (`cat new > file`, a bind-mounted single file keeps its inode), `docker exec pems-caddy caddy validate --config /etc/caddy/Caddyfile`, then `docker exec pems-caddy caddy reload --config /etc/caddy/Caddyfile`. Backups: `/opt/pems/caddy/Caddyfile.bak-*` | graceful, no dropped connections |
| E | VM | `sudo reboot` in an agreed window, then `.\deploy\deploy.ps1 -Action Status` | prove start-after-reboot (docker + containerd are enabled; all three containers have `restart: unless-stopped`) |

### 6a. Ordered administrator runbook (do in this order)

| Order | Step | Exact command / action | Verify | If it fails |
|---|---|---|---|---|
| 1 | Reserve the VM's address | Router/DHCP: reservation for the VM's NIC MAC (`ip -br link show eth0` on the VM) → `192.168.1.136` | `ip -4 addr show eth0` unchanged after a lease renewal | keep the hosts-file workaround (step 6) until fixed |
| 2 | Internal DNS | Router/DNS server: **A record `pems.home.arpa → 192.168.1.136`**. Also remove any record pointing at `192.168.1.109` | from any client: `nslookup pems.home.arpa` → `192.168.1.136` | per-PC hosts entry (step 6) |
| 3 | Disable the obsolete unit | on the VM: `sudo systemctl disable --now pems.service` (then optionally `sudo systemctl mask pems.service`). It runs a second ASP.NET process from the source tree with the Windows connection string (517+ restarts) and would start at every boot | `systemctl is-enabled pems.service` → `disabled`/`masked`; `docker ps` unchanged; `.\deploy\deploy.ps1 -Action BootCheck` has no FAIL | `sudo systemctl enable pems.service` restores the previous state (it is useless but harmless) |
| 4 | Fix the VM's stale hosts line | `sudo cp -p /etc/hosts /etc/hosts.bak-$(date +%Y%m%d) && sudo sed -i 's/^192\.168\.1\.109\s\+pems\.home\.arpa/192.168.1.136 pems.home.arpa/' /etc/hosts` | `getent hosts pems.home.arpa` → `192.168.1.136` | `sudo cp -p /etc/hosts.bak-<date> /etc/hosts` |
| 5 | SQL Server port (**applied 2026-10-09**: `127.0.0.1:1433`, SQL healthy after 26 s, web/Caddy untouched). Procedure: | see decision G: edit the `ports:` line of `sqlserver` to `"127.0.0.1:1433:1433"` in `/opt/pems/source/PEMS-cmms-edit-empoleeys-master/docker-compose.yml`, then `docker compose -p pems-cmms-edit-empoleeys-master up -d --no-deps sqlserver` (**SQL restarts, ~1 min outage; `pems-web` reconnects by itself**). Take a backup first (`.\deploy\deploy.ps1 -Action Backup`) | `docker port pems-sqlserver` → `127.0.0.1:1433`; `BootCheck` | restore the previous `ports:` line and run the same `up -d` |
| 6 | Client onboarding | **Managed PCs (domain/Intune):** deploy `pems-caddy-root.crt` to *Trusted Root Certification Authorities* by GPO (*Computer Configuration → Windows Settings → Security Settings → Public Key Policies*) or an Intune *Trusted certificate* profile, and rely on the DNS record from step 2. **Unmanaged PCs:** an administrator runs, elevated: `Import-Certificate -FilePath .\pems-caddy-root.crt -CertStoreLocation Cert:\LocalMachine\Root` (verify the SHA-256 fingerprint first, see step D) and, only if step 2 is not available, `Add-Content C:\Windows\System32\drivers\etc\hosts "192.168.1.136 pems.home.arpa"`. Firefox uses its own store unless *security.enterprise_roots.enabled* is set to true. Phones/tablets: install the root as a user CA profile | `nslookup`/`ping pems.home.arpa`; browser lock icon without warnings | never ask users to click through a warning |
| 7 | Reboot test (authorised window) | `.\deploy\deploy.ps1 -Action BootCheck` (baseline, before) → on the VM `sudo reboot` → wait ~3 min → `.\deploy\deploy.ps1 -Action BootCheck` (after) → browse `https://pems.home.arpa` | every line `[PASS]`; containers show a start time after `uptime -s`; `RestartCount <= 3` | see 6b |

**Do not** run `docker compose up/down` in `/opt/pems/source/PEMS-cmms-edit-empoleeys-master/`: that older Compose file still declares `pems-web` with `8080:8080` (and an `ollama` service) and would recreate the web container on the LAN-visible port. Use `deploy.ps1` only. `Reapply` (`.\deploy\deploy.ps1 -Action Reapply`) recreates `pems-web` from the **same image** with the kit's Compose file (backs up the previous Compose file, health-checks, restores it automatically if the new one is unhealthy).

### 6b. Reboot recovery design

* **Lesson (2026-10-09):** the source Compose file declared no `restart:` for `sqlserver`; the original container had `unless-stopped` only because it was set by hand. Recreating SQL from that file silently produced `restart=no`, which would have left the database down after a reboot. `BootCheck` caught it before the reboot; fixed with `docker update --restart unless-stopped pems-sqlserver` **and** `restart: unless-stopped` in the file. Always run `BootCheck` after recreating any container.

* `docker` and `containerd` are enabled in systemd; `pems-sqlserver`, `pems-web`, `pems-caddy` all use `restart: unless-stopped`, so Docker starts them on boot without any script. **No separate systemd unit is needed** (a second unit that ran `compose up` or `dotnet` would be exactly the competing process the project forbids).
* Start order is not enforced by Compose (the SQL container belongs to a different Compose file than `pems-web`). Resilience comes from: SQL healthcheck, EF `EnableRetryOnFailure`, the web healthcheck `start_period` of 90 s, and the restart policy – if SQL is slower than the app, the app exits and Docker restarts it until SQL answers. `BootCheck` proves the outcome (all healthy, `RestartCount <= 3`, no SQL-readiness lines in the log).
* If `BootCheck` shows a restart loop after a reboot: `docker logs pems-web --tail 50`; if the cause is SQL start-up time, raise the retry window (`sqlOptions.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(15), null)` in `PEMS.Web/Program.cs`) – a code change that needs its own release.
* If a container is missing after boot: `docker ps -a`, then `.\deploy\deploy.ps1 -Action Status`; for `pems-web` only, `.\deploy\deploy.ps1 -Action Reapply` (never `compose down`).

## 7. Linux OCR (fixed and proven)

The Tesseract 5.2.0 .NET wrapper loads `libleptonica-1.82.0.so` / `libtesseract50.so` from `<app>/x64` and imports `libdl`; Ubuntu 24.04 provides `liblept.so.5`, `libtesseract.so.5`, `libdl.so.2`.
`Dockerfile.release` adds the three links and sets `OCR__TessDataPath=/usr/share/tesseract-ocr/5/tessdata`. Proof: `deploy.ps1 -Action VerifyRuntime` extracts `PEMS OCR TEST 12345` (confidence 0.91, `eng` and `fas+eng`) through the wrapper;
a wrong tessdata path fails as it should. (The application's own `TesseractOcrService` swallows exceptions and returns an empty string, so application start-up proves nothing about OCR — always use the probe.)

## 8. Diagnostics

| Symptom | Check |
|---|---|
| Preflight FAIL `base image … tag moved` | `docker image ls --digests pems-web` — the base must be `sha256:c1c22ddc…`; never retag `pems-web:0fc34ab` |
| Preflight FAIL DataProtection key missing | a key file vanished from `pems-dpkeys`; restore from `/opt/pems/deploy-state/<ts>/dp-keys-merged` before deploying |
| `health check FAILED` → rolled back | `docker logs pems-web --tail 100` (the controller prints a redacted tail), SQL health, un-migrated schema |
| `Invalid column name` while migrating | use the `-sqlcmd-safe.sql` output (§5) |
| Login returns to the Login page | you are on `http://…:8080` (or any plain-HTTP URL): use `https://pems.home.arpa`. Also check the browser blocks no cookies for the site and the PC clock is correct |
| Browser warns about the certificate | install the Caddy root CA (step D); confirm the URL host is exactly `pems.home.arpa` (an IP or another name can never match the SAN) |
| `ERR_NAME_NOT_RESOLVED` / `pems.home.arpa` not found | DNS record or hosts entry missing on that PC (steps C/C′); `ipconfig /flushdns` |
| Windows `curl.exe` → `CRYPT_E_NO_REVOCATION_CHECK` | expected with Caddy's internal CA; use `--ssl-revoke-best-effort` (chain and host name are still verified) |
| `http://192.168.1.136/` opens a TLS error | stale Caddyfile: the catch-all `http://:80 { redir https://pems.home.arpa{uri} permanent }` is missing – re-apply `deploy/caddy/Caddyfile` (step H) |
| `pems.home.arpa` not found | steps B/C/C′ |
