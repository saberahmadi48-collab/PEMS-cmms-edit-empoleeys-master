# Demo deployment (branch `demo`, https://demo.parmisata.ir)

Setup, secrets, manual deploy and reset: [DEPLOYMENT_README.md](DEPLOYMENT_README.md). Pipeline: `.github/workflows/deploy-site.yml` (reusable), called by `deploy-demo.yml`. Push or merge to `demo` builds, runs the model-drift gate and a database-free test gate, publishes
a self-contained win-x64 build with `env/demo.env`, writes `env/demo.secrets.env` from GitHub secrets, sets `PEMS_ENV=demo` in `web.config`,
uploads by FTP and waits for `GET /health/database` to report `UpToDate` (first deploy installs the database, later deploys update it).

Secrets: `DEMO_FTP_HOST`, `DEMO_FTP_USER`, `DEMO_FTP_PASSWORD`, `DEMO_FTP_DIR` (`/`), `DEMO_FTP_PROTOCOL` (`ftp`; FTPS fails on this host),
`DEMO_FTP_PORT`, `DEMO_SQL_CONNECTION_STRING`, `DEMO_ADMIN_PASSWORD`, `DEMO_SITE_URL`.

## Added by SCRUM-313 / SCRUM-314 (2026-10-29, not yet run on GitHub)

Pre-deploy database backup (optional `DEMO_BACKUP_DIR`), anonymous smoke test after the health check, manual rollback by `rollback_ref`, and the rule that a demo deploy needs a staging deploy of the same source tree first. Procedures: [RUNBOOK_DEPLOY_SITES.md](RUNBOOK_DEPLOY_SITES.md). **Not built (depend on tasks that do not exist yet):** loading the sample data pack (#140, #352/#353), installing the demo licence from a secret (#145), automatic rollback after a failed smoke test, pre-flight/manual-fallback artifact (#301), the landing site (#294).

## Status on 2026-10-24 (updated after run 5)

| Step | Result |
|---|---|
| Build, drift gate, database-free tests, publish, runtime configuration | pass |
| FTP upload from the GitHub runner to `parmisata.ir` | **pass** (the runner can reach the host) |
| `app_offline.htm`, FTP upload (with retry), bring online | **pass** |
| `/health/database` from the runner | **pass: `attempt 1: HTTP 200 UpToDate`** - the demo database was installed and the app answers |
| Valid TLS certificate on `demo.parmisata.ir` | **open** (informational warning remains; owner action in Plesk) |

## Why the first runs failed and what to do

1. **Run 1 - drift gate:** the design-time EF factory needs a connection string and CI had none. Fixed: the gate gets a dummy value (no connection is opened).
2. **Run 2 - health check, HTTP 000 for 60 attempts.** Two causes found by probing the site:
   - **No valid TLS certificate.** `http://` answers `301` to `https://`, but the `https://` certificate fails verification (Plesk shows "Domain not secured").
     The health probe now uses `--insecure` so the app state is still visible, and a separate non-fatal step reports the certificate.
     **Action (owner):** Plesk > the subdomain > SSL/TLS Certificates > get a free Let's Encrypt certificate for `demo.parmisata.ir`. WebAuthn/passkeys also need HTTPS.
   - **The application does not answer.** With verification off the connection is accepted, then a request hangs and is reset after ~60 s. That is what a first start
     that is still installing, or one that fails/hangs on startup, looks like (the first run applies ~116 migrations and the seeders).
     The pipeline now enables stdout logging (`logs/stdout*` on the server) and the app writes `logs/pems-*.log`.
     **Action:** after the next deploy read those two files in Plesk > Files > `logs`. The usual causes, in order: the database is not reachable from the web host with
     the connection string in `DEMO_SQL_CONNECTION_STRING` (the host shows `10.45.12.3`; use `Server=10.45.12.3` without `:0`, try `,1433`); the ASP.NET Core Module is not
     installed or the app pool is stopped (HTTP 500.19/502.5 in the log); SQL Full-Text is not installed (only warns).
3. **Run 3 - FTP upload failed: `550 The process cannot access the file because it is being used by another process`.** The first deploy
   worked because nothing was running; once the app runs, IIS keeps `PEMS.AI.dll` and the other assemblies locked. Fixed: the pipeline uploads
   `app_offline.htm` first (the ASP.NET Core Module stops the app and releases the files), syncs the files, then deletes `app_offline.htm`
   (only after a successful upload; a failed upload leaves the demo showing the "being updated" page instead of a half-updated app).
   If a deploy ever fails and the page stays offline, delete `app_offline.htm` in Plesk > Files, or re-run the workflow.
4. **Run 4 - FTP `Timeout (control socket)`:** the host sometimes needs more than the action's 30 s default to accept the FTP control connection (the same
   happened once in FileZilla). The upload now uses a 180 s timeout and one automatic retry.
5. **Upload time:** the first upload is ~280 MB over plain FTP; later runs send only changed files. The step now has a 60-minute limit.

## Checking by hand

```bash
curl -sSk -m 120 https://demo.parmisata.ir/health/database
```

`UpToDate` = done. `UpdateAvailable` / `FirstRun` = installing or waiting for an administrator to apply it on Admin > Database Update.
