# Deploying PEMS to demo and staging

How the two online environments are built, deployed, secured, reset and recovered. Everything here was written for the shared Plesk/IIS host
(`parmisata.ir`); the same steps apply to any Windows host with IIS, the ASP.NET Core Module and SQL Server.
Background and failure history of the pipeline: [DEMO_DEPLOYMENT.md](DEMO_DEPLOYMENT.md). Database behaviour: [CODE_FIRST_DATABASE.md](CODE_FIRST_DATABASE.md).

Short per-operation procedures and the access/owner table: [RUNBOOK_DEPLOY_SITES.md](RUNBOOK_DEPLOY_SITES.md).
Decision record for the branch model: [ADR-0006](../../plan/adr/0006-demo-and-staging-branch-policy.md).

## 1. The model in one table

| Branch | What a push does | Subdomain | Database | Settings file | Secrets prefix |
|---|---|---|---|---|---|
| `main` | **nothing** (no workflow runs) | - | - | - | - |
| any working branch (`feature/...`, `fix/...`, `docs/...`) | **nothing** | - | - | - | - |
| `staging` | builds, tests, deploys | `staging.parmisata.ir` | its own staging database | `env/staging.env` | `STAGING_` |
| `demo` | builds, tests, deploys | `demo.parmisata.ir` | its own demo database | `env/demo.env` | `DEMO_` |

- Only pushes to `staging` and `demo` start a pipeline. `build.yml` and `deploy-uat.yml` are manual (`workflow_dispatch`) only.
- Promotion is a merge, in this order: working branch -> `main` -> `staging` (check it) -> `demo`.
  `git checkout staging && git merge main && git push`, then the same for `demo` once staging is good.
- The workflows live in `.github/workflows`: `deploy-site.yml` (the one reusable pipeline), `deploy-staging.yml` and `deploy-demo.yml` (thin callers that pass the target and the secrets prefix).
- Never commit secrets. Settings are in `env/<name>.env`, secrets only in GitHub secrets and in `env/<name>.secrets.env` on the server (git-ignored).

## 2. One-time setup per environment

Do the same for `demo` and for `staging`; only the names differ.

### 2.1 Create the subdomain (Plesk)
1. Plesk > **Websites & Domains** > the main domain (`parmisata.ir`) > **Add Subdomain**.
2. Subdomain name `demo` (or `staging`). Keep the suggested document root (for example `/demo.parmisata.ir`); it is the home folder of the FTP account in 2.3.
3. If DNS is not managed by Plesk, add an **A record** (`demo`, `staging`) pointing to the server IP at your DNS provider.
4. Subdomain > **Hosting Settings**: make sure the ASP.NET Core Module / IIS site is active; tick **Permanent SEO-safe 301 redirect from HTTP to HTTPS** after the certificate exists.
5. Subdomain > **SSL/TLS Certificates** > **Let's Encrypt** (DNS must already resolve) > get the free certificate. WebAuthn/passkeys need HTTPS.

### 2.2 Create the database
The hosting login usually cannot `CREATE DATABASE`, so the database is created in the panel and is **empty**; the application installs the schema itself on first start.
1. Plesk > **Databases** > **Add Database** (type Microsoft SQL Server): name `pems_demo` / `pems_staging`; create a **database user** with full rights on that database only.
2. Never reuse a database between environments and never point demo or staging at a live database.
3. Connection string (keep it for the secret):
   `Server=<db host>[,1433];Database=pems_demo;User Id=<user>;Password=<password>;TrustServerCertificate=True`
   (use the host or IP shown by Plesk without `:0`; try `,1433` if it does not connect).
4. SQL Full-Text Search is optional (the app only warns without it).

### 2.3 Create the FTP account
Subdomain > **FTP** > **Add FTP Account**: user `pems-demo-deploy` / `pems-staging-deploy`, home directory = the subdomain document root, strong password. One account per site.

### 2.4 GitHub secrets
Repository > **Settings > Secrets and variables > Actions > New repository secret** (or `gh secret set NAME`). Two sets, the prefix is `DEMO_` or `STAGING_`:

