/* ============================================================================================================
   PEMS — P4 / P5 pre-deployment diagnostics.   READ-ONLY: SELECT statements only (no DDL, no DML, no cleanup).

   HOW TO RUN (do this against the database you are about to migrate, never assume the target):
     sqlcmd -S <server> -d <TARGET_DB> -E -C -I -h -1 -W -i P4_P5_PREFLIGHT_DIAGNOSTICS.sql      (Windows auth)
     sqlcmd -S <server> -d <TARGET_DB> -U <user> -P <password> -C -I -h -1 -W -i ...              (SQL auth)
   Section 0 prints the server/database actually connected to — read it first and confirm it is the intended target.

   Expected result before the P4 migration can succeed: section 2 returns NO rows.
   ============================================================================================================ */
SET NOCOUNT ON;

PRINT '--- 0. Connected target (confirm this is the intended database) ---';
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, SUSER_SNAME() AS LoginName, GETDATE() AS ServerTime;

PRINT '--- 1. Migration state (code expects 105 migrations; P4 and P5 are the last two) ---';
SELECT COUNT(*) AS AppliedMigrations, MAX(MigrationId) AS LastAppliedMigration FROM __EFMigrationsHistory;
SELECT 'P4 migration applied' AS Item, COUNT(*) AS Present FROM __EFMigrationsHistory WHERE MigrationId = N'20261010141504_MaterialStockNoLocationUniqueness'
UNION ALL SELECT 'P5 migration applied', COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = N'20261011082322_ReplenishmentRecommendations'
UNION ALL SELECT 'P4 index present', COUNT(*) FROM sys.indexes WHERE name = N'UX_MaterialStocks_Item_Warehouse_NoLocation'
UNION ALL SELECT 'P5 tables present (expect 0 before, 4 after)', COUNT(*) FROM sys.tables WHERE name IN (N'NetDemandRuns', N'ReplenishmentRecommendations', N'ReplenishmentRecommendationSources', N'ReplenishmentRecommendationHistories');

PRINT '--- 2. BLOCKING: duplicate stock rows for the same item + warehouse with NO storage location (any row here makes the P4 migration refuse to run) ---';
SELECT MaterialItemId, WarehouseId, COUNT(*) AS RowsInGroup, SUM(OnHandQuantity) AS SumOnHand, SUM(ReservedQuantity) AS SumReserved,
       STRING_AGG(CONVERT(varchar(20), MaterialStockId), ',') AS MaterialStockIds,
       STRING_AGG(CONVERT(varchar(10), IsActive), ',') AS IsActiveFlags
FROM MaterialStocks WHERE StorageLocationId IS NULL
GROUP BY MaterialItemId, WarehouseId HAVING COUNT(*) > 1;

PRINT '--- 3. Informational: several ACTIVE rows for one item + warehouse (e.g. different locations). Valid data, but store-requisition reserve/issue refuse these (Option C) and P5 treats them as not definitive ---';
SELECT MaterialItemId, WarehouseId, COUNT(*) AS ActiveRows, SUM(CASE WHEN StorageLocationId IS NULL THEN 1 ELSE 0 END) AS NullLocationRows
FROM MaterialStocks WHERE IsActive = 1
GROUP BY MaterialItemId, WarehouseId HAVING COUNT(*) > 1;

PRINT '--- 4. Size of the data this change touches ---';
SELECT 'MaterialStocks' AS TableName, COUNT(*) AS Rows_, SUM(CASE WHEN StorageLocationId IS NULL THEN 1 ELSE 0 END) AS NullLocationRows FROM MaterialStocks
UNION ALL SELECT 'StorageLocations', COUNT(*), NULL FROM StorageLocations
UNION ALL SELECT 'InventoryTransactions', COUNT(*), NULL FROM InventoryTransactions
UNION ALL SELECT 'MaterialReservations', COUNT(*), NULL FROM MaterialReservations
UNION ALL SELECT 'StoreRequisitions', COUNT(*), NULL FROM StoreRequisitions
UNION ALL SELECT 'ProductionInputs', COUNT(*), NULL FROM ProductionInputs
UNION ALL SELECT 'ProductionOutputs', COUNT(*), NULL FROM ProductionOutputs;

PRINT '--- 5. Stock-data quality that makes P5 results non-definitive (informational; nothing is repaired) ---';
SELECT 'negative OnHand' AS Check_, COUNT(*) AS Rows_ FROM MaterialStocks WHERE OnHandQuantity < 0
UNION ALL SELECT 'negative Reserved', COUNT(*) FROM MaterialStocks WHERE ReservedQuantity < 0
UNION ALL SELECT 'Reserved > OnHand', COUNT(*) FROM MaterialStocks WHERE ReservedQuantity > OnHandQuantity
UNION ALL SELECT 'inactive row carrying quantity', COUNT(*) FROM MaterialStocks WHERE IsActive = 0 AND (OnHandQuantity <> 0 OR ReservedQuantity <> 0)
UNION ALL SELECT 'active row with NULL LastUpdated (freshness unknown)', COUNT(*) FROM MaterialStocks WHERE IsActive = 1 AND LastUpdated IS NULL;

PRINT '--- 6. Production ledger identity (a filtered unique ledger index is NOT part of this release; duplicates here would matter for a future one) ---';
SELECT ReferenceType, ReferenceId, COUNT(*) AS LedgerRows
FROM InventoryTransactions WHERE ReferenceType IN (N'ProductionInput', N'ProductionOutput')
GROUP BY ReferenceType, ReferenceId HAVING COUNT(*) > 1;

PRINT '--- 7. Reservation reconciliation (stock.Reserved vs remaining active reservations, per item + warehouse); rows returned = drift to review ---';
SELECT s.MaterialItemId, s.WarehouseId, SUM(s.ReservedQuantity) AS StockReserved,
       ISNULL((SELECT SUM(r.Quantity - r.IssuedQuantity) FROM MaterialReservations r
               WHERE r.MaterialItemId = s.MaterialItemId AND r.WarehouseId = s.WarehouseId AND r.Status IN (N'Active', N'PartiallyIssued')), 0) AS ReservationsRemaining
FROM MaterialStocks s WHERE s.IsActive = 1
GROUP BY s.MaterialItemId, s.WarehouseId
HAVING SUM(s.ReservedQuantity) <> ISNULL((SELECT SUM(r.Quantity - r.IssuedQuantity) FROM MaterialReservations r
               WHERE r.MaterialItemId = s.MaterialItemId AND r.WarehouseId = s.WarehouseId AND r.Status IN (N'Active', N'PartiallyIssued')), 0);

PRINT '--- 8. Roles and users (needed to resolve the three role names still unmapped for ReplenishmentRecommendation: Warehouse Clerk, Procurement, Maintenance Planner) ---';
SELECT r.Name AS RoleName, r.DisplayName, r.Domain, (SELECT COUNT(*) FROM AspNetUserRoles ur WHERE ur.RoleId = r.Id) AS Users
FROM AspNetRoles r ORDER BY r.Name;

PRINT '--- 9. Existing permission rows for the new module (expected: none before the first start of the new build; afterwards exactly WH.Manager) ---';
SELECT RoleName, ModuleName, CanView, CanCreate, CanEdit, CanDelete, CanApprove FROM RolePermissions WHERE ModuleName = N'ReplenishmentRecommendation';
