# Procurement P0 hardening: gate register, evidence log and decision log

Status: **P0 gate = NOT PASSED (BLOCKED).** The implemented subset is `READY_FOR_REVIEW` (implementation done, in-memory tests green, independent human review still pending). Several mandatory P0 items are `BLOCKED` on policy or on an unauthorized migration, and live-application verification is `NOT_RUN` (the SQL test suite has run; see P0-12). Nothing here is production readiness.

Revision note (2026-10-12, **P0.4**): the independent review (P0/P0.1/P0.3) found that the manual contract-creation path (`ContractController.Create` → `ContractService.CreateAsync`) still had the original R-01 defect and no `ContractNo` lock (finding H-1). By owner decision it is now inside the P0 scope: authorization is checked fail-closed before any insert, and contract + generic audits + `Contracts/Create/Allow` creator event commit in one SQL Server transaction under the same lock as the tender path (section 11). P0 remains NOT PASSED (BLOCKED).

Revision note (2026-10-12, **P0.3**): the tender-to-contract creation is now one SQL Server transaction (contract, tender link, both generic audit writes and the `Contracts/Create/Allow` creator event), verified on disposable SQL Server databases (section 10). This closes the "recoverable, not guaranteed" gap of section 6 **for that path only**. P0 remains NOT PASSED (BLOCKED): P0-09/P0-10 are blocked on decisions, SQL verification outside this path is `NOT_RUN`, and the independent review of P0, P0.1 and P0.3 has not been done.

Revision note (2026-10-12, **P0.1**): the independent review found a High defect (R-01, tender-to-contract creator event not reliably recorded) and two factual errors in this document (warehouse roles, retry wording). P0.1 corrects R-01 in code (section 1, row "P0.1") and corrects the document; the earlier statements are kept in the revision history (section 8), not erased. Remaining review findings R-02..R-07 are tracked in section 7.

Provenance note: before this document there was **no** dedicated Procurement gate register, evidence log or decision log in the repository (searched `docs/`, `.claude/`, root). The historical audit baseline is `docs/audits/PEMS_SUPPLY_CHAIN_PROCUREMENT_BASELINE_AUDIT.md` (kept unchanged; a provenance pointer was appended at its end). This file is the first consolidated gate/evidence/decision record for P0. Earlier phase records remain in `docs/procurement/PHASE1_REMEDIATION_AND_FOLLOWUP.md` and the P4/P5 documents.

## 1. Provenance of the change

