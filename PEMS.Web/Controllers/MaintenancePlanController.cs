using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;
using PEMS.Web.ViewModels;

namespace PEMS.Web.Controllers;

/// <summary>
/// برنامه‌ی نگهداری (PM Plan). از RCM کلاس‌محور به بعد Plan فقط از یک «تخصیص» (نسخه‌ی استراتژی +
/// تگ) ساخته می‌شود: تجهیز سمت سرور از تخصیص مشتق می‌شود و هرگز equipmentId از کلاینت پذیرفته نمی‌شود
/// (رفع ناهم‌خوانی equipmentId/strategyId و خطای FK با HTTP 500). ورودی نامعتبر = 400 + پیام محلی‌شده.
/// </summary>
public class MaintenancePlanController : Controller
{
    private const string Module = "MaintenancePlan";

    private readonly PEMSDbContext _context;
    private readonly IMaintenancePlanService _maintenancePlanService;
    private readonly IMaintenancePlanCommandService _planCommands;
    private readonly IStrategyAssignmentService _assignments;
    private readonly IPMGenerationService _pmGenerationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IStringLocalizer<SharedResource>? _l;

    private readonly IWorkOrderLifecycleService? _workOrderLifecycle;

    public MaintenancePlanController(PEMSDbContext context,
        IMaintenancePlanService maintenancePlanService, IMaintenancePlanCommandService planCommands,
        IStrategyAssignmentService assignments, IPMGenerationService pmGenerationService,
        ICurrentUserService currentUser, IPermissionService permissionService,
        IWorkOrderLifecycleService? workOrderLifecycle = null, IStringLocalizer<SharedResource>? localizer = null)
    {
        _workOrderLifecycle = workOrderLifecycle;
        _context = context;
        _maintenancePlanService = maintenancePlanService;
        _planCommands = planCommands;
        _assignments = assignments;
        _pmGenerationService = pmGenerationService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _l = localizer;
    }

    private Task<bool> Can(string action) => _permissionService.HasPermissionAsync(_currentUser.Roles, Module, action);

    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        if (!await Can("View")) return Forbid();

        ViewData["Title"] = "Maintenance Plans";
        ViewData["HelpKey"] = "MaintenancePlans.Index";
        ViewData["Section"] = "CMMS Integration";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _maintenancePlanService.GetListAsync(page, normalizedPageSize);

