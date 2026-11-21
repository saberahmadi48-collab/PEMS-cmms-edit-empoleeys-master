using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-656: what MaterialItemController read, as operations over IMaterialItemService and the lookups of its forms.</summary>
public sealed class MaterialItemOperations(IMaterialItemService items, PEMSDbContext context) : IMaterialItemOperations
{
    private const int ListLimit = 2000;

    public async Task<List<MaterialItemRow>> ListAsync(string? q = null, int? categoryId = null, bool lowStockOnly = false, bool criticalOnly = false)
        => (await items.GetListAsync(1, ListLimit, string.IsNullOrWhiteSpace(q) ? null : q.Trim(), categoryId, lowStockOnly, criticalOnly)).Items
            .Select(m => new MaterialItemRow(m.MaterialItemId, m.MaterialCode, m.Description, m.CategoryName ?? "", m.DisciplineName ?? "", m.UomCode ?? "", m.Manufacturer ?? "", m.VendorName ?? "",
                m.IsStockItem, m.IsCritical, m.IsActive, m.TotalOnHand, m.TotalAvailable, m.IsLowStock)).ToList();

    public async Task<List<MaterialItemDetailRow>> DetailAsync(int materialItemId)
    {
        var m = await items.GetDetailsAsync(materialItemId);
        if (m is null) return [];
        return [new MaterialItemDetailRow(m.MaterialItemId, m.MaterialCode, m.MaterialType ?? "", m.Description, m.LongDescription ?? "", m.CategoryName ?? "", m.DisciplineName ?? "", m.UomCode ?? "",
            m.Manufacturer ?? "", m.ManufacturerPartNumber ?? "", m.VendorName ?? "", m.Specification ?? "", m.Model ?? "", m.Size ?? "", m.MaterialGrade ?? "", m.IsStockItem, m.IsCritical, m.IsActive)];
    }

    public async Task<List<MaterialStockLineRow>> StocksAsync(int materialItemId)
        => (await items.GetDetailsAsync(materialItemId))?.Stocks
            .Select(s => new MaterialStockLineRow(s.MaterialStockId, s.WarehouseCode, s.StorageLocationCode ?? "", s.OnHandQuantity, s.ReservedQuantity, s.AvailableQuantity, s.MinimumQuantity, s.MaximumQuantity, s.ReorderPoint,
                s.IsOutOfStock ? "Out of stock" : s.IsLowStock ? "Low" : "OK")).ToList() ?? [];

    public async Task<List<InventoryTransactionRow>> TransactionsAsync(int materialItemId)
        => (await items.GetDetailsAsync(materialItemId))?.RecentTransactions
            .Select(t => new InventoryTransactionRow(t.InventoryTransactionId, t.TransactionDate, t.MaterialCode, t.TransactionType, t.WarehouseCode, t.Quantity,
                (t.ReferenceType ?? "-") + (t.ReferenceId.HasValue ? " " + t.ReferenceId.Value.ToString(CultureInfo.InvariantCulture) : ""), t.EmployeeName ?? "-", t.Reason ?? "-")).ToList() ?? [];

    public async Task<List<MaterialSourceImportRow>> ImportsAsync(int materialItemId)
        => (await items.GetDetailsAsync(materialItemId))?.SourceImports
            .Select(i => new MaterialSourceImportRow(i.MrsImportBatchId, i.SourceFileName, i.ImportDate, i.RawMrsNo ?? "", i.RawSupplier ?? "", i.RawPoNo ?? "")).ToList() ?? [];

    private static ReportChoiceRow Choice(int id, string label) => new(id.ToString(CultureInfo.InvariantCulture), label);

    public async Task<List<ReportChoiceRow>> CategoriesAsync()
        => (await context.MaterialCategories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.MaterialCategoryId, x.Name }).ToListAsync()).Select(x => Choice(x.MaterialCategoryId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => Choice(x.DisciplineId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> UomsAsync()
        => (await context.MaterialUOMs.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.MaterialUOMId, x.Code }).ToListAsync()).Select(x => Choice(x.MaterialUOMId, x.Code)).ToList();

    public async Task<List<ReportChoiceRow>> VendorsAsync()
        => (await context.Vendors.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.VendorId, x.Name }).ToListAsync()).Select(x => Choice(x.VendorId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> WarehousesAsync()
        => (await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync()).Select(x => Choice(x.WarehouseId, x.Code)).ToList();
}
