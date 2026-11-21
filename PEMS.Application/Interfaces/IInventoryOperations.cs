namespace PEMS.Application.Interfaces;

/// <summary>The totals of the inventory dashboard (one row).</summary>
public sealed record InventorySummaryRow(int Materials, int StockItems, int Critical, int OutOfStock, int Warehouses, int Reservations, int PendingRequisitions);

/// <summary>One low-stock line of the dashboard.</summary>
public sealed record InventoryLowStockRow(int MaterialStockId, string Warehouse, decimal OnHand, decimal Available, decimal? ReorderPoint);

/// <summary>One line of the transaction ledger (the dashboard shows the latest 15, the ledger page filters and pages them).</summary>
public sealed record InventoryTransactionRow(int InventoryTransactionId, DateTime Date, string Material, string Type, string Warehouse, decimal Quantity, string Reference, string By, string Reason);

/// <summary>
/// The inventory dashboard and the transaction ledger as read operations (SCRUM-652, 654). The module has no permission row of its own; like the old controllers
/// they stand on MaterialItem.View (owner decision D-OPEN-02 of P0-09).
/// </summary>
public interface IInventoryOperations
{
    [PEMS.Application.Common.PemsOperation("Inventory", "Summary", "MaterialItem.View")]
    Task<List<InventorySummaryRow>> SummaryAsync();

    [PEMS.Application.Common.PemsOperation("Inventory", "LowStock", "MaterialItem.View")]
    Task<List<InventoryLowStockRow>> LowStockAsync();

    [PEMS.Application.Common.PemsOperation("Inventory", "RecentTransactions", "MaterialItem.View")]
    Task<List<InventoryTransactionRow>> RecentTransactionsAsync();

    /// <summary>The ledger newest first (the most recent 2000 lines that match; the grid pages them).</summary>
    [PEMS.Application.Common.PemsOperation("Inventory", "Ledger", "MaterialItem.View")]
    Task<List<InventoryTransactionRow>> LedgerAsync(int? warehouseId = null, string? transactionType = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Inventory", "Warehouses", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> WarehousesAsync();
}