        return View(result);
    }

    // ==================== Create (from an assignment) ====================

    /// <summary>
    /// Create?assignmentId=N  → فرم با تگ فقط‌خواندنی.
    /// Create?strategyId=N&amp;equipmentId=M → تخصیص متناظر پیدا می‌شود (تگ از تخصیص می‌آید).
    /// بدون پارامتر → انتخاب از میان تخصیص‌های مجاز (برچسب متمایز: تگ · نوع · حالت خرابی · نسخه).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Create(int? assignmentId, int? strategyId, int? equipmentId)
    {
        if (!await Can("Create")) return Forbid();

        ViewData["Title"] = "New Maintenance Plan";
        ViewData["Section"] = "CMMS Integration";

        var vm = new PlanFormViewModel { JobPlans = await JobPlanLookupAsync() };

        if (!assignmentId.HasValue && strategyId.HasValue && equipmentId.HasValue)
        {
            var match = await _assignments.GetPlanEligibleAssignmentsAsync(equipmentId, strategyId);
            assignmentId = match.FirstOrDefault()?.AssignmentId;
        }

        if (assignmentId.HasValue)
        {
            vm.Context = await _planCommands.GetFormContextAsync(assignmentId.Value);
            if (vm.Context == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                vm.Errors.Add(T("Rcm_Err_AssignmentNotFound"));
            }
            else
            {
                vm.Input = new PlanInputDto
                {
                    MaintenanceStrategyAssignmentId = vm.Context.AssignmentId,
                    Title = vm.Context.SuggestedTitle,
                    TriggerType = vm.Context.RecommendedTriggerType,
                    IntervalDays = vm.Context.SuggestedIntervalDays,
                    ScheduleMode = RcmCodes.ScheduleMode.Fixed
                };
            }
        }
        else
        {
            vm.Choices = await _assignments.GetPlanEligibleAssignmentsAsync(equipmentId, strategyId);
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PlanInputDto input)
    {
        if (!await Can("Create")) return Forbid();

        var result = await _planCommands.CreateAsync(input, await _context.ResolveStrategyActorAsync(_currentUser));
        if (result.Success)
        {
            TempData["Message"] = T("Rcm_Info_PlanCreated");
            return RedirectToAction(nameof(Index));
        }

        var vm = new PlanFormViewModel
        {
            Input = input,
            JobPlans = await JobPlanLookupAsync(),
            Context = await _planCommands.GetFormContextAsync(input.MaintenanceStrategyAssignmentId)
        };
        foreach (var e in result.Errors)
        {
            var msg = _l.Translate(e);
            vm.Errors.Add(msg);
            if (!string.IsNullOrEmpty(e.Field)) ModelState.AddModelError(e.Field, msg);
        }

        // تخصیص نامعتبر/ناموجود = 404؛ سایر ورودی‌های نامعتبر = 400 (هرگز 500)
        Response.StatusCode = vm.Context == null ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        ViewData["Title"] = "New Maintenance Plan";
        ViewData["Section"] = "CMMS Integration";
        return View(vm);
    }

    // ==============================================================================
    // اگر تریگر مبتنی بر Meter باشد، اجرا با ثبت قرائت فعلی Meter انجام می‌شود، نه فقط تاریخ.
    // ==============================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordMeterReading(int planId, decimal currentReading)
    {
        if (!await Can("Edit")) return Forbid();

        var plan = await _context.MaintenancePlans.FirstOrDefaultAsync(x => x.MaintenancePlanId == planId);
        if (plan == null) return NotFound();

        if (plan.TriggerType is "Meter" or "Both" && plan.MeterInterval.HasValue)
        {
            plan.LastExecutionMeterReading = currentReading;
            plan.NextDueMeterReading = currentReading + plan.MeterInterval.Value;
        }

        await _context.SaveChangesAsync();

        TempData["Message"] = "قرائت Meter ثبت شد.";
        return RedirectToAction(nameof(Index));
    }

    // منطق واقعی به PMGenerationService منتقل شد و Idempotent است - کلیک تکراری WorkOrder تکراری نمی‌سازد.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateWorkOrder(int planId)
    {
        if (!await Can("Edit")) return Forbid();

        var workOrderNumber = await _pmGenerationService.GenerateForPlanAsync(planId);

        if (workOrderNumber == null)
        {
            TempData["Error"] = "این Plan یا وجود ندارد یا از قبل یک Work Order فعال (تکمیل‌نشده) دارد - برای جلوگیری از تکرار، Work Order جدیدی ساخته نشد.";
            return RedirectToAction(nameof(Index));
        }

        await GovernGeneratedAsync();
        var workOrderId = await _context.WorkOrders
            .Where(x => x.WorkOrderNumber == workOrderNumber)
            .Select(x => x.WorkOrderId)
            .FirstOrDefaultAsync();

        TempData["Message"] = $"Work order {workOrderNumber} generated from plan";
        return RedirectToAction("Details", "WorkOrder", new { id = workOrderId });
    }

    // اجرای دستی (انسان‌محور) روی همه‌ی Plan های سررسیدرسیده - Idempotent و بدون طوفان جبران.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateDueWorkOrders()
    {
        if (!await Can("Edit")) return Forbid();

        var result = await _pmGenerationService.GenerateDueWorkOrdersAsync();
        await GovernGeneratedAsync();

        TempData["Message"] = $"{result.WorkOrdersGenerated} Work Order جدید ساخته شد؛ {result.PlansSkippedAlreadyHasOpenWorkOrder} مورد به‌دلیل داشتن Work Order فعال از قبل، رد شد.";
        return RedirectToAction(nameof(Index));
    }

    // Phase 3B: Work Order تولیدشده باید حاکمیتی شود (Snapshot مبدأ از کاربر اجراکننده + شروع چرخه‌عمر).
    private async Task GovernGeneratedAsync()
    {
        if (_workOrderLifecycle == null) return;
        var employeeId = _currentUser.UserId.HasValue
            ? await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync() : null;
        var (ok, error, _) = await _workOrderLifecycle.GovernPendingNewAsync(new PEMS.Application.DTOs.WorkOrderActor(employeeId, _currentUser.Roles));
        if (!ok) TempData["Error"] = error;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int planId)
    {
        if (!await Can("Edit")) return Forbid();

        var plan = await _context.MaintenancePlans.FirstOrDefaultAsync(x => x.MaintenancePlanId == planId);
        if (plan == null) return NotFound();

        plan.IsActive = false;
        await _context.SaveChangesAsync();

        TempData["Message"] = "Plan deactivated";
        return RedirectToAction(nameof(Index));
    }

    // ==================== Helpers ====================

    private Task<List<LookupItem>> JobPlanLookupAsync()
        => _context.JobPlans.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.JobPlanNumber)
            .Select(x => new LookupItem { Id = x.JobPlanId, Code = x.JobPlanNumber, Text = x.Title }).ToListAsync();

    private string T(string key, params object[] args) => _l == null ? key : _l[key, args].Value;
}
