using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

public class VendorController : Controller
{
    private readonly IVendorService _service;
    private readonly IVendorRiskService _riskService;
    private readonly IVendorQualificationService _qualificationService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly Persistence.Context.PEMSDbContext _context;

    public VendorController(IVendorService service, IVendorRiskService riskService,
        IVendorQualificationService qualificationService,
        IPermissionService permissionService, ICurrentUserService currentUser,
        Persistence.Context.PEMSDbContext context)
    {
        _service = service;
        _riskService = riskService;
        _qualificationService = qualificationService;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _context = context;
    }

    // طبق «PEMS Phase 2B»: بررسی مجوز مشترک برای اکشن‌های تغییردهنده - داده‌های Permission
    // از قبل برای Vendors seed شده بودند (ContractManager: Create/Edit) ولی هرگز به کنترلر
    // وصل نشده بودند. صفحات فقط-خواندنی (Index/Details) دست‌نخورده می‌مانند.
    private async Task<bool> EnsurePermissionAsync(string action, string friendlyMessage)
    {
        if (await _permissionService.HasPermissionAsync(_currentUser.Roles, "Vendors", action))
            return true;
        TempData["Error"] = friendlyMessage;
        return false;
    }

    // P0-09 (D-OPEN-02، تصمیم مالک): صفحات فقط-خواندنی هم مجوز View می‌خواهند (قبلاً عمداً باز بودند).
    private Task<bool> CanViewAsync() => _permissionService.HasPermissionAsync(_currentUser.Roles, "Vendors", "View");

    // طبق «PEMS — Global Grid Hardening» P1: این Grid فیلتری نداشت؛ فقط صفحه‌بندی واقعی اضافه شد.
    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Vendors";
        ViewData["Section"] = "Contract Management";
        ViewData["HelpKey"] = "Vendor.Index";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetPagedAsync(page, normalizedPageSize);
        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await CanViewAsync()) return Forbid();

        ViewData["Title"] = "Vendor Details";
        ViewData["Section"] = "Contract Management";

        var vendor = await _service.GetByIdAsync(id);
        if (vendor == null) return NotFound();
        return View(vendor);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await EnsurePermissionAsync("Create", "شما مجاز به ثبت وندور جدید نیستید."))
            return RedirectToAction(nameof(Index));

        ViewData["Title"] = "New Vendor";
        ViewData["Section"] = "Contract Management";
        return View(new Vendor());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,VendorCode,Country,ContactName,ContactEmail,ContactPhone")] Vendor vendor)
    {
        if (!await EnsurePermissionAsync("Create", "شما مجاز به ثبت وندور جدید نیستید."))
            return RedirectToAction(nameof(Index));

        // P0 / F-01: [Bind] فقط فیلدهای مجاز فرم را می‌پذیرد و CreateAsync هم مستقل از Binding موجودیت جدید می‌سازد
        // (دفاع در عمق)؛ پس وضعیت صلاحیت/ریسک/فعال‌بودن هرگز از POST گرفته نمی‌شود.
        // خطا هم در ModelState و هم در TempData["Error"] (Toast سراسری Layout) ثبت می‌شود؛ فرم ورودی کاربر حفظ می‌شود.
        if (string.IsNullOrWhiteSpace(vendor.Name))
        {
            ModelState.AddModelError(nameof(Vendor.Name), "نام فروشنده الزامی است.");
            TempData["Error"] = "نام فروشنده الزامی است.";
        }

        if (ModelState.IsValid)
        {
            try
            {
                await _service.CreateAsync(vendor);
                TempData["Message"] = "Vendor created successfully";
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                TempData["Error"] = ex.Message;
            }
        }

        ViewData["Title"] = "New Vendor";
        ViewData["Section"] = "Contract Management";
        return View(vendor);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!await EnsurePermissionAsync("Edit", "شما مجاز به ویرایش این وندور نیستید."))
            return RedirectToAction(nameof(Details), new { id });

        ViewData["Title"] = "Edit Vendor";
        ViewData["Section"] = "Contract Management";

        var vendor = await _service.GetEntityByIdAsync(id);
        if (vendor == null) return NotFound();
        return View(vendor);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Vendor vendor)
    {
        if (!await EnsurePermissionAsync("Edit", "شما مجاز به ویرایش این وندور نیستید."))
            return RedirectToAction(nameof(Details), new { id = vendor.VendorId });

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Edit Vendor";
            ViewData["Section"] = "Contract Management";
            return View(vendor);
        }

        await _service.UpdateAsync(vendor);
        TempData["Message"] = "Vendor updated successfully";
        return RedirectToAction(nameof(Details), new { id = vendor.VendorId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        if (!await EnsurePermissionAsync("Edit", "شما مجاز به تغییر وضعیت فعال‌بودن این وندور نیستید."))
            return RedirectToAction(nameof(Index));

        await _service.ToggleActiveAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecalculateRisk(int id)
    {
        if (!await EnsurePermissionAsync("Edit", "شما مجاز به محاسبه‌ی مجدد ریسک این وندور نیستید."))
            return RedirectToAction(nameof(Details), new { id });

        await _riskService.RecalculateAsync(id);
        TempData["Message"] = "Risk score recalculated";
        return RedirectToAction(nameof(Details), new { id });
    }

    // طبق ممیزی Package Procurement Contract Finalization (§C/§I، Phase 1.1 + Phase 3): بدون این
    // اکشن، هیچ راه UI ای برای Qualified کردن یک فروشنده نبود و TenderService.InviteVendorAsync
    // هرگز برای هیچ فروشنده‌ای راضی نمی‌شد.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetQualification(
        int id, string newStatus, string? category, DateTime? effectiveDate, DateTime? expiryDate,
        string? evidenceReference, string? reason)
    {
        if (!await EnsurePermissionAsync("Edit", "شما مجاز به تغییر وضعیت صلاحیت این وندور نیستید."))
            return RedirectToAction(nameof(Details), new { id });

        var employeeId = _currentUser.UserId.HasValue
            ? await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync()
            : null;

        var result = await _qualificationService.SetQualificationAsync(
            id, newStatus, category, effectiveDate, expiryDate, evidenceReference, reason, employeeId);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "وضعیت صلاحیت فروشنده به‌روزرسانی شد.";

        return RedirectToAction(nameof(Details), new { id });
    }
}
