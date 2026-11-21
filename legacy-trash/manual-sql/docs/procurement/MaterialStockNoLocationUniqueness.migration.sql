BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM [MaterialStocks] WHERE [StorageLocationId] IS NULL GROUP BY [MaterialItemId], [WarehouseId] HAVING COUNT(*) > 1)
    THROW 51000, N'P4 pre-flight failed: duplicate MaterialStocks rows (same MaterialItemId and WarehouseId with no StorageLocationId) exist. Nothing was changed. Reconcile them manually first (see docs/procurement/INVENTORY_STOCK_ROW_INTEGRITY.md), then re-run the migration.', 1;

CREATE UNIQUE INDEX [UX_MaterialStocks_Item_Warehouse_NoLocation] ON [MaterialStocks] ([MaterialItemId], [WarehouseId]) WHERE [StorageLocationId] IS NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261010141504_MaterialStockNoLocationUniqueness', N'10.0.10');

COMMIT;
GO

