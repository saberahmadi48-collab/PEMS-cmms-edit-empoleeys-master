using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>
/// طبق سند Inventory بخش ۱۴: درخواست داخلی برداشت از انبار - Draft→Submitted→Approved→
/// Reserved→Issued→Closed. اسم عمداً StoreRequisition است، نه MaterialRequisition موجود
/// (که برای خرید از Vendor است، نه برداشت داخلی).
/// </summary>
[Area("Warehouse")]
public class StoreRequisitionController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly IStoreRequisitionLifecycleService _lifecycleService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly INumberingService _numberingService;

    private static readonly Dictionary<string, string> NextStatusMap = new()
    {
        ["Draft"] = "Submitted",
        ["Submitted"] = "Approved",
        ["Approved"] = "Reserved",
        ["Reserved"] = "Issued",
        ["PartiallyIssued"] = "Issued",
        ["Issued"] = "Closed",
    };

    // طبق CMMS Production Baseline v1 بند P0-02: تصمیم‌گیری اینکه کدام گذار وضعیت نیازمند
    // مجوز "Approve" است (نه فقط "Create") - Submitted صرفاً ارسال درخواست است، بقیه تصمیمات
    // انباردار/سرپرست هستند.
    private static readonly HashSet<string> ApprovalGatedTransitions = new(StringComparer.Ordinal)
    {
        "Approved", "Reserved", "Issued"
    };

    public StoreRequisitionController(PEMSDbContext context, IStoreRequisitionLifecycleService lifecycleService,
        ICurrentUserService currentUser, IPermissionService permissionService, INumberingService numberingService)
    {
        _context = context;
        _lifecycleService = lifecycleService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _numberingService = numberingService;
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول فیلترشده با
    // ToListAsync (بدون Skip/Take) بارگذاری می‌شد.
    public async Task<IActionResult> Index(string? status, int page = 1, int? pageSize = null)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Store Requisitions";
        ViewData["Section"] = "Inventory & Materials";
        ViewData["HelpKey"] = "StoreRequisition.Index";

        var query = _context.StoreRequisitions.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync();

        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);

        var requisitions = await query
            .OrderByDescending(x => x.RequestedDate)
            .ThenByDescending(x => x.StoreRequisitionId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => new StoreRequisitionListDto
            {
                StoreRequisitionId = x.StoreRequisitionId,
                RequisitionNumber = x.RequisitionNumber,
                Status = x.Status,
                WarehouseCode = x.Warehouse!.Code,
                RequestedByName = x.RequestedBy!.FirstName + " " + x.RequestedBy!.LastName,
                RequestedDate = x.RequestedDate,
                Purpose = x.Purpose
            })
            .ToListAsync();

        ViewBag.Warehouses = await _context.Warehouses.Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync();
        ViewBag.SelectedStatus = status;

        return View(new PagedResult<StoreRequisitionListDto>
        {
            Items = requisitions,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int warehouseId, string? purpose, int? workOrderId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "StoreRequisition", "Create"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        if (!employeeId.HasValue)
        {
            TempData["Error"] = "کارمند مرتبط با این کاربر پیدا نشد.";
            return RedirectToAction(nameof(Index));
        }

        var requisition = new StoreRequisition
        {
            RequisitionNumber = await _numberingService.GetNextNumberAsync("StoreRequisition"),
            Status = "Draft",
            WarehouseId = warehouseId,
            WorkOrderId = workOrderId,
            RequestedById = employeeId.Value,
            RequestedDate = DateTime.Now,
            Purpose = purpose,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.StoreRequisitions.AddAsync(requisition);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = requisition.StoreRequisitionId });
    }

    // P0-09 (D-OPEN-02، تصمیم مالک): صفحات فقط-خواندنی هم مجوز View می‌خواهند.
    private Task<bool> CanViewAsync() => _permissionService.HasPermissionAsync(_currentUser.Roles, "StoreRequisition", "View");

    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Store Requisition Details";
        ViewData["Section"] = "Inventory & Materials";

        var requisition = await _context.StoreRequisitions
            .Include(x => x.Warehouse).Include(x => x.RequestedBy).Include(x => x.ApprovedBy).Include(x => x.WorkOrder)
            .Include(x => x.Lines).ThenInclude(x => x.MaterialItem)
            .FirstOrDefaultAsync(x => x.StoreRequisitionId == id);

        if (requisition == null) return NotFound();

        ViewBag.MaterialItems = await _context.MaterialItems.Where(x => x.IsActive && x.IsStockItem)
            .OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).ToListAsync();

        ViewBag.StatusHistory = await _context.StoreRequisitionStatusHistories.Include(x => x.ChangedBy)
            .Where(x => x.StoreRequisitionId == id).OrderByDescending(x => x.ChangedDate).ToListAsync();

        ViewBag.NextStatus = NextStatusMap.TryGetValue(requisition.Status, out var next) ? next : null;

        // فقط برای نمایش/پنهان‌سازی دکمه‌ها؛ اجرا همیشه دوباره در سرویس بررسی می‌شود
        var actor = await GetActorAsync();
        var can = new Dictionary<string, bool>();
        foreach (var a in StoreRequisitionActions.Sensitive) can[a] = await _lifecycleService.IsAllowedAsync(actor, a);
        ViewBag.Can = can;
        ViewBag.IsRequester = actor.EmployeeId.HasValue && actor.EmployeeId == requisition.RequestedById;

        return View(requisition);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLine(int storeRequisitionId, int materialItemId, decimal requestedQuantity)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "StoreRequisition", "Create"))
            return Forbid();

        var requisition = await _context.StoreRequisitions.FirstOrDefaultAsync(x => x.StoreRequisitionId == storeRequisitionId);
        if (requisition == null) return NotFound();

        if (requisition.Status != "Draft")
        {
            TempData["Error"] = "فقط در وضعیت Draft می‌توان خط اضافه کرد.";
            return RedirectToAction(nameof(Details), new { id = storeRequisitionId });
        }

        if (requestedQuantity <= 0)
        {
            TempData["Error"] = "مقدار درخواستی باید بزرگ‌تر از صفر باشد.";
            return RedirectToAction(nameof(Details), new { id = storeRequisitionId });
        }

        await _context.StoreRequisitionLines.AddAsync(new StoreRequisitionLine
        {
            StoreRequisitionId = storeRequisitionId,
            MaterialItemId = materialItemId,
            RequestedQuantity = requestedQuantity,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = storeRequisitionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(int id, string? reason, string? requestKey)
    {
        var requisition = await _context.StoreRequisitions.FirstOrDefaultAsync(x => x.StoreRequisitionId == id);
        if (requisition == null) return NotFound();

        if (!NextStatusMap.TryGetValue(requisition.Status, out var next))
        {
            TempData["Error"] = "این درخواست در وضعیت نهایی است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // مجوز/هویت/SoD/جدول گذار همگی در سرویس اعمال می‌شوند (Controller فقط نمایش و مسیردهی است)
        var (success, error) = await _lifecycleService.AdvanceStatusAsync(id, next, reason, await GetActorAsync(), requestKey);

        TempData[success ? "Message" : "Error"] = success ? $"وضعیت به {next} تغییر کرد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>تعیین مقدار تأییدشده‌ی هر خط (فقط Submitted، مجوز Approve)؛ قواعد در سرویس اعمال می‌شود.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetApprovedQuantities(int id, Dictionary<int, decimal> approved, string? requestKey)
    {
        var (success, error) = await _lifecycleService.SetApprovedQuantitiesAsync(id, approved ?? new(), await GetActorAsync(), requestKey);
        TempData[success ? "Message" : "Error"] = success ? "مقدار تأییدشده ثبت شد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>تحویل کامل یا بخشی (مجوز Approve، همان گیت قبلیِ Issued). qty خالی = تحویل تمام باقی‌مانده‌ی رزرو.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int id, Dictionary<int, decimal> qty, string? reason, string? requestKey)
    {
        var lines = (qty ?? new()).Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        var (success, error) = await _lifecycleService.IssueAsync(id, lines.Count == 0 ? null : lines, reason, await GetActorAsync(), requestKey);
        TempData[success ? "Message" : "Error"] = success ? "تحویل ثبت شد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>بستن با کسری پس از تحویل بخشی: باقی‌مانده‌ی رزرو آزاد می‌شود.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseShort(int id, string? reason)
    {
        var (success, error) = await _lifecycleService.AdvanceStatusAsync(id, "Closed", reason, await GetActorAsync());
        TempData[success ? "Message" : "Error"] = success ? "درخواست با کسری بسته شد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        var (success, error) = await _lifecycleService.AdvanceStatusAsync(id, "Rejected", reason, await GetActorAsync());
        TempData[success ? "Message" : "Error"] = success ? "درخواست رد شد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        var (success, error) = await _lifecycleService.AdvanceStatusAsync(id, "Cancelled", reason, await GetActorAsync());
        TempData[success ? "Message" : "Error"] = success ? "درخواست لغو شد." : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<StoreRequisitionActor> GetActorAsync() =>
        new(await GetCurrentEmployeeIdAsync(), (_currentUser.Roles ?? new List<string>()).ToList());

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }
}
