# 01 - Engineering standards (read for every task)

Origin of each rule is given in brackets: `R<n>` = Instructions rule n in `plan/sheets/instructions.md`.

## 1. Architecture

Clean Architecture; dependencies point inward.

| Project | Responsibility |
|---|---|
| `PEMS.Domain` | entities, enums, `BaseEntity`; **no dependencies** |
| `PEMS.Application` | DTOs (`DTOs/`), interfaces (`Interfaces/`), application services (`Services/`) |
| `PEMS.Persistence` | `PEMSDbContext`, EF configurations (`Configurations/`), seeders, repositories, installer/updater |
| `PEMS.Migrations` | EF migrations, model snapshot, design-time factory (references Persistence; Web and Tests reference it; the context defaults `MigrationsAssembly` to it) |
| `PEMS.Infrastructure` | cache, external integrations |
| `PEMS.AI` | Ollama, OCR, parsing, extraction (local only) |
| `PEMS.WorkflowPlatform` | the one workflow engine |
| `PEMS.Web` | MVC controllers, views, areas `Admin`, `Warehouse` |
| `PEMS.Shared` | shared helpers |
| `PEMS.Tests` | unit + integration tests |

Rules:

- **Business logic never in controllers or views.** Controllers are thin: bind, authorize, call a service, map to a view model.
- **Engines once** [R3]: metadata, forms, workflow, access/RLS, events, reporting and licensing each have exactly one implementation. Modules call it. Parallel concepts (a second workflow, a second permission model, a second grid) are forbidden.
- **No new static modules** [R4]: a new module starts as metadata (form/view/workflow). If impossible, write an ADR ([plan/adr](../../plan/adr/README.md)) stating the reason and the missing extension point *before* building.
- **Extend, don't duplicate.** Inspect existing entities and controllers first; follow existing folder structure and naming exactly. (Example: Equipment -> Instrument hierarchy is extended, not paralleled.)
- **Central RLS** [R12]: every data path (list, search, report, export, API, AI) goes through the central row-level filter. No direct role checks.
- **Go forward, not backward-compatible** [R6]: no compatibility shims unless a live customer needs one.
- **No customer-specific forks** [R17]: differences live in licence, configuration, customer metadata packages and extension packs only.

## 2. Code style

- Match the surrounding code: naming, comment density, idiom. Read two neighbouring files before writing a new one.
- English identifiers; C# naming per .NET conventions; one type per file; async all the way (`Async` suffix, pass `CancellationToken` in service methods that do I/O).
- Domain entities inherit `BaseEntity` (`CreatedBy`, `ModifiedBy`, `RowVersion`, `TenantId`) unless there is a stated reason. Soft delete via `IsActive` where the module uses it. **History records (calibration, maintenance, audit) are append-only - never hard-delete or overwrite.**
- Lists use `PagedResult<T>` and one server-side paging contract (Lessons 4). Prefer enums or lookup tables over free strings.
- Persian XML comments on domain entities explaining business/ISO rules are the house style; everything else is English.
- Comments explain *why*, not *what*. No commented-out code, no TODO without a Jira key.
- Fix clearly poor code you pass through, within scope [R15].
- Dead or superseded files go to `legacy-trash/`, not deleted, until the owner agrees [R23].

## 3. Database [R6, R23]

