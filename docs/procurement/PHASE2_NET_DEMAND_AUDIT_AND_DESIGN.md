# PEMS Phase 2 — Net Demand and Inventory Intelligence
## Stage 1: Audit, domain design and implementation plan (design only — nothing implemented)

Scope of this stage: read-only audit of the repository (source, tests, Graphify) and of the shared development database `PEMSDB` (SELECT only), plus this document. No application code, test, migration or data was changed.

Legend used for every claim:
**[F]** verified fact (code, test or persisted data, with citation) · **[I]** inference from verified facts · **[R]** recommendation · **[D]** business/policy decision that is *not* made and must not be assumed.

---

## 0. Audit scope and method

* Instructions read: `CLAUDE.md`, `.claude/CLAUDE.md`, Phase 1 document `docs/procurement/PHASE1_REMEDIATION_AND_FOLLOWUP.md`, the lifecycle spec `docs/PEMS_Procurement_Lifecycle_Spec/PEMS_Procurement_Lifecycle_Spec.md` (§5 demand origins, §6.2 net-demand rules, §10 PO rules, phase table).
* Graphify: `graphify explain "MaterialStock"` (41 connections; writers/readers/tests listed below) and `graphify query` for call graphs; graph last refreshed after Phase 1 and no code has changed since.
* Persisted data (read-only, `PEMSDB`, 2026-10-10): `MaterialItems=0, MaterialStocks=0, MaterialReservations=0, InventoryTransactions=0, StoreRequisitions=1 (0 lines), WorkOrders=1, WorkOrderMaterials=0, JobPlanRequiredMaterials=0, MaintenanceSpareUsages=0, MaterialRequisitions=93, MaterialRequisitionItems=0, PurchaseOrders=63 (all Issued), IrnReceipts=0, IrnReceiptLines=0, Warehouses=9, StorageLocations=0, MaterialUOMs=12`. **[F]** The inventory model is therefore *empty* in the shared dev DB; every statement about stock behaviour below comes from code and tests, not from production-like data. Numerical examples in §6 are synthetic.
* Repository state at start: HEAD `6bee674` (local commits `47c2fd2`, `6bee674` preserved); the uncommitted Phase 1 work is intact (27 modified + untracked files); nothing was reset, stashed, committed or pushed.

---

## 1. Current architecture and data-flow map

```
                    (demand intent)                               (stock projection + ledger)
 StoreRequisition ──Approved──► ReserveLinesAsync ──► MaterialReservation + MaterialStock.ReservedQuantity ┐
   (+Lines)         Reserved   (StoreRequisitionLifecycleService)      + InventoryTransaction "Reservation" │
        │           Issue ───► OnHand-=, Reserved-= , InventoryTransaction "Issue" (WorkOrderId carried)    │
 WorkOrder ─ WorkOrderMaterial (required qty, no link to SR lines)                                          ├─► MaterialStock
        └─ AddSpareUsage (WorkOrderController) ─► OnHand-= + "Issue" tx + MaintenanceSpareUsage              │   (item × warehouse × location)
 MRS Excel import ─► MrsImportService ─► OnHand +=/-= + "Receipt"/"Issue" tx (location always NULL)         │
 IRN: Draft→Submitted→(disposition)PendingApproval→Closed ─► CompleteDispositionAsync ─► "Acceptance" tx    │
        (Accepted posted; Rejected/Hold stay on IrnReceiptLine only)                                       │
 ProductionOutput ─► ReceiveAsync only if ProductionBatch.QualityStatus=="Released"                         ┘
 MaterialStockService (Receive/Adjust/Return/Transfer/SetThresholds) — manual postings

 MaterialRequisition (+free-text Items)  ──►  ProcurementPackage (MR→…→Closeout)  ──►  PurchaseOrder (header only)
 PurchaseOrder ──(header FK only)──► IrnReceipt        (no PurchaseOrderLine, no IRN-line→PO-line link)
 PartCatalogueRequest (no MaterialItem link or demand-origin fields: the Phase 1 attempt was withdrawn, see Phase 1 doc §8)
```

Owners of behaviour **[F]**:
| Concern | Component (path) |
|---|---|
| Reservation / issue / release, status table, idempotency key, retry on concurrency | `PEMS.Persistence/Servises/StoreRequisitionLifecycleService.cs` (`RunAsync`, `ReserveLinesAsync`, `IssueCoreAsync`, `ReleaseRemainingAsync`); statuses in `PEMS.Domain/Entities/StoreRequisitionStatuses.cs` |
| Manual stock postings, thresholds | `MaterialStockService.cs` (`ReceiveAsync`, `AdjustAsync`, `ReturnAsync`, `TransferAsync`, `SetThresholdsAsync`, `GetLowStockAsync`) |
| Ledger read model | `InventoryTransactionService.cs` (read-only: `GetLedgerAsync`, `GetBatchBalancesAsync`) |
| QA gate before stock | `IrnReceiptService.cs` (`RecordDispositionAsync`, `CompleteDispositionAsync`); workflow binding `IrnReceiptDisposition` |
| Direct WO consumption (outside the lifecycle service) | `PEMS.Web/Controllers/WorkOrderController.cs` `AddSpareUsage`/`ReturnSpareUsage` |
| PM-generated requirements | `PMGenerationService.cs` (copies `JobPlanRequiredMaterial` → `WorkOrderMaterial`, one open WO per plan) |
| Initial/historic balances | `MrsImportService.cs` (commit chunks) |
| Production receipt with quality gate | `ProductionRunService.PostOutputToInventoryAsync` |
| Authorization | `IRoleModuleActionService` (module `StoreRequisition`, actions Submit/Approve/Reject/SetApprovedQuantity/Reserve/Issue/Close/CloseShort/Cancel) and `IPermissionService` (modules `MaterialItem`, `InventoryMasterData`, `IRN`, `StoreRequisition`, `MaterialRequisition`, `PurchaseOrder`, …) |
| Audit | `BaseEntity` change auditing (inherited by `MaterialStock`, `MaterialReservation`, `StoreRequisition(+Line)`, `IrnReceipt(+Line)`, `MaterialRequisition`, `PurchaseOrder`, …; **not** by `InventoryTransaction` (append-only ledger), `WorkOrderMaterial`, `JobPlanRequiredMaterial`, `MaterialRequisitionItem`) + `StoreRequisitionStatusHistory`, `IrnReceiptStatusHistory` |

---

## 2. Entity and lifecycle inventory

