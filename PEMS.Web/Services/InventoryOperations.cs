using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-652, 654: what InventoryController and InventoryTransactionController read, as operations; the same counts, the same low-stock list and the same ledger service call.</summary>
public sealed class InventoryOperations(PEMSDbContext context, IMaterialStockService stock, IInventoryTransactionService ledger) : IInventoryOperations
{
    private const int LedgerLimit = 2000;

    public async Task<List<InventorySummaryRow>> SummaryAsync()
        => [new InventorySummaryRow(
            await context.MaterialItems.CountAsync(x => x.IsActive),
            await context.MaterialItems.CountAsync(x => x.IsActive && x.IsStockItem),
            await context.MaterialItems.CountAsync(x => x.IsActive && x.IsCritical),
            await context.MaterialStocks.CountAsync(x => x.IsActive && x.OnHandQuantity <= 0),
            await context.Warehouses.CountAsync(x => x.IsActive),
            await context.MaterialReservations.CountAsync(x => x.Status == "Active"),
            await context.StoreRequisitions.CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Rejected" && x.Status != "Cancelled"))];

    public async Task<List<InventoryLowStockRow>> LowStockAsync()
        => (await stock.GetLowStockAsync(10)).Select(s => new InventoryLowStockRow(s.MaterialStockId, s.WarehouseCode, s.OnHandQuantity, s.AvailableQuantity, s.ReorderPoint)).ToList();

    public async Task<List<InventoryTransactionRow>> RecentTransactionsAsync()
        => (await ledger.GetLedgerAsync(1, 15)).Items.Select(Row).ToList();

    public async Task<List<InventoryTransactionRow>> LedgerAsync(int? warehouseId = null, string? transactionType = null, DateTime? fromDate = null, DateTime? toDate = null)
        => (await ledger.GetLedgerAsync(1, LedgerLimit, null, warehouseId, string.IsNullOrWhiteSpace(transactionType) ? null : transactionType, fromDate, toDate)).Items.Select(Row).ToList();

    public async Task<List<ReportChoiceRow>> WarehousesAsync()
        => (await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.WarehouseId.ToString(CultureInfo.InvariantCulture), x.Code)).ToList();

    private static InventoryTransactionRow Row(PEMS.Application.DTOs.InventoryTransactionRowDto t)
        => new(t.InventoryTransactionId, t.TransactionDate, $"{t.MaterialCode} - {t.MaterialDescription}", t.TransactionType, t.WarehouseCode, t.Quantity,
            (t.ReferenceType ?? "-") + (t.ReferenceId.HasValue ? " " + t.ReferenceId.Value.ToString(CultureInfo.InvariantCulture) : ""), t.EmployeeName ?? "-", t.Reason ?? "-");
}