| Item | Value |
|---|---|
| Original branch / HEAD before and after | `feat/cmms-passport-global-page-header` / `b2d2411` (nothing committed) |
| Source of the change | Patch `C:\pt4\P0_hardening.patch` (2,096 lines), SHA-256 `2068357e081c1c8820421510df945bb311e428a40ec9417e9a8f50e3f857b0b8`; manifest `C:\pt4\P0_changed_files.txt` |
| Implemented and first verified in | isolated copy `C:\pt4\src` (outside the repository) |
| Applied to the original with | `patch -p1` after a clean dry run; no rejects, no fuzz; no overlap with the pre-existing local modifications (P5 availability gate files, uploads, Graphify output) |
| Backup of the 17 original files before the patch | `C:\pt5\backup_original_before_P0\` (SHA-256 list `C:\pt5\backup_sha256.txt`) |
| Verification export of the integrated source | `C:\pt5\verify\` (built and tested there; the original working directory was not built because the running application owns its output folders) |
| Files changed (production, 13 + 1 view) | `PEMS.Persistence/Servises/{ContractService,ContractWorkflowService,ProcurementPackageService,PurchaseOrderService,TenderService,VendorService}.cs`; `PEMS.Web/Controllers/{ContractController,ProcurementPackageController,PurchaseOrderController,TenderController,VendorController}.cs`; `PEMS.Web/Areas/Warehouse/Controllers/{IrnReceiptController,MaterialItemController}.cs`; `PEMS.Web/Views/Contract/Details.cshtml` |
| Files changed (tests) | modified: `ContractWorkflowServiceTests`, `TenderControllerAuthorizationTests`, `TestSupport/FakeWorkflowRuntime`; new: `Procurement/{VendorCreateHardeningTests,TenderAwardHardeningTests,TenderControllerP0Tests,ContractResubmitAndApprovalTests,IdentifierValidationTests}`, `Inventory/{MaterialItemMutationAuthorizationTests,IrnApprovalRecoveryTests}` (70 new tests) |
| Separate documentation-only edit | `PEMS.Persistence/Servises/PermissionService.cs`: three comment lines added above the `MaterialItem` seed to correct the outdated "AdjustStock/TransferStock are ungated" comment. No code, role or permission-row change. |
| **P0.1 correction (R-01), applied directly to the original** | New: `PEMS.Application/DTOs/TenderContractResult.cs`, `PEMS.Tests/Procurement/{TenderContractCreatorEventTests,TenderContractCreatorEventControllerTests}.cs`. Modified: `PEMS.Application/Interfaces/ITenderService.cs`, `PEMS.Persistence/Servises/TenderService.cs`, `PEMS.Web/Controllers/TenderController.cs`, this document. Verified in an export of the modified original at `C:\pt6\verify\`. No migration, schema, seed or permission change. |
| **P0.3 (atomic contract creation), applied directly to the original** | New: `PEMS.Persistence/Servises/ContractCreationLocks.cs`, `PEMS.Tests/Procurement/{ContractCreationAtomicitySqlTests,ContractCreationScopeTests}.cs`, `PEMS.Tests/TestSupport/{DisposableSqlDatabase,SqlFaultInjection}.cs`. Modified: `PEMS.Persistence/Servises/TenderService.cs`, `PEMS.Web/Controllers/TenderController.cs`, `PEMS.Application/{Interfaces/ITenderService.cs,DTOs/TenderContractResult.cs}`, `PEMS.Tests/Procurement/TenderContractCreatorEvent{Tests,ControllerTests}.cs`, this document. Verified in an export at `C:\pt8\verify\`. No migration, schema, index, seed, permission or appsettings change. See section 10. |
| **P0.4 (manual contract creation, H-1), applied directly to the original** | New: `PEMS.Persistence/Servises/{ContractCreationScope,ContractCreatorEvents}.cs`, `PEMS.Application/DTOs/ContractCreateResult.cs`, `PEMS.Tests/Procurement/{ManualContractCreationTests,ManualContractCreationSqlTests}.cs`. Modified: `PEMS.Persistence/Servises/{ContractService,TenderService}.cs`, `PEMS.Application/Interfaces/IContractService.cs`, `PEMS.Web/Controllers/ContractController.cs`, `PEMS.Tests/TestSupport/SqlFaultInjection.cs`, `PEMS.Tests/Authorization/ContractControllerAuthorizationTests.cs`, this document. Verified in an export at `C:\pt9\verify\`; pre-P0.4 copies in `C:\pt9\backup_pre_p04\` (SHA-256 in `C:\pt9\backup_sha256.txt`). No migration, schema, index, seed, permission or appsettings change. See section 11. |
| Not touched | migrations, EF configurations, seed data, appsettings, `.csproj`, Part Catalogue, the database, running processes |

## 2. Gate register

| Gate | Requirement | Status | Evidence / blocker |
|---|---|---|---|
| P0-01 | F-01 vendor create accepts only the six permitted fields; qualification, risk, active cannot be forged | `READY_FOR_REVIEW` | `VendorController.Create` `[Bind]` (L79); `VendorService.CreateAsync` builds a new entity with `NotQualified`/`Unknown`/active (L134-135); `VendorCreateHardeningTests` (8) |
| P0-02 | F-02 award eligibility follows the documented technical/commercial rule | `READY_FOR_REVIEW` | `TenderService.SelectWinnerAsync` (L250-255); `TenderAwardHardeningTests` (pass/pass, fail-only, pass-then-fail, fail-then-pass, missing, pending, inactive-failed). Policy question D-OPEN-01 remains open |
| P0-03 | F-03 tender-to-contract: contract + tender link persisted together; authorization fail-closed; creator event for the existing SoD design; retry does not duplicate the contract | `READY_FOR_REVIEW` (revised by P0.1 and P0.3; independent re-review pending) | **P0.3:** contract, tender link, both generic audit saves and the creator event are one SQL Server transaction (section 10; SQL-verified on disposable databases, T1-T14). Before P0.3 the generic AuditLog was a separate physical save outside any transaction. Guard in `SelectWinner`/`CreateContract` and the `Contracts.Create` permission check: `TenderController`. Creator event: see row P0.1-R01. Open: D-OPEN-04, D-OPEN-10 |
| P0.1-R01 | R-01: a committed tender-created contract must not silently lack its `Contracts.Create` creator event; failures must be classified accurately; retry must repair without duplicating or re-attributing | `READY_FOR_REVIEW` (P0.1 in-memory; **superseded for SQL Server by P0.3**) | **Recoverable, not guaranteed (P0.1).** Section 6 describes P0.1; on SQL Server the guarantee is now atomic (P0.3, section 10). The P0.1 behavior remains only as the non-transactional fallback (InMemory/outer transaction). Code: `TenderService.CreateContractFromWinningBidAsync`, `EnsureContractCreatorEventAsync`, `RecordCreatorEventAsync`, `IsCommittedAsync`; `TenderController.CreateContract`; result type `TenderContractResult`. Tests: `TenderContractCreatorEventTests`, `TenderContractCreatorEventControllerTests` (17). SQL transaction/concurrency behavior `NOT_RUN`. |
| P0-04 | F-04 five stock-mutation endpoints enforce server-side authorization before any mutation service | `READY_FOR_REVIEW` | `MaterialItemController` checks precede each service call (L70, L117, L134, L166, L181); `MaterialItemMutationAuthorizationTests` (incl. real permission engine). Open: D-OPEN-03 |
| P0-05 | Contract final approval proceeds only on explicit Allow | `READY_FOR_REVIEW` | `ContractWorkflowService.ApproveAsync` (L184); `ContractResubmitAndApprovalTests`; one existing test updated to the new behavior |
| P0-06 | Contract resubmission after rejection reuses review rows and handles removed disciplines | `READY_FOR_REVIEW` | `SubmitForReviewAsync` (L75) and pending filter `IsRequired` (L157); view shows the button for `Rejected`; resubmit tests incl. smaller discipline set |
| P0-07 | IRN approval recovery is idempotent and cannot double-post | `READY_FOR_REVIEW` (partial) | recovery path in `IrnReceiptController.Approve` (L327-329) with stale-instance guard, and Disposition compensation (L281); `IrnApprovalRecoveryTests` (8). **A single transaction spanning workflow step and stock posting is not implemented (D-OPEN-09).** |
| P0-08 | PO / contract / package identifier validation | `READY_FOR_REVIEW` (code-level only) | services validate blank, duplicate (PO: case-insensitive within the project; contract/package: plain equality, so case follows the database collation), negative value, inverted dates; `IdentifierValidationTests`. **Database unique indexes `BLOCKED`: migration not authorized; concurrent duplicates are still possible.** |
| P0-09 | View permissions on sensitive Procurement and inventory read endpoints | `READY_FOR_REVIEW` (2026-10-14, decision D-11; independent review pending) | Section 13. View gates on Vendor, Contract, IRN, StoreRequisition, MaterialItem, Inventory dashboard, transaction ledger and the procurement report; `ProcurementReadViewSeeder` (additive). Roles without any permission on a module lose read access to it by decision. Persisted `RolePermissions` on real databases not verified (R-02 pattern). |
| P0-10 | Seeded SoD policies actually enforced | `READY_FOR_REVIEW` (partial; 2026-10-14, decisions D-12/D-13) | Section 13. Tender creator ≠ approver (SystemAdmin exempt, audited); Contracts `Edit↔Approve` now fed by content edits and enforced on approval and on revision approval; tender-created contracts feed `Contracts.Create↔Approve` (P0.1). Still not covered (no decision requested): IRN and MR SoD. |
| P0-11 | Error handling: authorization/validation failures do not become HTTP 500 in the touched flows | `READY_FOR_REVIEW` | catches added in Vendor, PO, Contract, Package creation and Contract Approve/Reject/RecordDisciplineReview. Not covered: other contract actions, `SubmitForReview` runtime exceptions |
| P0.3 | Option A: contract creation and its creator event atomic on SQL Server; schema-free `ContractNo` application lock; uncertain commits never reported as "not created" | `READY_FOR_REVIEW` (independent review pending) | Section 10. SQL tests T1-T14 on disposable `PEMS_TEST_P03_*` databases: 28/28 passed; concurrency repeats 8/8 and 8/8; sensitivity variants S1-S4 fail as expected. Residual risks: other paths creating contracts do not take the lock; no unique index (D-OPEN-08); uncertain commit was simulated, not a real network failure. |
| P0.4 | H-1: manual contract creation fail-closed, atomic with its creator event, under the shared `ContractNo` lock | `READY_FOR_REVIEW` (independent review pending) | Section 11. Manual SQL tests 25/25 passed (incl. 3 tests with a real session KILL); tender SQL regression 28/28; concurrency repeats 11/11 and 11/11; sensitivity V1-V6 fail as designed. Residual: no database uniqueness (D-OPEN-08); non-application writers; "commit applied but acknowledgement lost" still only simulated. |
| P0-12 | SQL-backed, concurrency and live-application verification | `PARTIAL` (SQL test suite run; live application `NOT_RUN`) | Tender-to-contract (section 10) and manual contract-creation (section 11, 12) paths: dedicated SQL runs. **2026-10-13 update:** the complete test suite, including every SQL-touching class (IRN, stock-row, MRS, store-requisition, numbering and the other concurrency classes), ran in the CMMS verification export `C:\pt11\verify2`: 7,879 tests, 7,879 passed, 0 failed, 0 skipped; 66 disposable databases created and dropped; `PEMSDB` untouched (source: `docs/cmms/CMMS_AUDIT_FIX_REPORT.md` §7). That run predates P0.5; the P0.5 paths were verified separately (section 12). Still `NOT_RUN`: the live application (no HTTP request), and a full-suite re-run on the final committed tree (`fbc0671`). |
| P0-13 | Independent human review of the patch | `PENDING` | required before any `PASSED` |
| P0.5 | M-1/H-1 follow-up: RCSI-safe verification of the contract-creation outcome (lock barrier instead of a plain read) | `READY_FOR_REVIEW` (independent review pending) | Section 12. Manual SQL 33/33, tender SQL 28/28, non-SQL suite 2806/2806; real session KILL and real in-flight sessions prove the barrier (R2-R7); "commit applied but acknowledgement lost" is still **not** truly reproduced (12.4). Legacy `CreateAsync` has no production caller (12.5). |
| **P0 overall** | all mandatory criteria evidenced and reviewed | **NOT PASSED (BLOCKED)** | P0-09 and P0-10 implemented per owner decisions D-11..D-13 (2026-10-14), independent review pending; P0-10 does not cover IRN/MR SoD; P0-12 live-application verification not run (SQL suite now run, see P0-12 row); P0-13 independent review of P0, P0.1, P0.3 and P0.4 pending; open items R-02..R-07 (section 7) |

## 3. Evidence log

| Date (2026-10-12) | Activity | Command / location | Result |
|---|---|---|---|
| Isolated copy | Baseline before edits | `dotnet test` on 11 relevant test groups | 165 passed, 0 failed |
| Isolated copy | After production edits, before new tests | same filter | 164 passed, 1 failed (an old test asserting a workflow exception for SystemAdmin; updated, intended behavior change) |
| Isolated copy | New regression tests | 7 new classes | 70 passed after correcting two mistakes in my own test setups |
| Isolated copy | Sensitivity check | new tests against the original production code | 45 of 62 failed; the 17 that passed are behavior-preservation cases |
| Isolated copy | Broad in-memory regression | Authorization, Procurement, Inventory, Web, gate, requisition, VDR (SQL/concurrency classes excluded) | 1,243 passed, 0 failed, 0 skipped |
| Original | Patch integration | `patch -p1` (dry run first) | 24 files patched, exit 0; all 24 byte-identical to the verified copy |
| Original | Whitespace check | `git diff --check` | clean |
| Verification export | Solution build | `dotnet build PEMS.slnx -c Debug` (in `C:\pt5\verify`) | 0 errors |
| Verification export | Focused P0 tests | `dotnet test ... --filter Procurement|MaterialItemMutationAuthorizationTests|IrnApprovalRecoveryTests` | 70 passed, 0 failed |
| Verification export | All non-SQL tests | `dotnet test ... --filter "<27 SQL-touching classes excluded>"` | 2,765 tests: 2,751 passed, 14 failed. All 14 were missing-input errors in the export (`deploy/`, `.github/`, `Dockerfile`, `docs/`, `.gitignore`); with those read-only inputs copied in, `LanguagePurityTests`, `TechnicalClassMatcherTests` and `LinuxDeploymentArtifactsTests` passed in separate re-runs (38 → 37 → 12/12 after adding `.gitignore`). The hard-coded UI-string ratchet passes. |
| Original | Graphify | `graphify update .` | succeeded at 2026-10-12 02:49; 37,671 nodes; graph contains the new test classes; no file outside `graphify-out` modified by it |
| **P0.1** Verification export (`C:\pt6\verify`) | Folder identity | recursive compare of all source folders against the modified original | identical (code only; the export also holds read-only copies of `docs/`, `deploy/` without the local host config, `.github/`, `scripts/`, `Dockerfile`, `.gitignore` that some repository tests read) |
| P0.1 | Solution build | `dotnet build PEMS.slnx -c Debug` | 0 errors |
| P0.1 | First run of the R-01 tests | `dotnet test ... --filter TenderContractCreatorEvent|PEMS.Tests.Procurement|TenderServiceTests|TenderControllerAuthorizationTests` | 76 passed, 2 failed. Both failures were my test-numbering error (in the controller path the guard's own Allow event uses two physical saves, so the event row is the fifth save, not the third); product code unchanged; tests corrected |
| P0.1 | R-01 tests (after the correction) | `dotnet test ... --filter TenderContractCreatorEvent` | 17 passed, 0 failed |
| P0.1 | Sensitivity check (verification export only; original untouched) | deliberately (a) report "nothing saved" after any save error and (b) disable the duplicate-event check | 5 of the 17 tests failed as expected (`GenericAuditSaveFailure_AfterCommit_*`, `EventRowCommitted_ButItsGenericAuditSaveFails_*`, `RepeatedRetries_AfterRepair_*`, `Retry_AfterSuccess_*`, controller `Retry_AfterAFullySuccessfulCreation_*`); the correct file was restored and the folder re-verified identical to the original |
| P0.1 | All non-SQL tests | `dotnet test ... --filter "<27 SQL-touching classes excluded>"` (same exclusion list as the P0 run, `C:\pt5\exclude_filter.txt`) | **2,782 passed, 0 failed, 0 skipped** (42 min). Includes the P0 tests, the P5 gate tests and the UI/language ratchets |
| P0.1 Original | Graphify | `graphify update .` | succeeded at 2026-10-12 11:22; 37,758 nodes; graph contains `TenderContractResult`, `ContractCreatorEventStatus`, `EnsureContractCreatorEventAsync`, `RecordCreatorEventAsync` and both new test classes; not copied from any other directory |
| P0.1 Original | Whitespace check | `git diff --check` | clean |

| **P0.3** Verification export (`C:\pt8\verify`) | Folder identity | SHA-256 of every file this stage touched, original vs export, before each run | identical |
| P0.3 | Build | `dotnet build PEMS.Tests/PEMS.Tests.csproj` | 0 errors |
| P0.3 | Safe-instance check (read-only) | `sqlcmd` against `localhost\SQLEXPRESS`: `SERVERPROPERTY` machine/edition/version/collation, `sys.databases` names | local machine, Express, 17.0.1000.7, `SQL_Latin1_General_CP1_CI_AS`; `PEMSDB`, `PEMSDB_PcatClone` and other pre-existing databases present and untouched |
| P0.3 | Changed P0.1 tests and related (non-SQL) | `dotnet test ... --filter "ContractCreationScopeTests|TenderContractCreatorEvent|TenderAwardHardening|TenderServiceTests|TenderControllerP0|TenderControllerAuthorization|ContractWorkflowServiceTests"` | 88 passed, 0 failed, 0 skipped (in-memory; **not** evidence of SQL atomicity) |
| P0.3 | **SQL tests T1-T14** (disposable `PEMS_TEST_P03_<hex>` databases, real migrations) | `dotnet test ... --filter "ContractCreationAtomicitySqlTests"` | **28 passed, 0 failed, 0 skipped** (final run); first run 26/26 |
| P0.3 | Concurrency repeatability | same project, filter `T8_|T9_|T10_`, two more runs | 8/8 and 8/8 passed |
| P0.3 | Sensitivity checks (export only) | S1-S4, see section 10.6 | S1 9/10 fail, S2 4/4 fail, S3 5/6 fail, S4 1/2 fail (as designed); production file restored and compared with `cmp` |
| P0.3 | All non-SQL tests | `dotnet test ... --filter "<27 SQL-touching classes excluded> & !ContractCreationAtomicitySqlTests"` (`C:\pt8\exclude_filter.txt`) | **2787 passed, 0 failed, 0 skipped** |
| P0.3 Original | Graphify | `graphify update .` (AST-only, the documented procedure) | succeeded at 2026-10-12 14:31; 37,920 nodes, 79,920 edges; graph contains `ContractCreationLocks`, `AcquireAppLockAsync`, `RunAtomicAsync` and the new test classes; before/after comparison of `git status` outside `graphify-out` is identical (Graphify modified no source file); not copied from any other directory |
| P0.3 Original | Whitespace check | `git diff --check` (outside `graphify-out`) and trailing-whitespace scan of the new files | clean |

| **P0.4** Verification export (`C:\pt9\verify`) | Build and identity | `dotnet build PEMS.Tests`; SHA-256 / `cmp` of the changed production files original vs export before and after the sensitivity variants | 0 errors; identical |
| P0.4 | **Manual-path SQL tests** (disposable `PEMS_TEST_P03_<hex>` databases, real migrations) | `dotnet test ... --filter ManualContractCreationSqlTests` | **25 passed, 0 failed, 0 skipped** (first run also 25/25) |
| P0.4 | Tender-path SQL regression after extracting the shared scope | `dotnet test ... --filter ContractCreationAtomicitySqlTests` | **28 passed, 0 failed, 0 skipped** |
| P0.4 | Concurrency repeatability | filter `ManualContractCreationSqlTests.M5_` + tender `T8_/T9_/T10_`, two more runs | 11/11 and 11/11 passed |
| P0.4 | Focused non-SQL tests (manual path, controller, identifier validation, grid wave, tender, contract workflow) | `dotnet test ... --filter "ManualContractCreationTests|ContractControllerAuthorizationTests|IdentifierValidationTests|GlobalGridHardeningWaveC1Tests|ContractCreationScopeTests|TenderContractCreatorEvent|TenderAwardHardening|TenderServiceTests|TenderControllerP0|TenderControllerAuthorization|ContractWorkflowServiceTests"` | 129 passed, 0 failed, 0 skipped (in-memory; **not** evidence of atomicity) |
| P0.4 | Sensitivity variants V1-V6 (export only) | section 11.7 | V1 5/6, V2 4/4, V3 4/4, V4 3/3, V5 2/2, V6 1/1 failed (as designed); production files restored and compared |
| P0.4 | All non-SQL tests | `dotnet test ... --filter "<27 SQL-touching classes + ContractCreationAtomicitySqlTests + ManualContractCreationSqlTests excluded>"` (`C:\pt9\exclude_filter.txt`) | **2806 passed, 0 failed, 0 skipped** |
| P0.4 Original | Graphify | `graphify update .` (AST-only, the documented procedure) | succeeded at 2026-10-12 22:42; 38,079 nodes, 80,520 edges; graph contains `ContractCreationScope`, `ContractCreatorEvents`, `CreateWithCreatorEventAsync`, `ContractCreateResult` and the new test classes; before/after `git status` outside `graphify-out` identical (Graphify modified no source file) |
| P0.4 Original | Whitespace check | `git diff --check` and trailing-whitespace scan of the new files | clean |
| P0.4 | Real-disconnect tests re-run | `ManualContractCreationSqlTests.G*` twice more on the final code, then G2 under variant V6 twice | G1-G3 3/3 and 3/3 passed; V6 failed both times with the raw `SqlException` ("transport-level error ... No process is on the other end of the pipe") |
**Not run (and why):** (P0.3 update: the new tender-to-contract SQL tests **were** run, on disposable databases only.) The 27 test files that can touch SQL Server or the regression database (listed in `C:\pt5\sql_touching_files.txt`, including IRN, stock-row, MRS, store-requisition concurrency/SQL tests and `FileSecurityHttpTests`); no real database was available for them by policy. The complete test suite was not run. No live HTTP request was made. The original working directory was not built (the running application locks its binaries). Visual UI behavior is unverified.

## 4. Decision log

**Defaults applied in this change (not company policy; reversible):**
- D-01 Vendor qualification/risk/active status are assigned only by the controlled server-side workflow.
- D-02 Award eligibility: for each mandatory category (technical, commercial) at least one active `Passed` and no active `Failed`; `Pending` never counts; no scoring invented.
- D-03 Contract creation from an award requires `Contracts.Create` in addition to `Tender.Approve`. The `Contracts.Create` creator event (keyed by the contract id) is written by `TenderService` immediately after the contract commits and is repairable by retry (P0.1; originally P0 wrote it from the controller after commit with `AuthorizeAsync`). P0.3 supersedes this wording: on SQL Server the event is written inside the contract's transaction (D-08). The P0.1 text is kept for history.
- D-08 (P0.3, approved by the gate owner) Option A: the creator event is written in the same transaction as the contract; if it cannot be written the contract is **not** created. Creation therefore fails closed; the `Tender.Approve` guard rows written before the transaction remain after such a rollback.
- D-09 (P0.3, approved by the gate owner) A schema-free, transaction-owned application lock on the normalized `ContractNo` serializes contract creation **through this path only**; it is not a uniqueness guarantee (section 10.7).
- D-10 (P0.4, decision of the gate owner) The manual contract-creation path is brought into the P0 scope (finding H-1) rather than accepted as out-of-scope. Authorization (user-aware `Contracts.Create`) is checked before any insert and fails closed; the creator event is written for the authenticated creator in the same transaction as the contract.
- D-04 Stock adjustment/transfer/material edit use the closest existing permission, `MaterialItem.Edit` (create uses `MaterialItem.Create`).
- D-05 A rejected contract may be resubmitted from `Draft` or `Rejected`.
- D-06 Identifier validation is code-level only; no migration.
- D-07 IRN approval: recoverable by retry, workflow outcome decides the posting direction.
- D-11 (2026-10-14, owner decision, closes D-OPEN-02) Read pages require `<Module>.View`: Vendors, Contracts (Index/Details; the Create form requires `Contracts.Create`), IRN, StoreRequisition, MaterialItem. The Inventory dashboard and the transaction ledger use `MaterialItem.View` (no permission rows exist for `Inventory`/`InventoryTransaction`; same closest-permission rule as D-04). The procurement report uses `ProcurementPackage.View` (no `Report` module exists). Role mapping: every role holding another permission on the same module also gets View (`ProcurementReadViewSeeder`, additive, written to `RolePermissions`/`RoleModuleActions` before the legacy sync so it is not revoked). Roles with no permission on a module lose read access to it (for example Viewer on Vendors/Contracts, EngineeringSupervisor on IRN).
- D-12 (2026-10-14, owner decision, closes D-OPEN-04) The tender creator may not approve the same tender (SelectWinner, CreateContract from the award, ApproveNegotiation). The creator is read from `Tender.CreatedBy` (stamped by the DbContext on insert), so the rule does not depend on a separately written event. SystemAdmin is exempt, but each use of the exemption writes an `AuthorizationAuditLog` row; if that row cannot be written the approval is denied. Tenders without a recorded creator are not blocked (documented limitation).
- D-13 (2026-10-14, owner decision, closes D-OPEN-05) The user who edited a contract may not approve it. Content edits (submit for review, add document, request revision) now write a `Contracts/Edit/Allow` event in the same save as the change; the existing `Contracts.Edit↔Contracts.Approve` SoD policy uses it in `ApproveAsync` and now also in `ApproveRevisionAsync` (previously role-only). Recording a discipline review is deliberately not an edit.

**Open decisions (not resolved here; remain open):**
- D-OPEN-01 Whether a later passing evaluation may supersede an earlier failed one (currently it may not; only `IsActive=false` voids an evaluation and there is no screen for it).
- D-OPEN-02 **Closed by D-11 (2026-10-14).** View permissions for read endpoints and their role mapping (P0-09).
- D-OPEN-03 A dedicated stock-adjustment permission and whether the current role assignments are appropriate (least privilege). `RoleCatalog` seeds `WH.Manager` with `MaterialItem.Edit` and `WH.Storekeeper` with `MaterialItem.Create` and `MaterialItem.Edit` (applied at startup by `RoleCatalogSeeder`, additive only), so those roles are expected to keep adjust/transfer. **The persisted `RolePermissions` rows have not been verified** (no database access); verification needs an explicitly authorized, read-only check of a test environment. (Corrected by P0.1: this entry originally said the `WH.*` roles hold no `MaterialItem` rows; see section 8.)
- D-OPEN-04 **Closed by D-12 (2026-10-14).** Tender Create↔Approve segregation of duties.
- D-OPEN-05 **Closed by D-13 (2026-10-14).** Enforcement of Contracts Edit↔Approve segregation of duties.
- D-OPEN-06 Vendor status requirements at award, PO and contract creation.
- D-OPEN-07 Whether rejected contracts should be resubmittable beyond the implemented behavior.
- D-OPEN-08 Database unique indexes, migrations and duplicate-data cleanup for identifiers.
- D-OPEN-09 A single transaction spanning the workflow engine and inventory posting.
- D-OPEN-10 Tender negotiation approval authorization (still role-only).
- D-OPEN-11 Any P1 scope (PO lines, award record, requisition-to-PO chain).

## 5. Release risks

- Behavior changes visible to users: stock adjustment, transfer and material create/edit/toggle by roles lacking `MaterialItem.Edit` (or `MaterialItem.Create` for create) now return Forbid (roles seeded with those permissions are unaffected, subject to the unverified persisted rows); duplicate/blank PO, contract, package and vendor entries are rejected; creating a contract from a tender additionally needs `Contracts.Create`; a retry after a creator-event failure now reports "contract already exists" and repairs the event instead of failing silently; contract approval by users lacking an explicit grant (including SystemAdmin) is denied.
- P0.3 behavior changes: creating a contract can now fail as a whole (with "nothing saved") when the creator event or an audit write fails, instead of leaving a contract without an event; under contention a request can wait up to 15 s for an application lock and then see a "busy" message; after a commit with an unconfirmable outcome the user sees "outcome unknown" and must check before repeating (repeating never creates a second contract).
- P0.4 behavior changes: manual contract creation now requires an authenticated user id and an explicit user-aware Allow for `Contracts.Create` (a user who passes the legacy role-only gate but is denied by the user-aware engine, for example a scope-limited or missing role assignment, can no longer create a contract; previously the contract was created and a non-Allow audit row was silently written); the manual path waits up to 15 s for the `ContractNo` lock and then shows a "busy" message; validation messages and HTTP behavior for blank/duplicate numbers are unchanged.
- Open P0 items (P0-09, P0-10, P0-12, P0-13) block a `PASSED` status.
- No claim is made that all SoD requirements or fail-closed authorization are satisfied across Procurement.

## 6. P0.1 / R-01: creator-event persistence and failure semantics

> **Superseded by section 10 (P0.3) for SQL Server.** The table below describes the P0.1 behavior, which now applies only to non-transactional providers (InMemory, or an outer transaction owned by the caller). On SQL Server stages 1-3 are one transaction: a failure in any of them rolls everything back.

**The failure sequence found by the review (confirmed against the source).** `PEMSDbContext.SaveChangesAsync` performs two physical saves for every call that has auditable changes: (1) the business data, then (2) the generic `AuditLog` rows. Creating a contract from a tender therefore had these distinct stages: (1) contract + tender link committed; (2) generic audit for that save; (3) the `Contracts.Create` event row (`AuthorizationAuditLog`, itself two physical saves: the row, then its generic audit). The P0 code reported "nothing was saved" for any `DbUpdateException`, even when stage 2 failed after stage 1 had committed; wrote the event from the controller with `AuthorizeAsync` after commit, so an exception there produced HTTP 500 with the contract committed and no event; and a retry only said "contract already exists", never writing the event. A contract without the event is invisible to the `Contracts.Create↔Approve` SoD check, so its creator could approve it.

**Behavior after P0.1:**

| Stage / situation | Persisted state | What the code now does |
|---|---|---|
| Stage 1 fails (nothing committed) | no contract, no link | Reports "nothing saved" (verified by reading the database, not assumed); no event |
| Stage 2 fails (generic audit) after stage 1 committed | contract + link committed | Reports success with `GenericAuditFailed=true`; the controller tells the user the contract was saved and the audit-log error was recorded; logged; the creator event is still written |
| Stage 3 fails (event row not saved) | contract committed, no event | Reports `NotRecorded`; the user message says the contract **is** saved and asks to repeat the action; logged; the failed row is detached so it cannot be inserted twice |
| Event row committed but its generic audit fails | event committed | Verified from the database and reported `Recorded` |
| Retry while the contract exists | no new contract | Never creates a second contract. If the event exists: `AlreadyRecorded`, nothing written. If missing: the event is written for the **original creator**, taken from `Contract.CreatedBy` (written in stage 1) mapped to exactly one user; never for the user who retries. If the creator cannot be identified uniquely: `CreatorUnidentified`, **nothing is written**, logged, manual review required |
| Authorization denial (guard or `Contracts.Create`) | no contract | No contract and no `Contracts.Create` event for anyone |

`Tender.Approve` guard events (resource = tender id) and `Contracts.Create` creator events (resource = contract id) remain separate rows. The guard still writes a `Tender.Approve` Allow row on every attempt (R-03, open).

**Guarantee level: recoverable, not guaranteed.** The creator event is **not** in the same transaction as the contract. Remaining failure window: a crash or database failure after stage 1 commits and before the event commits leaves a contract with no event until some later retry (or a call to `EnsureContractCreatorEventAsync`) repairs it. Nothing triggers the repair automatically. Further limits: attribution depends on `Contract.CreatedBy` (a user name; missing or renamed users give `CreatorUnidentified`); query-before-insert is not concurrency-safe without a database unique constraint, so two simultaneous repairs could each write a (benign) duplicate event; in-memory tests cannot prove SQL transaction, RowVersion or concurrency behavior.

**Design options if a guarantee is required (each needs approval; none was implemented):**
- A. Write the contract and the event inside one explicit relational transaction through the execution strategy (the application uses `EnableRetryOnFailure`). No schema change; needs SQL-backed verification.
- B. A unique index on `AuthorizationAuditLogs` (resource, action, resource id, decision) to make deduplication safe under concurrency. Requires a migration and a duplicate-data check.
- C. A transactional outbox. Requires schema and a processing job.
- D. Keep the current recoverable design and add an operational read-only detection query plus a repair procedure. Detection query (not run; no database access in this stage):
  `SELECT c.ContractId, c.ContractNo, c.CreatedBy FROM Contracts c JOIN Tenders t ON t.ContractId = c.ContractId WHERE NOT EXISTS (SELECT 1 FROM AuthorizationAuditLogs l WHERE l.ResourceName = 'Contracts' AND l.ActionName = 'Create' AND l.ResourceId = CONVERT(nvarchar(50), c.ContractId) AND l.Decision = 'Allow');`

## 7. Independent review findings still open (2026-10-12)

| ID | Finding | Status |
|---|---|---|
| R-01 | Creator event reliability | **Addressed by P0.1** (recoverable) and **by P0.3** (atomic on SQL Server, Option A, SQL-verified on disposable databases). Residual: other contract-creation paths, simulated (not real) commit failure, no unique index; independent re-review pending |
| R-02 | Actual persisted role-permission rows for `MaterialItem` unverified | Open; needs an explicitly authorized read-only check of a test environment. Document wording corrected by P0.1 |
| R-03 | The guard writes a `Tender.Approve` Allow event for every applicable attempt (including retries); idempotency semantics undefined | Open |
| R-04 | Contract workflow actions (SubmitForReview, Sign, MarkEffective, Terminate, RequestRevision, ApproveRevision, IssueNtp, AddDocument) and workflow-runtime `InvalidOperationException` can still produce HTTP 500 for expected denials | Open |
| R-05 | Vendor update has no duplicate check; import paths bypass validation; identifier normalization (for example Persian digits) and database collation not addressed | Open |
| R-06 | A mistaken `Failed` evaluation cannot be voided (no method/screen for `IsActive=false`); evaluator independence, bid-state checks and a late-evaluation race at award are not addressed | Open (policy-dependent) |
| R-07 | On resubmission a dropped discipline keeps its stale decision/comment (only `IsRequired` is cleared; ignored by approval) | Open (low) |

### 7.1 Independent review of P0/P0.1/P0.3 (2026-10-12): disposition after P0.4

| ID | Finding | Disposition |
|---|---|---|
| H-1 | Manual contract path outside P0.1/P0.3: no transaction, no `ContractNo` lock, post-commit `AuthorizeAsync` event whose result was ignored | **Addressed by P0.4** (section 11), SQL-verified on disposable databases |
| M-1 | "Verified not created" after a commit attempt assumes a READ COMMITTED verification read blocks behind an in-flight commit; `READ_COMMITTED_SNAPSHOT` state of `PEMSDB` unknown | Open (needs an owner-authorised read-only check of the database setting) |
| M-2 | Commit uncertainty only simulated | **Partly addressed**: a real session KILL at the commit boundary is now exercised (G1-G3); "commit applied, acknowledgement lost" remains simulated only |
| M-3 | No negative control for the creator-event lock | Open |
| M-4 | No controller tests for `OutcomeUnknown`/recovered messages | **Addressed for the manual controller** (non-SQL and SQL); still open for `TenderController` |
| M-5 | Detection query only covers tender-linked contracts | **Addressed**: an all-contracts variant is documented in 11.8 and exercised by test M10 |
| M-6 | Evidence-trail gaps (database names not logged, no before/after `sys.databases` snapshot) | Open; P0.4 runs also did not log per-test database names |
| M-7 | Creator attribution depends on `Contract.CreatedBy` for repair | Open (design decision) |
| M-8 | Lock-key normalization verified only for the tested classes/collation | Open |
| M-9 | No database uniqueness for events or `ContractNo` | Open (D-OPEN-08, needs a migration and approval) |
| L-3 | Unknown exception types rethrown (HTTP 500) even when verification proved nothing committed | **Fixed in both services** (P0.4): verified "nothing committed" is reported as "not created" for any exception type; test G2/G3 |
| other L | stale doc line references, S1 conflation, lock wait time, reload of pre-tracked entities, seeding | Open / unchanged |

## 8. Revision history (original wording preserved)

| Revision | Original statement | Correction |
|---|---|---|
| 2026-10-12, P0.1 | D-OPEN-03 and section 5: "`WH.*` roles hold no `MaterialItem` rows and cannot adjust/transfer until granted" / "warehouse roles lose adjust/transfer until a grant is made" | Incorrect. `RoleCatalog` seeds `WH.Manager` with `MaterialItem.Edit` and `WH.Storekeeper` with `MaterialItem.Create` and `Edit`. Persisted rows unverified; appropriateness is a least-privilege decision |
| 2026-10-12, P0.1 | Section 5: "unauthenticated-role stock edits now return Forbid" | Imprecise. Stock edits by roles lacking the required permission now return Forbid |
| 2026-10-12, P0.1 | P0-03: "atomic, fail-closed, audited, retry-safe"; "single `SaveChanges` = one transaction" | Overstated. Contract + link are one physical save; the generic audit is a separate save; the creator event is a separate, later write; only the existing `Contracts.Create` deduplication is claimed, and only as far as the tests demonstrate (sequential, in-memory) |
| 2026-10-12, P0.1 | Retry: "no second contract and no second audit row" | Applies to the `Contracts.Create` event only. The `ProtectedActionGuard` writes a new `Tender.Approve` Allow event per attempt (R-03) |
| 2026-10-12, P0.3 | Section 6 "Guarantee level: recoverable, not guaranteed. The creator event is **not** in the same transaction as the contract." and the stage table ("Stage 2 fails ... reports success with `GenericAuditFailed=true`") | No longer true on SQL Server: contract, link, generic audits and creator event are one transaction (section 10). `GenericAuditFailed` is now reachable only on non-transactional providers. Section 6 text kept for history |
| 2026-10-12, P0.4 | Section 10.7 (a): "the lock only synchronizes paths that take it; `ContractService.CreateAsync` ... can still create a contract with the same number" and the review statement that the manual path was out of scope | Superseded: `ContractService` (both `CreateWithCreatorEventAsync` and the legacy `CreateAsync`) now takes the same lock. What remains uncovered is non-application writers (direct SQL, external tools) and any future code path; the pattern search is in 11.4 |
| 2026-10-12, P0.4 | Section 10.9 / T14: detection query "exactly this text" and tender-linked contracts only | The query text in the test differs only in whitespace; manual contracts need the all-contracts variant (11.8) |
| 2026-10-12, P0.3 | Section 6 option D detection query "(not run; no database access in this stage)" | Run on **disposable databases only** (T14, exact text). It has still not been run against `PEMSDB` |
| 2026-10-12, P0.3 | Section 3 "the complete test suite was not run / SQL-backed ... `NOT_RUN`" | Partly superseded: SQL tests for the tender-to-contract path were run (section 10). Everything else SQL-related is still `NOT_RUN` |
| 2026-10-13 | P0-12: "The 27 other SQL-touching test classes ... remain `NOT_RUN`" | Superseded: they ran and passed in the full-suite CMMS verification run (7,879/7,879, disposable databases). The live application and a re-run on `fbc0671` remain `NOT_RUN`; P0 stays NOT PASSED (BLOCKED) |
| 2026-10-12, P0.1 | P0 overall: "NOT PASSED" | Now "NOT PASSED (BLOCKED)" with the independent review findings listed in section 7 |
| 2026-10-13, P0.5 | Sections 10.8 and 11.9: "a read at READ COMMITTED should block behind an in-flight commit, so a successful read reflects the final state" | **Wrong for `PEMSDB`** (`READ_COMMITTED_SNAPSHOT` is ON there): a plain read does not wait. Superseded by the lock-protected verification of section 12; the "verified and nothing found, so not created" branch no longer exists |

## 9. Rollback

The change is code-only, with no data or schema effect. To revert: restore the 17 files from `C:\pt5\backup_original_before_P0\`, delete the 7 new test files, and remove the three added comment lines in `PermissionService.cs` (original content is also in `C:\pt5\PermissionService.cs.HEAD`). No database action is required.

**P0.1 rollback:** the pre-P0.1 versions of `ITenderService.cs`, `TenderService.cs` and `TenderController.cs` are the files in the verification export `C:\pt5\verify\` (taken right after the P0 integration, before P0.1); to revert, copy those three back, delete `PEMS.Application/DTOs/TenderContractResult.cs` and the two `TenderContractCreatorEvent*Tests.cs` files. No separate backup of this document was taken before the P0.1 edits; its earlier statements are preserved in section 8.

**P0.4 rollback:** see section 11.10.

**P0.3 rollback:** see section 10.10 (pre-P0.3 files are in `C:\pt6\verify\`, identical to `C:\pt8\backup_pre_p03\`, which also holds this document as it was before P0.3).

## 10. P0.3: atomic tender-to-contract creation (2026-10-12)

**Approvals recorded (gate owner, P0.3 instruction):** (1) Option A: contract creation and its `Contracts/Create/Allow` creator event are one SQL Server transaction; (2) creating and dropping disposable `PEMS_TEST_*` databases on the local SQL Server Express instance, behind strict connection and name safeguards; (3) investigate and implement a schema-free `ContractNo` concurrency lock only if its semantics can be verified, otherwise document the limit and stop that subtask. Nothing else was approved or changed: no migration, no index, no table, no column, no business-policy decision.

### 10.1 What changed

| File | Change |
|---|---|
| `PEMS.Persistence/Servises/TenderService.cs` | `CreateContractFromWinningBidAsync` and `EnsureContractCreatorEventAsync` rewritten around one explicit transaction inside `Database.CreateExecutionStrategy().ExecuteAsync`. Constructor gained an optional `lockTimeoutMs = 15000` (DI still resolves; tests shorten it). Removed: `IsCommittedAsync`, `RecordCreatorEventAsync` (swallowed exceptions after commit). |
| `PEMS.Persistence/Servises/ContractCreationLocks.cs` (new) | Names of the three transaction-scoped application locks and the `ContractNo` normalization. |
| `PEMS.Application/DTOs/TenderContractResult.cs` | New result flags `OutcomeUnknown`, `RecoveredAfterUncertainCommit`; new status `ContractCreatorEventStatus.OutcomeUnknown`. |
| `PEMS.Application/Interfaces/ITenderService.cs` | Documentation only. |
| `PEMS.Web/Controllers/TenderController.cs` | `CreateContract` handles `OutcomeUnknown` (never says "not created"), the recovered-after-uncertain-commit message and the `OutcomeUnknown` event status. |
| `PEMS.Tests/Procurement/ContractCreationAtomicitySqlTests.cs` (new), `TestSupport/DisposableSqlDatabase.cs` (new), `TestSupport/SqlFaultInjection.cs` (new) | SQL Server tests T1-T14 and their safeguards/fault injection. |
| `PEMS.Tests/Procurement/ContractCreationScopeTests.cs` (new) | Non-SQL: lock names, DI resolution, change-tracker cleanup. |
| `PEMS.Tests/Procurement/TenderContractCreatorEventTests.cs`, `...ControllerTests.cs` | Adapted: they now state that they exercise the **non-transactional fallback** (InMemory), one test replaced (`GenericAuditSaveFailure_WithoutATransaction_*`), one added (`RepairFailureOnRetry_*`). |

### 10.2 Design as implemented (verified by tests unless marked)

Sequence for one call on SQL Server (physical saves A-D are the two-saves-per-`SaveChangesAsync` of `PEMSDbContext`):

1. Guard records for `Tender.Approve` (two saves) are written by `ProtectedActionGuard` **before** and **outside** the transaction. They survive a rollback of the contract creation (T12) and are written on every attempt (R-03 stays open).
2. `strategy.ExecuteAsync` -> `BeginTransaction` -> `sp_getapplock` on the tender -> re-read tender (fresh) -> if the tender already has a contract: ensure its creator event under the per-contract lock and return "already created" (or success-recovered when the contract was created by an earlier attempt of the same call) **before** any `ContractNo` check -> otherwise `sp_getapplock` on the normalized `ContractNo`, uniqueness check, then A (contract + tender link) and B (its generic AuditLog), C (`Contracts/Create/Allow` event) and D (its generic AuditLog) -> `CommitAsync`.
3. Any exception before the commit rolls back A-D together. The strategy re-runs the body for transient errors; each attempt starts from a fresh read.
4. If a commit was attempted and the call still fails, the service reads the database (tender link, event) and reports the true state; if that read is impossible it reports `OutcomeUnknown` and never "not created".

**Why a dedicated scope instead of `PostingScope`.** `PostingScope.RunAsync` was read and was **not** reused because it (a) takes exactly one lock, (b) calls `ChangeTracker.Clear()` on retry, which discards unrelated tracked state of the shared scoped `DbContext` (explicitly not allowed here), (c) returns only `(Success, Error)` and does not record that a commit was attempted. The new scope keeps its pattern (strategy -> transaction -> transaction-owned `sp_getapplock` -> body, same 15 s default, same InMemory/outer-transaction fallback) and adds multiple locks in a fixed order, targeted cleanup and commit-attempt tracking.

**Locks** (all `Exclusive`, `LockOwner = Transaction`, so they are released by commit **or** rollback or a dropped connection; acquired in this order only): `PEMS:Contracts.CreateFromTender:{tenderId}`, then `PEMS:Contracts.No:{SHA-256 hex of the normalized ContractNo}`, then `PEMS:Contracts.CreatorEvent:{contractId}`. Repair takes only the last one, so no wait cycle exists. A lock that is not granted within the timeout (return code < 0) throws internally, rolls back, and the caller gets a "busy, nothing saved" result; the lock is re-acquired on every retry attempt.

**Change-tracker hygiene.** The scope snapshots the tracked entities before it starts. After a failed attempt it detaches only entries it introduced (the new `Contract`, `AuthorizationAuditLog` and generic `AuditLog` rows, the tender it loaded). A tender/bid/package that the caller had already tracked is reloaded from the database before use; if the failed operation left a pointer to the detached contract on a pre-tracked tender, the pointer is cleared (or the tender detached when the database says a contract is linked). `ChangeTracker.Clear()` is never called. Unrelated tracked entities were verified to stay tracked and `Unchanged` (SQL test `FailedCreation_DoesNotDetachOrClearUnrelatedTrackedEntities_*` and the non-SQL test). A pending *edit* of an unrelated entity on the same context is still saved by the operation's own `SaveChanges` (this was already true before P0.3) and its tracker state is not restored if the transaction rolls back; the web request context never has such an edit.

### 10.3 Test environment and safeguards (names only; no secrets)

- Instance identified before any CREATE/DROP: `LAPTOP-DV0OQ25R\SQLEXPRESS`, `SERVERPROPERTY('MachineName')` equals this machine, Express edition, version 17.0.1000.7, server collation `SQL_Latin1_General_CP1_CI_AS`; Windows authentication; the connection string was never printed. `PEMS_TEST_CONNECTION` was not set (default `localhost\SQLEXPRESS`).
- `DisposableSqlDatabase` refuses to run unless: the name matches `^PEMS_TEST_P03_[0-9A-F]{12}$`; the Data Source is the local `SQLEXPRESS` (localhost / `.` / `(local)` / this machine); Integrated Security; the connected instance is on this machine and Express; the database does not already exist. It then applies the **real migration history** and, in `DisposeAsync`, drops exactly that name (single-user, rollback immediate). `PEMSDB`, `PEMSDB_PcatClone` and every pre-existing `PEMS_TEST_*` database (IRN, MRSDESC, VDRPAGED, VDRPO, left by other suites) were never touched. After the runs no `PEMS_TEST_P03_*` database remained. One database per test, e.g. `PEMS_TEST_P03_E17224CAD438` (observed while run 1 executed).

### 10.4 T1-T14 outcomes

| Id | Scenario | Result (final run) | Persisted state asserted by direct SQL |
|---|---|---|---|
| T1 | Happy path: contract, link, both generic audits and the creator event committed together; `CreatedBy` recorded | 1/1 passed |  |
| T2 | Failure at A (business save): nothing persisted, tracker clean | 1/1 passed | DB: Contracts 0, Tenders.ContractId NULL, creator events 0, generic audit rows for Contract/AuthorizationAuditLog/tender link 0 |
| T3 | Failure at B (generic audit of the business save): A is rolled back | 1/1 passed | same zero state (before P0.3 the contract stayed committed) |
| T4 | Failure at C (creator event row): contract rolled back | 1/1 passed | same zero state |
| T4b | Repair failure on retry: contract exists, reported as existing + `NotRecorded`; later retry repairs for the original creator | 1/1 passed | DB: 1 contract, 0 then 1 event (original creator) |
| T5 | Failure at D (generic audit of the event): event and contract rolled back | 1/1 passed | same zero state |
| T6 | Retry after every injected failure (inside T2-T5: same context, then a third call adds nothing) and a transient failure retried by the strategy | 1/1 passed | DB after retry: Contracts 1, event 1 (creator), `Contract` Insert audit 1, `AuthorizationAuditLog` Insert audit 1, tender-link audit 1 |
| T7a | Commit acknowledgement lost after a real commit (simulated): retry confirms, no duplicate | 1/1 passed | 1 real commit; Contracts 1, event 1 |
| T7b | ... retries exhausted, verification read works: success confirmed from the database | 1/1 passed | Contracts 1, event 1 |
| T7c | ... retries exhausted and verification read fails: `OutcomeUnknown`, never "not created"; next call = already created | 1/1 passed | Contracts 1, event 1 |
| T7d | Commit fails before reaching the server: rollback, retry creates exactly one | 1/1 passed | Contracts 1, event 1, 1 real commit |
| T7e | Every commit fails before the server, retries exhausted: verified "not created" | 1/1 passed | all zero |
| T8 | Concurrent requests for the same tender (2/4/4/6 callers; same and different numbers; 15 iterations each) | 4/4 passed | per iteration: exactly 1 contract, 1 event for the winning user; DB total = 15 contracts, 15 events per shape |
| T9 | Repair vs repair vs create on a legacy contract (15 iterations) | 1/1 passed | exactly 1 event, `UserId` = original creator |
| T10 | Identical `ContractNo` (case/space variants) across tenders, concurrent (2x1, 2x2, 4x1 callers; 12 iterations each) | 3/3 passed | exactly 1 contract per number, exactly 1 linked tender |
| T10c | Lock key vs database collation soundness | 1/1 passed | see 10.7 |
| T11 | Application-lock timeout and recovery: tender lock (a), `ContractNo` lock (b, tender lock not leaked), creator-event lock (c) | 3/3 passed | zero state during the hold; success after release |
| T12 | Guard (`Tender.Approve`) audit rows survive a rollback of the creation; each attempt writes its own row; exactly one creator event | 1/1 passed | after failed attempt: guard rows 1, contracts 0, events 0; after retry: guard rows 2, event 1 (manager one) |
| T13 | Creator self-approval denied by SoD after creation and after legacy repair (control user allowed; blind before repair) | 1/1 passed | DeniedBySoD for the creator; Allow for another user |
| T14 | Detection query: no rows after success and after repair; finds a deliberately seeded orphan | 1/1 passed | 0 rows / 1 row (the orphan) / 0 rows |
| - | Unrelated tracked entities untouched after a failed creation (shared context) | 1/1 passed | project entity still tracked and Unchanged; operation entries detached |

Final SQL run: **28 tests, 28 passed, 0 failed, 0 skipped**. Concurrency classes (T8, T9, T10) were run two more times, **8/8** and **8/8** passed (repeatability). The first full SQL run (26 tests, before T4b/T10c and the `ss/ae/oe` normalization) also passed 26/26.

### 10.5 Failure-mode table (SQL Server, Option A) with evidence

| Situation | Persisted state | Result reported | Evidence |
|---|---|---|---|
| Fail at A | nothing | not created ("nothing saved") | T2 |
| Fail at B (A already executed in the transaction) | nothing (A rolled back) | not created | T3 (before P0.3: contract committed) |
| Fail at C | nothing | not created | T4 |
| Fail at D | nothing | not created | T5 |
| Repair fails on retry (contract exists, event missing) | contract only (as before) | "already exists", event `NotRecorded`; later retry repairs for the original creator | T4b |
| Transient error before commit | rolled back, strategy re-runs | success, exactly one contract/event | T6 |
| Commit acknowledgement lost **after** a real commit (simulated) | committed | success, `RecoveredAfterUncertainCommit`; no duplicate; one real commit | T7a |
| ... and every retry also fails, verification read works | committed | success (confirmed by reading the database) | T7b |
| ... and verification read also fails | committed (unknown to the caller) | `OutcomeUnknown`, **not** "not created"; next call returns "already created" | T7c |
| Commit fails **before** reaching the server | nothing | retry creates exactly one | T7d |
| Every commit fails before the server, retries exhausted | nothing | "not created", justified by a successful verification read | T7e |
| Same tender, 2-6 concurrent callers, 15 runs x 4 shapes | 1 contract, 1 event (winner's) | one winner, others "already created" | T8 |
| Repair vs repair vs create on a legacy contract, 15 runs | 1 event, original creator | - | T9 |
| Same `ContractNo` (case/space variants), 2-4 tenders, 12 runs x 3 shapes | exactly 1 contract | losers "number exists" / "already created" | T10 |
| Lock held by another connection | nothing | "busy, nothing saved" within the timeout; lock not leaked; works after release | T11a/b/c |
| Crash mid-transaction | SQL Server rolls back | - | not executed (inherent to the engine); assumption |

### 10.6 Sensitivity checks (verification export only; the original was never changed by them)

Each variant was applied to the export copy of `TenderService.cs`, the tests re-run, and the file restored (byte-identical to the original, checked with `cmp`). The variants ran before the last change to `ContractCreationLocks.cs` (the `ss/ae/oe` normalization, which none of the variants touch); the full SQL class, the repeats and the non-SQL suite ran after it.

| Variant | Mutation of the production code (export only) | Result | Reading |
|---|---|---|---|
| S1 | run the body without the transaction (and therefore without the locks) | 1 passed, 9 failed of 10 | Rollback tests (T3, T4, T5), recovery (T7a), guard/rollback (T12) and all concurrency shapes (T8) fail: the tests do depend on the transaction. T7e passes because with no transaction there is no commit to fail. |
| S2 | remove the `ContractNo` lock | 0 passed, 4 failed of 4 | T10 (all shapes) and T11b fail; duplicates of the same number were actually created (2-3 contracts): the lock is what prevents them |
| S3 | remove the tender lock | 1 passed, 5 failed of 6 | T8 and T11a fail (the tender-lock hold has no effect, and concurrent callers no longer all end as 'one winner, others already created'); T9 still passes because the creator-event lock alone protects repair |
| S4 | remove the "created by an earlier attempt of this call" recovery branch | 1 passed, 1 failed of 2 | T7a fails (retry reports "already created" instead of success); T7b passes (it recovers through the verification read) |

### 10.7 `ContractNo` lock: what is verified and what is not

Verified on SQL Server: the lock is shared across connections (T10, T11b); the resource name is derived from a normalized value so that case and surrounding-space variants of the same number take the same lock (T10, T10c); the lock is transaction-owned and not leaked after a failure (T11b); timeouts return a clean "busy" result and recover (T11a/b); the race is real without it (S2: runs created two to three contracts with the same number). Normalization: trim, Unicode compatibility form (full-width, ligatures such as `fi`), upper-case, and the three collation expansions `ss=ß`, `ae=æ`, `oe=œ` that T10c found SQL Server treats as equal; the test `T10c` asserts "SQL says equal => lock keys equal" for these classes on this instance's collation.

**Unresolved (documented, not fixed):** (a) the lock only synchronizes paths that take it; `ContractService.CreateAsync`, imports, scripts and direct SQL can still create a contract with the same number; (b) other collations (accent-insensitive, Persian/Arabic collations) and expansions not enumerated by T10c could treat two numbers as equal while their lock keys differ; (c) a unique index remains the only complete guarantee (D-OPEN-08, needs a migration and a duplicate-data check, **not approved or done**); (d) the existing check is still a query (`AnyAsync`) so the uniqueness it gives is "no two callers of this path at once".

### 10.8 Known limitations and assumptions (not verified)

- "Uncertain commit" was **simulated** (an interceptor throws after a real commit or before commit). A genuine network failure, killed connection or server crash was not exercised.
- When retries are exhausted after a commit attempt, "not created" relies on the verification read being definitive: a read at READ COMMITTED should block behind an in-flight commit, so a successful read reflects the final state. This is reasoning about the engine, not a test.
- The in-memory suite (and any non-SQL provider) has no transaction: there a failure after the first save leaves the contract committed and the result is classified from the database (the P0.1 "recoverable" behavior). Only the SQL tests prove atomicity.
- Collation scope: verified only for `SQL_Latin1_General_CP1_CI_AS`.
- The lock wait (default 15 s) holds the request thread; under contention a user can see the "busy" message.
- Unchanged by P0.3 and still open: R-02..R-07, D-OPEN-01..11, authorization decisions, view permissions, `ContractWorkflowService` paths, PO/Package/Contract identifier uniqueness at database level, any other `SaveChanges` caller.

### 10.9 Recovery procedure (operations)

1. Detect (read-only; `T14` runs exactly this text on a disposable database): `SELECT c.ContractId, c.ContractNo, c.CreatedBy FROM Contracts c JOIN Tenders t ON t.ContractId = c.ContractId WHERE NOT EXISTS (SELECT 1 FROM AuthorizationAuditLogs l WHERE l.ResourceName = 'Contracts' AND l.ActionName = 'Create' AND l.ResourceId = CONVERT(nvarchar(50), c.ContractId) AND l.Decision = 'Allow');` It has **not** been run against `PEMSDB` (only after P0.1/P0.3 can new orphans be excluded; older contracts, created before these changes, may appear).
2. Repair: repeat "create contract" for the tender from the UI (any authorized user) or call `EnsureContractCreatorEventAsync(contractId)`; the event is written for `Contract.CreatedBy` (not for the caller) under a per-contract lock and transaction; `CreatorUnidentified` means manual review.
3. After an `OutcomeUnknown` message: do not assume either state; run the detection query and open the tender; repeating the action is safe (never a second contract).
4. A contract numbered identically by another path: resolve manually; no automatic cleanup exists.

### 10.10 Rollback

Code-only. Restore the pre-P0.3 (P0.1) versions from `C:\pt6\verify\` (identical to `C:\pt8\backup_pre_p03\`): `TenderService.cs`, `TenderController.cs`, `ITenderService.cs`, `TenderContractResult.cs`, `TenderContractCreatorEvent*Tests.cs`; delete `ContractCreationLocks.cs`, `ContractCreationAtomicitySqlTests.cs`, `ContractCreationScopeTests.cs`, `TestSupport/DisposableSqlDatabase.cs`, `TestSupport/SqlFaultInjection.cs`; restore this document from `C:\pt8\backup_pre_p03\P0_HARDENING_GATE_EVIDENCE.md.before_p03`. No data, schema or database action is required (the disposable databases are already dropped).

## 11. P0.4: manual contract creation inside the P0 scope (finding H-1, 2026-10-12)

**Owner decision (recorded as D-10):** bring `ContractController.Create` / `ContractService.CreateAsync` into the P0 remediation instead of accepting the gap. Constraints from the instruction: no migration, index, table, column, seed or permission change; no `ChangeTracker.Clear()`; reuse (not duplicate) the transaction/lock implementation; keep legacy repair and detection; do not claim all contract-creation paths are covered unless the repository audit supports it.

### 11.1 Disposition of H-1

| Before (verified by reading the code, review finding H-1) | After P0.4 |
|---|---|
| `ContractService.CreateAsync`: plain `AnyAsync` duplicate check, two separate commits, no `ContractNo` lock | One SQL Server transaction inside the execution strategy, under the same `ContractNo` application lock as the tender path (`ContractService.cs`, `CreateAttemptAsync`) |
| Creator event written **after** commit by `AuthorizeAsync` in the controller; its decision ignored; an exception gave HTTP 500 with the contract committed and no event; a non-Allow row left SoD blind | Authorization is evaluated **before** any insert (`CreateCoreAsync`); denied, inconsistent or failed evaluation = no contract. The `Contracts/Create/Allow` event for the authenticated creator and its generic audit are written inside the contract's transaction |
| No identity requirement | No authenticated user id = `Forbid`, no contract, denial recorded (`ContractController.Create`) |
| Unexpected exception after commit → HTTP 500 / unknown state | Commit-attempt tracking, verification read, `OutcomeUnknown` (never "not created" when unknown), `RecoveredAfterUncertainCommit` |

### 11.2 Changed files

| File | Change |
|---|---|
| `PEMS.Persistence/Servises/ContractCreationScope.cs` (new) | The transaction + execution strategy + transaction-owned `sp_getapplock` + targeted tracker cleanup, **extracted unchanged in behavior** from `TenderService` so both paths share one implementation (no `ChangeTracker.Clear()`) |
| `PEMS.Persistence/Servises/ContractCreatorEvents.cs` (new) | Shared creator-event helpers: exists check, row shape, per-contract-locked repair (`CreatedBy` → user) and the repair wrapper |
| `PEMS.Persistence/Servises/TenderService.cs` | Now delegates to the two shared helpers (same lock order and semantics); classification: a **verified** "nothing committed" after a commit attempt is reported as "not created" for any exception type (previously an unmapped exception such as a raw `SqlException` was rethrown as HTTP 500) |
| `PEMS.Persistence/Servises/ContractService.cs` | New constructor parameters (all optional so `new ContractService(ctx)` still compiles): access control, logger, lock timeout; `CreateWithCreatorEventAsync`, `EnsureCreatorEventAsync`; the legacy `CreateAsync` now goes through the same lock and transaction (no authorization and no event, as it has no user) |
| `PEMS.Application/Interfaces/IContractService.cs`, `PEMS.Application/DTOs/ContractCreateResult.cs` (new) | New methods and the result type |
| `PEMS.Web/Controllers/ContractController.cs` | `Create`: legacy role gate kept; identity required; service call; mapping of denied / indeterminate / unknown / nothing-saved / recovered results |
| `PEMS.Tests/...` | `ManualContractCreationTests` (non-SQL), `ManualContractCreationSqlTests` (SQL), additions to `TestSupport/SqlFaultInjection.cs` (no-retry strategy, any-`SqlException` strategy, session-KILL interceptor), `ContractControllerAuthorizationTests` now passes the access-control service to `ContractService` |

### 11.3 Sequence for one manual creation (SQL Server)

1. Controller: legacy role gate (`HasPermissionAsync`, unchanged policy) → authenticated user id required.
2. Service, non-DB validation (blank number, negative value, inverted dates) → returned as errors, authorization is not even evaluated.
3. Service, **outside the transaction**: `AuthorizeAsync(Contracts.Create, ResourceId = null)`. `Allowed && Reason == Allow` is the only pass; a denial is audited by `AuthorizeAsync` itself; an exception or an inconsistent decision is "indeterminate". Both stop here, before any contract exists. This pre-authorization row (ResourceId NULL) is distinct from the creator event (ResourceId = contract id) and from the `Tender.Approve` guard rows.
4. `strategy.ExecuteAsync` → begin transaction → `sp_getapplock` on the normalized `ContractNo` → re-read: a contract with this number created by an **earlier attempt of this call** is a recovered success, any other is "number exists" → insert contract (A) + its generic audit (B) → creator event for the authenticated creator (C) + its generic audit (D) → commit.
5. Any failure before commit rolls A–D back together. After a commit attempt, a failed call reads the database (contract by number, event) and reports success-recovered, "number exists", verified "not created", or `OutcomeUnknown`.

Lock order is unchanged (tender → contract number → creator event); the manual path takes only the contract-number lock, so no wait cycle exists. The creator in the event is the authenticated user of the request (`ICurrentUserService.UserId`); repair uses `Contract.CreatedBy`, never the user who repairs.

### 11.4 Contract-creation path inventory and lock coverage

Search (read-only, `*.cs` and `*.sql`, excluding `bin`, `obj`, migrations and tests): `new Contract`, `Contracts.Add`/`AddAsync`, `INSERT INTO Contracts`, `FromSqlRaw`/`ExecuteSql*` on contracts, `HasData` for contracts, `.ContractNo =`.

| Path | Transaction | `ContractNo` lock | Creator event |
|---|---|---|---|
| `TenderService.CreateContractFromWinningBidAsync` | yes | yes | atomic |
| `ContractService.CreateWithCreatorEventAsync` (UI) | yes | yes | atomic, creator = authenticated user |
| `ContractService.CreateAsync` (legacy, no user; only tests call it) | yes | yes | none by design; detect and repair (11.8) |
| imports, seeds, background jobs, raw SQL in application code | none found | n/a | n/a |

**What this does and does not prove.** Every contract-creating code path found by the patterns above takes the lock. It is a pattern search, not a proof: direct SQL, external tools, scripts, a future code path, or code that builds `Contract` objects in a way the patterns miss are not covered. The lock gives mutual exclusion between participating writers only; it is **not** a uniqueness guarantee. No `ContractNo` unique index exists; D-OPEN-08 (migration, duplicate-data check) is still needed and was not done.

### 11.5 SQL evidence (disposable `PEMS_TEST_P03_<hex>` databases, real migrations, direct SQL assertions)

| Id | Scenario | Result (final run) | Persisted state asserted by direct SQL |
|---|---|---|---|
| M1 | Happy path: contract, both generic audits, creator event for the actual creator; the pre-authorization row is separate (ResourceId NULL) | 1/1 passed | Contracts 1 (`CreatedBy` = creator), event 1 (creator id, ResourceId = contract id), `Contract` Insert audit 1, `AuthorizationAuditLog` Insert audits 2, pre-auth Allow rows 1 |
| M2 | Authorization denied (Viewer): no contract, denial persisted | 1/1 passed | Contracts 0, events 0, `DeniedByRole` row (ResourceId NULL) 1, Allow rows 0 |
| M3a/M4a/M3b/M4b | Failure injected at A, B, C, D inside the transaction; then retry on the same context; then a third call | 4/4 passed | after failure: Contracts 0, events 0, `Contract` audit 0, pre-auth row 1 (outside the transaction), tracker clean; after retry: Contracts 1, event 1, `Contract` audit 1; third call: "already exists", nothing added |
| M5 | Same `ContractNo` (case/space variants) across manual and tender paths concurrently: 2 manual, 1+1, 2+2 callers, 12 iterations each | 3/3 passed | exactly 1 contract per number, exactly 1 winner, losers "number exists", exactly 1 event for the winner, tender linked only if the tender caller won |
| M6 | Manual creator cannot approve their own contract (another user can) | 1/1 passed | creator: `DeniedBySoD`; control user: Allow |
| M7a-e | Commit uncertainty (simulated): ack lost after commit; exhausted + verification works; exhausted + verification fails; fails before the server; every commit fails | 5/5 passed | Contracts 1/1/1/1/0, `OutcomeUnknown` only in M7c, never "not created" in M7c |
| M8a-e | Controller on SQL: Allow; user-aware Deny after a passing role gate; exceptional save failure; unknown outcome; recovered after uncertain commit | 5/5 passed | redirects/Forbid/messages as asserted; DB state per case |
| M9 | Contract-number lock held elsewhere: timeout without committing; the legacy `CreateAsync` also blocks; recovers after release | 1/1 passed | zero state during the hold; success after release |
| M10 | Legacy manual contract without event: detected by the all-contracts query, repaired once under 6 concurrent callers, then SoD denies the creator | 1/1 passed | 1 detected row, then 1 event for the original creator, then 0 rows |
| G1 | **Real** session KILL at the commit boundary; retry strategy retries any `SqlException` | 1/1 passed | killed 1 session; commit failure was a `SqlClient.SqlException`; Contracts 1, event 1 |
| G2 | **Real** session KILL at the commit boundary; no retries: verified "not created", then a plain retry succeeds | 1/1 passed | Contracts 0 / events 0 after the kill; Contracts 1 / event 1 after the retry |
| G3 | **Real** session KILL, tender path, no retries | 1/1 passed | Contracts 0 / link NULL / events 0, then 1 / 1 |

Final runs: manual SQL class **25/25** passed; tender SQL regression class **28/28** passed (after the shared-scope extraction); concurrency tests repeated twice, **11/11 and 11/11** passed; focused non-SQL tests **129/129** passed; full non-SQL suite **2806 passed, 0 failed, 0 skipped**.

### 11.6 Simulated versus genuinely exercised

- **Genuinely exercised (G1-G3):** the test's interceptor closes the application's own SQL Server session with `KILL <spid>` on the disposable database just before the commit request is sent (guards: the session must belong to the expected `PEMS_TEST_P03_*` database, be a user session and not the administrative connection; the SQL Server service was never touched). SqlClient raised a real `SqlException` at commit (type asserted, error number not recorded), the server rolled the transaction back, and the code (a) retried with a new connection and created exactly one contract (G1), or (b) without retries read the database with a fresh connection and reported "nothing saved" instead of a server error, and a later retry succeeded (G2, G3).
- **Simulated:** "commit applied but acknowledgement lost" (M7a-c: an interceptor throws after the real commit), "commit never reached the server" (M7d/e), retry exhaustion and unreadable verification (injected read failures).
- **Not exercised:** a real commit that the server applied while the client lost the response (cannot be forced deterministically), network partitions, SQL Server restart or failover (prohibited), a different Windows login.

### 11.7 Sensitivity checks (verification export only; production files restored and compared)

| Variant | Mutation (verification export only) | Result | Reading |
|---|---|---|---|
| V1 | run the body without the transaction and locks (shared `ContractCreationScope`) | 1 passed, 5 failed of 6 | rollback tests (M3b, M4a, M4b), recovery (M7a) and the controller failure test (M8c) fail; M3a passes because a failure at A happens before anything is written |
| V2 | remove the `ContractNo` lock from `ContractService` | 0 passed, 4 failed of 4 | M5 (all shapes) and M9 fail; duplicate numbers were actually created |
| V3 | ignore a denied authorization decision | 0 passed, 4 failed of 4 | M2, M8b and the non-SQL denial tests fail (a contract was created for a denied user) |
| V4 | do not write the creator event | 0 passed, 3 failed of 3 | M1, M3b and M6 fail (no event; SoD blind) |
| V5 | remove the "created by an earlier attempt of this call" branch | 0 passed, 2 failed of 2 | M7a and M7b fail (retry reported "already exists") |
| V6 | remove the "verified nothing committed, any exception type" branch | 0 passed, 1 failed of 1 | G2 fails because the raw `SqlException` ("A transport-level error has occurred ... No process is on the other end of the pipe") reaches the caller instead of a verified "nothing saved" result (confirmed in two reruns with diagnostics). The very first V6 run failed one assertion earlier (the session-kill counter was 0); that was not reproduced in the two reruns or in 4 pristine runs of G1-G3, and its cause was not determined |

### 11.8 Detection and repair for contracts without a creator event

The P0.3 query only sees tender-linked contracts. Manual and legacy contracts need the all-contracts variant (read-only; run only on a disposable database in this stage, **not** on `PEMSDB`):

`SELECT c.ContractId, c.ContractNo, c.CreatedBy FROM Contracts c WHERE NOT EXISTS (SELECT 1 FROM AuthorizationAuditLogs l WHERE l.ResourceName = 'Contracts' AND l.ActionName = 'Create' AND l.ResourceId = CONVERT(nvarchar(50), c.ContractId) AND l.Decision = 'Allow');`

Contracts created before P0.1/P0.4 will appear (they never had an event). Repair: `IContractService.EnsureCreatorEventAsync(contractId)` (or `ITenderService.EnsureContractCreatorEventAsync`); both use the same helper: the event is written for `Contract.CreatedBy` under a per-contract lock and transaction; `CreatorUnidentified` means manual review. Test M10 covers detection, six concurrent repairs (one event), and SoD afterwards. The query does not detect an event recorded for the wrong user.

### 11.9 Limitations and unresolved items

- No database uniqueness for `ContractNo` or for the creator event (needs a migration and approval); duplicate events are harmless to SoD, duplicate numbers are possible through any non-participating writer.
- The `READ_COMMITTED_SNAPSHOT` setting of `PEMSDB` is unverified; the "verified not created" conclusion after a commit attempt relies on a verification read that reflects the final state (review finding M-1, open).
- The tests do not prove behavior for collations other than the test instance's, or for a genuinely applied-but-unacknowledged commit.
- Behavior change to be aware of: a user who passes the legacy role-only gate but is denied by the user-aware engine can no longer create a contract; before, the contract was created and the denial row was ignored.
- The pre-authorization row is written outside the transaction on every attempt (also after a rolled-back creation).
- Authorization is evaluated once before the transaction; a permission change between that check and the commit is not re-checked.
- The same repair attribution limits apply (`Contract.CreatedBy` is a user name).
- Remaining open items are unchanged: R-02..R-07, D-OPEN-01..11, P0-09, P0-10, M-1, M-3, M-6..M-9, and the independent review of P0.4.

### 11.10 Rollback

Code-only. Restore the pre-P0.4 files from `C:\pt9\backup_pre_p04\` (`TenderService.cs`, `ContractService.cs`, `IContractService.cs`, `ContractController.cs`, `ContractControllerAuthorizationTests.cs`, `TenderContractResult.cs`, and this document; SHA-256 list `C:\pt9\backup_sha256.txt`), delete `ContractCreationScope.cs`, `ContractCreatorEvents.cs`, `ContractCreateResult.cs`, `ManualContractCreationTests.cs`, `ManualContractCreationSqlTests.cs`, and remove the three helper classes appended to `TestSupport/SqlFaultInjection.cs` (`NoRetryStrategy`, `RetryAnySqlExceptionStrategy`, `KillSessionAtCommitInterceptor`). The P0.3 state is also preserved in `C:\pt8\backup_pre_p03` and `C:\pt6\verify`. No data, schema or database action is required (disposable databases were dropped by the tests).

## 12. P0.5: RCSI-safe contract-creation outcome verification (finding H-1 of the P0.4 review, 2026-10-13)

**Trigger.** The owner-authorised read-only diagnostic of `PEMSDB` (section 11 review) found `is_read_committed_snapshot_on = 1` (snapshot isolation off, collation `SQL_Latin1_General_CP1_CI_AS`), while the `model` database, and therefore every disposable test database, had it OFF. With RCSI ON a plain `SELECT` never waits behind a commit that is still being applied, so "I read the database and found no contract" is **not** evidence that the transaction did not commit. The earlier statement (sections 10.8 and 11.9) that a READ COMMITTED read "should block behind an in-flight commit" is false for this database setting and is superseded here.

### 12.1 Behavior after P0.5

After a **commit request was issued** (`State.CommitAttempted`) and the call still fails, the service no longer concludes anything from a plain read:

| Evidence | Result |
|---|---|
| The commit request was never issued (failure before `CommitAsync`) | Definitive: the transaction was rolled back by the client. Mapped from the exception type, **no verification read** (manual: `ContractService.ClassifyFailureAsync`; tender: `TenderService.ClassifyCreateFailureAsync`) |
| Commit requested, then a **lock-protected** read succeeds and finds a contract/link created by this call | Success, `RecoveredAfterUncertainCommit` |
| ... finds a contract/link created by someone else | "Number exists" / "already created" |
| ... finds nothing | "Nothing saved" (see the barrier argument below) |
| ... the lock cannot be obtained within the timeout, or the read fails | `OutcomeUnknown` (never "nothing saved") |
| Repair (`ContractCreatorEvents.EnsureAsync`), commit requested | event found = `Repaired`; not found after the lock = `NotRecorded`; lock/read failure = `OutcomeUnknown` |
| Non-transactional provider (InMemory, caller-owned transaction) | unchanged: direct read, no atomicity claim |

**The barrier argument (what is and is not claimed).** `ContractCreationScope.TryReadUnderLockAsync` first takes the same transaction-owned application lock that the original transaction took at the start of its attempt (tender lock for the tender path, contract-number lock for the manual path, creator-event lock for repair) and only then reads. A `LockOwner = Transaction` lock is released only when the owning transaction ends (commit, rollback, or the death of its session). So when the verifier is granted the lock, the original transaction has ended, and a statement started afterwards sees its final state even under RCSI. If the original session is half-dead and the server has not noticed yet, the lock is not granted, and the answer is `OutcomeUnknown`. This is a consequence of SQL Server's documented locking semantics **and** is proven by the tests below with real sessions (R2, R3); it is not assumed.

Retries are protected by the same lock: every attempt takes it before reading, so a retry cannot race a still-resolving earlier attempt.

### 12.2 Changed files

| File | Change |
|---|---|
| `PEMS.Persistence/Servises/ContractCreationScope.cs` | New `TryReadUnderLockAsync` / `LockedRead<T>` (lock-protected verification read; failure = not verified) |
| `PEMS.Persistence/Servises/ContractService.cs`, `TenderService.cs` | Failure classification rewritten as in 12.1 (the P0.4 "verified and nothing found, so not created" branch is gone) |
| `PEMS.Persistence/Servises/ContractCreatorEvents.cs` | Repair-failure classification as in 12.1 |
| `PEMS.Tests/TestSupport/DisposableSqlDatabase.cs` | Every disposable database now gets `READ_COMMITTED_SNAPSHOT ON` explicitly, **verified by reading `sys.databases`** (setup fails otherwise); `IsRcsiOnAsync`; an exact-name audit trail (`PEMS_TEST_DB_AUDIT_LOG`) |
| `PEMS.Tests/TestSupport/SqlFaultInjection.cs` | `SessionKiller` (guarded `KILL`), `InFlightHolder` (separate real session holding an application lock and optionally an uncommitted contract), `FirstReaderAfterCommitFailureHook` |
| `PEMS.Tests/Procurement/ManualContractCreationSqlTests.cs` | New tests R1-R7 (below) |

No schema, migration, index, seed, permission, appsettings or data change; no `ChangeTracker.Clear()`.

### 12.3 New SQL tests (disposable databases, RCSI ON, real sessions)

| Test | What it proves | Real vs simulated |
|---|---|---|
| R1 | The disposable database really has RCSI ON, independent of `model` | real |
| R2 | An open transaction holds the contract-number lock and an uncommitted contract. A plain read returns 0 immediately (RCSI blindness shown); the lock-protected read cannot verify while it is open; after the commit it sees the contract | real sessions and locks |
| R3 | Verification waits behind the open transaction, then the holder's session is **really killed** (`KILL`); the read is then verified and finds 0 (rolled back by the server) | real session kill |
| R4 | Manual path: commit requested and failed, original transaction still open when verification runs, plain read would have shown 0 → result is `OutcomeUnknown`, not "nothing saved"; after the holder commits, a retry reports "number exists" and creates no duplicate | commit failure injected; in-flight original = separate real session |
| R5 | Same, but the holder session is killed while verification waits → proven "nothing saved" (elapsed ≥ 500 ms shows it waited), then a retry creates exactly one contract and one event | as R4, plus real kill |
| R6a / R6b | The tender-path equivalents (tender lock) | as R4 / R5 |
| R7 | Repair path: `OutcomeUnknown` while the event lock is held by an in-flight session, `NotRecorded` only after that session is killed, then a normal repair writes the event for the original creator | as R4 / R5 |

The earlier tests (M1-M10, G1-G3, T1-T14) were re-run on RCSI-ON databases unchanged except where noted in the revision history.

### 12.4 Was a committed-but-unacknowledged transaction truly exercised?

**No.** What is genuinely exercised: server-side `KILL` of the application's session at the commit boundary (G1-G3, before the commit request reaches the server), a killed or committed in-flight session behind the verification lock (R2-R7), and RCSI visibility. What is simulated: an interceptor throwing after a real commit (M7a-c, T7a-c), commit failures before the server, and the "original transaction still resolving" state (a separate session stands in for it). **Not reproducible here:** a commit that the server applied while the client lost the response. Reproducing it requires interposing on the network (a TCP proxy that drops the commit acknowledgement); the local `SQLEXPRESS` instance accepts shared-memory connections only (TCP is not enabled, verified by a refused TCP connection), and enabling TCP needs a server configuration change and service restart, which are out of scope. The lock-barrier argument above is what covers that case, and R2/R3 prove the barrier, not the network drop.

### 12.5 Legacy `ContractService.CreateAsync` (audit, no change made)

- **Production callers: none.** The only production consumer of `IContractService` is `ContractController`, which no longer calls `CreateAsync` (it calls `CreateWithCreatorEventAsync`); `Program.cs` only registers the service; no other project, view or script calls it.
- **Test callers:** `IdentifierValidationTests` (6 contract-related calls), `ManualContractCreationTests` (1), `ManualContractCreationSqlTests` (M9, M10, R7 use it to seed an event-less contract).
- **What it can do:** create a contract without a user: **no** user-aware authorization and **no** creator event, so a contract created this way is invisible to the `Contracts.Create↔Approve` SoD check until repaired. It does take the shared `ContractNo` lock and the transaction.
- **Smallest safe options (not applied, API unchanged):** (1) mark it `[Obsolete]` on the interface and implementation (non-breaking; the ~7 test call sites then warn); (2) later move the seeding use in tests to a dedicated test helper and remove or make the method non-public after the owner confirms nothing outside this repository (scripts, other deployments) uses it; (3) detection and repair stay available (11.8). Needs an owner decision; project-scoped permission handling (finding M-4) was not touched.

### 12.6 Limitations and remaining blockers

- Only the **application lock** gives the barrier; writers that do not take it (direct SQL, other tools) are not covered, and no database uniqueness exists (D-OPEN-08, migration not approved).
- A half-open or very slow session makes verification time out (default 15 s, one wait per verification) and the user sees `OutcomeUnknown`, by design.
- Tests run in one process against one local instance; collation `SQL_Latin1_General_CP1_CI_AS` only; `PEMSDB` itself was not used for any test.
- Still open: R-02..R-07, D-OPEN-01..11, P0-09, P0-10, review findings M-3 (no negative control for the creator-event lock), M-4 (project-scoped users blocked by the pre-check), M-6..M-9, the legacy `CreateAsync` decision, and the independent review of P0.5. **P0 remains `NOT PASSED (BLOCKED)`.**

### 12.7 Rollback

Code-only. Restore the pre-P0.5 copies in `C:\pt10\backup_pre_p05\` (SHA-256 list `C:\pt10\backup_sha256.txt`): `ContractCreationScope.cs`, `ContractCreatorEvents.cs`, `ContractService.cs`, `TenderService.cs`, `DisposableSqlDatabase.cs`, `SqlFaultInjection.cs`, and this document; remove the R1-R7 tests from `ManualContractCreationSqlTests.cs`. No data, schema or database action is needed (all disposable databases were dropped by the tests; the audit trail lists them).

### 12.8 Evidence (2026-10-13)

| Check | Result |
|---|---|
| Manual-path SQL tests (`ManualContractCreationSqlTests`, disposable RCSI-ON databases, real migrations) | **33 passed, 0 failed** |
| Tender-path SQL regression (`ContractCreationAtomicitySqlTests`) | **28 passed, 0 failed** |
| Non-SQL suite in the pristine export (`C:\pt10\verify`, production files `cmp`-identical to the repository copies) | **2806 passed, 0 failed, 0 skipped** (42 min) |
| Sensitivity variants (production code changed in the export only): V1 no transaction, V2 no ContractNo lock, P1 plain-read verification, P2/P3/P4 unverified-means-not-created/recorded | each fails the intended tests, as designed (logs `C:\pt10\logs\sens_*.txt`) |
| `PEMSDB` | no test and no write touched it; the only database access in this task was the owner-authorised read-only diagnostic recorded in section 11 |
| Disposable databases | created and dropped by the tests only; no `PEMS_TEST_P03_*` database remains (verified with a read-only `sys.databases` query) |

## 13. P0-09 and P0-10 per owner decisions D-11, D-12, D-13 (2026-10-14)

### 13.1 Scope

Code-only change. No migration, schema, index or appsettings change. One additive startup seeder. No database outside the test process was touched.

| Decision | Change | Files |
|---|---|---|
| D-11 (P0-09) | View gate on read actions: `VendorController` Index/Details (`Vendors.View`), `ContractController` Index/Details (`Contracts.View`) and the Create form (`Contracts.Create`), `IrnReceiptController` Index/Details (`IRN.View`), `StoreRequisitionController` Index/Details (`StoreRequisition.View`), `MaterialItemController` Index/Details, `InventoryController.Index` and `InventoryTransactionController.Index` (`MaterialItem.View`), `ReportController.Procurement` (`ProcurementPackage.View`). Denial returns `Forbid`. | the eight controllers; `PEMS.Persistence/Seed/ProcurementReadViewSeeder.cs`; `PEMS.Web/Program.cs` (seeder runs after `RoleCatalogSeeder`, before `AccessControlSeeder.SeedFromLegacyAsync`) |
| D-11 role mapping | `ProcurementReadViewSeeder`: for Vendors, Contracts, IRN, StoreRequisition, MaterialItem and ProcurementPackage, a `RolePermissions` row with Create/Edit/Approve/Delete but no View gets `CanView = true`; a role that holds only a `RoleModuleActions` action on those modules gets a `View` action. Never removes anything; rows without any permission are untouched. It writes the legacy tables because the legacy sync deactivates View grants whose `CanView` is false. | as above |
| D-12 (P0-10) | `ITenderService.CheckCreatorApproverSeparationAsync`: the creator (`Tender.CreatedBy`) is refused in `SelectWinner`, `CreateContract` and `ApproveNegotiation` (the tender is resolved from the negotiation, not from the form). SystemAdmin creator: allowed, with an `AuthorizationAuditLog` row (`Reason` cites D-OPEN-04); if that row cannot be saved, the request is refused. Unknown current user: refused. Tender without a recorded creator: not blocked. | `TenderService.cs`, `ITenderService.cs`, `TenderApprovalSeparation.cs`, `TenderController.cs` |
| D-13 (P0-10) | `ContractEditorEvents`: `Contracts/Edit/Allow` event, saved in the same `SaveChanges` as the edit, from `SubmitForReviewAsync` (also used by `ChangeStatus`), `RequestRevisionAsync` and `AddDocument`. Not on validation failure. `ApproveRevisionAsync` now runs the user-aware `AuthorizeAsync` (Scope + SoD; only an explicit Allow passes); the controller refuses when the user id is unknown. Discipline review is not an edit. | `ContractEditorEvents.cs`, `ContractWorkflowService.cs`, `IContractWorkflowService.cs` (optional parameters, existing callers unchanged), `ContractController.cs` |

### 13.2 Behaviour changes users will see

- Read pages above return 403 for roles without View on the module. With the seeded defaults: Viewer keeps IRN, StoreRequisition, MaterialItem (therefore Inventory dashboard and ledger) and ProcurementPackage (therefore the procurement report), and loses Vendors and Contracts; EngineeringSupervisor loses IRN; maintenance roles without StoreRequisition rows cannot open store requisitions.
- The tender creator gets "separation of duties" instead of approving; SystemAdmin succeeds and is audited.
- A user who submitted a contract, added a contract document or requested a revision can no longer approve that contract or its revisions.

### 13.3 Evidence (2026-10-14/15, working tree, InMemory)

| Check | Result |
|---|---|
| Build `PEMS.Tests` (pulls in every project) | 0 errors |
| New and changed tests: `ProcurementP0ReadAndSodDecisionTests` (20: 14 facts + 6 theory cases), `ContractWorkflowServiceTests` P010_* (8), `VendorControllerAuthorizationTests` (updated pin: Viewer now forbidden), `StoreRequisitionGridPaginationTests` (now supplies a permission service) | all passed |
| Regression: Authorization, Procurement, Inventory, Web, Security namespaces, excluding `*Sql*`, `*Concurrency*`, `*LiveDb*`, `*RetryStrategy*`, `FileSecurityHttpTests` | **1,275 passed, 0 failed, 0 skipped** |
| Source-scanning architecture tests (`PurchaseOrderCreationPathTests`, `ReplenishmentIsolationTests`, `ReplenishmentAvailabilityGateTests`, `CmmsEndpointSecurityTests`, `UiStandardTests`, `LanguagePurityTests`, `LocalizationKeyCoverageTests`) | included above. A first run built with `--artifacts-path D:/pa` failed 36 of them only because their repository-root lookup walks up from the output folder; rebuilt with an in-repo output folder (deleted afterwards) they pass |
| Graphify | `graphify update .` at 2026-10-15 00:23: 39,763 nodes, 85,303 edges; graph contains the new seeder, event helper, separation method and test class. A first run exited 0 without rewriting `graph.json`; only the second run is claimed |

**Not run:** SQL-touching and concurrency classes, `FileSecurityHttpTests`, the live application. No SQL test was added for the new event rows; the Contracts.Edit event is saved in the same `SaveChanges` as the edit, which on SQL Server is one transaction. Persisted role rows on real databases remain unverified (R-02 pattern).

### 13.4 Rollback

Revert the files listed in 13.1 and delete the four new files (`TenderApprovalSeparation.cs`, `ProcurementReadViewSeeder.cs`, `ContractEditorEvents.cs`, `ProcurementP0ReadAndSodDecisionTests.cs`). Rows already written by the seeder (`CanView = true`, `View` actions) and the audit rows remain; they only grant read access to roles that already held another permission on the module, and can be reverted in the Permission Matrix.
