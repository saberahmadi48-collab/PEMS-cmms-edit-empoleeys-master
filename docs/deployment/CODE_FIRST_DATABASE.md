# Code-first database

The database is built and upgraded entirely from code. An empty SQL Server login is enough to get a working PEMS:
no `InitialCreate.sql`, no `dotnet ef database update`, no SSMS script. The old hand-run SQL is kept for reference
in `legacy-trash/manual-sql/` (see its README); nothing there is executed.

Decision record: [ADR-0003](../../plan/adr/0003-code-first-database.md) (Instructions rule 23).

## First run vs update

`IDatabaseUpdateService` (`PEMS.Persistence/Servises/DatabaseUpdateService.cs`) looks at the migration history and
decides what the next run is:

| State | Meaning | Applied automatically? |
|---|---|---|
| `FirstRun` | no history, no tables: empty database | yes - installs the whole schema |
| `UpdateAvailable` | history exists, shipped migrations are missing | yes - applies only the missing ones |
| `UpToDate` | every shipped migration is applied | nothing to do |
| `UnmanagedSchema` | tables exist but there is no history (not created by this migrator) | **never** - reported, left untouched |
| `DatabaseNewerThanApplication` | history holds migrations this build does not know | **never** - deploy the matching/newer build |
| `Unreachable` | server or database cannot be reached | no |

Every attempt is written to `DatabaseUpdateRuns` (trigger, mode, from/to migration, result, error, who).

## Where it runs

1. **Startup** (`PEMS.Web/Program.cs`, `DatabaseInitializer`):
   - `EnsureDatabaseExistsAsync` creates the database if it is missing, before anything connects (Hangfire installs
     its own schema as soon as it is configured). If the login cannot create databases a warning is logged and
     startup continues (the database may be pre-created in a hosting panel).
   - `MigrateAsync` runs the service (`FirstRun` -> install, `UpdateAvailable` -> update) before the seeders.
     A failed install/update stops startup with the error; `UnmanagedSchema` / `DatabaseNewerThanApplication`
     only log a warning.
2. **Admin > System Monitoring > Database Update** (`/Admin/DatabaseUpdate`, role `SystemAdmin`): shows the state,
   pending migrations and recent runs, and applies the install/update on demand (backup confirmation required).
3. **`GET /health/database`** (anonymous, no details): `200` + `UpToDate`, otherwise `503` + the state name. Used by
   the demo pipeline to wait for the install/update to finish.

## Settings (`Database__*` in `env/<name>.env`)

| Key | Default | Meaning |
|---|---|---|
| `CreateIfMissing` | `true` | create the database when absent |
| `MigrateOnStartup` | `true` | install/update automatically at startup |
| `MigrationCommandTimeoutMinutes` | `10` | timeout of one migration command (and of the pre-update backup) |
| `BackupBeforeUpdate` | `true` in production/uat, `false` elsewhere | take a COPY_ONLY backup before every update (startup and Admin page); the update is not started when the backup fails (SCRUM-280) |
| `BackupDirectory` | empty | folder **on the SQL Server machine** for that backup; empty = the instance's default backup folder |

Environment variable form: `Database__MigrateOnStartup=false`. With it off, the schema changes only from the
Admin page (or an external process); earlier docs describe the reviewed-script flow (`deploy.ps1 -Action
MigrationScript` / `Migrate`) which still works for such environments. With `BackupBeforeUpdate=false` take a backup
by hand before updating a database that holds real data; an application rollback does not undo a migration.

## Full-text search

It is the migration `FullTextSearch` (full-text objects cannot run in a transaction, so it is non-transactional).
When SQL Server Full-Text Search is not installed the statements do nothing and search falls back to `LIKE`.
Install the feature **before** the first run: an applied migration is not re-run.

## Demo pipeline

`.github/workflows/deploy-demo.yml` (push/merge to `demo`): build, model-drift gate, tests, publish, inject the demo
connection string and `Database` settings, FTP upload, then poll `/health/database` until `UpToDate`
(first deploy = install, later deploys = update).

## Migration chain gate (SCRUM-413)

The drift gate only proves that the model and the migrations agree; it opens no database. The **Migration Chain Gate**
(`.github/workflows/migration-chain.yml`, script `scripts/ci/test-migration-chain.sh`) applies the chain the way the
application does at start-up, on a throw-away SQL Server container (random password, no secrets, nothing deployed):

1. **Install** the whole chain on an empty database.
2. **Upgrade**: apply the chain up to the last migration of the base ref (default `origin/main`, the previous release), then the rest, so the migrations of the change run on a database that already has the old schema.
3. The model and the migrations still agree (no pending model changes).

`deploy-site.yml` (staging and demo) runs it as its first job, so a failing chain blocks the upload; the base is the commit the branch had before the push (`origin/main` when unknown). When the base's newest migration is not in the build's chain (a rollback deploy of older code) the upgrade step is skipped with a warning and the install step still applies.
A failure names the migration that was being applied. Run it from Actions > *PEMS Migration Chain Gate* > Run workflow (branch = the branch to
check, `base_ref` = the previous release). The same workflow then runs the data upgrade tests of the next section. The same script runs on a developer machine with a local SQL Server:
`PEMS_CHAIN_SERVER_CONNECTION="Server=...;User Id=...;Password=...;TrustServerCertificate=True" bash scripts/ci/test-migration-chain.sh origin/main`.

