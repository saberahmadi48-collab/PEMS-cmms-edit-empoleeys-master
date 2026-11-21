# PEMS Supply Chain & Procurement Chain — Baseline Audit (Phase 0 + Phase 1)

Status: **AUDIT ONLY — no code, schema or data was changed.** Implementation requires explicit approval of the decisions in section 9.
Evidence = repository code read on 2026-10-08, plus read-only SQL counts on the local dev DB (`PEMSDB`). Anything not inspected is listed in section 10 — nothing there is assumed.

## 1. Executive summary

1. **A first-class Tender domain already exists** (`Tender`, `TenderInvitedVendor`, `Bid`, `BidEvaluation`, `BidNegotiation`, `TenderService`, `TenderController`, tests). Phase 8's premise ("if none exists") is false; creating `TenderParticipant`/`TenderBid`/`TenderAward` would duplicate concepts. The real gaps are **bid items, award lines, a proper award record/approval and MR-line traceability**.
2. **`StoreRequisition` is the existing internal-demand ("Indent") concept.** No `Indent` entity must be created. There is **no MIV entity**: an issue is `InventoryTransaction(TransactionType="Issue", ReferenceType="StoreRequisition")`.
3. **`StoreRequisitionLifecycleService` has no transition validation and no transaction**: any status can be requested from any status; "Reserved" can be applied twice (duplicate reservations and double stock reservation); the only guard is the controller's `NextStatusMap`. Reservation is not tied to a requisition line, issue is all-or-nothing, `ApprovedQuantity` has no operation that sets it, and there is **no test for this service**.
4. **`ProcurementPackageService` auto-advances `Stage` from communications and approvals** (`VendorInquiry → … → Approval`), i.e. `Package Stage = MRQ = Tender = Approval` — the God-workflow smell the directive names. `TenderService` does not touch package stage.
5. **`PurchaseOrder` is header-only, defaults to `Issued`, and has no link to MR, package, tender or award.** 63 PO rows exist, all `Issued` (migration must backfill nothing destructive).
6. **`MaterialRequisitionQuotation` (MRQ) carries free-text `VendorName`** and embeds technical/commercial evaluation strings; it is a vendor-inquiry/quote record, not a tender.
7. **`MaterialRequisitionItem` has no link to `MaterialItem`** and no id used by anything downstream, so line-level traceability (MR item → bid → award → PO line → IRN line) does not exist.
8. Dev DB is nearly empty for the stock/tender side (0 stock rows, 0 tenders), so dev cannot reveal data-migration problems; production row counts must be taken before any migration (Phase 20).

## 2. Baseline checks

| Check | Result |
|---|---|
| `dotnet build PEMS.slnx -c Debug` | succeeded (last run during this session, after the actor-resolution work) |
| Full test suite | a run on the current tree was started and **had not finished when this audit was written**; the previous full run (2,166 tests) had 2 failures, one fixed (access-control parity), one the known timing-sensitive `MrsImportBatchCapacityTests` |
| Existing tests touching this area | `MaterialRequisitionServiceTests`, `MaterialRequisitionMrNumberConcurrencyTests`, `MaterialRequisitionGridPaginationTests`, `TenderServiceTests`, `TenderControllerAuthorizationTests`, `ProcurementPackageControllerAuthorizationTests`, `ProcurementPackageInspectionGateTests`, `ProcurementReportServiceTests`, `PurchaseOrderControllerAuthorizationTests`, `IrnReceipt*`, `MaterialItemReceiveStockAuthorizationTests`, `InventoryBatchTraceabilityTests`, `StoreRequisitionGridPaginationTests` (pagination only) |
| Missing tests | **StoreRequisition lifecycle, reservation, issue, release, concurrency; PO lifecycle; award; MR→PO traceability** |

## 3. Dev DB row counts (read-only)

