# Phase 2 — P5 Persisted replenishment recommendations (manual execution, v1)

Status: implemented and verified on disposable databases. **The migration is created but not applied to `PEMSDB`, `PEMSDB_PcatClone` or any shared database.**
Role-to-permission assignments are **pending owner approval** (nothing is granted automatically). P6 and later phases are not started. Part Catalogue was not touched.

## 1. Owner decisions implemented (from the P5 approval)

| # | Decision | Where it is implemented |
|---|---|---|
| 1 | One open recommendation per item+warehouse, explicit lifecycle, source/stock traceability, RowVersion, request-key idempotency | filtered unique index `UX_ReplenishmentRecommendations_Live`; `BaseEntity.RowVersion`; `NetDemandRuns.RequestKey` unique index |
| 2 | Existing authorization/audit/UI/execution-strategy conventions; permissions defined, not granted | module `ReplenishmentRecommendation` (View / Create / Edit); `ProtectedActionGuard`; `CreateExecutionStrategy()`; resx FA/EN; sidebar permission gate |
| 3 | Manual execution only | `Run` is a POST action; no scheduler, no Hangfire job |
| 4 | No MR / PO / reservation / purchasing documents | `ReplenishmentIsolationTests` (source scan) + `RunAndLifecycle_NeverTouch...` test |
| 5 | Work Order demand only when attribution is verified; exclude and report otherwise | WO demand has no warehouse ⇒ never netted; the material is reported "not definitive", no recommendation (§4) |
| 6 | No invented reorder thresholds / lead times / unit conversions | recommended quantity = calculated shortage; incompatible units are blocking issues (P3) |
| 7 | Dismissed does not reopen just because the calculation ran again; deterministic rule for material change; history preserved | §3.3 |
| 8 | Ambiguous multi-location stock blocks definitive recommendations (Option C) | `ReplenishmentRules.Evaluate` treats `StockMultipleLocationRows` as blocking |
| 9 | Option A (reservation migration) not implemented | not touched |
| 10 | Own additive migration, tested on disposable DBs only | `20261011082322_ReplenishmentRecommendations`; SQL script `ReplenishmentRecommendations.migration.sql` |
| 12 | `WorkOrderConcurrencyGateTests` kept as an unresolved baseline issue | §8 |
| 15 | Stop after P5 | no P6 code, no conversion action, `Converted`/`Superseded` never written |

## 2. Data model (all additive; no existing table changed)

| Table | Purpose | Notable constraints |
|---|---|---|
| `NetDemandRuns` | one manual run: counters, scope, calculator version, bounded "not definitive" summary, optional request key | unique filtered `RequestKey`; `RowVersion` |
| `ReplenishmentRecommendations` | one row per (item, warehouse) recommendation; snapshot (usable, gross/covered/unreserved demand, shortage, recommended qty), fingerprint, definitive flag, dismissal baseline, close reason | **unique `(MaterialItemId, WarehouseId)` where `Status IN ('Open','Dismissed')`**; FKs Restrict; `RowVersion` |
| `ReplenishmentRecommendationSources` | demand lines behind **each revision** (append-only) | `CHECK` exactly one of `StoreRequisitionLineId` / `WorkOrderMaterialId`; **no FK** to those tables on purpose (a `WorkOrderMaterial` row disappears when a WO plan is edited, and an FK must not block that existing flow) |
| `ReplenishmentRecommendationHistories` | append-only audit of every create / update / dismiss / reopen / auto-reopen / close / not-definitive / definitive-again | indexed by recommendation + revision |

## 3. Behaviour

### 3.1 Run (explicit, `Create` permission)
`INetDemandService.CalculateUnmaskedForSystemAsync` (new, additive; unmasked because origins must be persisted — only this service calls it, enforced by a source-scan test) →
for every `(item, warehouse)` result:

* **not definitive** (any blocking issue, ambiguous stock rows, multi-location stock, incompatible/unknown unit, WO demand without warehouse, unknown lifecycle…) → **no recommendation is created**; it is listed in the run summary with its codes. An existing live recommendation is flagged `IsCurrentlyDefinitive = false` once (history `NotDefinitive`) and is restored by a later definitive run (`DefinitiveAgain`).
* **definitive, shortage = 0** → an existing live recommendation is **Closed** (`ShortageResolved`).
* **definitive, shortage > 0** → create (Open, revision 1) or, if the input fingerprint changed, a new **revision** (snapshot + sources + history); unchanged fingerprint → only `LastEvaluatedAt/LastRunId` move (no history, no new rows).
* a live recommendation with **no result at all** in scope → **Closed** (`NoRemainingDemand`). Keys outside the run scope are never touched.

Quantity = the stock-only shortage estimate. Incoming purchase supply is not modelled (P0 policy), and the UI says so on every page.

### 3.2 Lifecycle

| From | To | Trigger | Who |
|---|---|---|---|
| — | Open | first definitive shortage | run (`Create`) |
| Open | Dismissed | dismiss with mandatory reason, valid `RowVersion` | `Edit` |
| Dismissed | Open | manual reopen (valid `RowVersion`) | `Edit` |
| Dismissed | Open | **auto-reopen on a material change** (§3.3) | run |
| Open / Dismissed | Closed | shortage resolved / no remaining demand | run |
| any | Converted, Superseded | **not implemented** (reserved for P6+) | — |

### 3.3 Dismissal rule (deterministic, no invented threshold)
At dismissal the shortage and the sorted source keys (`S:{lineId}`, `W:{id}`) are stored. A later run reopens it **only if** `currentShortage > dismissedShortage` (strictly greater, however small) **or** a demand source that was not present at dismissal now contributes. Equal or smaller shortage, fewer sources, or merely re-running never reopens. Non-material changes still refresh the snapshot (new revision, history `Updated`) while the status stays `Dismissed` and the dismissal baseline stays fixed. The auto-reopen history row has no actor (system) and a note stating why.

### 3.4 Idempotency and concurrency
* `RequestKey` (≤ 64): a repeated run returns the first run's summary (`Replayed`), calls no calculation and writes nothing; two simultaneous runs with the same key → the unique index lets one win, the other replays.
* Unchanged inputs ⇒ zero inserts, zero history rows.
* Two simultaneous runs: the live unique index rejects the duplicate; the loser re-reads (fresh `ChangeTracker`) and updates instead; at most 3 attempts, then the run is recorded `Failed` with a message (never swallowed).
* Dismiss / Reopen require the `RowVersion` that was read; a stale version ⇒ `Conflict` message, nothing changes (on SQL Server a missing version is also refused).
* The whole run is one `SaveChanges` inside `Database.CreateExecutionStrategy().ExecuteAsync` (the app uses `EnableRetryOnFailure()`).

## 4. Work Order and ambiguous demand
Work Order requirements have no warehouse (P0). They are never compared with any warehouse's stock and never guessed into one. A material that has such demand yields a warehouse-less result (`NotComparableNoWarehouse`) and makes the warehouse results of that material non-definitive (existing P3 behaviour) ⇒ reported, not recommended. Ambiguous WO↔SR overlaps, conflicting duplicate lines and unknown statuses are blocking in P3 and therefore also reported. Attribution rules (D1) remain an open owner decision.

## 5. Authorization and audit
* Module key **`ReplenishmentRecommendation`**, standard matrix actions: **View** = see, **Create** = run the calculation, **Edit** = review (dismiss/reopen). Approve/Delete are unused (Convert belongs to P6).
* The module was added to the Permission Matrix module list (`PermissionMatrixController`) so the owner can assign it; **no role had a grant at P5 acceptance**; the first (owner-approved) assignment is in §11. `SystemAdmin` keeps the existing generic bypass (not added to `RequiresExplicitGrant`; Run/Review are not approvals).
* **Role-to-permission assignments:** owner-approved on 2026-10-11 — see §11 (one applied, three blocked on an unresolved role identifier).
* State-changing actions go through `ProtectedActionGuard` (role check + user identity + scope/SoD + one `AuthorizationAuditLog` row per denial, fail-closed); the service re-checks the permission (defence in depth). Every change is also in the append-only history table and in the generic `AuditLog`.
* Source identity is masked in the details page for users without `StoreRequisition.View` / `WorkOrder.View` (quantities stay visible).