| Entity (file) | Purpose / source of truth | Quantity fields (type) | Lifecycle | Notes **[F]** |
|---|---|---|---|---|
| `MaterialItem` (`MaterialItem.cs`) | Material master; **unique `MaterialCode`** (`MaterialMasterConfigurations.cs:61`) | none; `UomId` (base UoM), `IsStockItem`, `IsCritical`, `IsSerialized`, `IsBatchControlled` | `IsActive` | Serial/batch flags exist but "architecture ready for the future" (entity comment); no serial/lot stock exists |
| `MaterialStock` (`MaterialStock.cs`) | **Projection** of current stock per item × warehouse × optional location | `OnHandQuantity`, `ReservedQuantity` decimal(18,3); policy `MinimumQuantity`, `MaximumQuantity`, `ReorderPoint`, `ReorderQuantity` | `IsActive` | `AvailableQuantity = OnHand − Reserved` is `[NotMapped]`; has `RowVersion` (BaseEntity) |
| `InventoryTransaction` | **Append-only ledger** (no `IsActive`) | `Quantity` (positive; sign by type), `UomId` | types in use: `Receipt, Issue, Return, TransferIn, TransferOut, Adjustment (signed), Reservation, ReservationRelease, Acceptance` | `Acceptance` is not in the entity's comment list but is written by `IrnReceiptService`; ledger sign convention is per-type (see `InventoryTransactionService.cs:66-72`) |
| `MaterialReservation` | Reservation per **SR line** | `Quantity`, `IssuedQuantity` | `Active, PartiallyIssued, FullyIssued, Released` (`MaterialReservationStatuses`; the entity comment also lists `Cancelled` which has no constant) | Only creator in the whole repo: `StoreRequisitionLifecycleService.ReserveLinesAsync`. `MaintenancePlanId` FK has no writer |
| `StoreRequisition` / `StoreRequisitionLine` | Internal issue request (Indent) | line: `RequestedQuantity`, `ApprovedQuantity?`, `IssuedQuantity` decimal(18,3), `UomId?` | `Draft→Submitted→Approved→Reserved→(PartiallyIssued)→Issued→Closed`; `Rejected`/`Cancelled`; `Closed` from `Reserved`/`PartiallyIssued` = close-short | Header has `WarehouseId`, optional `WorkOrderId`; transition table in `StoreRequisitionStatuses.Allowed`; idempotency key + status history |
| `WorkOrder` + `WorkOrderMaterial` | Planned material per WO | `RequiredQuantity` decimal(18,3), `UomId?` | WO: `Open→Assigned→InProgress→(OnHold)→Completed→Closed`; `Cancelled`/`Rejected` from early states (`WorkOrderLifecycleService.LegalTransitions`) | `WorkOrderMaterial` has no status, no issued/consumed field, no link to SR lines |
| `MaintenanceSpareUsage` | Actual consumption on a WO | `Quantity`, `ReturnedQuantity?`, `TotalCost` | `IsActive` | Written by `AddSpareUsage`; **not** written by SR issue |
| `JobPlanRequiredMaterial` | Template; copied (snapshot) to `WorkOrderMaterial` at PM generation | `RequiredQuantity` | — | `PMGenerationService` lines 60-120; "hasOpenWorkOrder" guard prevents a second open WO per plan |
| `MaterialRequisition` + `MaterialRequisitionItem` | Engineering/project **purchase** request | item: `Quantity` **double**, `Unit` string, `PartNumber` string, `ItemDescription` free text | `Draft, Submitted, Under Technical Review, Approved, Issued to Procurement, Closed, Cancelled` | No `MaterialItemId`. Import-created MRs (`MaterialRequisitionLinker`) are header-only (93 MRs, 0 items in dev DB) |
| `PurchaseOrder` | Header only (`Value`, `Currency`) | **none** | `Draft/Issued/Closed/Cancelled`; 63 imported POs are `Issued` | No `PurchaseOrderLine` anywhere (`grep PurchaseOrderLine` → only an explanatory comment in `IrnReceipt.cs:33`) |
| `IrnReceipt` / `IrnReceiptLine` | Material receiving + QC | line: `ReceivedQuantity`; after disposition `AcceptedQuantity`, `RejectedQuantity`, `HoldQuantity` (nullable, decimal(18,3)); Accepted+Rejected+Hold must equal Received (`RecordDispositionAsync`) | `Draft→Submitted→PendingApproval→Closed` (QA reject returns to `Submitted`); `Cancelled` | Header `PurchaseOrderId?` only; **no** line→PO-line link; only Accepted is posted (`Acceptance` tx) |
| `PartCatalogueRequest` | Part identity workflow | `FirstPurchaseQuantity`, `MinimumStock`, `MaxConsumptionRate`, `IssuedQty` decimal(18,4) | `Draft…Completed/Cancelled` | No link to `MaterialItem` and no demand-origin field exist (the Phase 1 attempt was withdrawn; Phase 1 doc §8) |
| `ProductionBatch` | Produced lot | `Quantity` | `QualityStatus: Produced/AwaitingQC/Released/Blocked/Rejected` | Stock posted only when `Released` |
| `SparePart` (legacy) | **Parallel** stock model (`QuantityOnHand`, `ReorderPoint`) | decimal | — | Still edited directly by `SparePartController`; independent of `MaterialStock` |

---

## 3. Demand-source eligibility matrix

Key evidence tests: `StoreRequisitionLifecycleServiceTests` (`Reservation_IsPerLine_EvenForDuplicateMaterialLines`, `Reserve_WhenStockIsInsufficient_FailsAtomically`, `Reserve_CalledTwice_DoesNotDoubleReserve`, `ApprovedQuantity_DrivesReservationAndCannotExceedRequested`, `PartialIssue_Requested100_Approved70_Issued50_Remaining20`, `IssueTransaction_CarriesTheWorkOrderForTraceability`), `StoreRequisitionClosureGateTests`, `StoreRequisitionHardeningTests`.