StoreRequisitions 1 · StoreRequisitionLines 0 · MaterialReservations 0 · MaterialStocks 0 · InventoryTransactions 0 · WorkOrderMaterials 0 · MaterialItems 0 · MaterialRequisitions 93 (Draft 92, Closed 1) · MaterialRequisitionItems 0 · MRQ 0 · ProcurementPackages 2 (Closeout/Closed) · Tenders 0 · Bids 0 · Contracts 0 · PurchaseOrders 63 (all `Issued`) · IrnReceipts 0 · Vendors 50.

## 4. Terminology map (Phase 1)

| Business term | PEMS entity | Meaning in code | Lifecycle (today) | Owner / source → destination | Gaps |
|---|---|---|---|---|---|
| Internal demand / Indent | `StoreRequisition` (+`Line`, `StatusHistory`) | warehouse material demand, optionally for a `WorkOrder` | Draft→Submitted→Approved→Reserved→Issued→Closed (+Rejected/Cancelled), unguarded in service | Maintenance/Operations → Warehouse | no line-level reservation, no partial issue, no shortage→MR link |
| Procurement requirement | `MaterialRequisition` (+`Item`) | engineering MR per project/discipline/equipment; has `ProcurementPackageId`, responsible engineer, required date | Draft… (Draft 92 / Closed 1) | Engineering → Procurement | items have no MaterialItem/PO/bid link; no formal lifecycle guard checked |
| Vendor quote / inquiry | `MaterialRequisitionQuotation` (MRQ) | per MR/package quote with free-text `VendorName`, quoted amount, tech/commercial evaluation strings, approval status | string statuses | Procurement → vendor | not a tender; free-text vendor |
| Procurement container | `ProcurementPackage` | project/discipline scope with `Stage` (MR…Closeout) and `Status`, children: MRs, MRQs, communications, approvals, tenders | stage advanced automatically | Procurement | stage inferred from child activity |
| Tender | `Tender` (+`TenderInvitedVendor`) | RFQ/tender for a package: dates, requirements, status Draft→Open→Closed→Awarded, `AwardedBidId`, `AwardedById`, `ContractId` | managed by `TenderService` | Procurement | no bid items; award is fields on Tender; no PO link |
| Bid | `Bid`, `BidEvaluation` (Technical/Commercial/…), `BidNegotiation` | vendor bid with values, evaluations by employee, negotiation rounds | Submitted→UnderTechnical/CommercialEvaluation→UnderNegotiation→Won/Lost | Vendor / evaluators | bid is total-value only |
| Contract | `Contract` (+revisions, NTP, documents, workflow) | created from winning bid (`CreateContractFromWinningBidAsync`) | contract workflow | Contracts | — |
| Purchase order | `PurchaseOrder` | header: number, project, vendor, optional contract, description, value, currency, status | **created as `Issued`** | Procurement → Vendor | no lines, no MR/tender/package link, no lifecycle |
| Vendor documents | `VDRItem` (document) with `PoId`, `MRId`, `VendorId` | vendor data requirement per PO/MR | VDR workflow | Vendor ↔ Engineering | — |
| Receiving | `IrnReceipt` (+`Line`, disposition) with `PurchaseOrderId`, `MrsImportBatchId` | inbound receipt; lines per `MaterialItem` | Draft… | Warehouse/QC | line has no PO line / MR item link |
| MRS | `MrsImportBatch/Row` | import pipeline of receipts into stock | staged review | Warehouse | — |
| Stock | `MaterialStock` (OnHand, Reserved, `BaseEntity` → has `RowVersion`) | per item/warehouse/location | — | Warehouse | invariants not enforced in DB/service |
| Reservation | `MaterialReservation` | reserved qty with `StoreRequisitionId`, `WorkOrderId`, `MaintenancePlanId` | Active/FullyIssued/Released | system | **no `StoreRequisitionLineId`** |
| Stock movement | `InventoryTransaction` | typed movement with `ReferenceType/ReferenceId`, `WorkOrderId` | append-only | system | no RowVersion/immutability guard |
| MIV | *(none)* | issue is an `InventoryTransaction` (type `Issue`) | — | — | no first-class issue document |
| Work order material | `WorkOrderMaterial` | required quantity per work order | — | Maintenance | not linked to StoreRequisition/line |