| Secret | Value |
|---|---|
| `<P>_FTP_HOST` | FTP host name, for example `parmisata.ir` |
| `<P>_FTP_USER` / `<P>_FTP_PASSWORD` | the FTP account of 2.3 |
| `<P>_FTP_DIR` | remote folder **relative to the FTP account's home**, with trailing slash. If the account home is the subdomain folder (section 2.3) this is `/` (this host). A wrong value fails with `550 The system cannot find the file specified`; the pipeline's early FTP check then lists the folders it can see |
| `<P>_FTP_PROTOCOL` | `ftp`, `ftps` or `ftps-legacy`. Empty means `ftps` everywhere in the pipeline. **This host needs `ftp`**: without it the file upload dies with `ECONNRESET ... before secure TLS connection was established` |
| `<P>_FTP_PORT` | optional, default 21 (set it to 21 explicitly, as demo does) |
| `<P>_SQL_CONNECTION_STRING` | connection string of 2.2 |
| `<P>_ADMIN_PASSWORD` | password of the first administrator, used only on an empty database; change it at first sign-in |
| `<P>_BACKUP_DIR` | optional: a folder **on the database server** that the SQL Server service account may write (for example `D:/Backups/pems-demo`). When set, every deploy first takes a copy-only `BACKUP DATABASE` there (`scripts/ci/backup-database.sh`) and a failing backup stops the deploy; the path is in the run summary. When empty the run only warns that there is no pipeline backup |
| `<P>_SITE_URL` | `https://demo.parmisata.ir` / `https://staging.parmisata.ir` (enables the post-deploy health check) |

Rotate any secret that was ever pasted into chat, a ticket or a commit.

### 2.5 Branches
`demo` and `staging` are long-lived. Protect them in GitHub (**Settings > Branches**: pull request or restricted pushes, no force push). `main` is protected the same way; none of them runs a build on `main`.

## 3. What the pipeline does (`deploy-site.yml`)

1. Restore, build, **model-drift gate** (`dotnet ef migrations has-pending-model-changes`), database-free test gate.
2. `dotnet publish -r win-x64 --self-contained -p:PemsEnv=<target>` (ships `env/<target>.env`).
3. Writes `env/<target>.secrets.env` from the `<P>_SQL_CONNECTION_STRING` and `<P>_ADMIN_PASSWORD` secrets and sets `PEMS_ENV=<target>` and a 15-minute startup limit in `web.config`.
4. Uploads `app_offline.htm` (the app releases its file locks), uploads the files by FTP (180 s timeout, one retry), removes `app_offline.htm`.
5. Polls `GET <SITE_URL>/health/database` until `200 UpToDate` (the first start installs the schema, later starts apply new migrations).
6. **Smoke test** (`scripts/ci/smoke-test.sh`, anonymous, writes nothing): `/health` = Healthy, `/health/database` = UpToDate, the login page has its form, a stylesheet is served, a protected page sends an anonymous visitor to the login page, an unknown path is 404. A `FAIL` line fails the run.
7. On `staging`: records the tag `staged/<tree hash>`. A `demo` run (`require_staged`) refuses a tree without that tag, so every demo deploy is preceded by a staging deploy of the same source tree (SCRUM-314). Manual runs can tick `allow_unstaged` (rollback of a build that was never staged).
8. Reports the TLS certificate state.

Before step 1 of a run the optional database backup runs (secret `<P>_BACKUP_DIR`, section 2.4) after the runtime configuration and before the app is taken offline. A manual run accepts `rollback_ref` (commit, tag or branch to deploy instead of the branch head).

**Identical code is not tested twice.** After build, drift gate and tests pass, the pipeline pushes the tag `gate-pass/<hash of the whole source tree>`. A later deploy of a tree with the same hash (typically `demo` right after `staging`, because the promotion is a plain merge) finds the tag, skips restore, build, drift gate and tests, and goes straight to publish and upload (about 6 minutes less). Any change in any file gives a new hash, so the gate runs again; a failed gate leaves no tag. To force the gate, run the workflow by hand with *Run workflow > force_tests* ticked, or delete the tag (`git push origin --delete gate-pass/<hash>`). NuGet packages are cached between runs. Old tags can be removed in bulk once a month: `git tag -l "gate-pass/*"`.

**Migration chain gate (required step).** The drift gate opens no database, so `deploy-site.yml` first runs the job `migration-gate` (`migration-chain.yml`): it applies the whole chain on a throw-away SQL Server (empty install, then upgrade from the commit the branch pointed to before this push; `origin/main` for a new branch or a manual run) and names the failing migration. If it fails nothing is built or uploaded. You can run it earlier by hand: Actions > *PEMS Migration Chain Gate* > **Run workflow** on your branch (SCRUM-413, [CODE_FIRST_DATABASE.md](CODE_FIRST_DATABASE.md)). It runs on every deploy, also when the gate-pass tag skips the build, and with the data upgrade tests of SCRUM-280 one run took about 8 minutes (run 40493228708). The deploy itself never connects to SQL Server; the migrator inside the application applies the migrations at start-up, and the post-deploy poll of `/health/database` confirms it.

