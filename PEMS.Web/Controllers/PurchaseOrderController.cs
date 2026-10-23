using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§C/§H، Phase 7.5): PurchaseOrder
/// Entity/Service/DTO از قبل کامل بودند - این کنترلر همان اتصال گمشده است، نه یک معماری تازه.
/// </summary>
public class PurchaseOrderController : Controller
{
    private readonly IPurchaseOrderService _service;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _accessControlService;

    public PurchaseOrderController(IPurchaseOrderService service, IPermissionService permissionService,
        ICurrentUserService currentUser, PEMSDbContext context, IAccessControlService accessControlService)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _context = context;
        _accessControlService = accessControlService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "PurchaseOrder", "View"))
            return Forbid();

        ViewData["Title"] = "Purchase Orders";
        ViewData["Section"] = "Contract Management";

        var list = await _service.GetAllAsync();
        return View(list);
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "PurchaseOrder", "View"))
            return Forbid();

        ViewData["Title"] = "Purchase Order Details";
        ViewData["Section"] = "Contract Management";

        var po = await _service.GetByIdAsync(id);
        if (po == null) return NotFound();

        // دکمه‌ی صدور فقط برای Draft و فقط برای دارنده‌ی Approve (نمایش؛ اجرای واقعی در Issue بررسی می‌شود)
        ViewBag.CanIssue = po.Status == PEMS.Domain.Entities.PurchaseOrderStatuses.Draft
            && await _permissionService.HasPermissionAsync(_currentUser.Roles, "PurchaseOrder", "Approve");
        return View(po);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "PurchaseOrder", "Create"))
            return Forbid();

        ViewData["Title"] = "New Purchase Order";
        ViewData["Section"] = "Contract Management";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();
        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();
        ViewBag.Contracts = await _context.Contracts
            .Select(x => new { x.ContractId, x.ContractNo }).ToListAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string poNumber, int projectId, int vendorId, int? contractId,
        string? description, decimal? value)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "PurchaseOrder", "Create"))
            return Forbid();

        int id;
        try
        {
            id = await _service.CreateAsync(poNumber, projectId, vendorId, contractId, description, value);
        }
        catch (InvalidOperationException ex)
        {
            // P0: شماره‌ی تکراری/خالی یا مبلغ نامعتبر - ردِ قابل‌فهم به‌جای HTTP 500 یا ثبت رکورد نامعتبر
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Create));
        }

        // ثبت Allow در AuthorizationAuditLog تا سیاست SoD «Create ↔ Approve» بتواند صادرکننده را
        // با سازنده‌ی همان PO مقایسه کند (همان الگوی ProcurementPackage)
        if (_currentUser.UserId.HasValue)
        {
            await _accessControlService.AuthorizeAsync(new AccessControlRequest
            {
                UserId = _currentUser.UserId.Value,
                ResourceName = "PurchaseOrder",
                ActionName = "Create",
                ResourceId = id.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
        }

        TempData["Message"] = "Purchase Order created as Draft";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// صدور PO (Draft → Issued). مجوز PurchaseOrder.Approve + SoD (سازنده‌ی PO نمی‌تواند خودش
    /// آن را صادر کند). این مجوز در حال حاضر به هیچ نقشی داده نشده و SystemAdmin هم بای‌پس
    /// ندارد (AccessControlService.RequiresExplicitGrant) - تا زمانی که سیاست سازمانی تعیین و
    /// Grant صریح داده نشود، صدور PO جدید عمداً Fail-Closed است.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int id)
    {
        var outcome = await ProtectedActionGuard.EvaluateAsync(
            HttpContext, _permissionService, _accessControlService, _currentUser, "PurchaseOrder", "Approve", id.ToString());
        if (outcome == ProtectedActionOutcome.DeniedBySoD)
        {
            TempData["Error"] = "طبق سیاست تفکیک وظایف، شما نمی‌توانید Purchase Order ای را که خودتان ایجاد کرده‌اید صادر کنید.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (outcome != ProtectedActionOutcome.Allowed) return Forbid();

        try
        {
            await _service.IssueAsync(id);
            TempData["Message"] = "Purchase Order issued";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
