# Environment settings and secrets

Every setting of PEMS lives here, never in code and never in `appsettings.json` (which is intentionally empty).
Each environment has two files:

| File | Content | In git? |
|---|---|---|
| `env/<name>.env` | all non-secret settings (URLs, ports, limits, policies, feature flags, culture, log retention) | yes |
| `env/<name>.secrets.env` | secrets only: database connection string, Redis, FTP account, first administrator password | **no** (git-ignored, never packaged) |
| `env/<name>.secrets.env.example` | template listing the secret keys of that environment | yes |

Environments: `local` (developer machine), `docker` (docker-compose), `demo` (online demo), `staging` (pre-production copy of the demo), `uat` (Azure UAT),
`production-kangan` (Kangan production). All files define exactly the same keys; a test enforces it.

Decision record: [ADR-0004](../plan/adr/0004-environment-and-secrets-files.md) (Instructions rule 24).

## Which environment is used

`PEMS_ENV=<name>` selects it. Without it, `ASPNETCORE_ENVIRONMENT=Development` means `local` and `UAT` means `uat`;
any other host environment loads no file and reads real environment variables only. `PEMS_ENV_DIR` points to the
folder if it is not found by walking up from the application folder.

Precedence, lowest to highest: `env/<name>.env` < `env/<name>.secrets.env` < real environment variables < command line.
A missing `<name>.env` stops the start with a clear message; missing secrets are allowed when they come from
environment variables.

## File format

`KEY=VALUE` per line, `#` for comment lines. `__` is the section separator: `Ollama__Url=...` is `Ollama:Url`,
`Fido2__Origins__0=...` is the first item of `Fido2:Origins`. Values may be quoted; there are no inline comments or
variable expansion, so a password may contain `#`, `$` and `=`.

## Setting up a machine

```bash
cp env/local.secrets.env.example env/local.secrets.env     # then edit the connection string and admin password
dotnet run --project PEMS.Web
```

- **Docker:** `cp env/docker.secrets.env.example env/docker.secrets.env`, then
  `docker compose --env-file env/docker.secrets.env up --build`.
- **Demo and staging:** see [../docs/deployment/DEPLOYMENT_README.md](../docs/deployment/DEPLOYMENT_README.md). The pipeline writes `env/<name>.secrets.env` from GitHub secrets (`DEMO_SQL_CONNECTION_STRING`,
  `DEMO_ADMIN_PASSWORD`) and sets `PEMS_ENV=demo` in `web.config`; `env/demo.env` ships with
  `dotnet publish -p:PemsEnv=demo`.
- **Kangan production:** fill every `REPLACE` value in `env/production-kangan.env` (it is a normal reviewed commit),
  create `env/production-kangan.secrets.env` on the server (mode 600) and start with `PEMS_ENV=production-kangan`.
- **dotnet ef:** (project `PEMS.Migrations`) the design-time factory reads `ConnectionStrings__PEMSConnection` from the environment or from
  `env/<PEMS_ENV or local>.secrets.env`; it has no fallback and always prints its target database.

## Verifying settings (manual checks)

Run these after changing or adding a setting (QA procedure: [../docs/guidelines/07-qa-check-in-the-ui.md](../docs/guidelines/07-qa-check-in-the-ui.md)). A real environment variable overrides the file, so a local run can test a production value without editing it.

| Check | How | Expected |
|---|---|---|
| Same keys everywhere | `dotnet test PEMS.Tests --filter "FullyQualifiedName~EnvironmentFilesTests"` | green; the key sets of all six files are identical and the policy test passes |
| A bad value is refused | start with `Hosting__HstsMaxAgeDays=abc` (PowerShell: `$env:Hosting__HstsMaxAgeDays="abc"; dotnet run --project PEMS.Web`) | the app stops with "Setting 'Hosting:HstsMaxAgeDays' must be a number"; a missing key stops it with "Missing required setting" |
| Upload limit and scanner (SCRUM-202) | start with `$env:Uploads__MaxFileSizeMb="1"`; attach a 2 MB PDF to a MOC. Then `$env:Uploads__MalwareScan__Mode="Command"`, `$env:Uploads__MalwareScan__Command="cmd.exe"`, `$env:Uploads__MalwareScan__Arguments="/c exit 1"` and attach a small PDF | first: "larger than 1 MB" and nothing is stored; second: "refused by the malware scanner". `Mode=Command` with an empty Command stops the start with a clear message |
| Password policy | start with `$env:Authentication__Password__RequiredLength="12"`; Admin > Users, set an 11-character password, then a 12-character one | 11 is rejected with the policy message, 12 is accepted (production-kangan ships 12, the others 8) |
| Password history | Admin > Users: reset a user to password P1, sign in as that user and change to P2, then change back to P1 and also try P2 again (default `Authentication__Password__HistoryCount=5`; `0` turns the rule off) | P1 and P2 are refused with "The new password must differ from your current password and your last 5 passwords" (Persian text in fa); a different password is accepted; with `0` they are accepted |
| Cookie lifetime and sliding | sign in; browser DevTools > Application > Cookies > `PEMS.Auth`; start with `$env:Authentication__Cookie__ExpireHours="0.1"` and use the site for more than 3 minutes | with `Authentication__Cookie__SlidingExpiration=true` the cookie expiry moves forward while you work; with `false` it stays fixed and you are signed out after 6 minutes |
| HSTS | on a deployed HTTPS environment (not `localhost`, not Development): DevTools > Network > the page request > Response headers | `Strict-Transport-Security: max-age=2592000` for `Hosting__HstsMaxAgeDays=30` (days x 86400) |
| Migration command timeout | Admin > Database Update on a database with pending migrations, with `Database__MigrationCommandTimeoutMinutes` at the default and with a very small value on a large database | the update completes with the default; a too-small value fails with a timeout message in the page and the log, and the next run after fixing the value completes |
| Pre-update backup (SCRUM-280) | on a copy of a database with pending migrations, start with `$env:Database__BackupBeforeUpdate="true"` (and once with `$env:Database__BackupDirectory="Z:\missing"`) | the update writes `<db>_preupdate_<utc>.bak` in the SQL Server default backup folder and the Admin > Database Update run shows it; with a missing folder the update is not started, the page and the log say why, and the database is unchanged |
| Secrets are not in the settings file | read `env/<name>.env` | no password, token or connection string (the test enforces it) |

## Rules

- A new setting is added to **all** `env/*.env` files (and to the secrets templates if it is secret), read through
  `IConfiguration`, and required with `Required*` (`PEMS.Web/Configuration`) instead of an in-code default.
- A value that would be harmful if public (password, token, key, connection string) goes only into the secrets file.
- No customer-specific code or branches: customer differences are the `production-<customer>.env` file and the licence.
- Not every literal is a setting: classify it first with [../docs/design/CONFIGURATION_CONSTANTS.md](../docs/design/CONFIGURATION_CONSTANTS.md) (setting / policy constant / domain rule / tuning).
- Rotate any secret that was ever pasted into chat, a ticket or a commit.
- `Definitions__RetainRetiredVersions=forever` (SCRUM-786, in every full environment file): retired definition versions are never deleted; `forever` is the only supported value and any other stops the start. `Definitions__StorageWarnMb` (default 1024): the storage report on Admin > Definitions > Storage warns above this size.
