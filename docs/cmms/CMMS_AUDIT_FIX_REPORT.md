# PEMS CMMS audit & fix — verification report (2026-10-13)

Scope: Work Request → Work Order lifecycle, User/Employee admin, dashboard, seed/test-data hygiene, security. Branch `feat/cmms-passport-global-page-header`. Nothing was committed, pushed, deployed or run against `PEMSDB`; no live HTTP; all new SQL tests use disposable `PEMS_TEST_P03_<hex>` databases (created and dropped by the tests).

## 1. Audit table

Status: **Fixed** = confirmed in code, changed, tested. **Not a defect** = checked, already correct (test added). **Partial / Deferred** = see section 6.

| # | Confirmed? | Root cause (file) | Fix summary | Tests |
|---|---|---|---|---|
| 1 | Yes | `WorkRequestService.ExecuteWorkflowActionAsync` / `AdvanceStatusAsync` accepted Reject/Cancel with no reason; UI said "Comment (optional)" | Shared rule `WorkflowActionRules` (Reject/Cancel/Hold/Return, and legacy Rejected/Cancelled/OnHold); reason stored in WorkflowHistory + status history + `RejectionReason`; UI field required | `WorkRequestAuditFixTests.A1_*` |
| 2 | Yes | `CreateAsync` accepted a request with no equipment/asset/FL; entity comment said all optional | Location required (asset → equipment derived); existence validated; old drafts cannot be submitted; FL-only requests pick the equipment at conversion (WO needs one) | `A2_*` (3) |
| 3 | Yes | No SoD in the WR service | Requester ≠ reviewer/approver/planner/rejecter, approver ≠ reviewer; default ON; config `Cmms:WorkRequest:EnforceSegregationOfDuties`; SystemAdmin override only with an `AuthorizationAuditLog` row (`Decision=SoDOverride`) | `A3_*` (3) |
| 4 | Yes (absent) | No risk/HSE/SLA model | 5×5 matrix (`WorkRequestRiskAssessor`): derived priority never below declared, HSE ≥ High, Emergency = Critical; SLA due dates stored/shown; in-app notification to HSE users | `A4_*` (parameterized + 2) |
| 5 | Yes | Failure data never captured on the request | Failure mode (catalog), effect, downtime captured; carried to the WO (mode id/code, effect, downtime, HSE flag) | `A5_A6_*` |
| 6 | Yes | `SuggestedDisciplineId` ignored at conversion | Suggested discipline → WO **Responsible** discipline (Maintenance org); Originating = requester's discipline snapshot (unchanged); planner can change it in Planning | `A5_A6_*`, `A6_*` |
| 7 | Explained + fixed | Counter was incremented in its own committed transaction before the document insert, so any rejected/failed/deleted attempt burned a number. Why `WR-2026-0003` specifically cannot be proven without reading `PEMSDB` (not done): two earlier numbers were consumed by failed/test/deleted attempts | Number and document commit in one transaction (`NumberingService` joins the ambient transaction); validation runs before allocation; WO number allocated after conversion validation. Race-safe via the row lock on the sequence | `WorkRequestNumberingSqlTests` (real SQL: rollback gives the number back; 8 parallel creates → gap-free unique) |
| 8 | Yes | `GetEffectiveRatesAsync` returned 0 silently | `LaborRateMissingException` (normal and overtime), message + link to the employee rate setup; Snapshot pattern kept | `B8_*` |
| 9 | Partly | Catalog (`FailureModes`/`FailureCauses`) and seeder already existed; completion form used free text and the service accepted anything | Completion validates against the active catalog (code or name, normalised, ids stored); form uses selects; free text still accepted only when the catalog is empty; `scripts/cmms/Link-FailureTextToCatalog.sql` (dry-run, unique matches only, text untouched) | `B9_*`, `FailureCatalogLinkSqlTests` (real SQL) |
| 10 | Yes (UI) / flow OK | Empty Material Master showed an unusable form; stock flow itself was correct | Guided empty state with link; end-to-end issue → return → cost test | `WorkOrderMaterialIntegrationTests`, render test |
| 11 | Not reproduced against real data; root cause found | `SetPlanning/AssignExecutor/Schedule` called `LifecycleInstanceAsync` unguarded: an inconsistent record (more than one lifecycle instance) threw → HTTP 500, no toast; non-concurrency `DbUpdateException` also escaped | Guarded lookups, `DbUpdateException` mapped to a message, controller `SafeAsync` as last line of defence — every outcome ends in a toast. If it still happens in UAT, the server log of the failing request is needed | `B11_*` |
| 12 | Yes | Forms were shown to users who would be refused | Section × phase × permission matrix drives Razor (hidden / read-only / editable / correctable); legacy Hold/Resume/Reject/Cancel/Advance buttons now follow Edit/Approve like the controller | render tests (incl. legacy) |
| 13 | **Not a defect** | Completion was already blocked while a referral is pending (EN 13460) | Added a two-discipline test (blocked until both respond) | `B13_*` |
| 14 | Yes | Hold / Return / Cancel had optional comments | `WorkflowActionRules` in the lifecycle service (workflow and legacy path), UI field required, reason in both histories | `B14_*` (2) |
| 15 | Partly | Reason/guard logic duplicated between Work Request and Work Order | Reason rule centralised; child-record guard centralised (`WorkOrderSectionGuard`). Other helper duplication (role expansion, `T()`, strategy wrappers) remains — deferred | `B15_*` |
| 16 | Partly | Password was already never echoed and roles were kept; the real defect: the employee was created before the user, so a rejected password left an **orphan employee** and every re-submit made another | Identity validators + role names validated first; compensating removal of an employee created by the failed attempt; role-assignment failure undoes the user | `C16_*` (2) |
| 17 | Partly | Lockout (5 attempts / 10 min), secure/HttpOnly cookies, length-8 + digit policy and Employee/Department/Discipline/Position validation already exist | No change to the policy values (owner decision); see section 6 | structural |
| 18 | Yes | Two causes: (a) HasData disciplines (codes EL, PR, PI, CV…) + `DisciplineSeeder` compared codes only → second "Electrical/Process/Piping/Civil"; (b) the test suite already refuses `PEMSDB` (isolated `PEMSDB_RegressionTest`), so current runs do not pollute — historic pollution cannot be measured without DB access | Seeder matches by code, alias or name; maintenance-group seeding resolves via aliases; `Cleanup-DuplicateDisciplines.sql` (dry-run, deactivates only unreferenced duplicates) and `Report-TestDataSuspects.sql` (read-only) | `DisciplineDedupeSqlTests` (real SQL) |
| 19 | Yes | "Active Contracts (coming soon)" tile (always 0) and "Discipline Performance — coming soon" panel | Real contract count (Effective/legacy Active) and a per-discipline document table | build + definitions |
| 20 | Yes | MDR import maps client response codes CO and AC (commented / approved with comments) to `Status="Rejected"`, so "Rejected" was mostly "returned with comments"; Pending counts only Submitted/Checked | `DashboardDefinitions` is the single source: Rejected = RE or internal; new card "Returned with comments (CO/AC)"; hints on every card. Import mapping left as is (historic data) | compile + definitions |
| 21 | Yes | 51 constant keys had no Persian entry; 7 case-insensitive duplicate resource names were silently dropped by the build (`start`, `end`, `open`, `overdue`…) | 67 entries added, duplicates removed and usages re-pointed, new strict test (no baseline) | `LocalizationKeyCoverageTests` |
| 22 | Yes | Missing Material Master / Teams gave dead forms | Guided messages with links (Material Master, Teams) | render test |
| 23 | Mostly confirmed | See section 5 | Antiforgery/role/anonymous structural tests; child-record IDOR (remove skill/material by id without checking the Work Order) fixed | `CmmsEndpointSecurityTests`, `Removing_a_record_of_another_work_order_*` |
| 24 | Yes | The generic audit stored **password hash before→after** and no values for security inserts/deletes | Secrets never stored (hash masked, stamps skipped); user / role-assignment / grant inserts and deletes record non-secret values; Work Order child changes in `WorkOrderChangeLogs` (who, when, state, stage, before/after, reason, IP) | `SecurityAuditLogTests`, guard tests |
| 25–31 | Yes | See section 2–4 | Policy matrix as code, guard on every child POST, immutability, Reopen, frozen cost, cleanup script, audit | ~5 000 parameterized cases + 25 behaviour tests |