Migrations and pipelines never touch accounts or passwords; `AccountSafetyOfMigrationsAndPipelinesTests` fails if one does.

## Rules for developers

- Change the model, then add a migration:
  `dotnet ef migrations add <Name> --project PEMS.Migrations --startup-project PEMS.Web`
- A task that changes the model ships its migration in the same pull request, and the Migration Chain Gate is green before the change goes to `staging` or `demo`.
- Never edit an applied migration; always add a new one (also for data fixes and non-model DDL such as the
  full-text objects: use `migrationBuilder.Sql`). Never touch accounts or passwords in a migration.
- CI fails (`Model drift gate`) when an entity change has no migration, and EF Core refuses to migrate a drifted
  model at startup.
- Reference data belongs in the seeders (`PEMS.Persistence/Seed`), not in hand-run SQL.
- Read-only diagnostics and data-repair tools under `scripts/` are operator tools, not schema setup.

## Supported releases and upgrade tests (SCRUM-280)

A customer database can be several releases behind. `deploy/db/supported-releases.json` lists the releases PEMS must
upgrade from (keep at least the last three, N-2). A release is its **migration set**: every migration of the chain up to
`lastMigration`, minus `notIncluded` - migrations merged later with an older timestamp, which that release never had
(for example `20261021210353_IncomingCorrespondenceRegistry` was merged after the 2026-10-22 build had shipped
`20261022152254_FullTextSearch`). EF applies such a migration on the next update, so the test must start from the real set.

`DatabaseUpgradePathSqlTests` (category `DbUpgrade`, run by the Migration Chain Gate workflow and locally with
`PEMS_TEST_CONNECTION`), for every listed release:

1. brings a throw-away database to exactly that migration set;
2. fills every table it can with one representative row (generic, from the catalog: required columns, parents before
   children; a refused table is reported, at least 60 % must take a row);
3. updates it with the real `DatabaseUpdateService`;
4. checks that nothing is pending, no table lost rows, and the schema (tables, columns, keys, indexes, foreign keys,
   checks, defaults, modules, full-text) equals a fresh install's.

It also proves the rollback point: a failed pre-update backup stops the update and leaves the database unchanged, and
restoring the backup returns the pre-update state.

**When you ship a release** (to a customer, UAT or the demo): add it to the manifest (`name`, short `commit`,
`lastMigration` = the newest migration of that commit, `notIncluded` = chain migrations older than that which the commit
lacks), then add the checksum lines `MigrationCompatibilityRulesTests` prints. To find `notIncluded`:

```bash
ids() { grep -E '/[0-9]{14}_[A-Za-z0-9_]+\.cs$' | grep -v '\.Designer\.cs$' | sed -E 's#.*/##; s/\.cs$//' | sort; }
comm -23 <(git ls-files PEMS.Migrations/Migrations | ids | awk -v l="<lastMigration>" '$0<=l') \
         <(git ls-tree -r --name-only <commit> | ids)
```

## Compatibility rules

- A migration in any supported release is never edited, renamed or removed: fix it with a new migration.
  `deploy/db/released-migrations.sha256` records them and `MigrationCompatibilityRulesTests` fails on a change.
- Migration ids are unique to the second and every migration keeps its Designer file.
- Expand, then contract: a change that would break the running code or old data is split. Add the new column/table
  (nullable or with a default) and copy the data in one release; stop using the old one in code; drop it in a later
  release. A NOT NULL column added to a filled table needs a default or a data step - the upgrade test fills tables, so
  it catches the omission.
- Data moves are written to be re-runnable and set-based (`UPDATE ... FROM`), wrapped in `EXEC(N'...')` when they use a
  column added in the same migration (see `deploy/Convert-EfScript.ps1`).
- A migration never touches accounts or passwords (`AccountSafetyOfMigrationsAndPipelinesTests`).

## Rollback

An update is rolled back by restoring the pre-update backup and deploying the previous application version.
With `BackupBeforeUpdate=true` the file is `<database>_preupdate_<yyyyMMdd_HHmmss>.bak` (UTC) in `BackupDirectory` or
the instance's default backup folder; Admin > Database Update shows it in the run history and the log names it.

1. Stop the application (or the container) so nothing writes to the database.
2. On the SQL Server (sqlcmd or SSMS, as a login allowed to restore):

   ```sql
   ALTER DATABASE [PEMSDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   RESTORE DATABASE [PEMSDB] FROM DISK = N'<path from the run history>' WITH REPLACE, CHECKSUM;
   ALTER DATABASE [PEMSDB] SET MULTI_USER;
   ```

3. Deploy the previous application version and start it. Its startup check sees an up-to-date database
   (`/health/database` = 200). Starting the new version again would simply re-apply the update.

The backups are not deleted automatically; remove old ones by hand once the update has been accepted. A login that may
not back up (shared hosting) keeps `BackupBeforeUpdate=false` and takes the host's backup before an update.