## 6. UI
`Warehouse` area, `ReplenishmentController` (thin): list (status / search / definitive filters, paging, last-run summary with the "not recommended and why" list, Run button for `Create`), details (snapshot, notices, blocking codes, current-revision sources, history, dismiss/reopen forms). Sidebar link appears only with `View`. Strings are resx keys `Repl_*` (EN + FA); verified in both cultures with no unresolved keys. Existing UI-standard tests (60) pass.

## 7. Migration and data status
* `PEMS.Persistence/Migrations/20261011082322_ReplenishmentRecommendations` — four `CreateTable`s, indexes, one CHECK; `Down` drops the four tables. Snapshot change is purely additive. `has-pending-model-changes`: none.
* Script for the owner: `legacy-trash/manual-sql/docs/procurement/ReplenishmentRecommendations.migration.sql`.
* Tested on disposable databases only: Up → Down → Up (`ReplenishmentMigrationTests`), a full-application HTTP smoke (19/19 checks) against a disposable database with all migrations, then dropped.
* `PEMSDB` and `PEMSDB_PcatClone`: **not migrated** (re-checked at the end of the task: last migration `20261008075704_OrganizationAssignmentEmployeeIndex`, no P4/P5 objects).
* Order of application when authorised: P4 duplicate diagnostic → P4 migration → P5 migration (P5 does not require the P4 index, but its results are only trustworthy on clean stock rows).

## 8. Unresolved baseline issue — `WorkOrderConcurrencyGateTests`
`Double_submit_by_the_same_user_applies_exactly_once_50_times` failed once in a 1-hour full-suite run ("double-submit: 1/50 violated: #1 successes=0": neither of the two forced-race callers succeeded in iteration 1). It passed 3/3 in isolation and in a 1 211-test Inventory+Cmms run. `WorkOrderLifecycleService.cs` and the test file were identical to HEAD at that time. **It is not waived and not classified**: not reproduced, not shown to be unrelated. Hypothesis (unverified): the forced-race harness (`Barrier(2)`, two parallel contexts) is sensitive to thread-pool/SQL load during the very long suite. To make the next occurrence diagnosable, the assertion message now also prints each racer's error/exception (message only; logic unchanged).

## 9. Out of scope / deferred (unchanged)
P6 conversion to MR / procurement package; P7 PO lines and incoming supply; P8 scheduling and alerts; Option A (row-specific reservations); reorder-point semantics, lead time, unit conversion; PM/engineering/MR demand; automatic role grants.

## 10. Verification record
* Full suite on the final code: **2749 passed, 0 failed, 0 skipped** (51 m). `WorkOrderConcurrencyGateTests` passed in this run — it remains an intermittent, unclassified baseline issue (§8).
* P5 tests: rules (pure), service (InMemory incl. real P3 stack), disposable-SQL (schema, live uniqueness, CHECK, concurrent runs, concurrent request key, stale RowVersion; Up/Down/Up migration), controller authorization (real engine, denial audit, no default grants), isolation (source scan).
* P0–P3 re-verified before and after: 75/75 (`NetDemand*`, `UsableStockServiceTests`); UI-standard/language-purity tests 60/60.
* End-to-end HTTP smoke of the real application pipeline (Development host, `EnableRetryOnFailure()`) on a disposable database: 19/19 checks (login, list, run, request-key replay, details, masked/unmasked sources, stale-version refusal, dismiss, no reopen on re-run, manual reopen, anonymous denial) and EN/FA rendering with no unresolved resource keys. The database and the host were removed afterwards.
* Part Catalogue: 54 Part Catalogue files (including its migrations) are byte-identical to the pre-P5 baseline (SHA-256 comparison). Shared databases: no P4/P5 objects; migrations unapplied.

## 11. Owner-approved follow-up (applied after acceptance)