## 2. Section H — policy matrix (`WorkOrderSectionPolicy`, used by Razor and every POST)

H = hidden, R = read-only, E = editable (section permission), C = correctable (section permission + `WorkOrder.CorrectActuals` + mandatory reason).

| Section \ Phase | Created | Planning | Preparation | Ops Issue | Released | Execution | On Hold | Completion | Verification | Final Appr. | Closed | Cancelled | Rejected |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Required skills / materials / Crew | E | E | E | R | R | R | R | R | R | R | R | R | R |
| Labor (actual) / Spare parts | H | H | H | H | R | E | R | C | C | R | R | R | H |
| Multi-discipline review | E | E | E | E | E | E | E | R | R | R | R | R | R |
| Responsible discipline | H | E | E | R | R | R | R | R | R | R | R | R | R |
| Executor assignment / Schedule | H | H | E | R | R | R | R | R | R | R | R | R | R |
| Job-plan checklist | R | R | R | R | R | E | R | R | R | R | R | R | R |

Deviation to confirm: Crew membership is read-only from Ops Issue onward (as specified); in practice crews change during execution — suggested follow-up is allowing member swaps in Execution.

## 3. Section H — POST actions verified

All rows were exercised by a direct controller POST (`WorkOrderSectionGuardTests`); "before" = state of the original code (read from git `HEAD`).

