# Inventory stock-row integrity (Phase 2 / P4)

Scope: `MaterialStock` rows only (the per-location balance projection). Part Catalogue is out of scope and untouched.

## 1. Business key

A stock row is identified by **(`MaterialItemId`, `WarehouseId`, `StorageLocationId`)**, where `StorageLocationId = NULL` means
"no location" and is **one identity** (not "unknown"). Evidence: the pre-existing unique index on those three columns, every write path
(`MaterialStockService`, IRN, MRS import, production) looks up with exactly this key, `MaterialStock` has no batch/status column
(batch lives only on the ledger), `TenantId` is unused, and nothing deactivates stock rows.

Gap that existed: SQL Server (and EF's generated filter `IS NOT NULL` on nullable unique-index columns) does not constrain the
NULL-location rows, so concurrent first creation, two IRN lines of the same material, or MRS-import chunks could create duplicates, and
readers (`FirstOrDefault`, `OrderByDescending(...)`) silently picked one.

## 2. Stock write/read paths (audit)

| Path | Key used | After P4 |
|---|---|---|
| `MaterialStockService.Receive/Adjust/Return/Transfer` | full key | `StockRowResolver.GetOrCreateAsync` / `FindExactAsync`; bounded `PostWithRetryAsync` (max 3, RowVersion conflicts only) |
| `IrnReceiptService.CompleteDispositionAsync` | full key per line | read-only ambiguity pass, then get-or-create all rows **before** any mutation |
| `StoreRequisitionLifecycleService` (`StockOfAsync`, reserve/issue/release) | item + warehouse (location ignored) | `FindForWarehouseAsync`; >1 active row (duplicate or multi-location) = explicit error, nothing changed |
| `MrsImportService.CommitAsync` | item + warehouse + NULL location | >1 candidate = `ErrorMessage` on the row, no posting; creation race is rejected by the new index (chunk fails loudly) |
| `ProductionRunService.PostInputToInventoryAsync` | full key | `FindExactAsync`; ambiguity = failure result |
| `WorkOrderController.AddSpareUsage` | item (+ warehouse choice) | deterministic order (`Available desc, MaterialStockId`) + ambiguity guard for the chosen key |
| `WorkOrderController.ReturnSpareUsage` | item | deterministic tie-break (`MaterialStockId`) |
| Read-only: `UsableStockService`, `MaterialItemService`, `InventoryController` | aggregate | unchanged (already report `AmbiguousRows`) |

Resolution rules: one row → use it; none → existing safe creation; several → fail safely with a bilingual message listing the
`MaterialStockId` values and stating nothing was changed. Rows are never summed, picked or merged automatically.

## 3. Concurrency strategy

* First creation: empty row inserted and saved on its own. A unique violation (SQL 2601/2627) detaches the loser and re-reads the
  winner (max 2 attempts). No quantity, transaction, receipt or reservation exists at that point, so retry cannot duplicate anything.
* Posting: one `SaveChanges` = one DB transaction (row + `InventoryTransaction`). On `DbUpdateConcurrencyException` (RowVersion), the
  attempt's `InventoryTransaction` objects are detached, touched stock rows reloaded, and the operation re-applied to the fresh value;
  max 3 attempts, then an explicit failure result with an actionable message (`Success=false`, nothing saved; never swallowed, no delays).
* IRN/SR keep their own idempotency (disposition status guard / `RequestKey`).

## 4. Migration `20261010141504_MaterialStockNoLocationUniqueness`

Index-only, additive: filtered unique index `UX_MaterialStocks_Item_Warehouse_NoLocation` on (`MaterialItemId`, `WarehouseId`)
`WHERE [StorageLocationId] IS NULL`. `Up` first runs a pre-flight that `THROW`s (whole migration rolls back, nothing changed) if
duplicate NULL-location rows exist. It never merges or deletes data. `Down` drops the index. Idempotent script:
`legacy-trash/manual-sql/docs/procurement/MaterialStockNoLocationUniqueness.migration.sql`.

The migration is **not applied** to `PEMSDB`/`PEMSDB_PcatClone`. Because it only adds an index, an un-migrated database keeps working
(EF queries are unchanged); without it duplicates are still detected and stopped at the application level.

### Read-only duplicate diagnostic (run before applying)

```sql
-- duplicates that block the index (NULL location)
SELECT MaterialItemId, WarehouseId, COUNT(*) AS Rows_, SUM(OnHandQuantity) AS OnHand, SUM(ReservedQuantity) AS Reserved,
       STRING_AGG(CONVERT(varchar(20), MaterialStockId), ',') AS MaterialStockIds
FROM MaterialStocks WHERE StorageLocationId IS NULL
GROUP BY MaterialItemId, WarehouseId HAVING COUNT(*) > 1;

-- same item+warehouse with several location rows (valid data, but ambiguous for warehouse-level SR reservation)
SELECT MaterialItemId, WarehouseId, COUNT(*) AS Rows_ FROM MaterialStocks WHERE IsActive = 1
GROUP BY MaterialItemId, WarehouseId HAVING COUNT(*) > 1;
```

### Manual reconciliation (if duplicates exist)

Warehouse owner decides per case. Typical safe procedure: compare each row against the ledger (`InventoryTransactions` for the item and
warehouse); pick the row to keep; move `OnHand`/`Reserved` into it with a recorded `Adjustment`; re-point `MaterialReservations` /
other FKs; deactivate or delete the empty duplicate only after approval; re-run the diagnostic; then apply the migration.

## 5. Acceptance-audit changes and remaining findings

Fixed during the final acceptance audit:

* **Work Order spare return** (`WorkOrderSpareReturn`, used by `WorkOrderController.ReturnSpareUsage`): the return destination is now the
  warehouse/location of the usage's own `Issue` transaction (`ReferenceType = "WorkOrder"`, same `WorkOrderId` + `MaterialItemId`), not the
  row with the highest on-hand. If no Issue exists (legacy data) or the Issues came from several warehouses/locations (the usage is not
  linked to a specific transaction), the return is refused with a diagnostic instead of guessing. The usage update, stock row and `Return`
  transaction are saved in one `SaveChanges`; a usage that belongs to another Work Order is rejected. A direct usage→transaction link would
  need a schema change and was **not** made.
* **Posting retry exhaustion**: `MaterialStockService` now returns `Success=false` with a bilingual message after 3 conflicted attempts instead
  of a raw `DbUpdateConcurrencyException`; `AddSpareUsage` catches the same exception from its single `SaveChanges`.
* **Production input/output posting** (`PostingScope`): `PostInputToInventoryAsync` / `PostOutputToInventoryAsync` are now serialized per
  document with a transaction-scoped `sp_getapplock`, re-read the document after the lock, and link an already-posted ledger row instead of
  posting again. Evidence: before the change, 3 parallel posts of one output produced 3 receipts (also on the pre-P4 HEAD, so this defect
  pre-dates P4); after it, exactly one. The scope runs inside `Database.CreateExecutionStrategy().ExecuteAsync(...)` because the app is
  configured with `EnableRetryOnFailure()` (Program.cs), which rejects a bare user transaction; the new tests use that same configuration.
* **Store requisition transactions under the production retry strategy**: `StoreRequisitionLifecycleService.RunAsync` called
  `BeginTransactionAsync()` directly, which throws `InvalidOperationException` ("execution strategy ... does not support user-initiated
  transactions") with the app's real `EnableRetryOnFailure()` configuration; the test suite builds contexts without that option, so it was never
  seen. `RunAsync` now wraps its existing loop in the execution strategy (`StoreRequisitionRetryStrategyTests` reproduces the failure
  before the change and passes after). Other transaction sites use the strategy already (Numbering, MDR import, Work Order lifecycle,
  Work Request, Organization hierarchy); `OrganizationImportService` (outside inventory) was not examined.

Documented, not fixed (need a design decision or a schema change):

* SR reservations are warehouse-level (`MaterialReservation` has no stock row/location). SR flows therefore support exactly one active stock
  row per (item, warehouse); a second row created later (another location, a duplicate) makes the next reserve/issue/release fail safely
  with the ambiguity message until reconciled. Fixing it means storing the stock row on the reservation (schema) or an aggregation rule.
* `ProductionRunService.PostInputToInventoryAsync`: the read-then-absolute-`Adjust` race is **fixed** (see `P4_CLOSURE_AND_P5_READINESS.md` §5: signed `AdjustByAsync`).
* MRS import creation races rely only on the unique index (the losing chunk fails loudly); concurrent identical MRS commits are stopped by
  the stock row-version conflict, not by a row-level guard (`MrsImportRow` has no RowVersion).
* `MrsImportBatchCapacityTests.Five_thousand_row_file...` failed on the unmodified HEAD as well. Root cause found and **fixed** in the P4 closure round: `Attach(row)` re-tracked the
  whole batch graph per chunk (quadratic); `Entry(row).State = Unchanged` brings the 5 000-row commit from 57-424 s to ~1 s (`P4_CLOSURE_AND_P5_READINESS.md` §4).

## 6. Data check on the shared databases

`PEMSDB` and `PEMSDB_PcatClone` were inspected read-only: 0 `MaterialStocks` rows, so no duplicates or NULL-location rows exist
today. This is a snapshot, not proof the data will stay clean; run the diagnostic above before applying the migration anywhere.
