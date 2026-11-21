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
            return Redirect("/p/maintenance-plans");
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

    // ==================== Helpers ====================

    private Task<List<LookupItem>> JobPlanLookupAsync()
        => _context.JobPlans.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.JobPlanNumber)
            .Select(x => new LookupItem { Id = x.JobPlanId, Code = x.JobPlanNumber, Text = x.Title }).ToListAsync();

    private string T(string key, params object[] args) => _l == null ? key : _l[key, args].Value;
}
