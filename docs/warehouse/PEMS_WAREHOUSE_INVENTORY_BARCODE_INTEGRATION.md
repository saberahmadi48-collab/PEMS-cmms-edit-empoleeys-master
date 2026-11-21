# PEMS — Warehouse and Inventory Barcode Integration

**Status:** Implemented for a bounded, evidence-based scope. The existing Barcode/QR Core (closed
last workstream) was reused unchanged — no second identity engine, resolver, label engine, or
audit system was created.

## 1. Source-level audit

Direct inspection of `PEMS.Domain/Entities/`, `PEMS.Application/Interfaces/`,
`PEMS.Persistence/Servises/`, and the relevant controllers/views found a materially complete
Inventory/Warehouse domain already in place:

| Concept | Finding |
|---|---|
| Material Master / MaterialItem | `MaterialItem` (business key `MaterialCode`) — already the Barcode Core's target type. |
| Inventory / Stock | `MaterialStock` — a mutable (Material × Warehouse × Location) quantity aggregate, **not** a stable "thing" to barcode (see §4). |
| Inventory Transaction | `InventoryTransaction` — already models `Receipt/Issue/Return/Transfer/Adjustment/Reservation/ReservationRelease` as a `TransactionType` string. This **is** the existing accounting path; nothing new was built here. |
| Warehouse / StorageLocation | `Warehouse` → `StorageLocation` (self-referencing Zone→Rack→Bin) — already Barcode Core target types from the prior workstream. No separate Rack/Bin entity exists or was needed. |
| Serial Number / Lot / Batch | `MaterialItem.IsSerialized`/`IsBatchControlled` are forward-looking flags with **no backing child entity** — confirmed unchanged since the prior workstream's audit. Not implemented as barcode targets (§4). |
| Package | No entity exists anywhere in the domain. Not implemented. |
| Goods Receipt / Material Receipt | `IMaterialStockService.ReceiveAsync` (existing) — creates an `InventoryTransaction` with `TransactionType="Receipt"`. Exposed via `MaterialItemController.ReceiveStock` (existing, unchanged). |
| Material Issue | Two existing paths: `WorkOrderController.AddSpareUsage` (creates `MaintenanceSpareUsage` + an `InventoryTransaction` with `TransactionType="Issue"`, deducts `MaterialStock.OnHandQuantity`) and `StoreRequisitionLifecycleService` (Store Requisition fulfillment path). Neither was modified. |
| Material Return | `IMaterialStockService.ReturnAsync` **exists at the service level but has no controller/UI wiring today** — a pre-existing gap, not introduced or fixed by this task (adding a Return UI is inventory feature work, out of scope for barcode integration). |
| Material Transfer | `IMaterialStockService.TransferAsync` (existing) — exposed via `MaterialItemController.TransferStock` (existing, unchanged). |
| Material Reservation | `MaterialReservation` entity exists; no dedicated service/controller found wired to it beyond the entity itself — not touched. |
| Material Request | `StoreRequisition`/`StoreRequisitionItem`/`StoreRequisitionStatusHistory` (existing lifecycle, `StoreRequisitionLifecycleService`) — not touched. |
| Work Order Material Requirement | `WorkOrderMaterial` (planning-only record: `WorkOrderId` + `MaterialItemId` + `RequiredQuantity`) — exposed via `WorkOrderController.AddRequiredMaterial` (existing, unchanged business logic). |
| Existing Barcode/QR integration | `IBarcodeService`/`BarcodeService`/`BarcodeController` (closed prior workstream) — the sole identification engine reused throughout this task. |
| Label/PDF generation | `IBarcodeRenderingService`/`BarcodeRenderingService` (closed prior workstream, QuestPDF-based) — untouched, already covers Material/Warehouse/StorageLocation labels since those target types were already supported. |
| Authorization | `IPermissionService`, `"Barcode"` module (closed prior workstream) — reused unchanged; `"WorkOrder"`/`Edit` for spare-usage/required-material posting (existing, unchanged); `MaterialItemController`'s stock actions were confirmed to have no permission gate at all (see §7 — a pre-existing condition, not introduced here). |
| Audit | `PEMSDbContext.SaveChangesAsync` global interceptor — reused unchanged; every `InventoryTransaction`/`MaterialStock`/`MaintenanceSpareUsage` write these existing actions make was already audited before this task and remains so. |