Watch a run: GitHub > **Actions**, or `gh run watch`. Slack (`#pems-github`) posts every run. Documentation-only pushes (`docs/**`, `plan/**`, `*.md`) do not redeploy.
Run it by hand: Actions > the workflow > **Run workflow** (choose the branch), or `gh workflow run deploy-staging.yml --ref staging`.

## 3a. Package size (why the first upload is large)

The package is a **self-contained win-x64** build, so the host needs no .NET runtime. Measured on 2026-10-25: 285 MB before optimisation, **245 MB** after.

| Part | Size | Note |
|---|---|---|
| .NET runtime + framework (self-contained) | ~75 MB | the price of not installing the runtime on the host |
| PEMS assemblies (`PEMS.Persistence.dll` alone is 29 MB: 120 migrations and their snapshots) | ~70 MB | |
| Third-party (QuestPDF/qpdf, OpenXml, BouncyCastle, PdfPig, Tesseract/Leptonica ...) | ~60 MB | |
| `wwwroot` (vendored libraries, no CDN) | ~18 MB | |
| Lato fonts for PDFs | 12 MB | |

Removed for environment packages (`-p:PemsEnv=<name>`, in `PEMS.Web.csproj` and the workflow): debug symbols (`.pdb`), satellite resource folders of third-party libraries for languages other than `en;fa` (14 MB), the x86 native libraries (the hosts are x64 only), and `wwwroot/uploads` (runtime user data; it is git-ignored and never shipped).
**How an upload stays small.** The upload is a *difference* upload, not a full copy: `FTP-Deploy-Action` stores `.ftp-deploy-sync-state.json` on the server (name, size and hash of every file it sent) and on the next run sends only new or changed files and deletes removed ones. The runtime and third-party libraries (about 170 MB) are byte-identical between builds, so after the first deploy they are never sent again; each deploy sends only the PEMS assemblies that really changed, views and static files, and a ~1 MB generated `staticwebassets.endpoints.json`. Two things make this work and must not be undone:
- `Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`. Without it the SDK writes the git commit hash into every PEMS assembly, so every commit changed the bytes of **all** PEMS assemblies (about 70 MB) even when their source was untouched. Verified: two builds of an unchanged project with different revisions now give identical files.
- The sync state file stays on the server; if it is lost the next run uploads everything again (about 245 MB).
Typical later deploy: a few MB to ~45 MB (`PEMS.Persistence.dll` alone is 29 MB whenever data-layer code changes).

Only the **first** upload is big: the FTP action keeps `.ftp-deploy-sync-state.json` on the server and later runs upload only changed files (normally the PEMS assemblies, a few MB). Do not delete that file.
Further savings, if ever needed: move the EF migrations into their own assembly so `PEMS.Persistence.dll` (29 MB) changes only when the schema does; publish with IIS Web Deploy (delta + compression) if the host offers it; or a framework-dependent publish (about 75 MB less) once the host has the .NET 10 Hosting Bundle installed; trimming is not safe for MVC/EF here.

## 4. Manual deploy (when the host is unreachable from GitHub or the pipeline is broken)

```powershell
# 1. On a build machine, from the branch you want to ship
git checkout staging            # or demo
dotnet publish PEMS.Web/PEMS.Web.csproj -c Release -r win-x64 --self-contained true -p:PemsEnv=staging -o .\publish
# 2. Create publish\env\staging.secrets.env (never commit it):
#      ConnectionStrings__PEMSConnection=<connection string>
#      Seed__Admin__Password=<admin password>
# 3. In publish\web.config set  PEMS_ENV=staging  (environmentVariables under <aspNetCore>) and startupTimeLimit="900"
# 4. Upload app_offline.htm to the site root, wait ~30 s, upload the contents of .\publish (FileZilla/WinSCP/Plesk File Manager), delete app_offline.htm
# 5. Open https://staging.parmisata.ir/health/database  -> 200 UpToDate
```
If the site keeps showing the "being updated" page, delete `app_offline.htm` in Plesk > **Files**. Logs: `logs/stdout*` and `logs/pems-*.log` in the site folder.

## 5. Manual reset of an environment's data

A reset returns demo or staging to an empty database that reinstalls itself. **It destroys all data of that environment only.**

