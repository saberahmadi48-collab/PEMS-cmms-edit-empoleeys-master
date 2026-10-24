using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// Procurement Package طبق سند Engineering Intelligence Redesign: جایگزین سیستم جدای MR/MRQ.
/// MR، MRQ، Vendor Inquiry، Technical Clarification، Response و Approval History همه از یک
/// صفحه مدیریت می‌شوند.
/// </summary>
public class ProcurementPackageController : Controller
{
    private readonly IProcurementPackageService _service;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IAccessControlService _accessControlService;

    public ProcurementPackageController(IProcurementPackageService service, PEMSDbContext context,
        ICurrentUserService currentUser, IPermissionService permissionService, IAccessControlService accessControlService)
    {
        _service = service;
        _context = context;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _accessControlService = accessControlService;
    }

    // طبق «PEMS — Final Global Repository Audit» بند ۱۹ (P0): این کنترلر هیچ بررسی مجوزی نداشت -
    // هر کاربر احرازهویت‌شده‌ای (صرف‌نظر از نقش) می‌توانست Package بسازد، مرحله را جلو ببرد، و
    // از همه مهم‌تر AddApproval (تاییدیه‌ی خرید) را ثبت کند.
    // طبق «PEMS — Global Grid Hardening» P1: این Grid فیلتری نداشت؛ فقط صفحه‌بندی واقعی اضافه شد.
    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "View"))
            return Forbid();

        ViewData["Title"] = "Procurement Packages";
        ViewData["Section"] = "Engineering Management";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetPagedAsync(page, normalizedPageSize);

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();

        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "View"))
            return Forbid();

        ViewData["Title"] = "Procurement Package Details";
        ViewData["Section"] = "Engineering Management";

        var detail = await _service.GetDetailAsync(id);
        if (detail == null) return NotFound();

        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();

        ViewBag.UnlinkedMRs = await _context.MaterialRequisitions
            .Where(x => x.ProcurementPackageId == null)
            .Select(x => new { x.MRId, x.MRNumber }).ToListAsync();

        // طبق ممیزی نهایی Ownership/UI (§7): این‌جا باید چرخه‌ی Package→Tender→Award→Contract
        // مستقیماً دیده شود - نه این‌که کاربر برای دیدن نتیجه‌ی یک Tender مجبور به کلیک به صفحه‌ی
        // جدا شود. از همان رابطه‌ی موجود Tender.ProcurementPackageId/AwardedBid/ContractId
        // می‌خوانیم - هیچ FK تازه‌ای لازم نیست.
        ViewBag.Tenders = await _context.Tenders
            .Include(x => x.AwardedBid).ThenInclude(x => x!.Vendor)
            .Where(x => x.ProcurementPackageId == id)
            .OrderByDescending(x => x.TenderId)
            .ToListAsync();

        return View(detail);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Create"))
            return Forbid();

        ViewData["Title"] = "New Procurement Package";
        ViewData["Section"] = "Engineering Management";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string packageNumber, int projectId, int? disciplineId, string title, string? description)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Create"))
            return Forbid();

        int id;
        try
        {
            id = await _service.CreateAsync(packageNumber, projectId, disciplineId, title, description);
        }
        catch (InvalidOperationException ex)
        {
            // P0: شماره‌ی خالی/تکراری - ردِ قابل‌فهم به‌جای ثبت رکورد نامعتبر
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Create));
        }

        // طبق ممیزی Package Procurement Contract Finalization (§F): ثبت Allow در
        // AuthorizationAuditLog تا اگر همین کاربر بعداً بخواهد همین Package را خودش تایید کند،
        // بررسی SoD در AddApproval زیر آن را مسدود کند.
        if (_currentUser.UserId.HasValue)
        {
            await _accessControlService.AuthorizeAsync(new AccessControlRequest
            {
                UserId = _currentUser.UserId.Value,
                ResourceName = "ProcurementPackage",
                ActionName = "Create",
                ResourceId = id.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
        }

        TempData["Message"] = "Procurement Package created";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStage(int packageId, string newStage)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Edit"))
            return Forbid();

        try
        {
            await _service.AdvanceStageAsync(packageId, newStage);
        }
        catch (InvalidOperationException ex)
        {
            // Closeout از این مسیر مجاز نیست (فقط از عملیات Closeout با Approve + گیت بازرسی)،
            // و Package بسته/لغوشده قابل‌احیا نیست - پیام دوستانه به‌جای خطای سرور
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkMR(int packageId, int mrId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Edit"))
            return Forbid();

        await _service.LinkExistingMRAsync(packageId, mrId);
        TempData["Message"] = "MR linked to package";
        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCommunication(int packageId, string type, string subject, string body, int? vendorId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Edit"))
            return Forbid();

        await _service.AddCommunicationAsync(packageId, type, subject, body, vendorId, _currentUser.FullName, null);
        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondCommunication(int packageId, int communicationId, string responseText)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Edit"))
            return Forbid();

        await _service.RespondToCommunicationAsync(packageId, communicationId, responseText, _currentUser.FullName);
        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCommunicationStatus(int packageId, int communicationId, string actionStatus)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "Edit"))
            return Forbid();

        await _service.SetCommunicationStatusAsync(packageId, communicationId, actionStatus);
        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddApproval(int packageId, string decision, string? comment)
    {
        // طبق ممیزی §F (قاعده‌ی کسب‌وکار ۶) + Phase 1 hardening: گارد مشترک Fail-Closed - مجوز نقش، هویت کاربر،
        // AuthorizeAsync آگاه-از-کاربر (Scope + SoD + Audit)؛ فقط Allowed صریح عبور می‌دهد و هر رد دیگر Forbid است.
        var outcome = await ProtectedActionGuard.EvaluateAsync(
            HttpContext, _permissionService, _accessControlService, _currentUser, "ProcurementPackage", "Approve", packageId.ToString());
        if (outcome == ProtectedActionOutcome.DeniedBySoD)
        {
            TempData["Error"] = "طبق سیاست تفکیک وظایف، شما نمی‌توانید Package ای را که خودتان ایجاد کرده‌اید تایید کنید.";
            return RedirectToAction(nameof(Details), new { id = packageId });
        }
        if (outcome != ProtectedActionOutcome.Allowed) return Forbid();

        await _service.AddApprovalAsync(packageId, _currentUser.FullName ?? "Unknown", decision, comment);
        TempData["Message"] = $"Approval recorded: {decision}";
        return RedirectToAction(nameof(Details), new { id = packageId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Closeout(int packageId)
    {
        // Closeout عملاً «تایید نهایی» است و با همان مجوز Approve محافظت می‌شود: همان گارد مشترک Fail-Closed
        // (نقش + هویت + Scope + SoD + Audit؛ فقط Allowed صریح) پیش از هر تغییر وضعیت.
        var outcome = await ProtectedActionGuard.EvaluateAsync(
            HttpContext, _permissionService, _accessControlService, _currentUser, "ProcurementPackage", "Approve", packageId.ToString());
        if (outcome == ProtectedActionOutcome.DeniedBySoD)
        {
            TempData["Error"] = "طبق سیاست تفکیک وظایف، شما نمی‌توانید Package ای را که خودتان ایجاد کرده‌اید ببندید.";
            return RedirectToAction(nameof(Details), new { id = packageId });
        }
        if (outcome != ProtectedActionOutcome.Allowed) return Forbid();

        try
        {
            await _service.CloseoutAsync(packageId);
            TempData["Message"] = "Package closed out";
        }
        catch (InvalidOperationException ex)
        {
            // طبق §D/§I قاعده‌ی ۱۱: گیت بازرسی الزامی - پیام خطای دوستانه به‌جای خطای سرور
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = packageId });
    }
}