| Source (id) | Eligible statuses **[R]** | Quantity & unit | Identity resolution | Cancel / close | Reservation relationship | Already covered elsewhere? | Reliable dedup? |
|---|---|---|---|---|---|---|---|
| **StoreRequisitionLine** (`StoreRequisitionLineId`) | **Approved**, **Reserved**, **PartiallyIssued** count as demand. `Submitted` = *potential* (not in hard shortage) **[D2]**. Excluded: Draft, Rejected, Cancelled, Issued, Closed | `(ApprovedQuantity ?? RequestedQuantity) − IssuedQuantity`, active lines only; unit `Line.UomId` (null ⇒ item base UoM) | direct `MaterialItemId` | `Rejected/Cancelled/Closed` run `ReleaseRemainingAsync`: reservation released, `ReservedQuantity` reduced, tx `ReservationRelease` **[F]** | `Approved`: nothing reserved ⇒ full line is *unreserved demand*. `Reserved/PartiallyIssued`: outstanding reservation `Σ(Quantity−IssuedQuantity)` (status Active/PartiallyIssued) is **already inside `ReservedQuantity`** ⇒ only `max(0, lineDemand − outstanding)` is unreserved (normally 0). Reservation is all-or-nothing per approval (`ReserveLinesAsync` fails the whole transition) **[F]** | If `StoreRequisition.WorkOrderId` is set the same need may also be in `WorkOrderMaterial` (see WO row) | Yes within SR (line id on reservation and tx) |
| **WorkOrderMaterial** (`WorkOrderMaterialId`) | WO status `Assigned`, `InProgress`, `OnHold`; `Open` = *potential* (plan still editable, `WorkOrderLifecycleService` allows plan change in Open/Assigned) **[D3]**. Excluded: Completed, Closed, Cancelled, Rejected | `RequiredQuantity − consumed − committedViaSR`, where consumed = `Σ(MaintenanceSpareUsage.Quantity − ReturnedQuantity)` + `Σ SR line IssuedQuantity` for the same WO+item; unit `UomId` | direct `MaterialItemId` | No own status; follows WO. Cancelled WO ⇒ excluded | **No reservation**: `AddSpareUsage` consumes the highest-available stock row directly (`OrderByDescending(OnHand−Reserved)`), no warehouse on a WO **[F]** | **Yes — gap**: SR lines are not linked to `WorkOrderMaterial` rows; the only join is `(SR.WorkOrderId, MaterialItemId)` **[I]** | Partial: inferential join on WO+item; no FK |
| **PM forecast** (`MaintenancePlan` → `JobPlan.RequiredMaterials`) | Optional bucket, default **off**: plan with `NextDueDate` inside a horizon **and no occupying WO** (`PMGenerationService` `hasOpenWorkOrder`) **[D3]** | `JobPlanRequiredMaterial.RequiredQuantity` | direct | Becomes a real `WorkOrderMaterial` snapshot at generation, so must stop being counted as forecast at that moment | none | Becomes the WO demand ⇒ risk of double count at the generation boundary | Yes if the "no occupying WO" rule is used |
| **MaterialRequisitionItem** | **Not stock demand [I]**: it is the procurement side (a purchase request, "Issued to Procurement"), free text, `double` qty, no `MaterialItemId`, 93 header-only MRs in dev. Treat as *pipeline/traceability*, not demand **[D10]** | `double Quantity`, `string Unit` | only by string `PartNumber`≈`MaterialCode` (unreliable) | Cancelled/Closed | none | It is *downstream* of SR/WO/replenishment (counting it as demand would double count) | **No** — gap, do not invent a link |
| **Replenishment policy** | Rows of `MaterialStock` with `ReorderPoint`/`Min`/`Max`/`ReorderQuantity` (soft demand) | per-row decimal(18,3) | row = item × warehouse × location | n/a | uses projected position | must be computed *after* committed demand, never added to it | n/a |
| **Engineering/project demand** | **No implementation** supports stock-netted project demand beyond the MR header above **[F]** | — | — | — | — | — | gap, report only |
| **PartCatalogueRequest (unidentified part)** | non-terminal statuses (`Draft, InApproval, Returned, OrgResolution`) with no stored material link ⇒ "pending identity" bucket; informational only | `FirstPurchaseQuantity` (initial buy), origin via Phase 1 typed FKs | none until `Completed` + `MaterialItemId` link | `Cancelled` excluded | none | The *need* is already in its origin SR/WO/MR; `FirstPurchaseQuantity` is a stocking quantity, not additional operational demand **[I]** | origin FKs are typed and validated (Phase 1) |

---

## 4. Usable stock

**Current definition [F]:** `AvailableQuantity = OnHandQuantity − ReservedQuantity` (`MaterialStock.cs`), and it is the *only* quantity that gates reservation (`ReserveLinesAsync`), transfer (`TransferAsync`) and WO consumption (`AddSpareUsage`). Reservation increments `ReservedQuantity` under a transaction with retry and optimistic concurrency (`StoreRequisitionLifecycleService.RunAsync`, `MaterialStock.RowVersion`), proven by `StoreRequisitionReservationConcurrencyTests.TwoConcurrentReservationsForTheLastStock_OnlyOneSucceeds_AndInvariantHolds`.

| Category | Classification | Evidence / gap |
|---|---|---|
| On-hand | **Existing and reliable** for posted flows (ledger + projection, every writer pairs a tx with the balance change) — caveats G4, G8 | `MaterialStockService`, `IrnReceiptService:203`, `MrsImportService:610/627`, `StoreRequisitionLifecycleService`, `WorkOrderController:523` |
| Reserved | **Existing and reliable** for SR-created reservations; WO direct usage bypasses reservations | `StoreRequisitionHardeningTests`, `…ReservationConcurrencyTests`; `MaterialReservation` has exactly one creator |
| Issued | **Existing and reliable** (ledger type `Issue` with `WorkOrderId`, line `IssuedQuantity`, reservation `IssuedQuantity`) | `FullPath_DraftToClosed_MovesStockOnlyThroughTransactions` |
| Quarantined / inspection-held | **Existing but incomplete** — implemented as *"not posted until released"*, not as a bucket: IRN posts only `AcceptedQuantity`; production posts only `Released` batches. The held quantity is visible only on `IrnReceiptLine.HoldQuantity`, and **no workflow ever releases a Hold into stock** | `IrnDispositionServiceTests.CompleteDisposition_full_hold_posts_no_stock_and_creates_no_transaction`; `ProductionInventoryQualityGateTests.PostOutputToInventoryAsync_AwaitingQcBatch_IsRejected`; comment in `ProductionRunService.cs` ~463: *"MaterialStock has no separate 'in stock but blocked' bucket — anything posted immediately raises AvailableQuantity"* |
| Rejected | **Existing but incomplete** — `RejectedQuantity` stored, never posted; no return-to-vendor or replacement flow | `…CompleteDisposition_full_rejection_posts_no_stock_and_creates_no_transaction` |
| Blocked / damaged (stock-level) | **Not implemented**. `ProductionBatch.QualityStatus=Blocked` is batch-level and only prevents *posting*; once stock exists there is no way to mark part of it unusable except an `Adjustment` write-off | `ProductionBatch.cs`, no entity/column elsewhere (`grep` quarantin/blocked/damaged in Domain+Services) |
| Warehouses | **Existing and reliable** (9 in dev) | `Warehouse.cs` |
| Storage locations | **Existing but incomplete**: 0 locations exist; every importer/receiver writes `StorageLocationId = NULL`; the only uniqueness is `IX_MaterialStocks_MaterialItemId_WarehouseId_StorageLocationId` **filtered `[StorageLocationId] IS NOT NULL`** ⇒ no uniqueness for the NULL-location rows that are the norm; `GetOrCreateStockAsync` can therefore race and create duplicate rows, and `StockOfAsync` picks `FirstOrDefault` ignoring location **[F]** | `sys.indexes.filter_definition`, `MaterialStockService.cs:229`, `StoreRequisitionLifecycleService.cs:219` |
| Units | **Existing but incomplete** — `MaterialUOM.ConversionFactor` exists but is only edited (`InventoryMasterDataController:138`), never used in any calculation; lines carry nullable `UomId`; MR uses a free-text `Unit` | `grep ConversionFactor` |
| Material identity restrictions (serial/lot) | **Not implemented** (flags only); batch identity exists only for produced items via `ProductionBatchId` on the ledger | `MaterialItem.cs` comments |
| Negative on-hand | **Policy decision required [D13]**: `MrsImportService` does `OnHand -= RawQtyIssued` with no guard; `AdjustAsync/TransferAsync/IssueCore` guard, import does not | `MrsImportService.cs:627` |