1. Put the site offline: upload `app_offline.htm` (section 4).
2. Empty the database, either way:
   - **Preferred:** Plesk > **Databases** > drop the database and add it again with the same name, user and password (the connection string stays valid).
   - **If the panel cannot recreate it:** run this once as the database user to drop everything, then continue.
     ```sql
     DECLARE @sql nvarchar(max) = N'';
     SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
       FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id;
     SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id;
     EXEC sp_executesql @sql;   -- repeat once if full-text catalogs or views remain
     ```
3. Delete `app_offline.htm`. The first request starts the app, which detects an empty database, installs the schema, runs the seeders and creates the administrator from `<P>_ADMIN_PASSWORD`.
4. Wait for `GET /health/database` = `200 UpToDate` (a few minutes), sign in as `admin`, change the password.
5. Reload the demo content (team task, plan row #304) from the demo overlay or by importing the team's Excel files in **Import Center**.

Not available yet (planned): a UI to export the current data as a versioned, hashed package, import it, and reset to a baseline package on every push, with a version-compatibility precheck (plan rows #352 and #353, SCRUM-369 and SCRUM-370; nightly/admin reset #141). Until they exist, use this manual procedure.

## 6. Roll back
Run the deploy workflow by hand with **rollback_ref** = the previous good commit (`gh workflow run deploy-demo.yml --ref demo -f rollback_ref=<sha>`, add `-f allow_unstaged=true` if that build never went through staging); the difference upload sends only the files that differ. Step-by-step: [RUNBOOK_DEPLOY_SITES.md](RUNBOOK_DEPLOY_SITES.md). The schema moves forward only (rule 6): if a bad migration was applied, restore the database from the pre-deploy backup (`<P>_BACKUP_DIR`) or the host's backup and redeploy the previous build.
Automatic rollback on a failed smoke test is not built yet (follow-up task); until then the run is red and the operator rolls back.

## 7. Checklist for a new environment
- [ ] subdomain, DNS and certificate (2.1)
- [ ] empty database and user (2.2), FTP account (2.3)
- [ ] `env/<name>.env` and `env/<name>.secrets.env.example` exist and have the same keys as the others (a test checks this)
- [ ] seven required GitHub secrets (2.4; protocol and port are optional), branch created and protected (2.5)
- [ ] first push -> green run -> `/health/database` is `UpToDate` -> administrator signs in

## 8. Verifying a deployed environment in the browser (manual checks)

Used after a deploy and by the QA check ([../guidelines/07-qa-check-in-the-ui.md](../guidelines/07-qa-check-in-the-ui.md)). Everything is done in the browser.

1. Open `<SITE_URL>/health/database`: `200 UpToDate`.
2. Sign in as the administrator; the first sign-in forces a password change that follows the environment's password policy ([../../env/README.md](../../env/README.md) "Verifying settings").
3. DevTools > Network > the page request: the response has `Strict-Transport-Security` with `max-age` = `Hosting__HstsMaxAgeDays` x 86400, and the padlock shows a valid certificate.
4. Admin > Database Update shows "up to date" and the latest migration.
5. Smoke: open the home page, one list, one form and one dashboard in `fa` (RTL) and `en`, at desktop width and 375 px; the console has no errors.
6. For a task being QA-checked: run its checklist (Jira comment; line 1 is the local test run, [../../README.md](../../README.md) "Local tests") and the README checks it references; record the result in the Jira issue.

## 9. Administrator password and updates (rule, SCRUM-410)

Owner decision 2026-10-27, for demo, staging and every client server:

- **A deploy, a merge into `staging` or `demo`, an update of a client server and a schema migration never create, reset or change a password.** The application seeds the administrator only when the user table is empty. `Seed__Admin__Password` in the secrets file is used for that one moment only; rewriting the file on every deploy does not change an existing account.
- **Only a fresh install or an explicit database reset (section 5) restarts the first-install flow:** the empty database gets the administrator from the seed settings and the first sign-in forces a new password. After a reset, tell the owner of the environment to sign in and change it at once.
- The client update kit (`deploy/`) replaces the application and runs the schema migration script; it never touches accounts. `PEMS_DEV_RESET_ADMIN_PASSWORD` works only in Development and is never set on a server.
- Guard: `AdminPasswordSurvivesUpdateTests` (build fails the tests if a start-up step starts changing an existing account). Manual check: M-14 in [../../README.md](../../README.md) "Manual checks in the UI".
- Planned (rows #353, #169, #170): the reset-on-deploy baseline and the first-run wizard reuse this flow; the signed update package keeps this rule.
