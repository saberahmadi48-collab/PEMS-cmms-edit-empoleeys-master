# Secrets and unsafe-defaults audit - 2026-10

Jira: SCRUM-17 (plan row #9). Auditor: Claude Code, for Hamid Maleki. Date: 2026-10-25. Audited commit: `72bc19c` (`main`).
Rotation work belongs to plan row **#317** (owner: Hamid Maleki); the step-by-step procedure is [docs/deployment/CREDENTIAL_ROTATION_RUNBOOK.md](../deployment/CREDENTIAL_ROTATION_RUNBOOK.md) and a read-only SQL report is `scripts/security/Report-DefaultCredentialExposure.sql`.
Decisions of 2026-10-26 (owner): history clean-up is held (#374, SCRUM-391); the FTPS certificate item is accepted conditionally and held (#375, SCRUM-392).

**No secret value appears in this report, the pull request, the commit messages or Jira.** Findings are identified by file, commit, line,
secret type and a mask of at most the first two characters plus the length (`Ab***`, 10 chars). Where this report says "needs rotation"
the value is treated as exposed to everybody who ever had read access to the repository.

## 1. Result in one paragraph

HEAD (after this pull request) contains **no live secret and no built-in credential**. Before it, HEAD still held two built-in passwords
(one for every user created from the employee form, one example password) and a documented default administrator password in two guides.
The history holds **five credentials that were once committed** (default administrator password, default new-user password, default SQL
`sa` password in compose, a load-test password, a weak example password) - none is a vendor token, private key or customer connection string.
All five are listed in section 5 with a decision and an owner; **three need rotation checks in every environment that ever ran those builds**
(section 6). Separately, 13 files of runtime user data are committed (section 4, S-1) - not secrets, but not shippable.

## 2. Scope and method

| Area | How |
|---|---|
| HEAD, all tracked text files (3,700 files) | ripgrep/`git grep` patterns: connection-string `Password=`/`Pwd=`, `password/secret/token/key = "literal"`, `const string *Password*`, private-key blocks, vendor token shapes (AWS, GitHub, Slack, Google, OpenAI-style, JWT), basic-auth URLs |
| HEAD, entropy | Shannon-entropy scan (>= 4.0 bits/char) of mixed-class tokens of 20+ characters in every tracked text file except vendored/minified/generated paths; every candidate was an identifier or migration name (false positives) |
| HEAD, specific files | `docker-compose.yml`, `Dockerfile`, `deploy/` (compose, `Dockerfile.release`, Caddyfile, scripts, `targets.example.json`), `scripts/`, `tools/`, `.github/workflows/*.yml`, `env/*.env` and `*.secrets.env.example`, `PEMS.Web/appsettings.json`, `WebApplication1/appsettings*.json`, `launchSettings.json`, `.gitignore`, `.dockerignore` |
| Office/binary files | text of all tracked `.xlsx`/`.docx` (two files with non-ASCII names could not be opened as ZIP and were only checked by name) searched for password/secret/key/connection-string patterns |
| Git history | every added line of every commit of every ref in the clone (`git log --all -p`) with the same patterns; pickaxe (`git log -S/-G`) on the names of the built-in credentials; full file-name history for key, certificate, `.env`, `appsettings*`, `web.config`, backup/database files |
| Refs | `main`, `staging`, `demo`, `fix`, `origin`, `feature/correspondence-cartable-ui`, `temp/code-first-database`, `transfer/from-personal-source`, `docs/SCRUM-14-adr-log`, `docs/SCRUM-26-licensing-design`, the `gate-pass/*` tag, 202 commits, one root commit (`2f5181d`, 2026-09-05) |

Tools: ripgrep (MIT/Unlicense), git, and two throw-away Python 3 scripts (standard library only) that print only file, commit, rule and a
two-character mask. **No dedicated secret scanner was installed**: the container has no scanner and no offline package source. Recommendation
for CI (row #317 or a follow-up): **gitleaks** (MIT licence, offline, single binary). **trufflehog is AGPL-3.0 and is excluded** by guideline chapter 03.

Limits - what this audit could not see:

- The clone was shallow at the start (162 commits); it was deepened with `git fetch --unshallow` and every result here is from the full 202-commit history.
- Pull-request refs (`refs/pull/*`), forks, deleted branches, GitHub Actions logs and secret settings, Jira, chat and developer machines were not
  available. A secret that only ever existed there is not covered.
- `dotnet` is not installed in this container, so no build or test was run (section 7).

## 3. HEAD findings and fixes (all in this pull request)

| ID | Where (HEAD before the fix) | Type | Fix |
|---|---|---|---|
| F-1 | `PEMS.Web/Controllers/EmployeeController.cs:33` (+ uses at :447 and :477, view `Views/Employee/Index.cshtml:178`) | Built-in password for every user created from the employee form (`Ab***`, 10 chars); also printed in the UI and in the success message | Removed. New helper `PEMS.Shared/Security/TemporaryPasswordGenerator.cs` creates a random 16-character one-time password per user (policy-proof: upper, lower, digit, symbol); the message shows it once; `MustChangePassword` stays `true`. Unit tests added |
| F-2 | `.claude/Role/FRAMEWORK_SETUP_GUIDE.md:35`, `.github/Role/FRAMEWORK_SETUP_GUIDE.md:35` | Documented default administrator password (`Ad***`, 11 chars) | Replaced by "the value of `Seed__Admin__Password` in `env/<name>.secrets.env` - there is no built-in default" |
| F-3 | `docker-compose.yml:16, 29, 46` | Unsafe default: SQL Server (1433), the unauthenticated Ollama API (11434) and the plain-HTTP app (8080) published on **all interfaces** | Published on `127.0.0.1` only, like `deploy/docker/compose.pems-web.yml`. The services still talk to each other over the compose network. Regression test added (`EnvironmentFilesTests`) that fails on any non-loopback port or any `${..PASSWORD:-default}` |
| F-4 | `tools/loadtest/pems-baseline.js:30`, `tools/LoadTestSeed/Program.cs:27` (and a console line that printed it, :98) | Hard-coded shared password of 210 test accounts (`Lo***`, 13 chars) | The seed tool reads `PEMS_LOADTEST_PASSWORD` or `LoadTest__Password` from `env/local.secrets.env` (key added to `env/local.secrets.env.example`); the k6 script reads `PEMS_LOADTEST_PASSWORD`. Both stop when it is missing; the seed tool no longer prints it |
| F-5 | `env/local.secrets.env.example:18` | Weak example password (`Ch***`, 18 chars) that works if copied unchanged | `<choose a password>` like the other five example files |
| F-6 | `.gitignore` | No rule for runtime file store, key or certificate files | Added `PEMS.Web/filestore/`, `*.pem`, `*.key`, `*.p12`, `*.snk` (`*.pfx` already existed). No tracked file matches the new key patterns |

Checked and clean at HEAD: `PEMS.Web/appsettings.json` (empty by design), `env/*.env` (no secret key; the existing test enforces it), the five
`*.secrets.env.example` files (placeholders), `docker-compose.yml` SQL password (`${SQL_SA_PASSWORD:?...}`, required, no default), `Dockerfile`,
`deploy/` kit (secrets come from the container environment; the deploy tests use obvious fake values), all five workflows (secrets only as
`${{ secrets.* }}`; `env/<target>.secrets.env` is generated at deploy time and never packaged), `.dockerignore` (excludes `env/*.secrets.env`),
`tools/TestAccountProvisioner` (random 16-character passwords, written outside the repository to `~/.pems-test`), `IdentitySeeder` (administrator is
seeded only from `Seed__Admin__*` settings; no fallback), `DevAdminPasswordReset` (off by default, needs an explicit environment variable, no literal password).

## 4. HEAD items that are not secrets but need a decision (not changed here)

| ID | Item | Why it was not changed | Owner |
|---|---|---|---|
| S-1 | 13 runtime user files are tracked in `PEMS.Web/filestore/` (letters, documents, an engineering comment, three MRS import workbooks; first committed in `2f5181d` and `68ce0ef`). `deploy/Build-Package.ps1` already keeps this folder out of release packages | Deleting files needs the owner's agreement (rule R23), and removing them from old commits needs a history rewrite. Nothing in the code or tests uses these 13 files (the store is `<app folder>/filestore`, created at run time and never published). Held as #374 (SCRUM-391): `git rm --cached` plus `git filter-repo` before the source is handed to a customer or auditor. The hard-coded store path in `Program.cs:686` (and three upload folders in controllers) is a separate R24 item, now task #379 (SCRUM-396) | Hamid Maleki |
| S-2 | `deploy-site.yml` uploads over FTPS with `curl --insecure` (certificate not verified) at 4 places; the FTP password is sent over a TLS channel that can be impersonated | Demo and staging sit on a shared Plesk host whose certificate may be invalid; the file documents this. Fix needs the host's certificate or CA - cannot be tested here. **Accepted conditionally by the owner 2026-10-26; task held as #375 (SCRUM-392)** | Hamid Maleki |
| S-3 | GitHub Actions referenced by tag, not commit SHA (`actions/*@v4`, `azure/login@v2`, `SamKirkland/FTP-Deploy-Action@v4.3.5` receives the FTP password) | Pinning needs the current SHAs from GitHub (no network here). Already in the scope of #289 (SCRUM-307: "pinned action versions and least-privilege token permissions") | Hamid Maleki |
| S-4 | `Dockerfile` and `deploy/docker/Dockerfile.release` run the app as root; `ollama/ollama:latest` and `mssql/server:2022-latest` are not pinned to a digest | Changing the user changes ownership of live volumes of the deployed VM; digests need the registry. Both need a test on the VM. Already in the scope of #172 (SCRUM-191: non-root user, minimal ports, upgrade test), which depends on this audit | Hamid Maleki |
| S-5 | `AllowedHosts=*` and the weak password policy (length 8, no upper-case, no symbol) in **all** six `env/*.env`, including `production-kangan.env` | Per-customer values; owner decision on the product default. Tracked in existing tasks (2026-10-26): `AllowedHosts` in #317 (SCRUM-334), password-policy default in #318 (SCRUM-335) | Hamid Maleki |
| S-6 | Internal addresses and an SSH account name of the test VM (`192.168.1.x`, `arsin@...`) in `deploy/caddy/Caddyfile` and `docs/audits/*`, `docs/deployment/*` | Not secrets; redact before the repository leaves the company. Tracked in the existing task #119 (SCRUM-139): delivery scan for internal addresses and account names | Hamid Maleki |

## 5. Historical findings (full history, 202 commits)

Decision values: **fixed** (the exposure at HEAD is gone), **needs rotation** (the value must be treated as compromised; owner rotates in #317),
**accepted** (no real credential; reason given). "Introduced" is the first commit that contains it; all five exist since the initial import `2f5181d` (2026-09-05).

| ID | Secret type and mask | Where (file:line at the last commit that had it) | Introduced -> removed | Decision | Owner |
|---|---|---|---|---|---|
| H-1 | Default administrator password, `Ad***`, 11 chars (+ default admin e-mail) | `PEMS.Web/Security/IdentitySeeder.cs:19` (constant, used :86); guides `.claude/Role/FRAMEWORK_SETUP_GUIDE.md:35`, `.github/Role/FRAMEWORK_SETUP_GUIDE.md:35` | `2f5181d` -> code `60c212b` (2026-10-23); guides this PR | Code and guides **fixed**. **Needs rotation** wherever a database was created before `60c212b`: demo, staging, uat, production-kangan, any customer or developer database. `MustChangePassword` was set, but nobody is forced to log in | Hamid Maleki (#317) |
| H-2 | Default password for users created from the employee form, `Ab***`, 10 chars | `PEMS.Web/Controllers/EmployeeController.cs:33`; also shown in the UI (`Views/Employee/Index.cshtml:178`) | `2f5181d` -> this PR (**still present at HEAD until now**) | **Fixed** in this PR. **Needs rotation**: every account created through "employee + user" that has not changed its password since (query: users with `MustChangePassword = 1` or no sign-in; force a reset) | Hamid Maleki (#317) |
| H-3 | Default SQL Server `sa` password as compose fallback `${SQL_SA_PASSWORD:-...}`, `P***`, in two places | `docker-compose.yml:14` (container env) and `:49` (application connection string) | `2f5181d` -> `60c212b` (2026-10-23) | **Fixed** (variable required, no default; ports now loopback-only). **Needs rotation** for every SQL container or volume created with the fallback, including the Linux test VM if it used this compose file | Hamid Maleki (#317) |
| H-4 | Shared password of 200 `loadtestNNN` and 10 `loadtestadminNN` (**SystemAdmin**) accounts, `Lo***`, 13 chars | `tools/loadtest/pems-baseline.js:30`, `tools/LoadTestSeed/Program.cs:27` | `2f5181d` -> this PR | **Fixed** in this PR. The seed tool refuses non-local databases, so exposure should be limited to developer databases: **owner to confirm and delete the `loadtest*` accounts** in any database where they exist (needs rotation = delete) | Hamid Maleki (#317) |
| H-5 | Weak example administrator password in `env/local.secrets.env.example`, `Ch***`, 18 chars | `env/local.secrets.env.example:18` | `60c212b` -> this PR | **Fixed**. Needs rotation only on a developer machine whose `local.secrets.env` still uses the example value; no shared environment | each developer (via Hamid, #317) |
| H-6 | Test fixtures: fake passwords and hashes in unit tests (`Ab***` 10, `S3***` 11, `HA***` 15 = a hash placeholder) and the fake secret `To***`/`Se***` in `deploy/tests` that proves secrets are not echoed | `PEMS.Tests/Organization/EmployeeRoleDisciplineManagementTests.cs`, `PEMS.Tests/FileStorage/StorageProviderServiceTests.cs`, `PEMS.Tests/Security/SecurityAuditLogTests.cs`, `deploy/tests/test-pems-docker.sh:21`, `deploy/tests/fakes/docker` | various | **Accepted**: values only exist inside in-memory databases or fake-docker runs and open no real account. Proposed owner confirmation in PR review | Hamid Maleki |
| H-7 | Connection strings in `appsettings*.json` history (`PEMS.Web/appsettings.json`, `.Development.json`, `.UAT.json`, `WebApplication1/*`) | all versions | `2f5181d` -> `60c212b` | **Accepted**: Windows integrated authentication (`Trusted_Connection=True`) on `localhost\SQLEXPRESS`, UAT file empty by design; no user name, password or remote server ever committed | - |
| H-8 | Non-credential hits: `BarcodeService.TokenAlphabet`, `StrategyImport` view `Model.Token`, `pems-ui.js`, `docs/design/LICENSING_TECHNICAL_DESIGN.md` `signing_key_id` label, two prose "secret" mentions in the plan workbook | - | - | **Accepted** (false positives) | - |
| H-9 | Customer/business files in the file store (see S-1) | `PEMS.Web/filestore/**` | `2f5181d` | **Held** by the owner (2026-10-26) as #374 / SCRUM-391 | Hamid Maleki |

Not found in any ref: private keys, certificates (`.pfx/.pem/.key/.p12`), `.env` or `*.secrets.env` files, real `appsettings.*` secrets, cloud/vendor tokens,
JWTs, basic-auth URLs, FTP or SFTP credentials, SSH keys, database backups. The licence-signing key does not exist in the repository.

## 6. Rotation list for row #317 (no values)

1. Administrator account `admin` (H-1): reset the password in demo, staging, uat, production-kangan and every customer or developer database seeded before 2026-10-23; confirm the seed settings (`Seed__Admin__Password`) are strong and unique per environment.
2. All users created through the employee form (H-2): force a password reset for any account that has not changed its password.
3. SQL Server `sa` (H-3): change the password of every SQL container started from the old compose file and of any connection string that used it.
4. `loadtest*` accounts (H-4): delete or disable in any database where `tools/LoadTestSeed` ran.
5. Developer machines (H-5): replace `Seed__Admin__Password` in any `env/local.secrets.env` copied from the old example.
6. The five values stay readable in git history; on 2026-10-26 the owner held the clean-up as #374 (SCRUM-391), to be done only after items 1-5. If history is rewritten, do it once (including `PEMS.Web/filestore`, S-1) and re-clone every working copy and runner.

## 7. Verification

- Re-scan of HEAD after the fixes: no match for the old built-in credential names (`DefaultAdminPassword`, `DefaultNewUserPassword`, `TestUserPassword`, the compose fallback) and no tracked file contains the removed default administrator password.
- `dotnet` (.NET 10 SDK) is **not available** in this container, so `dotnet build` and `dotnet test` (including `EnvironmentFilesTests` and the new `TemporaryPasswordGeneratorTests`) were **not run**. The new compose test's regular expressions were checked against the real `docker-compose.yml` with Python (3 ports found, all loopback, no password fallback). CI on the pull request must run them.
- Changed C# and JavaScript were reviewed by hand only; `tools/LoadTestSeed` and the k6 script were not executed.

## 8. How to repeat this audit

`git fetch --unshallow`, then search for `Password=`/`Pwd=`, quoted literals assigned to names containing password/secret/token/key, `const string *Password*`,
`-----BEGIN .* PRIVATE KEY`, vendor token prefixes, and entropy; run each over `git ls-files` and over `git log --all -p`; print only file, commit and a
two-character mask. Adopt gitleaks in CI so the next commit that adds a secret fails the build.
