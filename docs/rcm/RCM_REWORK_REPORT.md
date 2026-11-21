# RCM / Maintenance Strategy rework — audit & verification report

Standards: SAE JA1011 (RCM), ISO 14224, ISO 55001, ISO 9001, EN 13306.
Branch: `feat/cmms-passport-global-page-header` (working tree; nothing committed).

## 1. Audit table (issue → confirmed? → root cause → fix)

| # | Confirmed | Root cause (file) | Fix |
|---|-----------|-------------------|-----|
| 1 | Yes — strategy #9 in PEMSDB is RunToFailure + Safety, no failure mode, no justification | `MaintenanceStrategyController.Create` stored raw form values, no rule engine | `StrategyRules.Validate` (RcmRules.cs): RTF/Corrective rejected for Safety/Environmental/Hidden; enforced in `MaintenanceStrategyCommandService` for UI, Import and RCM bridge |
| 2 | Yes | `FailureModeCode` optional string | `FailureModeId` mandatory for every type (FK to ISO 14224 catalogue) |
| 3 | Yes | `Justification` optional | Mandatory, ≥ 20 chars, shown in list/details |
| 4 | Yes — duplicate was silently redirected | key was Equipment+FailureModeCode only | Duplicate = same class + failure mode + type among live rows → visible `Rcm_Err_DuplicateStrategy` (400 form) |
| 5 | Partly — `equipmentId` 500 was reachable on **plan** create (FK exception); strategy accepted any string | no server validation | All fields validated server-side; unknown refs return error codes → 400 (form) / 404 (unknown ids); no equipmentId is accepted any more |
| 6 | Yes — rows hold "پیشگیرانه", "روزانه", "Economic"… | dropdown labels persisted | Language-neutral codes (`RcmCodes`), `LegacyMaps`, idempotent data migration, display via `Rcm_<Group>_<Code>` resources |
| 7 | Partly — catalogue already existed (70 classes, 33 modes, 18 causes) but the strategy form used a hard-coded 7-item list and the legacy WO modal too | catalogue not wired | Strategy uses `FailureModeId`; picker filtered by class chain; catalogue admin UI (`FailureModeCatalogController`); WO completion lists are the catalogue filtered by the WO equipment class |
| 8 | Yes | thin entity | Added: functional failure, effect, hidden flag, CBM parameter/thresholds/unit, P-F, inspection interval, task type, owner discipline, skills, duration, review date, source |
| 9 | Partly — **`EquipmentCriticalities` has 0 rows in PEMSDB** | criticality never used | Class A forbids RTF/Corrective (assign + activation); WO priority = consequence × criticality; suggested interval factor A 0.5 / C 1.5; coverage shows "Unrated" honestly |
| 10 | Yes | no edit action | Edit (Draft in place with RowVersion check, Approved/Active → new version) + `MaintenanceStrategyHistory` (before/after) |
| 11 | Yes | no lifecycle | Draft→InReview→Approved→Active→Superseded/Retired via `IWorkflowRuntime` (`MaintenanceStrategyWorkflowSeeder`); author/submitter cannot approve; `ReviewDate` + "review overdue" badge |
| 12 | Yes | none | `FamilyId`/`VersionNumber`/`SupersedesStrategyId`; old version kept; plans show which version they use and an **Out of sync** flag |
| 13 | Partly — global `AuditLog` already captured raw changes | — | Verified by test + added business-level history for every lifecycle/assignment/override action |
| 14 | Yes — plan 5 exists on the RTF strategy | no check | `PlanRules`: plans only for Preventive/Predictive/CBM/FailureFinding; PM generation also skips RTF/Corrective plans |
| 15 | Yes (equipment/strategy posted separately; 500 on bad id). **Developer exception page: not a defect** — `Program.cs` only uses `UseExceptionHandler` outside Development and Docker/compose run `Production`; the 500 page the user saw came from the Development launch profile | `Create(int equipmentId, int maintenanceStrategyId…)` | Plan is created from an **assignment id**; equipment/strategy derived server-side; form shows tag read-only; unknown assignment → 404 view; `Error.cshtml` replaced by a localized page without environment advice |
| 16 | Yes (negative interval + 2020 due accepted) | no validation | Interval > 0 for Calendar/Both, meter type+interval for Meter/Both, past due date needs explicit confirmation, non-applicable fields normalized away |
| 17 | Yes (CBM plan with 30 d + meter) | no per-type rules | CBM/Predictive = calendar inspection only (and ≤ strategy inspection interval); Preventive = calendar/meter/both |
| 18 | Yes ("fvvsd") | none | ≥ 5 chars with letters, unique per equipment (case-insensitive), auto-suggested `Tag - FailureMode - Type` |
| 19 | Yes — plan 5 has `IntervalDays = NULL`, so `NextDueDate` could never advance (code only advanced when an interval existed) | `PMGenerationService` | Policy: **Fixed** (anchored on previous due, jumps to next future occurrence, no catch-up storm) and **Floating** (from actual completion, `ReconcileFloatingSchedulesAsync`); approved override interval honoured; open-WO guard kept and tested |
| 20 | Yes (Medium, empty discipline) | hard-coded `Priority = "Medium"` | Priority from consequence × criticality; `ResponsibleDisciplineId` + Maintenance org from strategy owner; skills note; Job Plan required for scheduled strategies (tasks/skills/materials copy unchanged) |
| 21 | Yes ("Preventive (Quarterly)" ×5) | label = type + frequency | Label = `Tag · Type · FailureMode · vN` |
| 22 | **Not confirmed server-side** — against live data `FT-6001`, `80-FT-6001`, `ft-6001` answer in 130–200 ms and return the tag; separators (`FT 6001`, `FT6001`) returned **nothing** | Search normalisation; the fallback loaded 500 arbitrary rows | Separator-insensitive tag matching, removed the 500-row padding, FTS only on relational providers; JS now has a 15 s timeout + stale-response guard so "Searching…" can never hang |
| 23 | Yes (data) / **No active leak** — junk is historical: 98 "RQ Disc", 55 "TQMOC", 45 "FPA Test Disc" rows; the current test base refuses PEMSDB (`PemsDbContextFactory`) | earlier suites ran on PEMSDB | `scripts/rcm/Invoke-RcmDisciplineCleanup.ps1` (dry-run default, confirm-by-name for apply, deactivate-only); bulk page lists active disciplines de-duplicated by name; suite re-run proved PEMSDB row counts unchanged |
| 24 | Yes | literals in views | 350 keys in both resx; formal wording; tests: every `Rcm_`/`Error_` key used in code exists in both cultures, equal key sets, Persian text really Persian (plus existing `LocalizationKeyCoverageTests`) |
| 25 | Yes (6 strategies / 21 625 equipment) | no report/import | `Coverage` page + CSV of critical equipment without strategy; Excel import (strategies, assignments) with row validation, preview → explicit commit, error report |
| 26 | Partly — permissions existed; no IDOR dimension in this code base (no per-record ownership / tenants) | — | Test enumerates every mutating endpoint for Viewer/Engineer; every POST asserted `[ValidateAntiForgeryToken]`; the client-supplied FK (equipmentId) was removed |
| 27 | Yes | tag shown inconsistently | `_EquipmentIdentity` partial (tag, name, class, area/unit, criticality) on strategy form, plan form, tag page, plan list, WO details; tag never free text |
| 28 | Yes | one strategy per tag | `MaintenanceStrategy` = class-level template; `MaintenanceStrategyAssignment` (+ documented override with independent approval); plans generated from an assignment; idempotent migration turns every tag-level row into template + assignment, ids preserved |
| 29 | Yes | bulk page created N strategies | `BulkCreate` is now class-level: class filter, strategy picker, criticality + conflict preview, atomic assign, per-tag skip report |
| 30 | Present — `EquipmentClass` hierarchy exists; 12 282 of 21 625 equipment classified | — | Catalogue depends on the class chain; coverage reports "unclassified" (9 343 to classify through the existing classification module) |
| 31 | Yes | one direction | `Tag` page (strategies, plans, open WOs) and strategy details (tags, version, plans) |