## 5. Findings — Store Requisition / inventory (Phases 3–6)

| # | Finding (evidence) | Severity |
|---|---|---|
| S1 | `AdvanceStatusAsync(id, nextStatus, …)` performs any requested status; no from→to table. A direct caller (not the controller) can go Draft→Issued, Closed→Reserved, etc. | P0 |
| S2 | No database transaction around status change + stock + reservation + transaction rows beyond a single `SaveChanges` (single implicit transaction) — acceptable for atomicity, but reserve/issue loop reads `AvailableQuantity` then mutates in memory (check-then-act). `MaterialStock` has `RowVersion`, so a concurrent writer gets `DbUpdateConcurrencyException`, which the service **does not catch** (surfaces as 500, no retry/message). | P1 |
| S3 | Calling `Reserved` twice creates a second set of reservations and increments `ReservedQuantity` again (no idempotency). | P0 |
| S4 | `ReleaseReservationsAsync` only handles `Active` reservations; Rejected/Cancelled after Issued leaves history inconsistent; no rule forbids cancelling an issued requisition. | P1 |
| S5 | Issue marks reservation `FullyIssued` and adds the whole reservation to `line.IssuedQuantity`, matching the line by `MaterialItemId` (`FirstOrDefault`). Duplicate material lines mis-attribute; no partial issue; `IssuedQuantity <= ApprovedQuantity` not enforced. | P0 |
| S6 | `ApprovedQuantity` is read (`ApprovedQuantity ?? RequestedQuantity`) but no code path sets it. | P1 |
| S7 | `MaterialStock` allows negative/over-reserved values (no check constraint, no guard on `OnHand − qty`). | P0 |
| S8 | Controller permission mapping: Approved/Reserved/Issued all require the same `StoreRequisition.Approve`; requester can hold Create+Approve (no SoD between requester/approver/issuer). | P1 |
| S9 | No test covers the lifecycle service. | P1 |
| S10 | `InventoryTransaction` rows do carry `ReferenceType/ReferenceId`, and every reserve/issue/release writes one — **good, keep**. | info |

## 6. Findings — Procurement (Phases 7–11)

| # | Finding | Severity |
|---|---|---|
| P1 | `ProcurementPackageService` sets `package.Stage` from communication type and from an approval decision; Stage enum `MR, VendorInquiry, TechnicalClarification, MRQ, Response, Approval, Closeout` encodes MRQ-centric flow. | P1 |
| P2 | `TenderService` (create, invite vendors, open/close bidding, submit bid, record evaluation, negotiation, select winner, create contract) is complete for header-level tendering but: bid has only `OriginalValue/NegotiatedValue/FinalValue` (no per-item pricing); an award is `Tender.AwardedBidId/AwardedById/AwardedDate` (no award record, no approval state, no partial/split award across vendors). | P1 |
| P3 | Evaluation SoD (technical ≠ commercial evaluator, buyer ≠ award approver) is not visible in `TenderService` code read so far (only permissions via controller). | P1 |
| P4 | `PurchaseOrder.Status` defaults to `Issued` in the entity and in `CreateAsync`/`FindOrCreateByNumberAsync` (the latter used by import flows). No lines; no `ProcurementPackageId`, `TenderId`, award or MR link — only `ContractId`. | P0 for traceability |
| P5 | `MaterialRequisitionQuotation.VendorName` is free text while `Vendor` master exists (50 vendors); `Bid`/`TenderInvitedVendor`/`PO` already use `VendorId`. | P2 |
| P6 | `MaterialRequisitionItem` (`Quantity` is `double`) has no `MaterialItemId`, no price/PO link. | P1 |
| P7 | `ProcurementReportService` counts `TotalMRQs` and quotation-based metrics; no tender/bid/award metrics. | P2 |
| P8 | Permission modules `Tender`, `PurchaseOrder`, `StoreRequisition` exist in the legacy 10-action permission grid (View/Create/Edit/Delete/Approve…). The fine-grained actions in Phase 14 (Submit, Reserve, Issue, TechnicalReview, EvaluateTechnical, …) do **not** exist; the `RoleModuleAction` mechanism (used by Permit and Part Catalogue) is the existing extension point. | P1 |

