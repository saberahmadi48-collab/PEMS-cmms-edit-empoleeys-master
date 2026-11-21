namespace PEMS.Application.Interfaces;

/// <summary>One material of the Material Master list.</summary>
public sealed record MaterialItemRow(int MaterialItemId, string MaterialCode, string Description, string Category, string Discipline, string Uom, string Manufacturer, string Vendor,
    bool IsStockItem, bool IsCritical, bool IsActive, decimal TotalOnHand, decimal TotalAvailable, bool IsLowStock);

/// <summary>The head of one material (one row): everything the details page shows.</summary>
public sealed record MaterialItemDetailRow(int MaterialItemId, string MaterialCode, string MaterialType, string Description, string LongDescription, string Category, string Discipline, string Uom,
    string Manufacturer, string ManufacturerPartNumber, string Vendor, string Specification, string Model, string Size, string MaterialGrade, bool IsStockItem, bool IsCritical, bool IsActive);

/// <summary>One stock line of a material (per warehouse and location).</summary>
public sealed record MaterialStockLineRow(int MaterialStockId, string Warehouse, string Location, decimal OnHand, decimal Reserved, decimal Available, decimal? Minimum, decimal? Maximum, decimal? ReorderPoint, string Status);

/// <summary>An import batch that fed the material.</summary>
public sealed record MaterialSourceImportRow(int MrsImportBatchId, string SourceFile, DateTime ImportDate, string MrsNo, string Supplier, string PoNo);

/// <summary>
/// The Material Master as read operations (SCRUM-656): the same IMaterialItemService calls as MaterialItemController (list, details with stock lines, recent transactions and import
/// sources) and the lookups of the create and edit forms. Create, edit and activate / deactivate are the generic entity actions on the MaterialItem registration; receive, adjust and
/// transfer stock are the operations marked on IMaterialStockService.
/// </summary>
public interface IMaterialItemOperations
{
    /// <summary>The list newest rules as before (search, category, low stock, critical); up to 2000 rows, the grid pages them.</summary>
    [PEMS.Application.Common.PemsOperation("MaterialItem", "List", "MaterialItem.View")]
    Task<List<MaterialItemRow>> ListAsync(string? q = null, int? categoryId = null, bool lowStockOnly = false, bool criticalOnly = false);

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Detail", "MaterialItem.View")]
    Task<List<MaterialItemDetailRow>> DetailAsync(int materialItemId);

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Stocks", "MaterialItem.View")]
    Task<List<MaterialStockLineRow>> StocksAsync(int materialItemId);

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Transactions", "MaterialItem.View")]
    Task<List<InventoryTransactionRow>> TransactionsAsync(int materialItemId);

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Imports", "MaterialItem.View")]
    Task<List<MaterialSourceImportRow>> ImportsAsync(int materialItemId);

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Categories", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> CategoriesAsync();

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Disciplines", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Uoms", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> UomsAsync();

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Vendors", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> VendorsAsync();

    [PEMS.Application.Common.PemsOperation("MaterialItem", "Warehouses", "MaterialItem.View")]
    Task<List<ReportChoiceRow>> WarehousesAsync();
}
