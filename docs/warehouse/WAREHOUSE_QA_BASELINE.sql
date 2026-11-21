-- SQL Server. Run with a SELECT-only account against an approved QA database.
-- The database name guard is intentional. Confirm the QA target before editing it.
IF DB_NAME() <> N'PEMSDB_QA407'
    THROW 50000, 'Expected the approved PEMS QA database.', 1;

SELECT DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS CollectedAtUtc;

SELECT N'MaterialStocks' AS Dataset, COUNT_BIG(*) AS TotalRows FROM dbo.MaterialStocks
UNION ALL SELECT N'InventoryTransactions', COUNT_BIG(*) FROM dbo.InventoryTransactions
UNION ALL SELECT N'MaterialReservations', COUNT_BIG(*) FROM dbo.MaterialReservations;

SELECT N'Duplicate stock keys' AS CheckName, COUNT_BIG(*) AS Exceptions
FROM (
    SELECT MaterialItemId, WarehouseId, StorageLocationId
    FROM dbo.MaterialStocks
    GROUP BY MaterialItemId, WarehouseId, StorageLocationId
    HAVING COUNT_BIG(*) > 1
) AS Duplicates
UNION ALL
SELECT N'Invalid active stock quantities', COUNT_BIG(*)
FROM dbo.MaterialStocks
WHERE IsActive = 1 AND (OnHandQuantity < 0 OR ReservedQuantity < 0 OR ReservedQuantity > OnHandQuantity)
UNION ALL
SELECT N'Stock location missing or assigned to another warehouse', COUNT_BIG(*)
FROM dbo.MaterialStocks AS S
LEFT JOIN dbo.StorageLocations AS L ON L.StorageLocationId = S.StorageLocationId
WHERE S.StorageLocationId IS NOT NULL AND (L.StorageLocationId IS NULL OR L.WarehouseId <> S.WarehouseId)
UNION ALL
SELECT N'Location parent missing or assigned to another warehouse', COUNT_BIG(*)
FROM dbo.StorageLocations AS L
LEFT JOIN dbo.StorageLocations AS P ON P.StorageLocationId = L.ParentLocationId
WHERE L.ParentLocationId IS NOT NULL AND (P.StorageLocationId IS NULL OR P.WarehouseId <> L.WarehouseId)
UNION ALL
SELECT N'Invalid active reservation quantities', COUNT_BIG(*)
FROM dbo.MaterialReservations
WHERE IsActive = 1 AND (Quantity < 0 OR IssuedQuantity < 0 OR IssuedQuantity > Quantity)
UNION ALL
SELECT N'Open reservations without warehouse attribution', COUNT_BIG(*)
FROM dbo.MaterialReservations
WHERE IsActive = 1 AND Status IN ('Active', 'PartiallyIssued') AND WarehouseId IS NULL;

-- Reservation records do not have a storage-location key. Compare at item/warehouse grain.
WITH Stock AS (
    SELECT MaterialItemId, WarehouseId, SUM(ReservedQuantity) AS ReservedBalance
    FROM dbo.MaterialStocks WHERE IsActive = 1
    GROUP BY MaterialItemId, WarehouseId
), Reservations AS (
    SELECT MaterialItemId, WarehouseId, SUM(Quantity - IssuedQuantity) AS OpenReservationBalance
    FROM dbo.MaterialReservations
    WHERE IsActive = 1 AND Status IN ('Active', 'PartiallyIssued') AND WarehouseId IS NOT NULL
    GROUP BY MaterialItemId, WarehouseId
)
SELECT COUNT_BIG(*) AS ItemWarehouseReservationDifferences
FROM Stock AS S
FULL OUTER JOIN Reservations AS R
    ON R.MaterialItemId = S.MaterialItemId AND R.WarehouseId = S.WarehouseId
WHERE COALESCE(S.ReservedBalance, 0) <> COALESCE(R.OpenReservationBalance, 0);

-- Profile ledger coverage first. Do not interpret this as an on-hand reconciliation.
-- Reconciliation requires a verified opening balance, sign rules and unit normalization.
SELECT TransactionType, COUNT_BIG(*) AS MovementCount,
    SUM(CONVERT(bigint, CASE WHEN UomId IS NULL THEN 1 ELSE 0 END)) AS MissingUomCount,
    MIN(TransactionDate) AS FirstMovement, MAX(TransactionDate) AS LastMovement
FROM dbo.InventoryTransactions
GROUP BY TransactionType
ORDER BY TransactionType;