### 11.1 Role assignments for `ReplenishmentRecommendation`
Mechanism: the existing `PermissionService.SeedDefaultsAsync` → `SeedMissingRolesAsync` pattern (adds the missing `(role, module)` row only; never overwrites an administrator's edit), which `AccessControlSeeder` syncs to the grant tables. Visible and editable in the Permission Matrix.

| Approved role | Real identifier | Permissions | Status |
|---|---|---|---|
| Warehouse Manager | `WH.Manager` (display "Warehouse Manager"; exists in both local DBs) | View, Create, Edit | **applied** |
| Warehouse Clerk | none named "clerk". Candidate: `WH.Storekeeper` ("Storekeeper — warehouse operator"; a custom role "Inventory controlling team" also exists in `PEMSDB`) | View | **not applied — ambiguous, needs the identifier** |
| Procurement | **no such role exists** in the code or in either database. Closest: `ContractManager` ("contracts, procurement packages and tenders, including their approval") — rejected as a guess because it carries approval permissions | View | **not applied — role missing/ambiguous** |
| Maintenance Planner | two candidates: `MNT.PlanningExpert` (described as "Maintenance planner") and `MNT.PlanningHead` (leads planning, approves work requests) | View | **not applied — ambiguous** |

Per the instruction to stop on ambiguous identifiers, only the first row was applied. To finish, the owner states the identifier(s); each is then one `New(<role>, "ReplenishmentRecommendation")` line (View only) in the same seed block, plus a test line. Nothing else changes.
Verified (`ReplenishmentPermissionAssignmentTests`): `WH.Manager` has exactly View/Create/Edit through the real engine and one Permission Matrix row (no Approve/Delete/other flags); every other role in `SystemRoles` and `RoleCatalog` has none of View/Create/Edit/Approve/Delete on the module; `SystemAdmin` has only the generic bypass (no row, not in `RequiresExplicitGrant`); upgrading an existing database changes no other module's permissions or grants (including an administrator's manual edit); re-seeding is idempotent and never overwrites an administrator's change to the new row; `WH.Manager` gains no PurchaseOrder / ProcurementPackage / MaterialRequisition / Tender / Contracts / WorkOrder Create/Approve/Issue.

### 11.2 Work Order demand — requires warehouse attribution (visible)
Unchanged principle (never assigned by inference), now explicit: the code `RequiresWarehouseAttribution` is added to every result that has Work Order demand without a warehouse, either the warehouse-less result of the material or the warehouse results it makes non-definitive. It appears (a) as a highlighted warning block with the material code and quantity on the list page, (b) in the run's "not recommended and why" list, (c) on the details page of an existing recommendation that became non-definitive for this reason, and (d) in the run record. No recommendation is created for such results.

### 11.3 Calculated shortage only (unchanged)
Recommended quantity = calculated shortage; no reorder point, lead time, safety stock, rounding or unit conversion. Manual execution only; no scheduler.

### 11.4 Calculation time and input freshness
Recorded: the run's start and completion time (inputs are read between them, without snapshot isolation), and per recommendation `StockLastUpdatedAt` (max `MaterialStock.LastUpdated` of the active rows) and `DemandLastChangedAt` (max status-history change of the contributing store requisitions). A value is recorded **only if every contributing input has a timestamp**; a stock row without `LastUpdated`, a Work Order source (no change timestamp exists) or a requisition without history makes it `unknown`. The UI shows "unknown" and an explicit statement that this does not establish that the figures are current. Neither value is part of the fingerprint (no revision churn); both refresh on every evaluation.

### 11.5 Preserved
Dismissal rule, history, idempotency (request key, live unique index), RowVersion/concurrency, traceability, advisory-only isolation (source-scan tests extended: input tables are read with `AsNoTracking` only).

### 11.6 Migration
The never-applied P5 migration was regenerated to include the two nullable freshness columns: `20261011082322_ReplenishmentRecommendations` (previous id `…062859` no longer exists anywhere). Deployment review: `P4_P5_DEPLOYMENT_REVIEW.md`; read-only diagnostics: `P4_P5_PREFLIGHT_DIAGNOSTICS.sql`.