## 7. Traceability audit (Phase 12) — which of the 18 questions the model can answer today

| Link | Today |
|---|---|
| Work Order → StoreRequisition | yes (`StoreRequisition.WorkOrderId`) |
| StoreRequisition → reservation/issue transactions | partially (ref by `StoreRequisitionId`, not line) |
| StoreRequisition shortage → MR | **no link** |
| MR → Package | yes (`MR.ProcurementPackageId`) |
| Package → Tender → Bids → Evaluations | yes |
| Award approver | partially (`Tender.AwardedById`) |
| Tender → Contract | yes (`Tender.ContractId`) |
| PO ↔ MR / Tender / Award / Package | **no link** (PO has Contract only) |
| PO line ↔ MR line | **PO has no lines** |
| PO → VDR | yes (`VDRItem.PoId`) |
| PO → IRN | yes (`IrnReceipt.PurchaseOrderId`); IRN line has no PO line |
| IRN → MRS → inventory transaction | IRN→MRS batch yes; transaction link to IRN not verified (**not inspected**) |
| Issue (`InventoryTransaction` Issue) → WO | via `WorkOrderId` on transaction **if set** — the lifecycle service does not set it (`WorkOrderId` omitted in the issue transaction) |

## 8. Proposed remediation (summary, additive only) — NOT started

Decisions that follow the evidence (details in section 9):

1. **Terminology:** keep `StoreRequisition` as the Indent; document MR ≠ SR; no new `Indent`.
2. **StoreRequisition lifecycle:** add an explicit transition table + guard inside `StoreRequisitionLifecycleService`; make reserve idempotent; add `StoreRequisitionLineId` (nullable FK, backfilled where unambiguous) to `MaterialReservation`; add `PartiallyIssued`; add approval-quantity operation; catch concurrency and retry/report; add DB check constraints (`OnHand>=0`, `Reserved>=0`, `Reserved<=OnHand`, issued ≤ approved ≤ requested after backfill validation); issue transaction sets `WorkOrderId`.
3. **Package:** make `Stage` derived/informational (computed from children) instead of mutated by communications/approvals; keep column for compatibility.
4. **Tender:** *extend, do not recreate*: add `TenderBidItem` (bid ↔ MR item), `TenderAward` + `TenderAwardLine` (award record, approval state), keep `Bid`/`BidEvaluation` as the bid/evaluation owners; evaluation SoD in service.
5. **MRQ:** keep as historical/compatibility vendor quote (no deletion); add `VendorId` (nullable) + backfill by exact normalized name match, keep `VendorName`.
6. **MR item:** add nullable `MaterialItemId`; keep `Quantity` as is (type change is a separate migration decision).
7. **PO:** add `PurchaseOrderLine` (+ traceability to MR item / award line / contract), add nullable `ProcurementPackageId`/`TenderAwardId`/`SourceMaterialRequisitionId`; change **new** PO default to `Draft` (existing rows untouched); lifecycle states with guard; the import path (`FindOrCreateByNumberAsync`) keeps creating `Issued` explicitly so existing imports do not change.
8. **Workflow:** reuse `PEMS.WorkflowPlatform` + the Enterprise Actor Resolution just built for approvals (SR approval, MR approval, award approval, PO approval); business services keep ownership of stock/lifecycle invariants.
9. **Authorization:** extend through `RoleModuleAction` with the Phase 14 actions plus explicit SoD checks in the services (requester ≠ approver, issuer ≠ requester, technical ≠ commercial evaluator, buyer ≠ award approver, PO creator ≠ PO approver).
10. **UI/Help/Import/Reports:** extend existing Supply Chain navigation, Help and report services; no new top-level sidebar entries.

## 9. Decisions required before implementation

