---
name: pems-migration-guard
description: Before touching the PEMS schema, a query, a transaction or a database service: the migration rules, the drift gate, and when a session without SQL Server must stop.
---

# PEMS schema and database changes (guideline 01 "Database", 05; AGENTS.md five rules)

- **Schema only through a new migration** in `PEMS.Migrations`; never edit an applied one. An entity or configuration change without a migration fails the CI drift gate: `dotnet ef migrations has-pending-model-changes --project PEMS.Migrations --startup-project PEMS.Web --no-build`.
- **A session without SQL Server changes views, components, scripts, documents and plan rows only.** A schema change, a migration, a query, a transaction or a service that touches the database waits for a session with SQL Server, or becomes its own task whose QA line 1 is the SQL test run. Say so in the plan instead of guessing.
- **Safe by default:** additive migrations (new nullable column or new table) first; no data-destroying step without a backup line in the QA checklist; seed data through the seed classes, not SQL in a migration unless unavoidable; the app installs the schema on an empty database, so a new table needs no manual script.
- **Concurrency and errors:** a unique key or row-version conflict is a `ConflictException` (code `unique`, `concurrency`), a deadlock or timeout an `UnavailableException`; never let SQL text or `ex.Message` reach a user (`pems-errors`).
- **Tests:** InMemory tests prove logic, not translations or constraints; the SQL families (names with Sql, LiveDb, Concurrency, Migration, Numbering, Atomicity, RealSql) are the end-of-sprint QA line and are never run in a session without SQL Server (`pems-test-gates`).
- Order of work: entity + configuration, migration, drift gate clean, service, tests, then the view.
