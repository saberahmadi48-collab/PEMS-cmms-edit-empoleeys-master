using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-05: مسیر رسمی قبل از WorkOrder اجرایی. برخلاف
/// StoreRequisition در فاز CMMS-00 (که بدون Permission ساخته شد و بعداً اصلاح شد)، این
/// Controller از همون ابتدا با بررسی مجوز واقعی ساخته می‌شود.
/// </summary>
public class WorkRequestController : Controller
{
    private readonly IWorkRequestService _service;
    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly IFunctionalLocationService _functionalLocationService;

    public WorkRequestController(IWorkRequestService service, PEMSDbContext context,
        IPermissionService permissionService, ICurrentUserService currentUser, IFunctionalLocationService functionalLocationService)
    {
        _service = service;
        _context = context;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _functionalLocationService = functionalLocationService;
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "View"))
            return Forbid();

        var wr = await _service.GetDetailsAsync(id);
        if (wr == null) return NotFound();

        ViewData["HelpKey"] = "WorkRequests.Details";
        ViewData["Title"] = "Work Request Details";
        ViewData["Section"] = "CMMS Integration";

        // Phase 2: ساختار گردش‌کار، مرحله‌ی جاری، اکشن‌های مجاز، شرکت‌کنندگان و تاریخچه از مدل خواندنی مرکزی می‌آید.
        // فقط رکورد Legacy (قبل از مهاجرت، بدون Instance) از مسیر قدیمی نمایش داده می‌شود.
        var actor = await BuildActorAsync();
        wr.Workflow = await _service.GetWorkflowViewAsync(id, actor);
        wr.IsLegacyWithoutWorkflow = wr.Workflow == null && wr.Status != "Draft" && wr.Status is not ("Rejected" or "Cancelled" or "Converted");
        wr.CanConvert = !wr.LinkedWorkOrderId.HasValue
                        && wr.Workflow?.AvailableActions.Any(a => a.ActionType == "Complete" && a.IsAllowed) == true;

        var historyUserIds = wr.Workflow?.HistorySummary.Where(h => h.UserId.HasValue).Select(h => h.UserId!.Value).Distinct().ToList() ?? new List<int>();
        ViewBag.EmployeeNames = historyUserIds.Count == 0 ? new Dictionary<int, string>()
            : await _context.Employees.Where(e => historyUserIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => e.FirstName + " " + e.LastName);

        // LEGACY: «وضعیت بعدی» فقط برای رکورد قدیمی بدون Instance محاسبه می‌شود؛ رکورد تحت گردش‌کار از مدل خواندنی می‌آید
        ViewBag.NextStatus = wr.IsLegacyWithoutWorkflow ? _service.GetLegacyNextStatus(wr.Status) : null;
        ViewBag.CanCreate = await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Create");

        return View(wr);
    }

    // SCRUM-437: Submit and WorkflowAction are action definitions (work-request-submit, work-request-workflow-action) that run the service operations WorkRequest.Submit and
    // WorkRequest.WorkflowAction through the generic handler operation.invoke; the service keeps every rule.

    private static string L(string fa, string en)
        => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? fa : en;

    /// <summary>کاربر اجراکننده از سشن سمت‌سرور (هرگز از ورودی فرم).</summary>
    private async Task<WorkRequestActor> BuildActorAsync()
        => new(await GetCurrentEmployeeIdAsync(), _currentUser.Roles);

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }
}