1. Confirm **StoreRequisition = Indent** (no new entity) and that there is **no separate MIV entity** (issue stays an `InventoryTransaction`, optionally with a thin issue document later).
2. Confirm **Tender is extended, not recreated** (Phase 8 entity list replaced by: `TenderBidItem`, `TenderAward`, `TenderAwardLine`; `TenderParticipant` = existing `TenderInvitedVendor`; `TenderBid` = existing `Bid`).
3. **Duplicate lines in one StoreRequisition:** forbid (unique `(StoreRequisitionId, MaterialItemId)`) or allow with per-line reservation? Recommendation: allow, reservation references the exact line (no data loss for existing rows).
4. **Partial issue:** required now? Recommendation: yes, implement `PartiallyIssued` and per-line issue quantities.
5. **PO default status:** switch new-PO default to `Draft` and keep imports explicit `Issued`? Does your organization approve POs through the workflow engine (needs a workflow definition + actor responsibilities)?
6. **Quantity type:** keep `double` on `MaterialRequisitionItem` or migrate to `decimal(18,4)`?
7. **Migration order / scope:** one phase at a time (recommended: S1–S7 inventory safety first, then Tender extensions, then PO), each with its own migration, tests and regression — or everything in one delivery?
8. Production data: please provide (or authorize running) read-only counts on the production database for the Phase 20 checks; dev data cannot validate backfills.

## 10. Not inspected in this pass (to be done before the related phase)

Relationship Explorer, Global Search providers for these entities, Help content for these screens, Import/Export formats other than the MRS/IRN pipeline, `ProcurementPackageController`/views and sidebar structure, VDR/IRN/MRS detail flows, audit-log coverage, other workflow definitions touching procurement, graphify graph for these communities (the earlier `graphify update` may still be running), and production data. Phase 21 (graph read/update) is pending.

## 11. Risk register (short)

| Risk | Mitigation |
|---|---|
| Backfilling `StoreRequisitionLineId` on existing reservations is ambiguous when duplicate material lines exist | leave NULL for ambiguous rows, report them; enforce NOT NULL only for new rows |
| Check constraints fail on existing bad stock rows | validation script first; add constraints only after clean-up or as `WITH NOCHECK` + report |
| Changing PO default breaks import flows | keep explicit `Issued` in import path; add test |
| Package stage change affects dashboards/reports | keep column, compute and keep values compatible, update reports in the same change |
| Concurrent reserve under SQL Server | row-versioned `MaterialStock` + retry loop, or `UPDLOCK/HOLDLOCK` inside an explicit transaction; decide with load expectations |

---

## 12. Implementation log

### Slice 1 — Store Requisition / inventory safety (Phases 3–6) — implemented

Confirmed by the product owner's flow diagram: StoreRequisition is the internal indent; stock-available path ends in an `InventoryTransaction` (Issue), no MIV entity; the Tender path extends the existing `Bid`. Decisions taken for this slice (recommendations from section 9, items 3–4): duplicate material lines are **allowed** and reservation is per exact line; **partial issue is implemented**. Items 5–8 (PO, quantity type, production data) are **still open** and the PO/Tender/MRQ/Package slices are **not started**.

