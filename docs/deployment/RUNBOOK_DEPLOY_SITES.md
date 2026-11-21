# Runbook: deploy, roll back, reset and restore the demo, its staging copy and the landing website

SCRUM-317 (plan #299), written against the pipeline of SCRUM-313 (#295) and SCRUM-314 (#296). Background and one-time setup: [DEPLOYMENT_README.md](DEPLOYMENT_README.md).
**Never write a password, connection string or key into a ticket, chat, commit or this file.** Secrets are named here, values live in GitHub secrets, `env/<name>.secrets.env` on the server and the password manager.

| Site | Address | Branch that deploys it | Pipeline | State |
|---|---|---|---|---|
| Demo staging | `staging.parmisata.ir` | `staging` | `deploy-staging.yml` -> `deploy-site.yml` | automated |
| Demo | `demo.parmisata.ir` | `demo` | `deploy-demo.yml` -> `deploy-site.yml` | automated; refuses a source tree that was not deployed to staging first |
| Landing website | main domain | `main` of the separate repository `pems-landing` | none yet (SCRUM-312, plan #294) | **manual until #294**; its staging copy is part of #294 |

Only a push to `staging` or `demo` (or a manual *Run workflow*) deploys. Never push to either branch to "try something". Pushes to `main` and working branches deploy nothing.

## Access and owners

| Access | Held by | Where it lives | Used for |
|---|---|---|---|
| GitHub repository admin (branches, secrets, run workflows) | Owner | GitHub | set secrets, start rollbacks |
| Push/merge to `staging` and `demo` | Owner, Hamid | **agreement only**: the branches are not protected and the repository has no ruleset (owner decision 2026-11-09, SCRUM-556), so anyone with write access could push | the only way a deploy starts; only these two people push or merge, never a force-push or a branch deletion |
| `STAGING_*` and `DEMO_*` secrets (`FTP_HOST`, `FTP_USER`, `FTP_PASSWORD`, `FTP_DIR`, `FTP_PROTOCOL`, `FTP_PORT`, `SQL_CONNECTION_STRING`, `ADMIN_PASSWORD`, `SITE_URL`, optional `BACKUP_DIR`) | Owner | GitHub Actions secrets | the pipeline; one FTP account and one database user per site |
| Plesk panel (files, databases, certificates, backups) | Owner, Hamid | Plesk of `parmisata.ir` | reset, restore, delete `app_offline.htm`, read `logs/` |
| FTP account of a site | the pipeline (secret) | Plesk > FTP | uploads only that site's folder |
| SQL login of a site | the pipeline (secret), Plesk | Plesk > Databases | full rights on that site's database only |
| Administrator of the application (`admin`) | Owner of the environment | the database (password changed at first sign-in) | sign in, Admin > Database Update |
| Landing: hosting folder, FTP account, `pems-landing` repository | Owner | Plesk, GitHub | manual landing deploy until #294 |

Owner of each environment: demo and staging = Hamid (day to day); secrets = the owner. Who may push to `staging` and `demo` is the agreement in the table above, not a GitHub rule; if a ruleset is added later, update both rows. Rotation of a leaked value: [CREDENTIAL_ROTATION_RUNBOOK.md](CREDENTIAL_ROTATION_RUNBOOK.md).

## Demo and demo staging (same steps; replace `staging` by `demo`)

### Deploy
1. Merge the change into `main` (pull request).
2. `git checkout staging && git merge main && git push` -> *PEMS Staging Deploy* starts. Watch: `gh run watch` or Actions.
3. The run does: migration gate, build, drift gate, tests (skipped when the `gate-pass` tag exists), publish, database backup (if `STAGING_BACKUP_DIR` is set), upload by FTP, wait for `/health/database` = `UpToDate`, **smoke test**, then records the tag `staged/<tree>`.
4. Open `https://staging.parmisata.ir` and check the change by hand ([DEPLOYMENT_README.md](DEPLOYMENT_README.md) section 8).
5. Promote: `git checkout demo && git merge staging && git push` -> *PEMS Demo Deploy*. It stops at once with "never deployed to staging" when the source tree differs from what staging ran. Fix by merging staging into demo, not by bypassing.
6. Green run = health `UpToDate` and every smoke line `PASS`. Anything else: the run is red, the site stays on the new build; roll back.

### Roll back
1. Find the last good commit: the previous green run of the same workflow, or `git log demo`.
2. Actions > *PEMS Demo Deploy* (or Staging) > **Run workflow** > branch `demo`, **rollback_ref** = that commit (and **allow_unstaged** if that build never went through staging). The pipeline deploys that commit; the FTP difference upload sends only files that differ.
3. The schema moves forward only. If the bad build applied a migration, the older build may refuse the newer schema: restore the database as well (Restore, below), using the backup the bad deploy took (its path is in the run summary, "Database backup before this deploy").
4. Check `/health/database` and the smoke test lines of the rollback run. Then fix forward on `main`; do not leave `demo` on a rolled-back commit without recording why.

### Reset (empty the data, destroys all data of this environment only)
Follow [DEPLOYMENT_README.md](DEPLOYMENT_README.md) section 5: upload `app_offline.htm`, drop and recreate the empty database in Plesk (same name, user, password), delete `app_offline.htm`, wait for `UpToDate`, sign in as `admin` and change the password. The data pack and the nightly reset are not built yet (plan #140, #141, #352, #353).

### Restore (database back to a pre-deploy backup)
1. Take the site offline (`app_offline.htm`).
2. In Plesk > Databases > the database > Import/Restore, or as the database user: `RESTORE DATABASE [<db>] FROM DISK = N'<path from the run summary>' WITH REPLACE`. Without `<PREFIX>_BACKUP_DIR` no pipeline backup exists: use the Plesk backup manager.
3. Deploy the matching build (Roll back, step 2) so schema and code agree, delete `app_offline.htm`, check `UpToDate`.

### Stuck or failed
- Site shows "being updated": delete `app_offline.htm` in Plesk > Files, or re-run the workflow.
- `550 ... used by another process`: re-run; the pipeline already stops the app first.
- 500.30 on start: read `logs/stdout*` and `logs/pems-*.log`; first suspect the SQL user/password inside `<P>_SQL_CONNECTION_STRING`. Do not paste the string anywhere.
- Certificate warning at the end: Plesk > SSL/TLS Certificates > Let's Encrypt for that subdomain.

## Landing website (manual until SCRUM-312 / #294)

The landing site lives in its own repository and has no pipeline yet; the demo pipeline never touches it. Until #294 exists:
- **Deploy:** build in `pems-landing`, upload the output to the landing site's folder in Plesk (File Manager or its own FTP account), then open the main domain.
- **Roll back:** keep the previous build folder (rename, do not delete) and put it back.
- **Reset:** not applicable (static content). **Restore:** Plesk backup of the landing folder.
- Staging copy of the landing site and its pipeline steps belong to #294 (see SCRUM-312); this runbook gets a section for it when it exists.