## 2. Behaviour changes worth knowing

* **Retiring a strategy** closes its assignments → linked plans stop generating work orders (previously deactivating a strategy did not touch plans).
* PM generation **skips** plans whose strategy is RunToFailure/Corrective or whose assignment is closed.
* Legacy rows that break the safety rule (strategy #9) migrate to **Draft**; weak-justification / non-catalogue rows stay Active but get `ReviewDate = now` (review queue).
* Plans whose equipment ≠ strategy equipment (none in PEMSDB today) are left unlinked, not rewritten.

## 3. Deliverables

* EF migration `20261013070629_RcmClassLevelStrategy` (additive only; data migration = `PEMS.Persistence/DataMigrations/RcmDataMigrationSql.cs`, idempotent, never deletes).
* `scripts/rcm/` — `Invoke-RcmDryRun.ps1`, `Apply-RcmMigration.ps1`, `Invoke-RcmDisciplineCleanup.ps1`, `New-RcmPatchSet.ps1`, SQL files. **Run order on PEMSDB:** dry-run → discipline dry-run → (backup) → `Apply-RcmMigration.ps1 -Execute`.
* **Note:** PEMSDB is behind the working tree — migrations 20261010…20261012 (other in-progress work) will be applied by the same `database update`.
* Dry-run on PEMSDB: 7 legacy strategies → 7 assignments, 3 plans linked, 1 strategy → Draft, 1 plan stops generating (plan 5), 0 failure modes map to the catalogue (all 7 enter the review queue).

## 4. Deferred (with reason / next step)

| Item | Reason | Next step |
|------|--------|-----------|
| Meter-trigger due logic | `RecordMeterReading` overwrites `NextDueMeterReading` with `reading + interval`, so a meter plan can never become due; separate defect outside the 31 items | Add `CurrentMeterReading`, due when `current ≥ NextDueMeterReading`, generation by meter |
| Automatic review-date notifications | UI badge only | Hangfire job → My Work inbox |
| Merge of referenced duplicate disciplines (Piping PI/PIP, 11 × "Other") | re-points FKs across many tables | Human-approved merge script |
| Equipment criticality data (0 rows) | master data, not code | Run the criticality assessment; coverage by criticality becomes meaningful |
| Per-record IDOR scoping | code base has no ownership/tenant model for these entities | Revisit with the organisation hierarchy |
| Print/PDF of strategy | not requested | — |

## 5. Verification

* **Build:** 0 errors (solution incl. Razor views).
* **Full suite (8 061 tests, run from an in-repo artifacts folder):** 8 053 passed. 8 failures: 1 transient SQL Server pipe error (`VDRImportServiceVendorResolutionTests`, infrastructure) and 7 `WorkOrderDetailsRenderTests` caused by a null `RequestServices` in my WO identity lookup — fixed; the 15 affected tests were re-run and pass. New RCM tests: rules (pure), lifecycle + workflow/SoD, assignments (atomic rollback, conflicts, override), plan validation, scheduling (Fixed/Floating, no catch-up), WO priority/discipline, coverage, catalogue, search, Razor render, real-SQL (data migration idempotency, filtered unique index, real RowVersion conflict), authorization/antiforgery, resource-key completeness.
* **Isolation:** PEMSDB row counts before/after the whole suite are identical (disciplines 249, strategies 7, plans 3, work orders 4, employees 73).
* **Applied to PEMSDB (2026-10-13):** backup `D:\pa\PEMSDB_before_rcm.bak` (COPY_ONLY, 255 MB) → `dotnet ef database update` (5 migrations: MaterialStockNoLocationUniqueness, ReplenishmentRecommendations, AddWorkOrderChangeLog, WorkRequestRiskFailureCapture, RcmClassLevelStrategy). Result: 7 strategies kept their ids, 7 assignments, 3 plans linked, strategy 9 → Draft, 1 plan (#5) no longer generates work orders.
* **Live query check (read-only) on the migrated DB:** list with default, page, `equipmentId`, `classId`, `status`, `type`, `q`; detail; coverage; plan list — all return correct rows.
* Import is registered in the Import Center (the only import entry point); `ImportCenterNavigationTests` counts updated 13→14 entries.