| Finding | Change |
|---|---|
| S1 any→any transitions | `StoreRequisitionStatuses` table (Draft/Submitted/Approved/Reserved/PartiallyIssued/Issued/Closed/Rejected/Cancelled) enforced in `StoreRequisitionLifecycleService`, not the controller |
| S2 concurrency | whole operation in a transaction (relational) + retry on `DbUpdateConcurrencyException` (3 attempts) with an understandable final message; real SQL race test (two requisitions, last stock) |
| S3 double reserve | reserve is idempotent per line (target − already reserved); `Reserved→Reserved` also illegal |
| S4 release | Rejected/Cancelled/Closed release the *remaining* reservation (Quantity − Issued) with `ReservationRelease` transactions; issued requisitions can only be Closed |
| S5 line attribution / partial issue | `MaterialReservation.StoreRequisitionLineId` + `IssuedQuantity`; `IssueAsync` per line, `PartiallyIssued` state, `Issued ≤ Approved ≤ Requested`; legacy reservations are attached only when the material has exactly one line, otherwise issue is refused |
| S6 approved quantity | `SetApprovedQuantitiesAsync` (Submitted only, 0 ≤ approved ≤ requested, approver ≠ requester); defaults persisted at approval; at least one positive line |
| S7 stock invariants | service guards + SQL `CHECK` constraints (`WITH NOCHECK`, existing rows untouched) on `MaterialStocks`, `StoreRequisitionLines`, `MaterialReservations`; `AdjustAsync` can no longer drop OnHand below Reserved |
| S8 SoD | requester cannot approve, set approved quantities or issue their own requisition |
| S9 tests | `StoreRequisitionLifecycleServiceTests` (47 cases incl. theories), `StoreRequisitionReservationConcurrencyTests` (real SQL) |
| issue transaction | now carries `WorkOrderId` (traceability Work Order ← issue) |
| found by the real-DB test | a reservation with no employee wrote `ReservedById = 0` (FK violation); now rejected with a clear message |

Migration: `20261007215305_StoreRequisitionLineTraceability` (additive columns, FK, index, deterministic idempotent backfill, three check constraints). **Before production**: run the validation queries for rows that violate the new constraints (negative/over-reserved stock, `Issued > COALESCE(Approved, Requested)`, `Issued > Quantity`); `WITH NOCHECK` lets the migration succeed but any later UPDATE touching a violating row will be rejected until it is corrected.

UI: Store Requisition details now shows Remaining, an approved-quantity editor (Submitted), a per-line "issue now" form (Reserved/PartiallyIssued), "Close short", and Cancel from Reserved. Not yet done: Persian/English resource keys for the new labels (the existing page already mixes hard-coded text), Help content, workflow routing for SR approval, fine-grained `RoleModuleAction` permissions (Submit/Reserve/Issue/…).


---

## 13. PHASE 6.5 — PRODUCTION SAFETY & AUTHORIZATION GATE / FINAL CLOSURE GATE

Full detail: `docs/audits/STORE_REQUISITION_PRODUCTION_SAFETY_AUDIT.md` (section 9). Summary: service-level state machine, per-line reservation, idempotent reserve/issue (request key), partial issue, approved quantity locked after approval, fine-grained `StoreRequisition.*` permissions + active-employee identity + SoD enforced in the service, `InventoryTransaction` linked to line and reservation, existing `AuditLog` reused, three additive migrations (the last conditionally trusts the CHECK constraints). Final build: focused 101/101, real-SQL concurrency green, full regression 2299/2300 (the single failure is the pre-existing `MrsImportBatchCapacityTests` timing test; green when re-run alone). Dev 24-check safety script: 0 violations; constraints trusted on dev. **Production data audit and production constraint trust are UNKNOWN → OVERALL PHASE 6.5: BLOCKED — PRODUCTION EVIDENCE REQUIRED.** Deferred: Workflow Platform integration, Details-page localization, Help. No Tender/PO/MRQ/Package work started.

---

## 14. Procurement P0 hardening — implementation provenance (appended 2026-10-12; sections 1-13 above are historical and unchanged)

The findings of the later Procurement audit that touch this baseline (F-01 vendor over-posting, F-02 award rule, F-03 tender-to-contract path, F-04 ungated stock mutation) were addressed in part by a code-only patch (no migration, no schema or seed-row change) integrated on branch `feat/cmms-passport-global-page-header` (HEAD `b2d2411`, uncommitted). Gate status, evidence, decisions, open policy items and rollback: `docs/procurement/P0_HARDENING_GATE_EVIDENCE.md`. **P0 is NOT PASSED:** the implemented subset is `READY_FOR_REVIEW`; read-endpoint View permissions, full SoD enforcement and SQL/concurrency verification remain open or not run; independent review is pending. Baseline findings P2 (header-only bids/awards, no award record or approval state), P3 (evaluator independence and award segregation of duties) and P4 (PO without lines or source links) are **not** closed by this patch. Nothing in sections 1-13 should be read as closed by this entry.
