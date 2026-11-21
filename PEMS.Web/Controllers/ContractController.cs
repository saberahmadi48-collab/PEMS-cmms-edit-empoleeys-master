using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Web.Security;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Controllers;

public class ContractController : Controller
{
    private readonly IContractService _service;
    private readonly IContractWorkflowService _workflowService;
    private readonly IWorkflowRuntime _runtime;
    private readonly IAccessControlService _accessControlService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;

    public ContractController(
        IContractService service,
        IContractWorkflowService workflowService,
        IWorkflowRuntime runtime,
        IAccessControlService accessControlService,
        IPermissionService permissionService,
        ICurrentUserService currentUser,
        PEMSDbContext context)
    {
        _service = service;
        _workflowService = workflowService;
        _runtime = runtime;
        _accessControlService = accessControlService;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _context = context;
    }

    // P0-09 (D-OPEN-02، تصمیم مالک): صفحات فقط-خواندنی هم مجوز View می‌خواهند.
    private Task<bool> HasAsync(string action) => _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", action);

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await HasAsync("Create")) return Forbid();

        ViewData["Title"] = "New Contract";
        ViewData["Section"] = "Contract Management";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();

        return View();
    }

    // طبق §J (Test Coverage Gap): این کنترلر پیش از این هیچ بررسی مجوزی روی Create نداشت
    // (اصلاً [RequirePermission] هم نبود). بررسی این‌جا به‌صورت درون‌خطی است - دقیقاً همون الگوی
    // ProcurementPackageController - چون [RequirePermission] یک IAsyncAuthorizationFilter است و
    // فقط داخل پایپ‌لاین واقعی MVC اجرا می‌شود، نه وقتی اکشن مستقیماً در تست صدا زده می‌شود.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection = false)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Create"))
            return Forbid();

        var ip = GetClientIp();

        // P0.4 / H-1: بدون هویت احرازشده نه ایجادکننده قابل‌انتساب است نه SoD قابل‌ارزیابی؛ قرارداد ساخته نمی‌شود (قبلاً رویداد بی‌صدا نوشته نمی‌شد).
        if (!_currentUser.UserId.HasValue)
        {
            try { await _accessControlService.RecordDenialAsync(null, "Contracts", "Create", null, ip, AccessDecisionReason.DeniedByPolicy, "شناسه‌ی کاربر احرازهویت‌شده در دسترس نیست."); }
            catch (Exception) { /* شکست ثبت ممیزی هرگز به Allow تبدیل نمی‌شود */ }
            return Forbid();
        }

        // P0.4 / H-1: مجوز آگاه-از-کاربر (Fail-Closed، پیش از هر درج)، تراکنش واحد قرارداد + AuditLog + رویداد «Contracts/Create/Allow» برای همین کاربر، و قفل
        // شماره‌ی قرارداد - همه داخل سرویس (همان قفل و تراکنش مسیر مناقصه). قبلاً رویداد بعد از Commit با AuthorizeAsync نوشته می‌شد، نتیجه‌اش نادیده گرفته
        // می‌شد و استثنای آن قرارداد را بی‌رویداد (SoD کور) می‌گذاشت.
        var result = await _service.CreateWithCreatorEventAsync(contractNo, projectId, vendorId, scope, value, startDate, endDate,
            requiresInspection, _currentUser.UserId.Value, ip);

        if (result.AuthorizationDenied)
            return Forbid();

        if (result.AuthorizationIndeterminate)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Create));
        }

        if (result.OutcomeUnknown)
        {
            // Commit نامطمئن بود و بررسی بعدی هم وضعیت را روشن نکرد: هرگز «ساخته نشد» نمی‌گوییم
            TempData["Error"] = result.Error;
            return Redirect("/p/contracts");
        }

        if (!result.Success)
        {
            // P0: شماره‌ی خالی/تکراری، مبلغ منفی، بازه‌ی تاریخ نامعتبر یا شکست ذخیره (Rollback کامل) - ردِ قابل‌فهم به‌جای ثبت رکورد نامعتبر
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Create));
        }

        if (result.CreatorEvent == ContractCreatorEventStatus.NotRecorded)
            TempData["Error"] = "قرارداد ساخته شد، اما رویداد «ایجادکننده‌ی قرارداد» ثبت نشد (فقط در Provider بدون تراکنش ممکن است)؛ برای ترمیم با مدیر سیستم تماس بگیرید.";
        else if (result.RecoveredAfterUncertainCommit)
            TempData["Message"] = "Contract created successfully (the commit result was uncertain and was confirmed by re-reading the database; no duplicate was created)";
        else
            TempData["Message"] = "Contract created successfully";

        return Redirect($"/p/contract-details?id={result.ContractId}");
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
