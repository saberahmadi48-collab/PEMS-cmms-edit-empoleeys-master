# Credential rotation runbook (SCRUM-17 findings, plan row #317)

Owner: Hamid Maleki (plan row #317, Doer: User). Source: [docs/audits/SECRETS_AUDIT_2026-10.md](../audits/SECRETS_AUDIT_2026-10.md).
**Never write a password into a ticket, chat, commit or this file.** New values go only into GitHub secrets, `env/<name>.secrets.env` (mode 600) or a password manager.

Why this cannot be done from the repository: the credentials live in databases and servers (demo, staging, uat, production-kangan, developer machines)
that source control and the AI sessions cannot reach. This runbook is the exact procedure; tick each box and record the date in the #317 Jira issue.

## 0. Find what is exposed (read-only, per database)

Run `scripts/security/Report-DefaultCredentialExposure.sql` on every database. It lists administrators, accounts that never signed in or must still
change their password, `loadtest*` accounts and the number of accounts created before 2026-10-23. It reads no password or hash.

## 1. Administrator account `admin` (audit H-1)

Applies to every database seeded before 2026-10-23: demo, staging, uat, production-kangan, customer and developer databases.

1. Sign in as `admin` is **not** needed; use another SystemAdmin, or if `admin` is the only one, sign in once and change the password first.
2. Admin > Users > edit `admin` > enter a new strong, unique password (>= 16 characters, from the password manager) > save.
3. Demo and staging: the seed password is only used on an **empty** database, so changing the GitHub secret `DEMO_ADMIN_PASSWORD` / `STAGING_ADMIN_PASSWORD` does not change the existing account. Do step 2 in the application, then set the secret to the same new value so a rebuilt demo matches.
4. Production-kangan: `env/production-kangan.secrets.env` `Seed__Admin__Password` must also be a new unique value (used only on an empty database).
5. Optional hardening: create a named SystemAdmin per person and deactivate `admin` (Admin > Users > toggle active).

## 2. Users created from the employee form (audit H-2)

1. From report section 2, take every account with `MustChangePassword = 1` or no `LastLoginDate`.
2. For each: Admin > Users > edit > set a new password and hand it over once through a safe channel, or deactivate the account if it is not used.
3. Accounts created after this change get a random one-time password, so only older accounts need this.

## 3. SQL Server `sa` of containers started from the old compose file (audit H-3)

1. List containers/volumes created from the old `docker-compose.yml` (the one with a default `SQL_SA_PASSWORD`): `docker ps -a`, `docker volume ls`.
2. In each running SQL Server container change the password: `ALTER LOGIN sa WITH PASSWORD = '<new value from the password manager>'` (run through `sqlcmd` interactively, not as a command-line argument or script in git).
3. Put the new value in `SQL_SA_PASSWORD` **and** in the `Password=` part of `ConnectionStrings__PEMSConnection` in `env/docker.secrets.env`, then `docker compose --env-file env/docker.secrets.env up -d`.
4. Check no SQL port is reachable from the network: after this change the compose file publishes 1433, 11434 and 8080 on `127.0.0.1` only.

## 4. `loadtest*` accounts (audit H-4)

Report section 3 lists them. Deactivate them in Admin > Users (toggle active); they own no business data. Never run `tools/LoadTestSeed` against a shared database
(the tool already refuses non-local databases). The password now comes from `LoadTest__Password` in `env/local.secrets.env` or `PEMS_LOADTEST_PASSWORD`.

## 5. Developer machines (audit H-5)

Anyone who copied `env/local.secrets.env.example` before 2026-10-26 and kept the example administrator password: change `Seed__Admin__Password` in `env/local.secrets.env`
and the password of the local `admin` (step 1).

## 6. After rotation

- Re-run the report; sections 1-3 must show only accounts you recognise with changed passwords.
- Rotate anything that was pasted into chat or a ticket (#317 text): demo FTP password, demo database password.
- The old values remain readable in git history. Purging history is a separate, held task (plan row in the backlog) and must happen only after this runbook is complete.
