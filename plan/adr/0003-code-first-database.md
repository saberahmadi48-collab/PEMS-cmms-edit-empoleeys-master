# ADR-0003: Fully code-first database

- **Status:** Accepted
- **Date:** 2026-10-22
- **Decided by:** Hamid Maleki, project owner and Member A (Instructions rule 23, "Owner decision 2026-10-22"); implemented under plan row #309
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #309 (implementation, Partial), #310 (update service), #311 (legacy SQL retired), #312 (demo install); written under #2 (SCRUM-14)
- **Sources:** Instructions rules 6 and 23; `plan/rows/0309.md`; [CODE_FIRST_DATABASE.md](../../docs/deployment/CODE_FIRST_DATABASE.md)

## Context

The model was code-first with a migration history, but nothing applied the migrations at startup (`Program.cs` only ran seeders) and
full-text search was a manual SSMS script with a hard-coded `USE PEMSDB`. Installing a new environment or updating a release depended on
hand-run SQL, which does not scale to customer installations (UI-only operations, rule 20) or to the demo pipeline.

## Decision

The database is **fully code-first**:

- A new environment is installed, and every release updated, by the migrator at startup or from Admin > Database Update. There is no hand-run SQL for schema setup or updates, no `InitialCreate.sql`, no SSMS script and no manual `dotnet ef database update` (rule 23).
- DDL that EF cannot model goes into a migration (`migrationBuilder.Sql`); full-text search is the migration `FullTextSearch`.
- Applied migrations are never edited; a change adds a new migration (rule 6). A model change without a migration fails the CI "Model drift gate".
- Superseded SQL moves to `legacy-trash/`; it is not deleted until the owner agrees.
- Settings `Database:CreateIfMissing` and `Database:MigrateOnStartup` control the behaviour; environments that need reviewed scripts set `MigrateOnStartup=false` and use the Admin page or `deploy.ps1`.
- `GET /health/database` is `200` only when `UpToDate`, for pipelines. An unmanaged schema or a database newer than the application is reported and never modified.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Hand-run schema SQL (`InitialCreate.sql`, SSMS scripts) | Was the state before; manual, environment-specific (`USE PEMSDB`), and drifts from the model. |
| Manual `dotnet ef database update` per environment | Needs tooling and developer knowledge on every target; not completable in a UI (rule 20). |

## Consequences

- Good: an empty SQL login yields a working PEMS; the demo and staging pipelines wait on `/health/database`; drift is caught in CI.
- Cost: a bad migration cannot be undone by an application rollback (restore from backup); SQL Server Full-Text must be installed before the first run because an applied migration is not re-run.
- Status of verification (row #309, 2026-10-22/23): verified on LocalDB; not yet verified: the CI drift gate on GitHub, a restart no-op in the full app, hosting on the real demo server. The row stays `Partial`; this ADR records the decision, not the verification.
- Hybrid storage ([ADR-0001](0001-hybrid-storage-for-user-built-entities.md)) guided DDL must fit this rule.