## 2. Audit Matrix

| Capability | Existing Source | Reuse | Extend | New | Do Not Create |
|---|---|---|---|---|---|
| Material Master | `MaterialItem` | ✅ | | | |
| MaterialItem | `MaterialItem` | ✅ | | | |
| Inventory | `MaterialStock`, `InventoryTransaction` | ✅ | | | |
| Stock | `MaterialStock` | ✅ (not a barcode target itself) | | | |
| Warehouse | `Warehouse` | ✅ (already a Barcode Core target) | | | |
| StorageLocation | `StorageLocation` | ✅ (already a Barcode Core target) | | | |
| Rack/Bin | `StorageLocation` self-reference | ✅ (same entity, no separate one) | | | Separate Rack/Bin entity |
| Serial Number | `MaterialItem.IsSerialized` flag only | | | | Serial entity/target (no backing data model) |
| Lot/Batch | `MaterialItem.IsBatchControlled` flag only | | | | Lot/Batch entity/target (no backing data model) |
| Package | none | | | | Package entity/target |
| Receipt | `IMaterialStockService.ReceiveAsync` + `MaterialItemController.ReceiveStock` | ✅ | ✅ (scan-assist UI only) | | New Receipt service/entity |
| Issue | `WorkOrderController.AddSpareUsage` | ✅ | ✅ (scan-assist UI only) | | New Issue service/entity |
| Transfer | `IMaterialStockService.TransferAsync` + `MaterialItemController.TransferStock` | ✅ | | | New Transfer service (not scan-assisted this pass — see §8) |
| Material Request | `StoreRequisition*` | ✅ (untouched) | | | |
| Work Order Material | `WorkOrderMaterial` + `WorkOrderController.AddRequiredMaterial` | ✅ | ✅ (scan-assist UI only) | | |
| Barcode Core | `IBarcodeService`/`BarcodeService` | ✅ | ✅ (`ResolveJson` adapter) | | Second identity engine/resolver |
| QR Core | ZXing.Net encoding (Barcode Core) | ✅ | | | Second QR engine |
| Label Printing | `IBarcodeRenderingService` | ✅ (unchanged — already covers these target types) | | | Second rendering engine |
| Authorization | `IPermissionService`, `"Barcode"`/`"WorkOrder"` modules | ✅ | | | New permission actions |
| Audit | `PEMSDbContext.SaveChangesAsync` interceptor | ✅ | | | Second audit system |

## 3. Reuse of the existing Barcode Core

No new `BarcodeIdentity`-equivalent entity, no new resolver, no new audit/label engine was created.
The **only** addition to the Barcode Core itself is one thin adapter action,
`BarcodeController.ResolveJson(string code)`, which calls the exact same
`IBarcodeService.ResolveAsync` that the existing `Resolve` action already uses — it exists only
because `Resolve` performs a full-page redirect, which is unusable for inline "scan to pre-fill a
field without leaving the page" interactions inside the Receive/Issue/Required-Material forms.
`ResolveJson` returns only `{success, targetType, targetId, label}` — the same minimal,
non-sensitive shape `Resolve` already exposes, never raw business data.

## 4. Barcode target level decisions

- **Material identity = `MaterialItem`** (the Material Master/definition), not `MaterialStock`.
  `MaterialStock` is a derived, mutable (Material × Warehouse × Location) quantity row — it changes
  every time stock moves and has no stable physical identity of its own to print on a label. A
  physical material label identifies *what the thing is* (e.g., "Bearing 6312"), not *how many are
  currently in which bin* — exactly matching how the existing `MaterialItemController` structures
  Details (identity) vs. Stock (a list of quantity rows) as separate concepts. This was already the
  correct decision made in the prior workstream and is reaffirmed here with the fuller inventory
  model now in view.
- **Warehouse Location identity = `Warehouse` and `StorageLocation`**, already correct and already
  implemented.
- **Serialized items / Lots / Batches — not separate identities.** `MaterialItem` carries
  `IsSerialized`/`IsBatchControlled` flags, but no entity anywhere represents an individual serial
  unit or lot/batch. Inventing one now would be new inventory-domain architecture, not barcode
  integration, and is explicitly out of scope ("Do not create a new inventory system").
