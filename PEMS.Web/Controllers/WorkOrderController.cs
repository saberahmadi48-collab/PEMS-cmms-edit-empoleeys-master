using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق درخواست: تکمیل بخش‌های CMMS - Work Order قلب واقعی CMMS است (کار نگهداری واقعی که
/// انجام می‌شود). گردش‌کار: Open → Assigned → InProgress → Completed → Closed. طبق درخواست
/// بازبینی: یک WorkOrder می‌تواند نیاز به ثبت نظر چند دیسیپلین تعمیراتی داشته باشد
/// (Multi-Craft Coordination، EN 13460) قبل از این‌که Completed بشه.
/// </summary>
public class WorkOrderController : Controller
{
    private static readonly string[] StatusFlow = { "Open", "Assigned", "InProgress", "Completed", "Closed" };

    // Canonical CMMS values. These values must match the Create/Edit UI and WorkOrder domain contract.
    private static readonly string[] ValidWorkOrderTypes = { "Preventive", "Corrective", "Emergency", "Predictive" };
    private static readonly string[] ValidPriorities = { "Low", "Medium", "High", "Critical" };

    private readonly PEMSDbContext _context;
    private readonly IWorkOrderReviewService _reviewService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IWorkOrderLaborService _laborService;
    private readonly IWorkOrderLifecycleService _lifecycleService;
    private readonly ICrewService _crewService;
    private readonly INumberingService _numberingService;
    private readonly IMaterialStockService _materialStockService;
    private readonly IWorkOrderSectionGuard? _sectionGuard;

    // sectionGuard اختیاری است تا سازنده‌ی ۹-پارامتری قبلی (تست‌ها) سازگار بماند؛ DI همیشه آن را تأمین می‌کند. بدون آن، تمام جهش‌های بخش‌های فرزند Fail-Closed رد می‌شوند.
    public WorkOrderController(PEMSDbContext context, IWorkOrderReviewService reviewService,
        ICurrentUserService currentUser, IPermissionService permissionService, IWorkOrderLaborService laborService,
        IWorkOrderLifecycleService lifecycleService, ICrewService crewService, INumberingService numberingService,
        IMaterialStockService materialStockService, IWorkOrderSectionGuard? sectionGuard = null)
    {
        _sectionGuard = sectionGuard;
        _context = context;
        _reviewService = reviewService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _laborService = laborService;
        _lifecycleService = lifecycleService;
        _crewService = crewService;
        _numberingService = numberingService;
        _materialStockService = materialStockService;
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول فیلترشده
    // (با ۳ Include) با ToListAsync (بدون Skip/Take) بارگذاری می‌شد؛ شمارنده‌های Dashboard هم
    // با یک بارگذاری کامل دومِ کل جدول (allActive) محاسبه می‌شدند. حالا Grid با SQL واقعی
    // صفحه‌بندی می‌شود و شمارنده‌ها CountAsync هستند - با همون Scope سراسری قبلی (مستقل از
    // فیلتر status/equipmentId/assignedToMe).
    public async Task<IActionResult> Index(string? status, int? equipmentId, bool assignedToMe = false, int page = 1, int? pageSize = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "View"))
            return Forbid();

        ViewData["Title"] = "Work Orders";
        ViewData["HelpKey"] = "WorkOrders.Index";
        ViewData["Section"] = "CMMS Integration";

        var query = _context.WorkOrders.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (equipmentId.HasValue) query = query.Where(x => x.EquipmentId == equipmentId.Value);

        // طبق PEMS-UI-04: از همون رابطه‌ی موجود و قابل‌اتکای AssignedEmployeeId استفاده می‌شود -
        // نه یک معماری انتساب جدید.
        if (assignedToMe)
        {
            var employeeId = await GetCurrentEmployeeIdAsync();
            query = query.Where(x => x.AssignedEmployeeId == employeeId);
        }

        var totalCount = await query.CountAsync();

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var normalizedPage = page < 1 ? 1 : page;

        // طبق A-3: سه Include قبلی (Equipment/AssignedTeam/AssignedEmployee) حذف شدند - فقط
        // یک فیلد اسکالر از هرکدام لازم بود که مستقیم در Select قابل‌دریافت است.
        var workOrders = await query
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.WorkOrderId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => new WorkOrderListDto
            {
                WorkOrderId = x.WorkOrderId,
                WorkOrderNumber = x.WorkOrderNumber,
                EquipmentId = x.EquipmentId,
                EquipmentTagNumber = x.Equipment != null ? x.Equipment.TagNumber : null,
                WorkOrderType = x.WorkOrderType,
                Priority = x.Priority,
                AssignedTeamName = x.AssignedTeam != null ? x.AssignedTeam.Name : null,
                AssignedEmployeeName = x.AssignedEmployee != null ? x.AssignedEmployee.FirstName + " " + x.AssignedEmployee.LastName : null,
                AssignedTo = x.AssignedTo,
                Status = x.Status,
                CreatedDate = x.CreatedDate
            })
            .ToListAsync();

        ViewBag.Equipments = await _context.Equipments.Where(x => x.IsActive)
            .Select(x => new { x.EquipmentId, x.TagNumber, x.Name }).ToListAsync();

        ViewBag.SelectedStatus = status;
        ViewBag.SelectedEquipmentId = equipmentId;

        if (equipmentId.HasValue)
        {
            ViewBag.SelectedEquipmentLabel = await _context.Equipments
                .Where(x => x.EquipmentId == equipmentId.Value)
                .Select(x => x.TagNumber + " - " + x.Name)
                .FirstOrDefaultAsync();
        }

        var activeQuery = _context.WorkOrders.Where(x => x.IsActive);
        ViewBag.OpenCount = await activeQuery.CountAsync(x => x.Status == "Open");
        ViewBag.InProgressCount = await activeQuery.CountAsync(x => x.Status == "InProgress" || x.Status == "Assigned");
        ViewBag.CompletedCount = await activeQuery.CountAsync(x => x.Status == "Completed" || x.Status == "Closed");
        ViewBag.CriticalOpenCount = await activeQuery.CountAsync(x => x.Priority == "Critical" && x.Status != "Completed" && x.Status != "Closed");
        ViewBag.AssignedToMe = assignedToMe;

        return View(new PagedResult<WorkOrderListDto>
        {
            Items = workOrders,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? equipmentId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Create"))
            return Forbid();

        ViewData["Title"] = "New Work Order";
        ViewData["Section"] = "CMMS Integration";

        ViewBag.PreselectedEquipmentId = equipmentId;

        if (equipmentId.HasValue)
        {
            ViewBag.PreselectedEquipmentLabel = await _context.Equipments
                .Where(x => x.EquipmentId == equipmentId.Value)
                .Select(x => x.TagNumber + " - " + x.Name)
                .FirstOrDefaultAsync();
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int equipmentId, string workOrderType, string priority,
        string? description)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Create"))
            return Forbid();

        // Server-side validation must use exactly the same canonical values exposed by the UI.
        if (equipmentId <= 0 || !await _context.Equipments.AnyAsync(x => x.EquipmentId == equipmentId && x.IsActive))
        {
            TempData["Error"] = "تجهیز انتخاب‌شده نامعتبره.";
            return RedirectToAction(nameof(Create));
        }

        workOrderType = workOrderType?.Trim() ?? string.Empty;
        priority = priority?.Trim() ?? string.Empty;

        if (!ValidWorkOrderTypes.Contains(workOrderType, StringComparer.Ordinal))
        {
            TempData["Error"] = "نوع WorkOrder نامعتبره.";
            return RedirectToAction(nameof(Create));
        }

        if (!ValidPriorities.Contains(priority, StringComparer.Ordinal))
        {
            TempData["Error"] = "اولویت نامعتبره.";
            return RedirectToAction(nameof(Create));
        }

        // Phase 3B: Work Order جدید همیشه حاکمیتی است (Snapshot دیسیپلین/دامنه‌ی مبدأ + Instance چرخه‌عمر با Binding صریح، یک تراکنش).
        // شماره‌گذاری از NumberingService (اتمیک، P0-04).
        var workOrderNumber = await _numberingService.GetNextNumberAsync("WorkOrder");
        var (created, createError, newId) = await _lifecycleService.CreateGovernedAsync(new WorkOrderCreateRequest
        {
            WorkOrderNumber = workOrderNumber, EquipmentId = equipmentId, WorkOrderType = workOrderType, Priority = priority, Description = description
        }, await BuildActorAsync());

        if (!created)
        {
            TempData["Error"] = createError;
            return RedirectToAction(nameof(Create));
        }

        TempData["Message"] = L($"Work Order {workOrderNumber} ایجاد شد", $"Work order {workOrderNumber} created");
        return RedirectToAction(nameof(Details), new { id = newId });
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "View"))
            return Forbid();

        ViewData["Title"] = "Work Order Details";
        ViewData["Section"] = "CMMS Integration";
        ViewData["HelpKey"] = "WorkOrders.Details";

        var workOrder = await _context.WorkOrders
            .Include(x => x.Equipment)
            .Include(x => x.MaintenancePlan)
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedEmployee)
            .FirstOrDefaultAsync(x => x.WorkOrderId == id);

        if (workOrder == null) return NotFound();

        // هویت تجهیز (تگ، نام، کلاس ISO 14224، ناحیه/واحد، بحرانی‌بودن) همه‌جا یکسان نمایش داده می‌شود
        if (HttpContext?.RequestServices?.GetService(typeof(IStrategyAssignmentService)) is IStrategyAssignmentService identityService)
            ViewBag.EquipmentIdentity = await identityService.GetEquipmentIdentityAsync(workOrder.EquipmentId);

        // طبق قاعده‌ی دیسیپلین در AssignExecutorAsync/AddLaborEntryAsync (Phase 6D): وقتی دیسیپلین مسئول
        // مشخص است، فقط تیم/کارمند همان دیسیپلین نمایش داده می‌شود تا انتخابگر با قاعده‌ی واقعی سرور
        // هم‌خوان باشد (پیش از این هر تیم/کارمند فعالی نمایش داده می‌شد و فقط بعد از Submit رد می‌شد).
        var teamsQuery = _context.Teams.Where(x => x.IsActive);
        var employeesQuery = _context.Employees.Where(x => x.IsActive);
        if (workOrder.ResponsibleDisciplineId.HasValue)
        {
            teamsQuery = teamsQuery.Where(x => x.MaintenanceGroup!.DisciplineId == workOrder.ResponsibleDisciplineId);
            employeesQuery = employeesQuery.Where(x => x.DisciplineId == workOrder.ResponsibleDisciplineId);
        }
        ViewBag.Teams = await teamsQuery.Select(x => new { x.TeamId, x.Name }).ToListAsync();
        ViewBag.SparesUsed = await _context.MaintenanceSpareUsages.Include(x => x.MaterialItem)
            .Where(x => x.WorkOrderId == id && x.IsActive).ToListAsync();
        ViewBag.MaterialItems = await _context.MaterialItems.Where(x => x.IsActive && x.IsStockItem)
            .Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).ToListAsync();
        // Phase 3B: ساختار گردش‌کار/اقدام‌ها/شرکت‌کنندگان/تاریخچه از مدل خواندنی مرکزی؛ فقط Work Order Legacy از مسیر قدیمی نمایش داده می‌شود
        var actor = await BuildActorAsync();
        ViewBag.Workflow = await _lifecycleService.GetWorkflowViewAsync(id, actor);
        ViewBag.IsLegacy = ViewBag.Workflow == null;
        var historyUserIds = ((PEMS.WorkflowPlatform.Interfaces.WorkflowRuntimeViewModel?)ViewBag.Workflow)?.HistorySummary.Where(h => h.UserId.HasValue).Select(h => h.UserId!.Value).Distinct().ToList() ?? new List<int>();
        ViewBag.EmployeeNames = historyUserIds.Count == 0 ? new Dictionary<int, string>()
            : await _context.Employees.Where(e => historyUserIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => e.FirstName + " " + e.LastName);
        ViewBag.OriginatingDisciplineName = workOrder.OriginatingDisciplineId.HasValue
            ? await _context.Disciplines.Where(d => d.DisciplineId == workOrder.OriginatingDisciplineId).Select(d => d.Name).FirstOrDefaultAsync() : null;
        ViewBag.ResponsibleDisciplineName = workOrder.ResponsibleDisciplineId.HasValue
            ? await _context.Disciplines.Where(d => d.DisciplineId == workOrder.ResponsibleDisciplineId).Select(d => d.Name).FirstOrDefaultAsync() : null;
        ViewBag.NextStatus = ViewBag.IsLegacy ? GetNextStatus(workOrder.Status) : null;
        ViewBag.LaborEntries = await _context.WorkOrderLabors.Include(x => x.Employee).Include(x => x.Team)
            .Where(x => x.WorkOrderId == id && x.IsActive).OrderByDescending(x => x.WorkDate).ToListAsync();
        ViewBag.TotalLaborCost = await _laborService.GetTotalLaborCostAsync(id);
        ViewBag.SpareCost = (await _context.MaintenanceSpareUsages.Where(x => x.WorkOrderId == id && x.IsActive)
            .ToListAsync()).Sum(x => x.TotalCost ?? 0);
        ViewBag.Employees = await employeesQuery
            .Select(x => new { x.EmployeeId, FullName = x.FirstName + " " + x.LastName }).ToListAsync();
        ViewBag.ReviewStatus = await _reviewService.GetDisciplineReviewStatusAsync(id);
        ViewBag.HasPendingReview = await _reviewService.HasPendingDisciplineReviewAsync(id);
        ViewBag.MyDisciplineId = await GetCurrentEmployeeDisciplineIdAsync();
        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();
        ViewBag.Findings = await _context.WorkOrderDisciplineFindings.Include(x => x.Discipline)
            .Where(x => x.WorkOrderId == id).OrderByDescending(x => x.RespondedDate).ToListAsync();
        ViewBag.StatusHistory = await _context.WorkOrderStatusHistories.Include(x => x.ChangedBy)
            .Where(x => x.WorkOrderId == id).OrderByDescending(x => x.ChangedDate).ToListAsync();
        ViewBag.RequiredSkills = await _context.RequiredSkills.Include(x => x.Skill)
            .Where(x => x.WorkOrderId == id).ToListAsync();
        ViewBag.AllSkills = await _context.Skills.Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.SkillId, x.Name }).ToListAsync();
        ViewBag.Crews = await _context.Crews.Include(x => x.Members).ThenInclude(m => m.Employee)
            .Where(x => x.WorkOrderId == id && x.IsActive).ToListAsync();

        ViewBag.RequiredMaterials = await _context.WorkOrderMaterials.Include(x => x.MaterialItem)
            .Where(x => x.WorkOrderId == id).ToListAsync();
        // SCRUM-472: supply code, sourced quantities and the warehouse task of the required materials
        ViewBag.WarehouseIntake = HttpContext?.RequestServices?.GetService(typeof(IWarehouseIntakeService)) is IWarehouseIntakeService warehouseIntake
            ? await warehouseIntake.GetAsync(id) : null;

        // H25/H26: تنها منبع حقیقت «کدام بخش در کدام فاز چه قابلیتی دارد»؛ Razor فقط همین را رندر می‌کند و POSTها همین را اعمال می‌کنند
        ViewBag.Sections = _sectionGuard == null ? null : await _sectionGuard.GetPageAsync(id, BuildCaller(await GetCurrentEmployeeIdAsync()));
        ViewBag.HasMaterialItems = await _context.MaterialItems.AnyAsync(x => x.IsActive);
        ViewBag.CanEditWorkOrder = await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Edit");
        ViewBag.CanApproveWorkOrder = await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Approve");
        // B9: حالت/علت خرابی از کاتالوگ ISO 14224 (فرم تکمیل به‌جای متن آزاد Select نشان می‌دهد؛ سرور هم همین را اعمال می‌کند)
        // کاتالوگ مشترک با استراتژی RCM: فقط حالت‌های خرابی کلاس تجهیز این دستور کار (+ والدهای کلاس + حالت‌های عمومی)
        var classChain = new HashSet<int>();
        for (int? cid = workOrder.Equipment?.EquipmentClassId; cid.HasValue && classChain.Add(cid.Value);)
            cid = await _context.EquipmentClasses.Where(c => c.EquipmentClassId == cid.Value).Select(c => c.ParentEquipmentClassId).FirstOrDefaultAsync();
        var failureModeQuery = _context.FailureModes.Where(x => x.IsActive);
        if (classChain.Count > 0)
            failureModeQuery = failureModeQuery.Where(x => x.EquipmentClassId == null || classChain.Contains(x.EquipmentClassId.Value));
        ViewBag.FailureModeOptions = await failureModeQuery.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new PEMS.Application.DTOs.FailureOption(x.Code, x.Name)).ToListAsync();
        ViewBag.FailureCauseOptions = await _context.FailureCauses.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new PEMS.Application.DTOs.FailureOption(x.Code, x.Name)).ToListAsync();

        // طبق CMMS Production Baseline v1 بند ۱۰.۴: چک‌لیست اجرای واقعی - وقتی این WorkOrder از
        // یک Plan دارای Job Plan ساخته شده باشد
        ViewBag.Tasks = await _context.WorkOrderTasks
            .Where(x => x.WorkOrderId == id).OrderBy(x => x.TaskNumber).ToListAsync();

        return View(workOrder);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(int id, int? assignedTeamId, int? assignedEmployeeId,
        string? failureModeCode, string? failureCauseCode, string? rootCause, decimal? downtimeHours, string? reason,
        string? failureEffect, string? correctiveAction, decimal? contractorCost, string? contractorName)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Edit")) return Forbid();
        var workOrder = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == id);
        if (workOrder == null) return NotFound();
        var next = GetNextStatus(workOrder.Status);
        if (next == null) { TempData["Error"] = "This work order is already at its final status"; return RedirectToAction(nameof(Details), new { id }); }
        if (next == "Closed" && !await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Approve"))
        { TempData["Error"] = "برای بستن نهایی WorkOrder مجوز کافی نداری."; return RedirectToAction(nameof(Details), new { id }); }
        if (next == "Completed" && await _reviewService.HasPendingDisciplineReviewAsync(id))
        { TempData["Error"] = "این WorkOrder یک بازبینی چند-دیسیپلینه‌ی باز داره - قبل از تکمیل، همه‌ی دیسیپلین‌های ارجاع‌شده باید نظرشون رو ثبت کنن."; return RedirectToAction(nameof(Details), new { id }); }

        var (success, error) = await _lifecycleService.AdvanceStatusAsync(
            id, next, reason, await GetCurrentEmployeeIdAsync(), assignedTeamId, assignedEmployeeId,
            failureModeCode, failureCauseCode, rootCause, downtimeHours, failureEffect, correctiveAction,
            contractorCost, contractorName);

        if (!success) { TempData["Error"] = error; return RedirectToAction(nameof(Details), new { id }); }

        TempData["Message"] = $"Work order advanced to {next}";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==============================================================================
    // طبق CMMS Production Baseline v1 بند ۹.۲ ("optional controlled states"): مسیرهای انشعابی
    // که قبلاً اصلاً در دسترس نبودند - AdvanceStatus فقط مسیر خطی رو به جلو رو پوشش می‌ده.
    // اعتبارسنجی واقعی گذار مجاز/غیرمجاز داخل WorkOrderLifecycleService انجام می‌شه.
    // ==============================================================================

    // طبق بخش ۱۴ سند: ساخت سریع یک Crew اختصاصی برای همین WorkOrder، بدون خروج از صفحه
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDisciplineReview(int workOrderId, List<int> disciplineIds)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "Edit")) return Forbid();
        var denied = await GuardChildAsync(workOrderId, WoSection.DisciplineReview, WoSectionAction.Add);
        if (denied != null) return denied;
        try
        {
            await _reviewService.RequestDisciplineReviewAsync(workOrderId, disciplineIds, await GetCurrentEmployeeIdAsync(), GetClientIp(), GetUserAgent());
            await RecordChildChangeAsync(workOrderId, WoSection.DisciplineReview, WoSectionAction.Add, null, null, null, "DisciplineIds=" + string.Join(",", disciplineIds ?? new List<int>()));
            TempData["Message"] = "بازبینی چند-دیسیپلینه شروع شد.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id = workOrderId });
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }

    private async Task<int?> GetCurrentEmployeeDisciplineIdAsync()
    {
        var employeeId = await GetCurrentEmployeeIdAsync();
        if (!employeeId.HasValue) return null;
        return await _context.Employees.Where(e => e.EmployeeId == employeeId.Value).Select(e => (int?)e.DisciplineId).FirstOrDefaultAsync();
    }

    private WoCaller BuildCaller(int? employeeId)
        => new(_currentUser.UserId, _currentUser.UserName, employeeId, _currentUser.Roles, HttpContext?.Connection.RemoteIpAddress?.ToString());

    private static bool IsFa() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";

    private bool WantsJson()
        => Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// H25-H27: تنها نقطه‌ی بررسی «بخش × فاز × مجوز» برای تمام POSTهای جهشی. null = مجاز. رد = پیام مشخص و بومی‌شده؛ برای فرم‌های کلاسیک Toast + بازگشت به صفحه
    /// (PRG؛ تا صفحه‌ی خطای خالی نشان داده نشود) و برای فراخوان JSON/AJAX وضعیت واقعی HTTP 400/403/404 با بدنه‌ی JSON. هرگز بی‌صدا و هرگز 500.
    /// </summary>
    private async Task<IActionResult?> GuardChildAsync(int workOrderId, WoSection section, WoSectionAction action, string? reason = null, string? recordRef = null)
    {
        if (_sectionGuard == null)
        {
            TempData["Error"] = L("دروازه‌ی بخش‌های Work Order پیکربندی نشده است؛ تغییر مجاز نیست.", "The Work Order section guard is not configured; the change is not allowed.");
            return RedirectToAction(nameof(Details), new { id = workOrderId });
        }

        var result = await _sectionGuard.AuthorizeAsync(workOrderId, section, action, BuildCaller(await GetCurrentEmployeeIdAsync()), reason, recordRef);
        if (result.Allowed) return null;

        var message = result.Message(IsFa());
        Response.Headers["X-PEMS-Policy"] = result.Code;
        if (WantsJson()) return StatusCode(result.HttpStatus, new { code = result.Code, error = message, phase = result.Phase.ToString() });
        if (result.HttpStatus == 404) return NotFound();

        TempData["Error"] = message;
        TempData["PolicyCode"] = result.Code;
        return RedirectToAction(nameof(Details), new { id = workOrderId });
    }

    private async Task RecordChildChangeAsync(int workOrderId, WoSection section, WoSectionAction action, string? recordRef, string? reason, string? before, string? after)
    {
        if (_sectionGuard == null) return;
        try { await _sectionGuard.RecordChangeAsync(workOrderId, section, action, BuildCaller(await GetCurrentEmployeeIdAsync()), recordRef, reason, before, after); }
        catch (Exception) { _context.ChangeTracker.Clear(); /* ثبت ممیزی بهترین‌تلاش است؛ جهش قبلاً ذخیره شده */ }
    }

    /// <summary>B11: آخرین خط دفاع - هر استثنای پیش‌بینی‌نشده به Toast خطای مشخص تبدیل می‌شود (هرگز صفحه‌ی خطا/سکوت).</summary>
    private async Task<(bool Success, string? Error)> SafeAsync(Func<Task<(bool Success, string? Error)>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            return (false, L("عملیات انجام نشد (خطای غیرمنتظره): ", "The operation could not be completed (unexpected error): ") + ex.GetBaseException().Message);
        }
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? GetUserAgent() => Request.Headers["User-Agent"].ToString();

    private static string L(string fa, string en)
        => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? fa : en;

    private async Task<WorkOrderActor> BuildActorAsync() => new(await GetCurrentEmployeeIdAsync(), _currentUser.Roles);

    private static string? GetNextStatus(string current)
    {
        var index = Array.IndexOf(StatusFlow, current);
        if (index < 0 || index >= StatusFlow.Length - 1) return null;
        return StatusFlow[index + 1];
    }
}