**Recommendation [R]:** do **not** add quantity buckets to `MaterialStock` in this phase. Define

`UsableStock(i,w) = Σ over active stock rows of max(0, OnHand − Reserved)`

(clamp per row; flag rows with negative OnHand or `Reserved > OnHand` as data-quality exceptions rather than hiding them), because that is exactly the quantity the existing write paths use to grant or refuse stock, and "held/rejected" are already outside OnHand by construction. Held-in-QC quantity may be *reported* (from `IrnReceiptLine.HoldQuantity` on non-cancelled receipts) but never counted as usable. The only schema-level stock fix recommended is the NULL-location uniqueness index (§10, S5).

---

## 5. Eligible incoming supply

**What exists [F]:** `PurchaseOrder` has no quantities and no lines; `IrnReceipt.PurchaseOrderId` is a header FK; `IrnReceiptLine` has no PO-line reference; imported POs (63) have no lines; there are 0 IRNs. `IrnReceipt` entity comment states PO lines are deferred ("Step 7").

**Consequence [I]:** open-PO quantity *cannot be computed today*. Any "incoming" number derived from PO headers would be invented. Therefore v1 of the engine must treat incoming supply as **unknown (0, explicitly flagged `IncomingSupplyUnknown`)** and prevent re-recommending purchases by tracking its own planned supply (§7, §8: converted recommendations count as *planned supply* until closed).

