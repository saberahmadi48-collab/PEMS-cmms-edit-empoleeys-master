using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>طبق سند Inventory بخش ۲۵-۳۱: داشبورد KPI موجودی.</summary>
[Area("Warehouse")]
public class InventoryController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly IMaterialStockService _materialStockService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;

    public InventoryController(PEMSDbContext context, IMaterialStockService materialStockService,
        IPermissionService permissionService, ICurrentUserService currentUser)
    {
        _context = context;
        _materialStockService = materialStockService;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index()
    {
        // P0-09 (D-OPEN-02، تصمیم مالک): ماژول «Inventory» هیچ ردیف مجوزی ندارد؛ نزدیک‌ترین مجوز موجود
        // MaterialItem.View است (همان الگوی D-04 که تغییر موجودی را به MaterialItem.Edit می‌بندد).
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MaterialItem", "View"))
            return Forbid();

        ViewData["Title"] = "Inventory Dashboard";
        ViewData["Section"] = "Inventory & Materials";

        ViewBag.TotalMaterialItems = await _context.MaterialItems.CountAsync(x => x.IsActive);
        ViewBag.StockItems = await _context.MaterialItems.CountAsync(x => x.IsActive && x.IsStockItem);
        ViewBag.CriticalItems = await _context.MaterialItems.CountAsync(x => x.IsActive && x.IsCritical);
        ViewBag.OutOfStockCount = await _context.MaterialStocks.CountAsync(x => x.IsActive && x.OnHandQuantity <= 0);
        ViewBag.TotalWarehouses = await _context.Warehouses.CountAsync(x => x.IsActive);
        ViewBag.ActiveReservations = await _context.MaterialReservations.CountAsync(x => x.Status == "Active");
        ViewBag.PendingStoreRequisitions = await _context.StoreRequisitions.CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Rejected" && x.Status != "Cancelled");

        ViewBag.LowStockItems = await _materialStockService.GetLowStockAsync(10);

        ViewBag.RecentTransactions = await _context.InventoryTransactions
            .Include(x => x.MaterialItem).Include(x => x.Warehouse)
            .OrderByDescending(x => x.TransactionDate)
            .Take(15)
            .ToListAsync();

        return View();
    }
}
