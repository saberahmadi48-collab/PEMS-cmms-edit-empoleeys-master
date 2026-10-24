using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>
/// طبق سند Inventory بخش ۵-۲۵: کاتالوگ متریال - همون الگوی مودال-روی-Index که برای
/// Asset/Instrument استفاده شده.
/// </summary>
[Area("Warehouse")]
public class MaterialItemController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly IMaterialItemService _materialItemService;
    private readonly IMaterialStockService _materialStockService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IBarcodeService _barcodeService;

    public MaterialItemController(
        PEMSDbContext context, IMaterialItemService materialItemService,
        IMaterialStockService materialStockService, ICurrentUserService currentUser,
        IPermissionService permissionService, IBarcodeService barcodeService)
    {
        _context = context;
        _materialItemService = materialItemService;
        _materialStockService = materialStockService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _barcodeService = barcodeService;
    }

    // P0-09 (D-OPEN-02، تصمیم مالک): صفحات فقط-خواندنی هم مجوز View می‌خواهند.
    private Task<bool> CanViewAsync() => _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "View");

    public async Task<IActionResult> Index(string? q, int? categoryId, bool lowStockOnly = false, bool criticalOnly = false, int page = 1)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Material Master";
        ViewData["Section"] = "Inventory & Materials";
        ViewData["HelpKey"] = "MaterialItems.Index";

        var result = await _materialItemService.GetListAsync(page, 25, q, categoryId, lowStockOnly, criticalOnly);

        ViewBag.SearchTerm = q;
        ViewBag.CategoryId = categoryId;
        ViewBag.LowStockOnly = lowStockOnly;
        ViewBag.CriticalOnly = criticalOnly;

        ViewBag.Categories = await _context.MaterialCategories.Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.MaterialCategoryId, x.Name }).ToListAsync();
        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync();
        ViewBag.Uoms = await _context.MaterialUOMs.Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new { x.MaterialUOMId, x.Code }).ToListAsync();
        ViewBag.Vendors = await _context.Vendors
            .OrderBy(x => x.Name).Select(x => new { x.VendorId, x.Name }).ToListAsync();

        return View(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string materialCode, string description, string? materialType, string? longDescription,
        int? categoryId, int? disciplineId, int? uomId, string? manufacturer, string? manufacturerPartNumber,
        int? vendorId, string? specification, string? model, string? size, string? materialGrade,
        bool isStockItem, bool isCritical)
    {
        // P0 / F-04: تعریف ماده‌ی جدید در Material Master مجوز Create ماژول MaterialItem می‌خواهد (همان مجوزی که ReceiveStock دارد)
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Create"))
            return Forbid();

        var (success, error, itemId) = await _materialItemService.CreateAsync(
            materialCode, description, materialType, longDescription, categoryId, disciplineId, uomId,
            manufacturer, manufacturerPartNumber, vendorId, specification, model, size, materialGrade,
            false, false, isStockItem, isCritical, _currentUser.UserName);

        if (!success) { TempData["Error"] = error; return RedirectToAction(nameof(Index)); }

        TempData["Message"] = "متریال ساخته شد.";
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Material Details";
        ViewData["Section"] = "Inventory & Materials";

        var item = await _materialItemService.GetDetailsAsync(id);
        if (item == null) return NotFound();

        ViewBag.Warehouses = await _context.Warehouses.Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync();

        // طبق «PEMS — Enterprise Barcode System» بند «UI»: همون پارتیال مشترک _BarcodePanel
        // که Equipment/Details هم استفاده می‌کند.
        ViewBag.BarcodePanel = new PEMS.Web.Models.BarcodePanelViewModel
        {
            TargetType = "MaterialItem",
            TargetId = id,
            ActiveBarcode = await _barcodeService.GetActiveByTargetAsync("MaterialItem", id),
            CanManage = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Barcode", "Create")
        };

        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int materialItemId, string materialCode, string description, string? materialType, string? longDescription,
        int? categoryId, int? disciplineId, int? uomId, string? manufacturer, string? manufacturerPartNumber,
        int? vendorId, string? specification, string? model, string? size, string? materialGrade,
        bool isStockItem, bool isCritical)
    {
        // P0 / F-04: ویرایش Material Master مجوز Edit ماژول MaterialItem می‌خواهد
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Edit"))
            return Forbid();

        var (success, error) = await _materialItemService.UpdateAsync(
            materialItemId, materialCode, description, materialType, longDescription, categoryId, disciplineId,
            uomId, manufacturer, manufacturerPartNumber, vendorId, specification, model, size, materialGrade,
            false, false, isStockItem, isCritical, _currentUser.UserName);

        TempData[success ? "Message" : "Error"] = success ? "متریال به‌روزرسانی شد." : error;
        return RedirectToAction(nameof(Details), new { id = materialItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        // P0 / F-04: فعال/غیرفعال‌کردن ماده مجوز Edit ماژول MaterialItem می‌خواهد
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Edit"))
            return Forbid();

        await _materialItemService.ToggleActiveAsync(id, _currentUser.UserName);
        TempData["Message"] = "وضعیت متریال به‌روزرسانی شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReceiveStock(int materialItemId, int warehouseId, decimal quantity, string? reason)
    {
        // طبق ممیزی Material Management Step 1 (یافته امنیتی) + Step 3 بخش ۱۴: این مسیر مستقیماً
        // OnHandQuantity را بدون هیچ QC/Disposition ای افزایش می‌دهد - یعنی می‌تواند گیت جدید IRN
        // را کاملاً دور بزند. حداقل مرز امنیتی لازم همین است؛ AdjustStock/TransferStock (اصلاح/
        // جابجایی، نه دریافت مادی جدید) عمداً این‌جا دست‌نخورده مانده‌اند.
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Create"))
            return Forbid();

        var (success, error) = await _materialStockService.ReceiveAsync(
            materialItemId, warehouseId, null, quantity, reason, await GetCurrentEmployeeIdAsync());

        TempData[success ? "Message" : "Error"] = success ? "دریافت کالا ثبت شد." : error;
        return RedirectToAction(nameof(Details), new { id = materialItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustStock(int materialItemId, int warehouseId, decimal newOnHandQuantity, string? reason)
    {
        // P0 / F-04: اصلاح مستقیم موجودی (مقدار مطلق OnHand) پیش از این هیچ بررسی مجوزی نداشت. کمترین مجوز موجود و هم‌معنا:
        // MaterialItem.Edit (نقش‌های هم‌الگو با InventoryMasterData). مجوز اختصاصی «اصلاح موجودی» تصمیم سیاستی جدا و باز است.
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Edit"))
            return Forbid();

        var (success, error) = await _materialStockService.AdjustAsync(
            materialItemId, warehouseId, null, newOnHandQuantity, reason, await GetCurrentEmployeeIdAsync());

        TempData[success ? "Message" : "Error"] = success ? "اصلاح موجودی ثبت شد." : error;
        return RedirectToAction(nameof(Details), new { id = materialItemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferStock(int materialItemId, int fromWarehouseId, int toWarehouseId, decimal quantity, string? reason)
    {
        // P0 / F-04: انتقال موجودی بین انبارها همان مجوز MaterialItem.Edit را می‌خواهد (دلیل بالا)
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "Edit"))
            return Forbid();

        var (success, error) = await _materialStockService.TransferAsync(
            materialItemId, fromWarehouseId, null, toWarehouseId, null, quantity, reason, await GetCurrentEmployeeIdAsync());

        TempData[success ? "Message" : "Error"] = success ? "انتقال بین انبارها ثبت شد." : error;
        return RedirectToAction(nameof(Details), new { id = materialItemId });
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }
}