| POST | Created | In Execution | Completion | Verification | Closed | Before the fix |
|---|---|---|---|---|---|---|
| AddLaborEntry | rejected | accepted | correction only (permission + reason) | correction only | rejected | Service allowed only InProgress (already rejected elsewhere); no audit, no permission model |
| AddSpareUsage | rejected | accepted | correction only | correction only | rejected | **Accepted in every state** (stock deducted on Created and Closed) — fixed |
| ReturnSpareUsage | rejected | accepted | correction only | correction only | rejected | Accepted in every state — fixed |
| AddRequiredSkill / RemoveRequiredSkill | accepted | rejected | rejected | rejected | rejected | Accepted in every state; remove used only the record id (another Work Order's record could be removed) — fixed |
| AddRequiredMaterial / RemoveRequiredMaterial | accepted | rejected | rejected | rejected | rejected | same — fixed |
| CreateCrewForWorkOrder, CrewController create/add/remove member | accepted | rejected | rejected | rejected | rejected | Accepted in every state (the `AUDITCREW` on a closed WO) — fixed; non-existent WO id rejected by the service |
| RequestDisciplineReview | accepted | accepted | rejected | rejected | rejected | Accepted in every state — fixed |
| SetPlanning / AssignExecutor / SetSchedule | Planning/Preparation only (see matrix) | rejected | rejected | rejected | rejected | Rejected by the lifecycle service already; now also by the matrix, with audit and a toast |
| CompleteTask / ReopenTask | rejected | accepted | rejected | rejected | rejected | Accepted in every state — fixed |

Rejections: form posts redirect with a specific localized toast (PRG, policy code in `TempData["PolicyCode"]` and header `X-PEMS-Policy`); AJAX/JSON callers get a real HTTP 400 (rule), 403 (permission) or 404 (missing), never a 500. Every rejection is written to `WorkOrderChangeLogs`; attempts on Closed/Cancelled/Rejected or without permission are flagged `IsSecurityEvent` and, for finished work orders, also written to `AuthorizationAuditLogs`.

**Reopen** (H28): new permission `WorkOrder.Reopen` (granted to `MNT.Manager`), mandatory reason, returns the Work Order to the verification stage (found from workflow data: the stage whose outgoing transition needs `Verify`, no hard-coded name), recorded in workflow history, status history and the change log; the maintenance-history cost is re-computed when it closes again. Correction permission is a new `WorkOrder.CorrectActuals` (granted to `MNT.DisciplineHead`) because no seeded role holds both Edit and Approve.

**Cost freeze** (H29): after Closed no labor/spare/skill/material/crew change is possible (tests assert labor, spare, stock and `MaintenanceHistory.Cost` are unchanged after many attempts by authorized users); corrections before closure keep the rate snapshot and are audited.

## 4. Deliverables and how to apply

- Schema: two migrations, additive only (no data loss): `20261012222003_AddWorkOrderChangeLog` (new table) and `20261012230433_WorkRequestRiskFailureCapture` (nullable columns / bool defaults on `WorkRequests`, `WorkOrders`; one FK to `FailureModes`). Both applied cleanly by `Migrate()` on disposable databases. Not applied to `PEMSDB`.
- Idempotent seed: `WorkOrderReopenPermissionSeeder` (called from `Program.cs` after the v2 lifecycle seeder). `DisciplineSeeder` is alias-aware.
- Scripts (`scripts/cmms/`, all default to **dry-run**): `Report-PostClosureChildRecords.sql` (H30), `Deactivate-PostClosureCrews.ps1` (flag-only, needs `-Execute -ConfirmCrewIds -ConfirmDatabase`), `Cleanup-DuplicateDisciplines.sql`, `Report-TestDataSuspects.sql`, `Link-FailureTextToCatalog.sql`. Run them on a restored copy first.
- Patch set: `scripts/cmms/Export-CmmsAuditPatch.ps1` writes a `git diff` of exactly the files of this work to a folder of your choice.

## 5. Security notes (E23)

Verified in code/tests: every mutating action of Work Request / Work Order / Crew / Admin controllers has `[ValidateAntiForgeryToken]`; Admin controllers are restricted to `SystemAdmin` (the Admin `SettingsController` is a bare redirect); mutating Work Request/Work Order/Crew actions perform a server-side permission check; child-record IDOR closed; global authenticated-user filter, `HttpOnly` + `Secure` (outside Development) + `SameSite=Lax` cookie, account lockout.
Not verified here / client-dependent: HTTPS termination, HSTS and forwarded-header handling behind the reverse proxy; no per-IP login throttling exists (lockout is per account) — recommend a rate limiter at the proxy or `AddRateLimiter` on the login endpoint.

## 6. Deferred / partial items (reason → next step)

| Item | Reason | Next step |
|---|---|---|
| A4 SLA breach handling | Due dates are stored and shown; there is no timer, escalation or breach dashboard. Emergency requests still follow the normal approval path | Decide the escalation owners, then add a Hangfire job + dashboard widget |
| A7 residual gap | A failure inside the conversion transaction after the WO number is allocated can still burn a WO number (WR numbers are gap-free) | Move the allocation inside the conversion strategy block |
| 7 root cause on real data | Needs read access to `PEMSDB` (`NumberingSequences`, `WorkRequests`) — not authorized for this task | Run a read-only `SELECT` of the sequence value vs `MAX(RequestNumber)` |
| B15 | Role-expansion, `T()` and strategy helpers are still duplicated | Extract a shared `WorkflowServiceBase` |
| C17 password policy | Raising length/complexity changes behaviour for all users — owner decision | Make `Identity` password options configuration-driven |
| C18 historic pollution | Counting leftover rows needs `PEMSDB` | Run `Report-TestDataSuspects.sql` and `Cleanup-DuplicateDisciplines.sql` (dry-run) on a copy |
| D21 hard-coded literals | The existing ratchet (`language-purity-baseline.txt`) still allows legacy literal text in views | Burn the baseline down view by view |
| Phase detection by stage name | `WorkOrderSectionPolicy.ResolvePhase` maps seeded stage names to phases (falls back to business status for unknown names) | If stages are renamed, update the mapping or drive it from stage metadata |

## 7. Test results

- Verified in an export copy (`C:\pt11\verify`, `verify2`); the working tree was not built in place.
- Full suite (7 875 tests, 1 h 7 m): **7 873 passed, 2 failed**. The two failures were test expectations invalidated by the changes — `WorkRequestWorkflowTests.Conversion_business_rules_*` (message is now bilingual) and `LanguagePurityTests.EveryEnglishKey_*` (the neutral resource key `open` collided with `Open`; renamed). Both were fixed and re-run green together with all Web, Work Request and Work Order test classes (147/147). The full suite was not re-run end to end after those two one-line fixes.
- New tests: policy matrix (~4 700 parameterized cases), guard/POST/Reopen/cost-freeze (22), rendered-HTML (6), Work Request audit fixes (A1–A6), Work Order audit fixes (B8–B15), SQL-backed (numbering, discipline dedupe, failure-catalog link, post-closure report), localization coverage, endpoint security, security audit log, dashboard definitions, Create-User.
- Disposable databases: 66 created and 66 dropped in the full run (audit trail `PEMS_TEST_DB_AUDIT_LOG`); none remain. `PEMSDB` untouched.
- Pre-existing, not created by this work and not touched: databases `PEMS_CONC_*` (7, dated 2026-10-07/08) are leftovers of an older concurrency test; remove them after checking nothing needs them.
- **Final full run after all fixes (export C:\pt11\verify2): 7 879 tests, 7 879 passed, 0 failed, 0 skipped (1 h 3 m); 66 disposable databases created and 66 dropped.** This supersedes the 7 873 / 2 figures above.
