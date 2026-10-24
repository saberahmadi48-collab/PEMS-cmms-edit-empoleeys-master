using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>طبق سند Inventory بخش ۱۰: دفتر کل تراکنش‌ها - فقط خواندنی.</summary>
[Area("Warehouse")]
public class InventoryTransactionController : Controller
{
    private readonly IInventoryTransactionService _service;
    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;

    public InventoryTransactionController(IInventoryTransactionService service, PEMSDbContext context,
        IPermissionService permissionService, ICurrentUserService currentUser)
    {
        _service = service;
        _context = context;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - P1): قبلاً pageSize=30 مستقیم و
    // بدون Normalize پاس داده می‌شد و از QueryString هم پذیرفته نمی‌شد.
    public async Task<IActionResult> Index(int? materialItemId, int? warehouseId, string? transactionType, DateTime? fromDate, DateTime? toDate, int page = 1, int? pageSize = null)
    {
        // P0-09 (D-OPEN-02، تصمیم مالک): ماژول «InventoryTransaction» هیچ ردیف مجوزی ندارد؛ نزدیک‌ترین مجوز موجود
        // MaterialItem.View است (همان الگوی D-04).
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "View"))
            return Forbid();

        ViewData["Title"] = "Inventory Transaction Ledger";
        ViewData["Section"] = "Inventory & Materials";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetLedgerAsync(page, normalizedPageSize, materialItemId, warehouseId, transactionType, fromDate, toDate);

        ViewBag.Warehouses = await _context.Warehouses.Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.TransactionType = transactionType;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(result);
    }
}