- **Package — not a target.** No entity exists.
- **Receipt / Issue / Transfer / Return — not barcode targets themselves.** These are business
  *transactions* (verbs), not identifiable *things*. A barcode never represents "this Receipt" — it
  represents the Material and/or Location involved, and scanning those assists the existing
  transaction-posting actions. This resolves the task's explicit "Material Definition vs. Physical
  Stock Instance" ambiguity concern: neither a transaction nor a stock-quantity row is ever a
  barcode target; only the two stable identities (Material, Location) are.

## 5. Warehouse scenarios implemented

| Scenario | Status |
|---|---|
| A. Material Identification (Open Material → view/generate Barcode → print label) | **Already fully supported** by the existing Barcode Core (Material tab on `MaterialItem/Details`, `Barcode/Index`'s scan-to-resolve box) — zero new code needed, reaffirmed by inspection. |
| B. Warehouse Location Identification (Warehouse → Location → Rack → Bin → generate/print) | **Already fully supported** — `Warehouse`/`StorageLocation` were already Barcode Core targets. |
| C. Receipt (scan Material, scan Warehouse, enter quantity, post) | **Scan-assisted.** Material is already known (the Receive modal opens from that Material's own Details page); a new "scan Warehouse barcode" input in the Receive Stock modal pre-selects the existing `warehouseId` dropdown. Posting still goes through the unmodified `MaterialItemController.ReceiveStock` → `IMaterialStockService.ReceiveAsync`. |
| D. Issue (scan Material, validate stock, issue to Work Order) | **Scan-assisted.** A new "scan Material barcode" input on the WorkOrder Details page's existing spare-usage form pre-selects the existing `materialItemId` dropdown. Posting still goes through the unmodified `WorkOrderController.AddSpareUsage`, which still performs all existing stock-availability validation and `InventoryTransaction`/`MaintenanceSpareUsage` creation. |
| E. Transfer (scan source, scan material, scan destination) | **Not scan-assisted this pass.** The existing Transfer modal already pre-fills its source warehouse from the stock row the user clicked (context already known); extending it to destination-warehouse scanning would use the exact same `ResolveJson` pattern already proven for Receive, and is a natural next increment, not added here to keep this pass to its smallest useful slice. |
| F. Return | **Not implemented.** No existing UI calls `IMaterialStockService.ReturnAsync` at all — building one is inventory feature work (a new screen/workflow), not barcode integration, and is explicitly out of scope for this task. |

## 6. Work Order integration

`WorkOrderMaterial` (planning) and the actual Issue path (`AddSpareUsage`) both received the same
scan-assist treatment (§5.D and the "Required Materials" form). In both cases:
- The barcode identifies the real, existing `MaterialItem` record — nothing new is encoded.
- Barcode scanning **only pre-fills the existing `<select name="materialItemId">`** — the existing
  server-side actions perform all validation, quantity/stock checks, `InventoryTransaction`
  creation, and audit exactly as before. No accounting logic was added, moved, or duplicated.
- If a scanned code resolves to a `MaterialItem` not present in the page's own dropdown (e.g., an
  inactive material not offered on this Work Order), the UI reports it and does **not** submit —
  the existing dropdown's own population logic remains the authoritative list.

## 7. Barcode resolution & authorization

`ResolveJson` requires the exact same `Barcode`/`View` permission as every other read action on
`BarcodeController`. It performs no target-specific authorization of its own, for the same reason
already documented in the prior workstream's final review: `MaterialItemController`'s actions
(`Details`, `ReceiveStock`, `AdjustStock`, `TransferStock`) and `WorkOrderController`'s
(`AddSpareUsage`, `AddRequiredMaterial`, gated by `"WorkOrder"`/`Edit`) were inspected directly —
`WorkOrderController`'s mutating actions **do** already enforce `"WorkOrder"`/`Edit`, unchanged by
this task. `MaterialItemController`'s stock-mutating actions have **no permission gate at all** —
confirmed unchanged, pre-existing, and out of this task's scope to fix (fixing it would mean adding
new authorization to an existing controller's core actions, which is Warehouse-module work, not
barcode integration, and risks breaking whatever access pattern the module's current users rely on
without a dedicated review of that module). Barcode scanning does not bypass or weaken whatever
gate each target action already has — it only ever pre-fills a form field a user with page access
could already fill in manually.