- **Schema only through migrations.** `dotnet ef migrations add <Name> --project PEMS.Migrations --startup-project PEMS.Web`. Never edit an applied migration; add a new one. DDL EF cannot model goes into `migrationBuilder.Sql`.
- The app applies migrations itself (startup migrator / Admin > Database Update). No hand-run SQL for schema setup or update.
- CI "model drift" gate: `dotnet ef migrations has-pending-model-changes` must be clean - an entity change without a migration fails the build.
- **Every task that changes an entity or a mapping ships its migration in the same pull request** (owner decision 2026-10-27, SCRUM-413): the model, the migration and the snapshot move together, so the migrator applies the change on staging, demo and client servers and the database never lags behind the code.
- **Migration chain gate** (`.github/workflows/migration-chain.yml`, `scripts/ci/test-migration-chain.sh`): the chain is applied to a real SQL Server (empty install, then upgrade from `origin/main`) and must pass before a change goes to `staging` or `demo`. Run it by hand from the Actions tab when your change adds a migration. A migration that fails there is fixed by a new migration, never by editing the applied one.
- A migration or a pipeline step never inserts, changes or deletes accounts or passwords (section 6; `AccountSafetyOfMigrationsAndPipelinesTests`). Take a backup before updating a database that holds real data.
- **Upgrade from every supported release** (SCRUM-280): `deploy/db/supported-releases.json` lists the database states we must upgrade from (at least N-2); `DatabaseUpgradePathSqlTests` upgrades each, with data, and compares it with a fresh install (same workflow as the chain gate). When a release ships, add it there and its checksum lines.
- **Released migrations are immutable** (`deploy/db/released-migrations.sha256`, `MigrationCompatibilityRulesTests`). Breaking changes go expand-then-contract over two releases (add and copy, switch the code, drop later); a NOT NULL column on a filled table needs a default or a data step. Rules and the rollback procedure: [CODE_FIRST_DATABASE.md](../deployment/CODE_FIRST_DATABASE.md) "Compatibility rules", "Rollback".
- Environments with real data (`production-*`, `uat`) set `Database__BackupBeforeUpdate=true`: every update first takes a COPY_ONLY backup and stops if it fails; the backup is the rollback point.
- **Decided, not built yet - do not contradict** (owner decision 2026-10-27; plan rows are the scope): a migration that drops or narrows a column, deletes or truncates data, or rewrites values needs an explicit `// data-loss-approved: SCRUM-xxx` marker and must keep a per-table checksum of the unchanged columns (#384, SCRUM-414); after a merge into `staging` or `demo` the pipeline proves the commit's newest migration is the applied one, and a failed start-up migration leaves a maintenance page and a 503 health state instead of a crash (#385, SCRUM-415). Until they exist, a migration review checks these by hand.
- **Prove a new pipeline step before it can block a deploy.** A workflow cannot be dispatched from its branch until it is on the default branch (API answers 404), and a gate wired into `deploy-site.yml` that has never run can stop every staging/demo deploy. Merge the workflow first as manual-only, show one green and one deliberately red run (a throw-away branch with a failing migration that is never merged), then wire it in with a second pull request (SCRUM-413).
- **A transaction you open runs inside the execution strategy** (SCRUM-497). The app connects with `EnableRetryOnFailure`, which refuses `BeginTransaction` outside `Database.CreateExecutionStrategy().ExecuteAsync(...)`; InMemory tests cannot see this. Use `PostingScope` or the EF Core pattern (apply the changes once, then inside the strategy: begin, `SaveChangesAsync(acceptAllChangesOnSuccess: false)`, commit; `AcceptAllChanges()` after it), and cover the write with a SQL Server test that uses `EnableRetryOnFailure` (`OrganizationImportSqlServerTests`, `WorkOrderConcurrencyGateTests`).
- Seeders are idempotent. Reference data goes in seeders or migrations, not in hand scripts.
- **Document numbers come from `INumberingService`**, never from `Count()+1` or a typed value alone. Allocation is atomic (`NumberingSequences`). Procurement documents (MR, package, tender, bid, PO, contract) use the administrator's pattern (`NumberingSchemes`, Admin > Numbering, SCRUM-40). A create service calls `ResolveNumberAsync(type, typedNumber, projectId, disciplineId)`, which keeps a typed number only when the active scheme allows manual entry (no active scheme: the typed number, required as before), skips numbers that already exist, and never renumbers existing documents. A create form shows the field with the `DocumentNumberField` view component. Import paths keep the number from the source document.
- Dynamic/BPMS storage direction (hybrid: real tables for user-built entities, JSON column for custom fields) - see chapter 04.

## 4. Settings and secrets [R24, R10]

- Every setting lives in `env/<name>.env` for **every** environment (`local`, `docker`, `demo`, `uat`, `production-<customer>`) and is read with `Required*`.
- Secrets only in `env/<name>.secrets.env` (git-ignored) or environment variables; add the key to `*.secrets.env.example`.
- No environment-dependent value (URL, host, credential, port, lifetime, culture, policy flag) hard-coded. `appsettings.json` stays empty.
- No real secret, key or connection string anywhere in the repo, package, log, Jira comment or chat. The licence-signing private key never touches a development machine.
- A test keeps env key sets identical and scans committed files for secrets; a new setting that is missing from one file fails it.
- Before moving a literal into settings, classify it ([docs/design/CONFIGURATION_CONSTANTS.md](../design/CONFIGURATION_CONSTANTS.md)): only environment- or customer-dependent values are settings; policy constants and domain rules stay in code.

## 5. Offline product [R1]

Production has no internet: no CDN, no external call, no telemetry, no online licence/update check. A new library must run fully offline and must not need an online source at build time. Static assets are served from `wwwroot`. (Website and demo are the only online parts and are separate deployables [R18].)

**Enforced** (SCRUM-20): `OfflineAssetsTests` fails when a view, area view, `PEMS.UI` file, script or stylesheet names an external host (allow-list: XML namespaces and documentation or placeholder text, in the test), and `tools/UiAudit/pattern-audit.py` exits with an error on a CDN reference. Vendor a library under `wwwroot/lib/<name>/` with its licence file and a `README-PEMS.txt` (source, version, SHA-256, upgrade steps) and add it to the licence inventory (row #121). Take the files from the package registry (npm tarball), not from a CDN. Libraries that ship only ES modules (pdf.js 4.x) are loaded through a small module in `wwwroot/js/` that sets the global and resolves worker paths relative to itself, so a virtual directory keeps working; the page scripts that use them become `type="module"` (they run after parsing, in order) and must be valid strict-mode code.

## 6. Security

- **One validator** (SCRUM-372, 373, 380): every write path (form, API, import) checks a field with `PEMS.Application.Validation.FieldValidator` and reports the stable constraint codes of [FIELD_VALIDATION_CATALOG.md](../architecture/FIELD_VALIDATION_CATALOG.md) in the one RFC 9457 error list; a new constraint is a new code, a new case and a catalog row (a test keeps them in step), never a hand-written check in a controller or service.
- Validate on the server; encode output (Razor does by default - never `Html.Raw` on user data); antiforgery on every state-changing request (including `fetch`).
- Authorization at the service/policy level, not only by hiding UI. Check RLS (section 1) on every new query.
- No string-built SQL; use EF/parameters.
- **Uploads (SCRUM-202):** every action that receives a file carries `[UploadPolicy(UploadProfile.X)]`; the global `UploadGuardFilter` runs `IUploadGuard` (allowlist in `UploadPolicy`, size from `Uploads__MaxFileSizeMb`, content check, `IMalwareScanner` hook) before the action. Do not write your own extension/size checks and never write into `wwwroot`: store through `IFileStorageService` (images shown in pages through `IProtectedImageStore`), serve through `FilesController` (`Download`, `Preview`, `Image`) so `SafeFileHeaders` adds nosniff, CSP and Content-Disposition. A new file type goes into `UploadPolicy.Types` and a profile. `UploadGuardTests` fail when an upload action has no policy.
- Never log secrets or personal data; use Serilog structured logging.
- AI features run on-premise (Ollama/Tesseract); nothing leaves the organisation.
- **A deploy, update or migration never creates, resets or changes a password** (owner decision 2026-10-27, SCRUM-410). This holds for staging, demo and every client server. The start-up seeder runs only on an empty user table (a fresh install or an explicit database reset, which restarts the first-install administrator flow with a forced password change); `DevAdminPasswordReset` needs Development plus `PEMS_DEV_RESET_ADMIN_PASSWORD=true`. Deploy workflows and the client update kit may write the secrets file, copy files and run schema migrations, nothing else. A new start-up step, workflow step or migration that touches `AspNetUsers` credentials breaks this rule; `AdminPasswordSurvivesUpdateTests` guards it and must stay green; `AccountSafetyOfMigrationsAndPipelinesTests` extends the guard to migrations, workflows and deploy scripts (SCRUM-413). Prove that such a guard test guards: switch the guard off, expect red tests, restore it (a test that seeds the same user name twice passes even without the guard because the duplicate fails harmlessly - pair it with a case where a different user exists).
- **Password history** (SCRUM-412, built): a new password - the forced first change, a voluntary change and every reset path (administrator, second administrator, recovery code, break-glass) - must differ from the current one and the last N (`Authentication__Password__HistoryCount`, default 5, `0` = off). One mechanism: `PasswordHistoryValidator` (Identity password validator) refuses it with a localized message and `PemsUserManager` stores the replaced hash in table `PasswordHistory` (hashes only, trimmed to N). Set passwords only through `UserManager` (`PasswordHistoryTests` fails when code assigns `PasswordHash` or hashes by hand); the development reset is the only user of `PasswordHistoryBypass`.
- **Decided, not built yet - do not contradict** (owner decision 2026-10-27): the first administrator is created in the UI, not with a generated password: when the user table is empty and no `Seed__Admin__Password` is set, every request goes to `/setup` until the administrator chooses user name, e-mail and password (policy and history apply) as the last install step, guarded by a one-time setup code on the server; `/setup` is 404 once an administrator exists and an update never reaches it; a database reset shows it again; a password from an env or secrets file (staging, demo, `Seed__Admin__Password`) creates the administrator with a forced first change, and an administrator password is never part of a client build (#381, SCRUM-411, part of the first-run wizard #169, SCRUM-188).

## 7. Localization [R5]

Product default is Persian, RTL; displayed dates Jalali, stored Gregorian/UTC. All user-visible text through localization keys (resources) - none hard-coded in views or JS. Test every screen in `fa` (RTL) and `en`.

## 8. Tests and Definition of done

Tests: `dotnet test PEMS.slnx`. Many need SQL Server and use the isolated database `PEMSDB_RegressionTest` (never `PEMSDB`); set `PEMS_TEST_CONNECTION` for another server.

A task is **done** only when:

1. Every *Done when* item is met and demonstrated (file:line, commit, test name or screenshot) [R13].
2. New behaviour has tests: unit tests for logic, integration tests for persistence/query paths, a regression test for each fixed bug. UI changes are verified in a real browser (EN + FA, desktop + 375 px) - not just compiled.
3. `dotnet build PEMS.slnx -c Release` and `dotnet test PEMS.slnx` are green; the model-drift gate is clean; nothing previously passing is now skipped or deleted. If the author cannot run them (no SDK in a cloud session), the run is line 1 of the QA checklist and the issue is not Done until it is green ([chapter 07](07-qa-check-in-the-ui.md) section 2a).
4. Offline, settings, RLS, localization checks above hold.
5. The six artefacts (R22) are updated: guideline/memory, plan file, **Supportability Matrix** [R21], Instructions (only if a rule changed), `CHANGELOG.md`, commit.
6. Test Plan: a Done row whose Test Plan status is "Needs QA" is not trustworthy [R14] - add or update the case.
7. After the merge and the move to In Review, the QA check in the UI passes ([chapter 07](07-qa-check-in-the-ui.md)); only then the issue is Done.

What a session runs and what goes to the checklist (SQL Server, full suite, browser) is in [chapter 07 section 2b](07-qa-check-in-the-ui.md); without SQL Server only SQL-safe changes are made.

## 9. Git

- One logical change per commit; message `SCRUM-123 type(scope): summary` (types: feat, fix, refactor, docs, test, chore, ci). *Note:* Instructions rule 7 still says `vX.Y.Z: description`; rule 25 (2026-10-23, issue key first) is newer and was confirmed by the owner on 2026-10-25 (chapter 06, decisions).
- One short-lived working branch per task or approved batch, created from `main`; delete it (local and remote) once merged - details in chapter 00 sections 3-4.
- Stage specific files only. Re-run `git status` before every commit.
- Version bumps: system = CalVer `YYYY.MM.PATCH`, components = SemVer; editing the plan files does not change a version [R9].
- Plan files (`plan/rows`, `plan/sheets`) change only by pull request; edit the file or use `plan_sync.py set/report/new`, then run `python tools/plan-sync/plan_sync.py check`.
