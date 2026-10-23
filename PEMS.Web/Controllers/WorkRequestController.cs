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

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول فیلترشده
    // با ToListAsync (بدون Skip/Take) بارگذاری می‌شد.
    public async Task<IActionResult> Index(string? status, int page = 1, int? pageSize = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "View"))
            return Forbid();

        ViewData["HelpKey"] = "WorkRequests.Index";

        ViewData["Title"] = "Work Requests";
        ViewData["Section"] = "CMMS Integration";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetListAsync(page, normalizedPageSize, status);
        ViewBag.SelectedStatus = status;

        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? equipmentId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Create"))
            return Forbid();

        // پیش‌پر کردن تجهیز وقتی از Digital Equipment Passport آمده (فقط نمایشی؛ ثبت همچنان از همین Action)
        if (equipmentId.HasValue)
        {
            var eq = await _context.Equipments.Where(e => e.EquipmentId == equipmentId.Value)
                .Select(e => new { e.EquipmentId, e.TagNumber, e.Name }).FirstOrDefaultAsync();
            if (eq != null) { ViewBag.PrefillEquipmentId = eq.EquipmentId; ViewBag.PrefillEquipmentText = eq.TagNumber + " — " + eq.Name; }
        }

        ViewData["Title"] = "New Work Request";
        ViewData["Section"] = "CMMS Integration";

        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();
        ViewBag.FunctionalLocations = await _functionalLocationService.GetPickerListAsync();
        // A5: حالت خرابی از کاتالوگ ISO 14224 (نه متن آزاد)
        ViewBag.FailureModes = await _context.FailureModes.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.FailureModeId, Label = x.Code + " - " + x.Name }).ToListAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, string? description, int? equipmentId, int? assetId,
        int? functionalLocationId, string? reportedCondition, string priority, int? suggestedDisciplineId,
        bool isHseRelated = false, bool isEmergency = false, int? riskLikelihood = null, int? riskSeverity = null,
        int? failureModeId = null, string? failureEffect = null, decimal? reportedDowntimeHours = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Create"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        if (!employeeId.HasValue)
        {
            TempData["Error"] = L("کارمند مرتبط با این کاربر پیدا نشد.", "No employee is linked to this user.");
            return RedirectToAction(nameof(Index));
        }

        var (success, error, id) = await _service.CreateAsync(new WorkRequestCreateRequest
        {
            Title = title, Description = description, RequestedById = employeeId.Value, EquipmentId = equipmentId, AssetId = assetId,
            FunctionalLocationId = functionalLocationId, ReportedCondition = reportedCondition, Priority = priority, SuggestedDisciplineId = suggestedDisciplineId,
            IsHseRelated = isHseRelated, IsEmergency = isEmergency, RiskLikelihood = riskLikelihood, RiskSeverity = riskSeverity,
            FailureModeId = failureModeId, FailureEffect = failureEffect, ReportedDowntimeHours = reportedDowntimeHours
        });

        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Create));
        }

        TempData["Message"] = "Work Request created";
        return RedirectToAction(nameof(Details), new { id });
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

    /// <summary>ارسال پیش‌نویس به گردش‌کار مرکزی (شروع Instance از طریق Binding صریح). مجوز سمت‌سرور در سرویس اعمال می‌شود.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "View"))
            return Forbid();

        var (success, error) = await _service.SubmitAsync(id, await BuildActorAsync());
        TempData[success ? "Message" : "Error"] = success ? L("درخواست به گردش‌کار ارسال شد.", "The request was submitted to the workflow.") : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// اجرای اکشن گردش‌کار. actionType از کاربر می‌آید اما «مجاز بودن» را سرور از مدل مرکزی تعیین می‌کند
    /// (شرکت‌کننده + RequiredPermission + SoD + شرط‌ها)؛ نه مخفی‌بودن دکمه و نه هیچ وضعیتی که کلاینت بفرستد مرجع امنیت نیست.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WorkflowAction(int id, string actionType, string? comment)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "View"))
            return Forbid();

        var (success, error) = await _service.ExecuteWorkflowActionAsync(id, actionType, comment, await BuildActorAsync());
        TempData[success ? "Message" : "Error"] = success ? L($"اقدام «{actionType}» انجام شد.", $"Action \"{actionType}\" completed.") : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // --- مسیر Legacy: فقط برای رکوردهای بدون Instance گردش‌کار (سرویس رکوردهای تحت گردش‌کار را رد می‌کند) ---
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(int id, string? reason)
    {
        var wr = await _service.GetDetailsAsync(id);
        if (wr == null) return NotFound();

        var next = _service.GetLegacyNextStatus(wr.Status);
        if (next == null)
        {
            TempData["Error"] = L("این درخواست در وضعیت نهایی است.", "This request is in a final status.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // Submitted->UnderReview فقط نیازمند Create است (خودِ درخواست‌دهنده)؛ UnderReview->Approved
        // و Approved->Planned تصمیم نظارتی‌اند و نیازمند Approve.
        var requiredAction = next is "Approved" or "Planned" ? "Approve" : "Create";
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", requiredAction))
            return Forbid();

        var (success, error) = await _service.AdvanceStatusAsync(id, next, reason, await GetCurrentEmployeeIdAsync());

        TempData[success ? "Message" : "Error"] = success ? L($"وضعیت به {next} تغییر کرد.", $"Status changed to {next}.") : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Approve"))
            return Forbid();

        var (success, error) = await _service.AdvanceStatusAsync(id, "Rejected", reason, await GetCurrentEmployeeIdAsync());
        TempData[success ? "Message" : "Error"] = success ? L("درخواست رد شد.", "The request was rejected.") : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Create"))
            return Forbid();

        var (success, error) = await _service.AdvanceStatusAsync(id, "Cancelled", reason, await GetCurrentEmployeeIdAsync());
        TempData[success ? "Message" : "Error"] = success ? L("درخواست لغو شد.", "The request was cancelled.") : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertToWorkOrder(int id, string workOrderType, int? equipmentId = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkRequest", "Approve"))
            return Forbid();

        var (success, error, workOrderId) = await _service.ConvertToWorkOrderAsync(id, workOrderType, _currentUser.UserName, await BuildActorAsync(), equipmentId);

        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Message"] = L("WorkOrder ساخته شد.", "Work Order created.");
        return RedirectToAction("Details", "WorkOrder", new { id = workOrderId });
    }

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