**Target definition once PO lines exist [R]** (all quantities in the line's UoM; a quantity is in exactly one bucket):

| Bucket | Definition | Counts as incoming? |
|---|---|---|
| Ordered | `PurchaseOrderLine.OrderedQuantity` on PO with `Status = Issued` (not Draft/Closed/Cancelled) | base |
| Received (any disposition) | Σ `IrnReceiptLine.ReceivedQuantity` linked to the PO line, IRN status ∈ {Submitted, PendingApproval, Closed} | removes from "outstanding" |
| In QC | received on IRN `Submitted`/`PendingApproval` | **[D6]** count as incoming until the IRN is `Closed` (policy), never as usable |
| Accepted & posted | `AcceptedQuantity` on IRN `Closed` (already inside `OnHand` via `Acceptance` tx) | **no** — would double count |
| Hold | `HoldQuantity` | no (neither usable nor incoming) **[D7]** |
| Rejected | `RejectedQuantity` | no; whether it re-opens the PO line is **[D6]** |
| Outstanding | `max(0, Ordered − ReceivedAnyDisposition)` | **yes** |

`IncomingSupply = Outstanding + (InQC if D6 says so)`. The atomicity guarantee that prevents double counting is already in the code: `CompleteDispositionAsync` posts Accepted stock and moves the IRN to `Closed` in one `SaveChanges` (`Concurrent_CompleteDispositionAsync_calls_never_post_stock_twice` — real SQL), so a read taken in one consistent snapshot sees the quantity either as in-QC or as on-hand, never both.

---

## 6. Candidate calculation, rules and numerical examples

### 6.1 Terms (per material item *i*, per warehouse *w*; WO demand has no warehouse and is pooled — **[D1]**)
* `Usable(i,w) = Σ max(0, OnHand − Reserved)` over active stock rows.
* `UnreservedDemand(i,w) = Σ SR-line demand not covered by reservation + WO demand not covered by SR/consumption` (§3).
* `Incoming(i,w) = IncomingSupply (§5) + PlannedSupply` where `PlannedSupply` = Σ quantity of this engine's own *converted, not yet closed* recommendations (the idempotency device until PO lines exist).
* `Projected = Usable + Incoming − UnreservedDemand`.

### 6.2 Result
* `CommittedShortage = max(0, −Projected)` — hard shortage caused by eligible demand (this replaces the starting formula `max(0, Demand − Usable − Incoming)`: **reservations are already subtracted inside Usable**, so the spec's wording "minus valid reservations" must not be applied a second time; reserved demand is excluded from `UnreservedDemand` instead).
* `PolicyTopUp` (soft, only if `ReorderPoint` is set on the row): triggered when `Projected ≤ ReorderPoint`. Proposed (s,S/Q) reading: `target = MaximumQuantity ?? (ReorderPoint + ReorderQuantity)`; `PolicyTopUp = max(0, target − max(Projected,0)) − CommittedShortage` (never negative). **The semantics of Min/Max/ReorderQuantity are not defined anywhere in the repository [D4]**; v1 must output `CommittedShortage` as the recommendation and expose `BelowReorderPoint` plus an *informational* suggested top-up, not a purchase quantity.
* `RecommendedQuantity = CommittedShortage` (v1) — rounded only per an approved UoM rule **[D5]**; mixed or unconvertible units ⇒ the row is blocked with reason `UnitMismatch` (never auto-converted: `ConversionFactor` is unused and unspecified).
* Unidentified part (no stored link to a `MaterialItem`): no stock arithmetic; row appears as `PendingIdentity`; never creates a `MaterialItem`, PO or recommendation quantity.

### 6.3 Numerical examples (synthetic; one item, one warehouse)

| # | Situation | Usable | Unreserved demand | Incoming | Projected | Committed shortage |
|---|---|---|---|---|---|---|
| E1 normal | SR-1 approved 10, OnHand 4, Reserved 0 | 4 | 10 | 0 | −6 | **6** |
| E2 partial supply | same + converted recommendation of 4 (planned supply) | 4 | 10 | 4 | −2 | **2** |
| E3 reserved stock | OnHand 10, Reserved 6 (SR-A already reserved 6, not unreserved), SR-B approved 5 | 4 | 5 | 0 | −1 | **1** (naive "5 − 4 − 6 reserved" = −5 ⇒ 0 shortage would be wrong) |
| E4 partial receipt (needs PO lines) | PO line ordered 20; IRN received 12 (Submitted); no demand change | 0 | 0 | 8 outstanding (+12 in QC if D6) | — | 0; after disposition Accepted 9 / Hold 2 / Rejected 1 and IRN Closed: OnHand +9, outstanding stays 8, Hold 2 excluded, Rejected 1 only if D6 reopens |
| E5 cancelled demand | SR approved 10 then Cancelled (reservation released) or WO Cancelled | — | 0 (excluded) | — | — | 0; `ReservedQuantity` returns via `ReservationRelease` |
| E6 unidentified part | `PartCatalogueRequest` with `FirstPurchaseQuantity 5`, no `MaterialItem`, origin WO | n/a | not computed | — | — | **no quantity**; listed under *pending identity* |
| E7 WO + SR overlap | WO requires 8 of X; SR for same WO approved 5 (unissued); 1 already consumed via spare usage | — | WO uncovered = 8 − 1 − 5 = 2; SR 5 ⇒ total **7**, not 13 | — | — | depends on Usable |

**Assumptions:** same UoM everywhere; stock rows clamped at ≥ 0; SR `Approved` counts, `Submitted` does not; WO `Assigned/InProgress/OnHold` count; no lead-time/usage-rate (not available in the model).

### 6.4 Prevention strategy
| Risk | Prevention |
|---|---|
| Duplicate demand across modules | Demand is built from *typed source rows* (SR line, WO material) with explicit precedence: SR demand wins; WO demand is reduced by consumption and by live SR lines on the same WO+item; MR is never demand; PartCatalogue never adds quantity. Where no FK exists (SR↔WO) the rule is documented as inferential and covered by tests E7; adding a real link is a recommended schema change (§10, S6/G3) |
| Double subtraction of reservations | Use `Usable = OnHand − Reserved` once; count only *unreserved* demand (outstanding reservation per line is subtracted from the line, not from stock) |
| Incoming vs received double count | §5 bucket table; evaluate in a single consistent read (see below) |
| Cancelled/ineligible demand | status whitelist per source; terminal SR/WO statuses excluded; verified by tests |
| Held/rejected counted as usable | Never posted (verified tests above); held shown only in a report |
| Concurrent changes during calculation | Read all inputs in one transaction under **snapshot/repeatable-read** if the database allows (needs verification of `READ_COMMITTED_SNAPSHOT` setting **[D14]**) or fall back to read-committed plus an *input fingerprint* (max `RowVersion`/ledger id per input) stored on the recommendation; the **action** that consumes a recommendation (convert to MR/package) re-validates the fingerprint and refuses stale input. The engine never takes stock locks and never reserves |
| Duplicate recommendations across runs | One *open* recommendation per (item, warehouse) enforced by a filtered unique index; each run **upserts** (updates quantity, appends a revision) instead of inserting; a run id is stored; converted recommendations feed `PlannedSupply` so the next run does not recommend the same quantity again; dismissed ones are suppressed until inputs change materially **[D15]** |

---

## 7. Integration and authorization boundaries

* **Owning service [R]:** a **new application service** (`INetDemandService`, implemented in `PEMS.Persistence/Servises`) is justified because the existing owners are *write/posting* services (`MaterialStockService`, `StoreRequisitionLifecycleService`) and extending them with read-model aggregation would couple posting code to reporting. It reads through `PEMSDbContext`, **reuses** `StoreRequisitionStatuses`, `MaterialReservationStatuses`, `WorkOrderLifecycleService` status vocabulary and `IrnReceipt` semantics, and **never writes** `MaterialStock`/`InventoryTransaction`/reservations. Reservation stays exclusively in `StoreRequisitionLifecycleService`.
* **Pure calculator [R]:** the arithmetic lives in a pure static class in `PEMS.Application` (pattern of `PartCatalogueRules`) so it is exhaustively unit-testable without a database.
* **Workflow:** no new workflow engine. v1 recommendations are advisory records. If an approval step is later required, bind it through the existing `IWorkflowRuntime`/binding service exactly as `PartCatalogue` and `IrnReceiptDisposition` do **[D9]**. Conversion to a purchase artefact must go through the existing `MaterialRequisitionService` / `ProcurementPackageService`, guarded by the Phase 1 `ProtectedActionGuard`.
* **Who may ask for a calculation [R]:** warehouse/inventory UI, SR approval screen (shortage hint), WO planning (read-only hint). All through one service method with the caller's permission checked first.
* **Permissions [R]:** introduce a new permission module (name e.g. `Replenishment`) with actions *View*, *Review* (run/refresh), *Convert*. **Seed the permission rows only; grant nothing** (same rule as `PurchaseOrder.Approve` in Phase 1). Which role/position owns each action is **[D9]**.
* **Origin validation:** an origin is referenced by typed ids (the Phase 1 pattern): existence, active/lifecycle, and `View` on the origin module. If an origin is inaccessible to the caller, its quantity **must still be included in totals** (dropping it would silently understate shortage) and be shown as "restricted source" **[D9]**; an invalid/missing origin makes the contribution `Excluded` with a reason, not silently zero.
* **Transactions / concurrency / idempotency [R]:** calculation = read-only snapshot; persistence of recommendations = one transaction per run chunk with `RowVersion`; "run" endpoint accepts a request key (pattern of `StoreRequisitionLifecycleService.RunAsync`: unique `(entity, RequestKey)` index) so double-clicks and retries apply once.
* **Audit:** new entities inherit `BaseEntity` (automatic change auditing) and get an append-only status/revision history like `StoreRequisitionStatusHistory`; sensitive actions (Convert) write `AuthorizationAuditLog` via `ProtectedActionGuard`.

---

## 8. Identified data-model and workflow gaps (all **[F]** unless stated)

| # | Gap | Effect on the calculation |
|---|---|---|
| G1 | No `PurchaseOrderLine`; no IRN-line→PO-line link; imported POs carry no quantities | Incoming supply uncomputable; v1 uses planned supply only |
| G2 | `MaterialRequisitionItem`: free text, `double` quantity, string unit, no `MaterialItemId`; imported MRs header-only | MR cannot be netted or deduplicated; excluded from demand |
| G3 | No link between `StoreRequisitionLine` and `WorkOrderMaterial`; WO release gate ignores materials (`WorkOrderReleaseGateService` has no material reference) | WO/SR overlap handled by inference only |
| G4 | Stock uniqueness missing for NULL location; `StockOfAsync` ignores location; zero locations exist | Possible duplicate rows ⇒ usable stock split/duplicated |
| G5 | `HoldQuantity` has no release/resolution path | Held stock invisible forever to supply |
| G6 | No stock-level quarantine/blocked/damaged bucket (by design: gate-before-post) | Cannot make already-posted stock unusable except by Adjustment |
| G7 | UoM conversion unused; nullable `UomId` on lines | Mixed units must be blocked, not converted |
| G8 | `MrsImportService` can drive OnHand negative | Clamp + flag |
| G9 | Reorder policy stored per stock row (item×warehouse×location); `IsLowStock` ignores demand and incoming; `GetLowStockAsync` materialises every stock row with a reorder point into memory | Low-stock flag is not a replenishment signal; performance risk at scale |
| G10 | Legacy `SparePart` model holds separate quantities/reorder points | Two sources of truth for some spares **[D12]** |
| G11 | `MaterialReservation.MaintenancePlanId` never written; `Cancelled` status mentioned in comment but not defined | No PM reservations exist |
| G12 | `ReservedQuantity` is maintained incrementally (`Math.Max(0,…)` in release) — drift would be hidden; no reconciliation | Add a reconciliation diagnostic |
| G13 | `WorkOrderController.AddSpareUsage` writes stock directly from a controller, picks the best warehouse, no retry loop | Consumption is not warehouse-aware; behaviour differs from SR path |
| G14 | No lead time, usage rate or supplier data on `MaterialItem` | Only simple reorder-point logic is possible |
| G15 | Ledger `Acceptance` type undocumented in entity comment; sign convention per type | Ledger-based reconciliation needs an explicit sign table |

---

## 9. Recommended implementation sequence (Phase 2 packages)

| Pkg | Content | Depends on | Schema |
|---|---|---|---|
| **P0** | Business decisions D1–D15 (§13) and PO-issuer decision from Phase 1 | — | none |
| **P1** | Read-only `InventoryAvailability` query service: usable stock, reservations reconciliation (stock vs Σ active reservations), data-quality exceptions (negative, `Reserved > OnHand`, duplicate NULL-location rows) | P0(D1,D13) | none |
| **P2** | Pure `NetDemandCalculator` (+ rules for units, clamps, bucket arithmetic) with the full test matrix of §12 | P0(D4,D5) | none |
| **P3** | Demand collectors: SR lines, WorkOrderMaterial (+consumption), optional PM forecast, PartCatalogue pending-identity; dedup rules; still read-only | P1, P2 | none |
| **P4** | NULL-location stock uniqueness migration (after a duplicate scan on every real database) | P1 findings | S5 |
| **P5** | Persisted recommendations: run, recommendation, sources, history; idempotent upsert; permissions seeded (no grants); list/detail UI in the Warehouse area | P3, P4, P0(D9) | S1–S4 |
| **P6** | Conversion to MR/Procurement Package via existing services with traceability, guarded by `ProtectedActionGuard` | P5, PO-issuer decision | S6 (MR item link) |
| **P7** | `PurchaseOrderLine` + IRN-line link + incoming supply + open-order buckets | P0(D6,D8) | S7 |
| **P8** | Scheduling (Hangfire is already referenced by `HangfireDashboardAuthFilter`), alerting, performance hardening | P5 | none |

P1–P3 can ship without any migration, which is the lowest-risk first increment.

---

## 10. Proposed additive migrations (none created or applied)

| ID | Change | Why current schema cannot do it | Additive / nullability / backfill | Index & constraints | FK delete | Compatibility, rollback |
|---|---|---|---|---|---|---|
| S1 | `NetDemandRuns` (id, started/completed, requested-by employee, scope hash, status, request key) | No place to record a run or its idempotency key | New table | unique `(RequestKey)` filtered not null | `Restrict` | Pure add; drop table on rollback |
| S2 | `ReplenishmentRecommendations` (item, warehouse?, usable/unreserved/planned-supply/shortage snapshots, recommended qty, `UomId`, status Open/Dismissed/Converted/Superseded/Closed, reason codes, latest run, input fingerprint, `RowVersion`, typed FKs to the converted MR / ProcurementPackage) | No persisted recommendation | New table; no backfill | **filtered unique `(MaterialItemId, WarehouseId) WHERE Status='Open'`**; CHECK at most one conversion target | `Restrict` | Existing data untouched |
| S3 | `ReplenishmentRecommendationSources` (recommendation + typed nullable FKs `StoreRequisitionLineId`, `WorkOrderMaterialId`, `PartCatalogueRequestId`, contribution qty) | Needs traceability from a recommendation to each demand row | New table; CHECK exactly one FK (same pattern as Phase 1 origin) | indexes on each FK | `Restrict` | additive |
| S4 | `ReplenishmentRecommendationHistory` (append-only revisions/actions) | Audit of changes between runs | New table | `(RecommendationId, ChangedDate)` | `Restrict` | additive |
| S5 | Unique index `MaterialStocks(MaterialItemId, WarehouseId) WHERE StorageLocationId IS NULL` | Current unique index excludes NULL locations (the only kind that exists) | Index only; **requires a pre-check that no duplicates exist on each real database** (dev: 0 rows) | filtered unique | n/a | If duplicates exist the migration must not be applied until they are merged by a reviewed data fix; rollback = drop index |
| S6 | `MaterialRequisitionItems.MaterialItemId` nullable FK (+ optional back-link) | MR items are free text | Nullable column; **no backfill** (never guess from `PartNumber`) | index | `Restrict` | Existing/imported MRs unchanged |
| S7 | `PurchaseOrderLines` (+ `IrnReceiptLines.PurchaseOrderLineId` nullable) | No PO quantities | New table + nullable column; imported POs stay line-less and are *explicitly* "no quantity data" — **no reinterpretation of historical POs or their `Issued` status** | unique `(PurchaseOrderId, LineNo)` | `Restrict` | Needs P0(D8) first |
| (avoidable) | Per-item reorder policy table | Policy lives on `MaterialStock` rows | **Avoid in v1**: reuse `MaterialStock.ReorderPoint/Min/Max/ReorderQuantity` | — | — | — |

---

## 11. Security, authorization, audit and concurrency considerations
* No bypass of Phase 1 controls: any action that changes procurement state uses `ProtectedActionGuard` (role + identity + scope + SoD + one audit row per attempt, fail-closed); read-only calculation uses `IPermissionService`/`IRoleModuleActionService` checks on the new module.
* SystemAdmin has no implicit approval authority (Phase 1 `RequiresExplicitGrant`); do **not** add the new module to that list unless Convert is considered an approval — **[D9]**.
* Totals must not leak restricted origins' identities but must include their quantities (§7).
* All writes are confined to the new tables; the engine has no code path that can alter stock, reservations or the ledger (enforce with a source-scan test like `PurchaseOrderCreationPathTests`).
* Concurrency: optimistic (`RowVersion`) + request-key idempotency + stale-input refusal on conversion; existing reservation concurrency remains owned and tested by `StoreRequisitionReservationConcurrencyTests`.

---

## 12. Test plan and measurable acceptance criteria

All database-mutating tests use `PemsDbContextFactory` (default `PEMSDB_RegressionTest`, refuses `PEMSDB` without an explicit override) or dedicated disposable databases created with `MigrateAsync` (pattern of `PartCatalogueTraceabilitySqlTests`). Nothing runs against `PEMSDB`/`PEMSDB_PcatClone`.

| Area | Tests | Acceptance |
|---|---|---|
| Arithmetic & units | pure calculator: E1–E7; decimal(18,3) boundaries; clamp negatives; mixed units blocked; rounding rule per D5 | 100 % of table in §6.3 reproduced; property test: `Recommended ≥ 0`, `Projected` identity holds |
| Dedup | SR vs WO overlap (E7), duplicate SR lines for same item, MR never counted, PM forecast stops at WO generation, PartCatalogue adds no quantity | exact expected totals, no double counting |
| Reservations | calculation does not change `ReservedQuantity`; reserved SR contributes 0 unreserved demand; release on Cancel/Reject/CloseShort restores demand/usable | stock rows and ledger byte-identical before/after a run |
| Receipts | Accepted posted ⇒ not incoming; Hold/Rejected excluded; partial receipt; IRN reject→resubmit | no unit appears in two buckets (invariant test over randomised sequences) |
| Cancellation/closure | SR Rejected/Cancelled/Closed, WO Cancelled/Completed | excluded |
| Idempotency | same request key twice; two runs back-to-back | one open recommendation per (item, warehouse); second run = 0 inserts, updated revision only |
| Concurrency (real SQL) | run while a reservation/receipt commits; two concurrent runs; conversion with stale fingerprint | no duplicate open recommendation; stale conversion refused; no deadlock |
| Performance | seeded 10 000 items × 50 000 demand lines on a disposable DB | set-based queries (no N+1, verified by query count), `AsNoTracking` projections, no full-table materialisation; **target time to be fixed after a measured baseline in P1** and then asserted |
| Authorization & audit | permission matrix (none/View/Review/Convert), restricted origin masking, `ProtectedActionGuard` on Convert, audit row per denied attempt | zero mutations on denial |
| Regression | existing Inventory, Procurement, Maintenance, Part Catalogue groups unchanged | no new failures |

**`MrsImportBatchCapacityTests` (separate review) [F]:** the test stages and commits 5 000 rows (assertions: stage < 30 s, commit < 60 s) and is *not* a real-SQL test. Observed: whole class passes alone on the current tree (10/10, 1 m 12 s) but the test failed in three full-suite runs (7 m 3 s, 12 m 46 s, 11 m 1 s). No clean unmodified-tree baseline exists, so it must **not** be called unrelated. **[R]** profile it alone, then with `--parallel none` and with the full suite under CPU contention, record stage/commit times, and look for O(n²) change-tracker behaviour in `MrsImportService` before touching any threshold. No threshold change is proposed.

---

## 13. Unresolved business decisions (block implementation of the marked packages)
| ID | Decision | Blocks |
|---|---|---|
| D1 | Warehouse scope: per warehouse, or pooled with transfer considered before buying? WO demand has no warehouse | P1–P3 |
| D2 | Do `Submitted` (not yet approved) SRs count as demand, or only `Approved`+? | P3 |
| D3 | WO statuses that count (is `Open` demand?), and whether/what horizon for PM forecast | P3 |
| D4 | Semantics of `MinimumQuantity/MaximumQuantity/ReorderPoint/ReorderQuantity` ((s,S) vs (s,Q)), safety stock, lead time source | policy top-up in P2/P5 |
| D5 | UoM conversion and rounding policy (round up to pack size?) | P2 |
| D6 | Count receipts in QC as incoming? Does a Rejected quantity re-open the PO line? | P7 - **decided 2026-10-30 (SCRUM-473):** in QC counts as incoming until the IRN is Closed; the buyer decides replace (default, reopens) or close short |
| D7 | Who resolves `HoldQuantity` and how it re-enters supply | P7 / usable stock - **decided 2026-10-30 (SCRUM-473):** the Warehouse Manager from My Work; accepted part posted to stock, rejected part follows D6 |
| D8 | What an "open PO" is when imported POs have no lines (explicitly out of the calculation?) | P7 - **decided 2026-10-30 (SCRUM-473):** out of the calculation; only stock lines of issued POs count |
| D9 | Owners of View/Review/Convert on recommendations; whether an approval workflow is needed; masking rule for restricted origins; whether SystemAdmin needs an explicit emergency grant | P5, P6 |
| D10 | Is an MR demand or supply pipeline; scope of "engineering/project demand" | P3/P6 |
| D12 | Retire or bridge legacy `SparePart` quantities | P1 |
| D13 | Negative stock: clamp and alert, or block the import path? | P1 |
| D14 | Run cadence, staleness limits, snapshot-isolation availability | P5/P8 |
| D15 | Dismissal semantics and re-trigger threshold | P5 |
| — | PO issuer (`PurchaseOrder.Approve`) — carried over from Phase 1; blocks issuing the POs the engine will eventually recommend | P6/P7 |

---

## 14. Files and methods expected to change during implementation (indicative; none changed now)

New: `PEMS.Application/Interfaces/INetDemandService.cs`; `PEMS.Application/Inventory/NetDemandCalculator.cs` (pure rules); `PEMS.Application/DTOs/NetDemandDtos.cs`; `PEMS.Persistence/Servises/NetDemandService.cs`, `InventoryAvailabilityService.cs`; `PEMS.Domain/Entities/ReplenishmentRecommendation*.cs`; EF configurations + migrations (S1–S5); `PEMS.Web/Areas/Warehouse/Controllers/ReplenishmentController.cs` + views; tests under `PEMS.Tests/Inventory/NetDemand*Tests.cs` (+ a real-SQL class).
Modified (small): `PEMS.Web/Program.cs` (DI); `PEMS.Persistence/Servises/PermissionService.cs` / `AccessControlSeeder.cs` (seed new permission rows, no grants); `PEMS.Persistence/Context/PEMSDbContext.cs` (DbSets); later `MaterialStockService.GetOrCreateStockAsync` (race handling once S5 exists) and, only if D13 says so, `MrsImportService` negative-stock guard; P6/P7: `MaterialRequisitionService`, `ProcurementPackageService`, `PurchaseOrderService`, `IrnReceiptService`.
Explicitly **not** to be modified by the engine: `StoreRequisitionLifecycleService` reservation/issue logic, `IrnReceiptService.CompleteDispositionAsync` posting, `ProductionRunService` quality gate.

---

## 15. Status of this deliverable
Documentation only. No code, tests, migrations, permissions or data were changed; no commit or push was made; `PEMSDB` and `PEMSDB_PcatClone` were only read.

---

## 16. P0 — Frozen version-one policies (implementation gate P0–P3)

Authorised by the user; implemented in `PEMS.Application/Inventory/*`, `PEMS.Persistence/Servises/{UsableStockService,NetDemandCollector,NetDemandService}.cs`. Read-only: no writes, reservations, transactions, recommendations, MR/PO creation or permission grants. Status names below were re-verified against `StoreRequisitionStatuses`, `MaterialReservationStatuses` and `WorkOrderLifecycleService.LegalTransitions`.

| Topic | Frozen v1 policy | Where enforced |
|---|---|---|
| Scope | Stock is calculated **per warehouse**; stock is never pooled across warehouses; no cross-warehouse aggregate is reported (it would imply transferability) | `NetDemandService` groups by `(material, warehouse)` |
| Store Requisition demand | Only active SRs and lines in **Approved, Reserved, PartiallyIssued**; remaining = `(ApprovedQuantity ?? RequestedQuantity) − IssuedQuantity`. `Submitted` = *potential*, reported but not counted. Draft/Rejected/Cancelled/Issued/Closed are not demand | `NetDemandCollector` |
| Work Order demand | Active WOs in **Assigned, InProgress, OnHold**; remaining = `RequiredQuantity − Σ(MaintenanceSpareUsage.Quantity − ReturnedQuantity)`. `Open` = potential (plan still editable) and is reported, not counted; Completed/Closed/Cancelled/Rejected are not demand; unknown status ⇒ flagged. *The user prompt did not enumerate WO statuses; this set is the audit recommendation (D3) and is an assumption to confirm* | `NetDemandCollector` |
| WO ↔ SR duplicates | No FK links an SR line to a `WorkOrderMaterial`. The only persisted relation is `StoreRequisition.WorkOrderId`. If any non-Rejected/Cancelled SR exists for the same **(WorkOrderId, MaterialItemId)**, the WO requirement is **not counted** (`AmbiguousOverlapWithStoreRequisition`), the SR stays counted, and every warehouse result of that material is marked non-definitive. No text/description matching | `NetDemandCollector`, `NetDemandService` |
| WO demand has no warehouse | `WorkOrderMaterial` carries no warehouse (and `AddSpareUsage` picks the best-stocked row at issue time). WO demand is reported in a **warehouse-less group** (`NotComparableNoWarehouse`), never netted against any stock, and marks that material's warehouse results non-definitive (`WorkOrderDemandNotAttributableToWarehouse`). *Blocker for a definitive WO result: an approved warehouse-attribution rule.* | `NetDemandCalculator`, `NetDemandService` |
| Not demand sources in v1 | Material Requisitions, PM forecasts, engineering/project demand, unidentified parts / Part Catalogue quantities. None is read by the collector (test `MaterialRequisitionsPartCatalogueRequestsAndPmPlans_AreNotDemandSources`) | collector |
| Material identity | Direct `MaterialItemId` only. Missing, inactive or non-stock materials are excluded with a reason; no free-text matching | collector |
| Units | No conversion. Stock is in the material's base UoM (no unit on `MaterialStock`; reservation/issue apply line quantities 1:1). Line `UomId` **null ⇒ base unit** (verified: the SR line and WO material UI never set it), equal to base ⇒ compatible, different ⇒ `Incompatible`; material without base UoM ⇒ `Unknown`. Both are excluded from the numbers and the result is non-definitive | collector, `UsableStockService`, calculator |
| Usable stock | `Σ(OnHand − Reserved)` over active rows of the (material, warehouse) — the quantity that reservation, transfer and WO usage already use. Negative OnHand, negative Reserved, `Reserved > OnHand` are **reported, never altered**; the raw value is returned, the calculation clamps at 0 explicitly and the result is non-definitive. Held/Rejected IRN quantities are never posted so they are never usable; no quarantine/blocked bucket is claimed | `UsableStockService`, calculator |
| Duplicate stock rows | ≥ 2 active rows with `StorageLocationId IS NULL` for one (material, warehouse) ⇒ `AmbiguousRows`, no totals, not calculable (no arbitrary pick; repairing them is P4). Rows with distinct locations are summed at warehouse level and flagged non-blocking. Inactive rows holding quantity are excluded and flagged blocking. No active row ⇒ zero stock (as the reservation service treats it), non-blocking | `UsableStockService` |
| Reservation convention | `Usable = OnHand − Reserved` already removes reserved stock once. Demand already covered by an active reservation (`Σ(Quantity − IssuedQuantity)` for Active/PartiallyIssued reservations of the same SR line) is removed from **demand**, never from stock again: `Unreserved = Gross − Covered`; `StockOnlyShortage = max(0, Unreserved − max(0, Usable))`. Legacy reservations without a line are attributed only when exactly one active line of that SR has the material, otherwise the lines are excluded as ambiguous | calculator doc-comment, collector |
| Incoming supply | **Unknown / not modeled**: no PO lines, no receipt→PO-line link. Never read from PO headers, never 0. Result kind is `StockOnlyShortageEstimate`; `ProjectedBalance` and `CommittedShortage` are `null`. The audited formula `Projected = Usable + Incoming − Unreserved`, `CommittedShortage = max(0, −Projected)` is implemented and tested but only runs when a *known* incoming value is supplied (never in v1). IRN receipts are never added as incoming | calculator |
| Reorder policy | Kept separate from shortage. If `ReorderPoint` exists the status is `PolicyPresentNotEvaluated` (policy echoed, `SuggestedQuantity` always `null`); otherwise `NotConfigured`; if the stock is not calculable ⇒ `NotCalculable`. No lead time, rounding, or reorder semantics invented | calculator |
| Permissions | Read-only; **no grant, no new role/module**. Viewing any result needs the existing `MaterialItem.View`; origin identity (SR number/ids, WO number/ids) is shown only with `StoreRequisition.View` / `WorkOrder.View`, otherwise masked (`IsRestricted`, ids 0) while the quantity stays in the totals. SR/WO requester ownership is not part of the existing view model and is not enforced | `NetDemandService` |

### Unresolved business decisions after P0 (still open)
D1-attribution (which warehouse a Work Order's demand belongs to) · D3 (confirm `Open`/PM treatment) · D4 (reorder-point semantics, lead time) · D5 (unit conversion and rounding) · D6/D7/D8 (incoming-supply rules, hold resolution, meaning of an "open PO" for line-less imports) · D9 (who may review/convert recommendations; masking rule is implemented as proposed) · D12 (legacy `SparePart`) · D13 (negative stock: block the import or only alert) · D14 (snapshot isolation, run cadence) · D15 (dismissal semantics) · the Phase 1 PO-issuer decision.

### Deferred (not started): P4 stock-uniqueness index, P5 persisted recommendations, P6 conversion to MR/package, P7 PO lines / incoming supply, P8 scheduling; quarantine/blocked schema; `SparePart` reconciliation; unit conversion; MR/PM/engineering/unidentified-part demand.
