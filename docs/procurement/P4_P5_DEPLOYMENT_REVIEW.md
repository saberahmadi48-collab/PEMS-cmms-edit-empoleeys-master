# P4 + P5 database deployment review (for separate approval — nothing here has been applied)

Status: **review only.** No migration, DDL, DML, cleanup or data repair was run against any shared or real database. All write-style rehearsals ran on disposable databases that were dropped afterwards.

## 1. Which database is the target? — NOT established (stop condition)
The repository points at two different things named `PEMSDB`:

| Candidate | Evidence | Inspected? |
|---|---|---|
| Local developer instance `localhost\SQLEXPRESS`, database `PEMSDB` (clone: `PEMSDB_PcatClone`) | `PEMS.Web/appsettings.json` | read-only, reference only |
| `PEMSDB` inside the Docker SQL Server on the configured Linux test VM | `deploy/` kit (`pems-docker.sh db-backup/db-apply` always target `PEMSDB`); a real host is configured in the git-ignored `deploy/targets.local.json` | **not reachable from here and not touched** |

Because the request did not name the target and the VM's database state is unknown, the diagnostics are delivered as a script to run against the **explicitly confirmed** database: `legacy-trash/manual-sql/docs/procurement/P4_P5_PREFLIGHT_DIAGNOSTICS.sql` (SELECT-only; section 0 prints the server and database actually connected to). The local reference run (read-only) is in §6.

## 2. Pending migrations, order and dependencies
Code has 105 migrations; both local databases have 103 applied (last `20261008075704_OrganizationAssignmentEmployeeIndex`). The pending set is exactly:

1. `20261010141504_MaterialStockNoLocationUniqueness` (**P4**) — filtered unique index on `MaterialStocks`; first statement is a pre-flight `THROW` if duplicate NULL-location rows exist.
2. `20261011082322_ReplenishmentRecommendations` (**P5**) — four new tables, indexes, one CHECK constraint (all additive).

Dependencies: P5 only needs existing `MaterialItems` and `Warehouses` (FKs) — no dependency on the P4 index. Apply **P4 first** anyway (the duplicate diagnostic is its gate and P5 results are only trustworthy on clean stock rows), then P5. Both are reversible (`Down`: drop index / drop the four tables).
**The target must already have everything up to `20261008075704` applied**; if its last migration is older, other pending migrations exist and must be reviewed first — use the kit's idempotent script (`deploy.ps1 -Action MigrationScript`, then `-FromMigration <last applied>` for the delta).

Artifacts (generated without a database connection): `legacy-trash/manual-sql/docs/procurement/P4_P5_delta_after_20261008075704.sql` (P4 then P5, each in its own transaction), `MaterialStockNoLocationUniqueness.migration.sql`, `ReplenishmentRecommendations.migration.sql`.

## 3. Rehearsal evidence (disposable databases only)
Two databases created at the pre-P4 schema (103 migrations):

| Case | Command | Result |
|---|---|---|
| Clean data | `sqlcmd -b -I -i P4_P5_delta_after_20261008075704.sql` | exit 0; history 105; P4 index present; 4 P5 tables |
| 2 duplicate NULL-location rows | same | **exit 1**, `Msg 51000 P4 pre-flight failed… Nothing was changed`; history still 103, no index, no P5 tables, both stock rows intact (5 and 9) |
| same, **without `-b`** | `sqlcmd -I -i …` | still nothing persisted (the open transaction is rolled back on disconnect), but **always use `-b`** so the run stops at the first error |

Other evidence: EF Up → Down → Up on a disposable database; the P4 pre-flight failing without changing data; a full HTTP smoke of the application against a migrated disposable database (19/19 checks). `-I` is required (filtered indexes need QUOTED_IDENTIFIER ON); the kit's `db-apply` already uses `-b -I`.

## 4. What else happens when the new application build starts
The application does not migrate on startup, **but its startup seeding writes data** (DML through the app, not DDL): `PermissionService.SeedDefaultsAsync` adds one `RolePermissions` row — `WH.Manager` / `ReplenishmentRecommendation` / View+Create+Edit — if absent, then `AccessControlSeeder.SeedFromLegacyAsync` syncs it to the grant tables. It never modifies or removes an existing row (verified by test: all other permissions are byte-identical after the upgrade; an administrator's edit to the new row is never overwritten). Order: **migrations first, then start the new build** — if the build starts before the P5 migration, the P5 pages fail for users who hold the new permission (SystemAdmin, `WH.Manager`) until the tables exist; the rest of the application is unaffected.

## 5. Backup and rollback requirements (to be met before approval is requested)
**Before:** (1) maintenance window; stop or quiesce writes to inventory (receipts, SR issue, MRS import, production posting) while P4 runs — the index build takes a short schema lock on `MaterialStocks`. (2) Full verified backup immediately before: `BACKUP DATABASE … WITH COPY_ONLY, CHECKSUM, COMPRESSION` then `RESTORE VERIFYONLY … WITH CHECKSUM`, copy off the host; the kit's `db-backup` does exactly this and `db-apply` refuses a backup older than 3600 s. (3) Record `SELECT MigrationId FROM __EFMigrationsHistory` and the §4 row counts. (4) Run the diagnostics; **section 2 must return no rows**; reconcile any duplicates manually with the warehouse owner (never automatically). (5) A reviewer signs off the exact script that will run (`--confirm-reviewed`).

**Rollback options:** *Application only* — the previous build ignores the new tables, index and permission row (all additive), so rolling the application back needs no database action (the kit's `rollback` deliberately does not revert the database). *Schema* — `Down` of P5 drops the four tables (loses recommendations/history created since) and `Down` of P4 drops the index; run only if needed, after a fresh backup. *Data/hard failure* — restore the verified backup (loses everything written after it; hence the write quiesce). A failed P4 pre-flight needs no rollback (nothing was changed).

## 6. Local reference run (read-only, NOT the deployment target)
`PEMSDB`: 103 migrations, 0 `MaterialStocks` rows, 0 duplicate groups, no P4/P5 objects, 22 roles, no `ReplenishmentRecommendation` permission rows. `PEMSDB_PcatClone`: same counts. The rehearsal database with seeded duplicates correctly reported the blocking group in section 2. An empty local result says nothing about the Linux test VM or production.

## 7. Approval needed before any deployment
(a) The explicit target (server + database) and confirmation of its last applied migration; (b) the diagnostics result for that target; (c) backup/rollback plan acknowledged; (d) maintenance window; (e) the three unresolved role identifiers (see the P5 document §5) if their permissions should ship in the same release.