## 8. Inventory accounting rules — compliance

No stock balance is ever changed from `BarcodeController` or from any new JavaScript. All quantity
changes still flow exclusively through `IMaterialStockService`/`WorkOrderController.AddSpareUsage`'s
existing, unmodified code paths, preserving whatever transaction/concurrency/audit behavior they
already had. No SQL was added. No new quantity-mutation code path was introduced anywhere.

## 9. Labels

No new rendering code. `IBarcodeRenderingService` (SVG/PNG/PDF, QuestPDF-based) already renders
labels for `Warehouse`/`StorageLocation`/`MaterialItem` barcodes — reused unchanged, since these
target types were already registered in the prior workstream.

## 10. Lifecycle

Unchanged — Create/Disable/Revoke/Replace/Print/Reprint/Verify all still behave exactly as closed
in the prior workstream's final review, including the DB-level filtered-unique-index duplicate
protection and the orphan-record and concurrent-bulk-generation fixes from that review.

## 11. Database changes

**None.** No migration was created or required — this entire integration is application-layer
(one new controller action + view/JS additions). Verified via `git status`: no new migration files.

## 12. API changes

One new action: `GET /Barcode/ResolveJson?code=...` — returns
`{ success, targetType, targetId, label }` on success or
`{ success: false, errorCode, errorMessage }` on failure. Same permission gate, same underlying
service call, as the existing `Resolve` action — not a new API surface, an adapter view of the
existing one.

## 13. UI changes

- `WorkOrder/Details.cshtml`: added a "Scan Material barcode" input above the existing Add Spare
  Usage form, and above the existing Add Required Material form — both pre-fill the existing
  `<select name="materialItemId">`.
- `MaterialItem/Details.cshtml`: added a "Scan Warehouse barcode" input inside the existing Receive
  Stock modal, pre-filling the existing `<select name="warehouseId">`.
- No page was redesigned; no existing form field, action, or route changed.

## 14. Authorization changes

None to the permission model. `ResolveJson` reuses the existing `"Barcode"`/`View` check; no new
permission action was created.

## 15. Audit events

No new audit code. Every write these existing actions make (`InventoryTransaction`,
`MaterialStock` update, `MaintenanceSpareUsage`, `WorkOrderMaterial`) was already captured by the
global `AuditLog` interceptor before this task and remains so — this task added no writes of its
own (`ResolveJson` is read-only).

## 16. Tests

`PEMS.Tests/Authorization/BarcodeControllerAuthorizationTests.cs` (+3): `ResolveJson` permission
gate (Forbid without `Barcode`/`View`), unknown-code returns `success:false`, a valid `MaterialItem`
barcode resolves to the correct `targetType`/`targetId`. No tests were added for
`ReceiveStock`/`AddSpareUsage`/`AddRequiredMaterial` themselves since their server-side logic was
not modified — the changes are client-side form pre-fill only, and the material/location resolution
they now can use is already covered by the existing `BarcodeServiceTests` (Material/Warehouse
resolution, invalid-target rejection, authorization) from the prior workstream.

**Results:** targeted Barcode run 43/43 passed; full regression **1329/1329 passed** (1326
baseline + 3 new, exact match). One full-suite run showed 2 transient failures in
`EngineeringRevisionQualityPerformanceTests` (a live-SQL-Server-dependent, unrelated Engineering
Documents test) under full-suite parallel load; both passed cleanly in isolation and the full suite
was re-run clean at 1329/1329 — not a regression from this work.

## 17. Known limitations

- Transfer's destination-warehouse scanning was not added this pass (§5.E) — a natural, low-risk
  follow-up using the identical `ResolveJson` pattern already proven twice.
- No Return UI exists to scan-assist, since none existed before this task.
- `MaterialItemController`'s stock-mutating actions remain unauthorized (pre-existing condition,
  documented, not fixed — out of this task's scope).
- Serial/Lot/Package barcode targets remain unimplemented because no backing data model exists for
  them yet — this is a data-model gap, not a barcode-integration gap.
- Work Order material planning (`AddRequiredMaterial`) and actual issue (`AddSpareUsage`) do not
  cross-validate against each other at scan time (e.g., warning if an issued material wasn't on the
  planned list) — not implemented, to keep this pass to "assist existing forms," not "add new
  business rules."
