using System.Text;
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
/// ==============================================================================
/// استراتژی نگهداری RCM (SAE JA1011 / ISO 14224 / EN 13306) - الگوی کلاس‌محور:
///   • الگو روی کلاس تجهیز تعریف می‌شود (Create/Edit)، با چرخه‌عمر Draft→InReview→Approved→Active.
///   • تخصیص به تگ‌ها از «تخصیص دسته‌جمعی» (BulkCreate) یا Details انجام می‌شود.
///   • همه‌ی قوانین (RTF+Safety، حالت خرابی اجباری، توجیه، تکراری، ...) سمت سرور در سرویس اعمال
///     می‌شوند؛ خطا همیشه پیام محلی‌شده + HTTP 400 (نه 500) است.
///
/// مجوزها (ماژول MaintenanceStrategy): View=مشاهده، Create=ساخت الگو، Edit=ویرایش/ارسال برای بازبینی/
/// درخواست Override، Approve=تایید/فعال‌سازی/بایگانی/تخصیص/تصمیم Override. همه‌ی POST ها Antiforgery دارند.
/// ==============================================================================
/// </summary>
public class MaintenanceStrategyController : Controller
{
    private const string Module = "MaintenanceStrategy";

    private readonly PEMSDbContext _context;
    private readonly IMaintenanceStrategyService _strategyService;
    private readonly IMaintenanceStrategyCommandService _commands;
    private readonly IStrategyAssignmentService _assignments;
    private readonly IFailureModeCatalogService _catalog;
    private readonly IStrategyCoverageService _coverage;
    private readonly IFunctionalLocationService _functionalLocationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IStringLocalizer<SharedResource>? _l;

    public MaintenanceStrategyController(PEMSDbContext context, IMaintenanceStrategyService strategyService,
        IMaintenanceStrategyCommandService commands, IStrategyAssignmentService assignments,
        IFailureModeCatalogService catalog, IStrategyCoverageService coverage,
        IFunctionalLocationService functionalLocationService, ICurrentUserService currentUser,
        IPermissionService permissionService, IStringLocalizer<SharedResource>? localizer = null)
    {
        _context = context;
        _strategyService = strategyService;
        _commands = commands;
        _assignments = assignments;
        _catalog = catalog;
        _coverage = coverage;
        _functionalLocationService = functionalLocationService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _l = localizer;
    }

    private Task<bool> Can(string action) => _permissionService.HasPermissionAsync(_currentUser.Roles, Module, action);

    private Task<StrategyActor> ActorAsync() => _context.ResolveStrategyActorAsync(_currentUser);

    // ==================== List ====================

    public async Task<IActionResult> Index(int? equipmentId, int? classId = null, string? status = null,
        string? type = null, string? q = null, int page = 1, int? pageSize = null)
    {
        if (!await Can("View")) return Forbid();

        ViewData["HelpKey"] = "MaintenanceStrategies.Index";
        ViewData["Title"] = "Maintenance Strategies";
        ViewData["Section"] = "CMMS Integration";

        var filter = new StrategyListFilter
        {
            Page = page,
            PageSize = DataGridPageSize.Normalize(pageSize),
            EquipmentId = equipmentId,
            EquipmentClassId = classId,
            Status = RcmCodes.Status.All.Contains(status ?? "") ? status : null,
            StrategyType = RcmCodes.StrategyType.IsValid(type) ? type : null,
            Search = q,
            // پیش‌فرض: نسخه‌های جایگزین‌شده/بایگانی‌شده در فهرست اصلی نیستند (فیلتر Status آن‌ها را نشان می‌دهد)
            IncludeInactive = status is RcmCodes.Status.Retired or RcmCodes.Status.Superseded
        };
        var result = await _strategyService.GetListAsync(filter);
        if (string.IsNullOrEmpty(filter.Status))
            result.Items = result.Items.Where(x => x.Status != RcmCodes.Status.Superseded).ToList();

        var vm = new StrategyIndexViewModel
        {
            Page = result,
            Filter = filter,
            Classes = await ClassLookupAsync(),
            CanCreate = await Can("Create"),
            CanEdit = await Can("Edit"),
            CanApprove = await Can("Approve")
        };

        if (equipmentId.HasValue)
        {
            vm.SelectedEquipmentLabel = await _context.Equipments
                .Where(x => x.EquipmentId == equipmentId.Value)
                .Select(x => x.TagNumber + " - " + x.Name)
                .FirstOrDefaultAsync();
        }

        ViewBag.SelectedEquipmentId = equipmentId;
        ViewBag.SelectedEquipmentLabel = vm.SelectedEquipmentLabel;
        return View(vm);
    }

    // ==================== Details ====================

    public async Task<IActionResult> Details(int id)
    {
        if (!await Can("View")) return Forbid();

        var detail = await _strategyService.GetDetailAsync(id);
        if (detail == null) return NotFound();

        ViewData["Title"] = "Maintenance Strategy";
        ViewData["Section"] = "CMMS Integration";

        var actor = await ActorAsync();
        var vm = new StrategyDetailsViewModel
        {
            Strategy = detail,
            CanEdit = await Can("Edit"),
            CanApprove = await Can("Approve"),
            IsAuthorOrSubmitter = actor.EmployeeId.HasValue && detail.AuthorEmployeeId == actor.EmployeeId
        };
        if (TempData["RcmErrors"] is string joined && joined.Length > 0)
            vm.Errors = joined.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (TempData["RcmAssignReport"] is string report && report.Length > 0)
            vm.AssignReport = report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|')).Where(p => p.Length >= 2).ToList();
        return View(vm);
    }

    // ==================== Create / Edit ====================

    [HttpGet]
    public async Task<IActionResult> Create(int? equipmentId = null, int? classId = null)
    {
        if (!await Can("Create")) return Forbid();

        ViewData["Title"] = "New Maintenance Strategy (RCM)";
        ViewData["Section"] = "CMMS Integration";

        var form = await NewFormAsync(new StrategyInputDto { RecommendedTriggerType = RcmCodes.Trigger.Calendar });
        if (equipmentId.HasValue)
        {
            // تگ فقط برای «شروع» انتخاب می‌شود: کلاس و هویت تجهیز از ثبت تجهیز خوانده و فقط‌خواندنی نمایش داده می‌شود
            form.StartedFromEquipment = await _assignments.GetEquipmentIdentityAsync(equipmentId.Value);
            form.Input.EquipmentClassId = form.StartedFromEquipment?.EquipmentClassId;
        }
        else if (classId.HasValue)
        {
            form.Input.EquipmentClassId = classId;
        }
        return View("Edit", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StrategyInputDto input)
    {
        if (!await Can("Create")) return Forbid();

        var result = await _commands.CreateAsync(input, await ActorAsync());
        if (!result.Success)
            return await FormWithErrorsAsync(null, input, result.Errors);

        TempData["Message"] = T("Rcm_Info_StrategyCreated");
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!await Can("Edit")) return Forbid();

        var detail = await _strategyService.GetDetailAsync(id);
        if (detail == null) return NotFound();

        ViewData["Title"] = "Edit Maintenance Strategy";
        ViewData["Section"] = "CMMS Integration";

        var form = await NewFormAsync(detail.Input);
        form.MaintenanceStrategyId = id;
        form.Status = detail.Status;
        form.VersionNumber = detail.VersionNumber;
        form.WillCreateNewVersion = detail.Status is RcmCodes.Status.Approved or RcmCodes.Status.Active;
        form.SelectedFailureModeLabel = detail.FailureModeName;
        return View("Edit", form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StrategyInputDto input)
    {
        if (!await Can("Edit")) return Forbid();

        var result = await _commands.UpdateAsync(id, input, await ActorAsync());
        if (!result.Success)
            return await FormWithErrorsAsync(id, input, result.Errors);

        TempData["Message"] = T(result.InfoCode ?? "Rcm_Info_StrategySaved");
        return RedirectToAction(nameof(Details), new { id = result.Id ?? id });
    }

    // ==================== Lifecycle ====================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        if (!await Can("Edit")) return Forbid();
        return Outcome(await _commands.SubmitForReviewAsync(id, await ActorAsync()), id, "Rcm_Info_Submitted");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, bool approve, string? comment)
    {
        if (!await Can("Approve")) return Forbid();
        return Outcome(await _commands.ReviewAsync(id, approve, comment, await ActorAsync()), id,
            approve ? "Rcm_Info_Approved" : "Rcm_Info_Rejected");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id, bool moveAssignments = true)
    {
        if (!await Can("Approve")) return Forbid();
        return Outcome(await _commands.ActivateAsync(id, moveAssignments, await ActorAsync()), id, "Rcm_Info_Activated");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retire(int id, string? comment)
    {
        if (!await Can("Approve")) return Forbid();
        return Outcome(await _commands.RetireAsync(id, comment, await ActorAsync()), id, "Rcm_Info_Retired");
    }

    // سازگاری با فرم/لینک قبلی «Deactivate» - اکنون یعنی بایگانی (Retire) با کنترل تغییر
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int strategyId)
    {
        if (!await Can("Approve")) return Forbid();
        var result = await _commands.RetireAsync(strategyId, null, await ActorAsync());
        if (result.Errors.Any(e => e.Code == "Rcm_Err_StrategyNotFound")) return NotFound();
        return Outcome(result, strategyId, "Rcm_Info_Retired", nameof(Index));
    }

    // ==================== Assignment ====================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, List<int> equipmentIds)
    {
        if (!await Can("Approve")) return Forbid();

        var result = await _assignments.AssignAsync(id, equipmentIds ?? new List<int>(), await ActorAsync());
        return AssignOutcome(result, id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int assignmentId, int strategyId, string? comment)
    {
        if (!await Can("Approve")) return Forbid();
        return Outcome(await _assignments.UnassignAsync(assignmentId, comment, await ActorAsync()), strategyId, "Rcm_Info_Unassigned");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestOverride(int assignmentId, int strategyId, int? overrideIntervalDays,
        string? overrideTaskNote, string? overrideJustification)
    {
        if (!await Can("Edit")) return Forbid();
        return Outcome(await _assignments.RequestOverrideAsync(assignmentId, overrideIntervalDays, overrideTaskNote,
            overrideJustification, await ActorAsync()), strategyId, "Rcm_Info_OverrideRequested");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecideOverride(int assignmentId, int strategyId, bool approve, string? comment)
    {
        if (!await Can("Approve")) return Forbid();
        return Outcome(await _assignments.DecideOverrideAsync(assignmentId, approve, comment, await ActorAsync()), strategyId,
            approve ? "Rcm_Info_OverrideApproved" : "Rcm_Info_OverrideRejected");
    }

    // ==================== Bidirectional views / reports ====================

    /// <summary>از تگ: همه‌ی استراتژی‌های تخصیص‌یافته، Planها و WOهای باز.</summary>
    public async Task<IActionResult> Tag(int equipmentId)
    {
        if (!await Can("View")) return Forbid();

        var view = await _assignments.GetTagViewAsync(equipmentId);
        if (view == null) return NotFound();

        ViewData["Title"] = "Tag Maintenance";
        ViewData["Section"] = "CMMS Integration";
        return View(view);
    }

    public async Task<IActionResult> Coverage()
    {
        if (!await Can("View")) return Forbid();

        ViewData["Title"] = "Strategy Coverage";
        ViewData["Section"] = "CMMS Integration";
        return View(await _coverage.GetCoverageAsync());
    }

    public async Task<IActionResult> ExportUncovered()
    {
        if (!await Can("View")) return Forbid();

        var coverage = await _coverage.GetCoverageAsync(topUncoveredCritical: 5000);
        var sb = new StringBuilder("Tag,Name,Class,Area,Unit,Criticality\n");
        foreach (var r in coverage.CriticalWithoutStrategy)
        {
            var e = r.Equipment;
            sb.Append(string.Join(',', new[] { e.Tag, e.Name, e.EquipmentClassName, e.Area, e.Unit, e.CriticalityClass }
                .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""))).Append('\n');
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", "critical-equipment-without-strategy.csv");
    }

    // ==================== Failure-mode picker (JSON) ====================

    [HttpGet]
    public async Task<IActionResult> FailureModes(int? classId, string? q)
    {
        if (!await Can("View")) return Forbid();
        return Json(await _catalog.SearchAsync(classId, q, 50));
    }

    // ==================== Bulk (class-level) assignment ====================

    [HttpGet]
    public async Task<IActionResult> BulkCreate(List<int>? disciplineIds, List<string>? equipmentTypes,
        List<int>? functionalLocationIds, string? tagSearch,
        List<string>? plants = null, List<string>? areas = null, List<string>? units = null,
        string? functionalLocationMode = null, string? tagList = null,
        int? strategyId = null, int? classId = null)
    {
        // فعال‌سازی هم‌زمان چند تجهیز بالاترین ریسک این کنترلر است - فقط Supervisor/Manager (Approve)
        if (!await Can("Approve")) return Forbid();

        ViewData["Title"] = "Bulk Define Maintenance Strategy";
        ViewData["Section"] = "CMMS Integration";

        disciplineIds ??= new List<int>();
        equipmentTypes ??= new List<string>();
        functionalLocationIds ??= new List<int>();
        plants ??= new List<string>();
        areas ??= new List<string>();
        units ??= new List<string>();
        var flMode = string.Equals(functionalLocationMode, "Descendants", StringComparison.OrdinalIgnoreCase) ? "Descendants" : "Exact";

        var requestedTags = (tagList ?? "")
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<int> effectiveFunctionalLocationIds;
        if (functionalLocationIds.Count == 0)
            effectiveFunctionalLocationIds = new List<int>();
        else if (flMode == "Descendants")
            effectiveFunctionalLocationIds = await _functionalLocationService.GetDescendantIdsAsync(functionalLocationIds, includeSelf: true);
        else
            effectiveFunctionalLocationIds = functionalLocationIds;

        // رشته‌های هم‌نام (تکراری در داده) یک گزینه‌ی واحد نمایش داده می‌شوند و انتخاب آن‌ها همه‌ی شناسه‌های هم‌نام را شامل می‌شود
        var activeDisciplines = await _context.Disciplines.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();
        var disciplineGroups = activeDisciplines
            .GroupBy(x => (x.Name ?? "").Trim().ToLowerInvariant())
            .Select(g => new { Canonical = g.OrderBy(x => x.DisciplineId).First(), Ids = g.Select(x => x.DisciplineId).ToList() })
            .OrderBy(g => g.Canonical.Name)
            .ToList();
        var effectiveDisciplineIds = disciplineIds
            .SelectMany(id => disciplineGroups.FirstOrDefault(g => g.Ids.Contains(id))?.Ids ?? new List<int> { id })
            .Distinct().ToList();

        var matchedTagCount = 0;
        if (requestedTags.Count > 0)
        {
            matchedTagCount = await _context.Equipments
                .Where(x => x.IsActive)
                .Select(x => x.TagNumber)
                .Where(t => requestedTags.Contains(t))
                .CountAsync();
        }
        ViewBag.RequestedTagCount = requestedTags.Count;
        ViewBag.MatchedTagCount = matchedTagCount;

        // کلاس تجهیز (ISO 14224) + زیرکلاس‌ها: فیلتر ردهبندی‌شده‌ی محدودکننده
        HashSet<int>? classScope = null;
        if (classId.HasValue)
        {
            var pairs = await _context.EquipmentClasses.AsNoTracking()
                .Select(c => new { c.EquipmentClassId, c.ParentEquipmentClassId }).ToListAsync();
            classScope = new HashSet<int> { classId.Value };
            var queue = new Queue<int>(classScope);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var child in pairs.Where(p => p.ParentEquipmentClassId == cur))
                    if (classScope.Add(child.EquipmentClassId)) queue.Enqueue(child.EquipmentClassId);
            }
        }

        // این فرم برای «فعال‌سازی کنترل‌شده» است، نه مرور کل ناوگان؛ بدون حداقل یک فیلتر هیچ تجهیزی نمایش داده نمی‌شود
        // و حتی با فیلتر، نتیجه به سقف صریح محدود است.
        const int maxResults = 200;
        List<PEMS.Domain.Entities.Equipment> equipmentList;

        var hasNarrowingFilter = effectiveDisciplineIds.Count > 0 || equipmentTypes.Count > 0 ||
                                  functionalLocationIds.Count > 0 || plants.Count > 0 || areas.Count > 0 || units.Count > 0 ||
                                  !string.IsNullOrWhiteSpace(tagSearch) || classScope != null;
        var hasFilter = hasNarrowingFilter || requestedTags.Count > 0;

        if (!hasFilter)
        {
            equipmentList = new List<PEMS.Domain.Entities.Equipment>();
            ViewBag.NoFilterApplied = true;
            ViewBag.MatchingCount = 0;
        }
        else
        {
            var resolvedIds = new HashSet<int>();
            var totalMatchingCount = 0;

            if (hasNarrowingFilter)
            {
                var query = _context.Equipments.Where(x => x.IsActive).AsQueryable();

                if (effectiveDisciplineIds.Count > 0) query = query.Where(x => effectiveDisciplineIds.Contains(x.DisciplineId));
                if (equipmentTypes.Count > 0) query = query.Where(x => equipmentTypes.Contains(x.EquipmentType));
                if (classScope != null) query = query.Where(x => x.EquipmentClassId != null && classScope.Contains(x.EquipmentClassId.Value));
                if (effectiveFunctionalLocationIds.Count > 0)
                    query = query.Where(x => x.FunctionalLocationId.HasValue && effectiveFunctionalLocationIds.Contains(x.FunctionalLocationId.Value));
                if (plants.Count > 0)
                    query = query.Where(x => x.FunctionalLocationRef != null && x.FunctionalLocationRef.Plant != null && plants.Contains(x.FunctionalLocationRef.Plant));
                if (areas.Count > 0)
                    query = query.Where(x => x.FunctionalLocationRef != null && x.FunctionalLocationRef.Area != null && areas.Contains(x.FunctionalLocationRef.Area));
                if (units.Count > 0)
                    query = query.Where(x => x.FunctionalLocationRef != null && x.FunctionalLocationRef.Unit != null && units.Contains(x.FunctionalLocationRef.Unit));
                if (!string.IsNullOrWhiteSpace(tagSearch))
                    query = query.Where(x => x.TagNumber.Contains(tagSearch.Trim()));

                totalMatchingCount += await query.CountAsync();
                var narrowedIds = await query.OrderBy(x => x.TagNumber).Take(maxResults).Select(x => x.EquipmentId).ToListAsync();
                foreach (var id in narrowedIds) resolvedIds.Add(id);
            }

            if (requestedTags.Count > 0)
            {
                var tagMatchIds = await _context.Equipments
                    .Where(x => x.IsActive && requestedTags.Contains(x.TagNumber))
                    .Select(x => x.EquipmentId)
                    .ToListAsync();

                totalMatchingCount += tagMatchIds.Count(id => !resolvedIds.Contains(id));
                foreach (var id in tagMatchIds) resolvedIds.Add(id);
            }

            ViewBag.MatchingCount = totalMatchingCount;
            equipmentList = await _context.Equipments
                .Where(x => resolvedIds.Contains(x.EquipmentId))
                .OrderBy(x => x.TagNumber)
                .Take(maxResults)
                .ToListAsync();
            ViewBag.NoFilterApplied = false;
        }
        ViewBag.MaxResults = maxResults;

        var equipmentIds = equipmentList.Select(x => x.EquipmentId).ToList();
        var criticalityByEquipment = await _context.EquipmentCriticalities
            .Where(x => x.IsActive && equipmentIds.Contains(x.EquipmentId))
            .ToDictionaryAsync(x => x.EquipmentId, x => x.CriticalityClass);

        ViewBag.Equipments = equipmentList;
        ViewBag.CriticalityByEquipment = criticalityByEquipment;

        // پیش‌نمایش تعارض/بحرانی‌بودن برای الگوی انتخاب‌شده
        var vm = new BulkAssignViewModel { StrategyId = strategyId };
        vm.UsableStrategies = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(s => s.IsActive && (s.Status == RcmCodes.Status.Approved || s.Status == RcmCodes.Status.Active))
            .OrderBy(s => s.EquipmentClass!.Name).ThenBy(s => s.FailureModeCode)
            .Select(s => new LookupItem
            {
                Id = s.MaintenanceStrategyId,
                Code = s.StrategyType,
                Text = (s.EquipmentClass != null ? s.EquipmentClass.Name : "-") + " · " + s.StrategyType + " · " +
                       (s.FailureMode != null ? s.FailureMode.Name : s.FailureModeCode) + " · v" + s.VersionNumber
            }).Take(500).ToListAsync();
        if (strategyId.HasValue && equipmentIds.Count > 0)
        {
            var candidates = await _assignments.EvaluateAsync(strategyId.Value, equipmentIds);
            vm.Candidates = candidates.ToDictionary(c => c.Equipment.EquipmentId);
        }
        ViewBag.BulkAssign = vm;
        ViewBag.Classes = await ClassLookupAsync();
        ViewBag.SelectedClassId = classId;

        ViewBag.Disciplines = disciplineGroups.Select(g => new { g.Canonical.DisciplineId, g.Canonical.Name }).ToList();

        ViewBag.EquipmentTypes = await _context.Equipments.Where(x => x.IsActive && x.EquipmentType != "")
            .Select(x => x.EquipmentType).Distinct().OrderBy(x => x).ToListAsync();

        ViewBag.FunctionalLocations = await _functionalLocationService.GetPickerListAsync();
        ViewBag.EquipmentWithFunctionalLocationCount = await _context.Equipments.CountAsync(x => x.IsActive && x.FunctionalLocationId != null);

        ViewBag.Plants = await _context.FunctionalLocations.Where(x => x.IsActive && x.Plant != null && x.Plant != "")
            .Select(x => x.Plant!).Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Areas = await _context.FunctionalLocations.Where(x => x.IsActive && x.Area != null && x.Area != "")
            .Select(x => x.Area!).Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Units = await _context.FunctionalLocations.Where(x => x.IsActive && x.Unit != null && x.Unit != "")
            .Select(x => x.Unit!).Distinct().OrderBy(x => x).ToListAsync();

        ViewBag.SelectedPlants = plants;
        ViewBag.SelectedAreas = areas;
        ViewBag.SelectedUnits = units;
        ViewBag.SelectedFunctionalLocationMode = flMode;
        ViewBag.SelectedTagList = tagList;
        ViewBag.SelectedDisciplineIds = disciplineIds;
        ViewBag.SelectedEquipmentTypes = equipmentTypes;
        ViewBag.SelectedFunctionalLocationIds = functionalLocationIds;
        ViewBag.SelectedTagSearch = tagSearch;

        return View();
    }

    /// <summary>
    /// تخصیص دسته‌جمعی یک الگوی Approved/Active به تگ‌های انتخاب‌شده (اتمیک؛ تعارض‌ها رد و گزارش می‌شوند).
    /// دیگر برای هر تگ یک استراتژی مستقل ساخته نمی‌شود.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCreate(List<int> equipmentIds, int strategyId)
    {
        if (!await Can("Approve")) return Forbid();

        if (equipmentIds == null || equipmentIds.Count == 0)
        {
            TempData["Error"] = T("Rcm_Err_NoEquipmentSelected");
            return RedirectToAction(nameof(BulkCreate), new { strategyId });
        }

        var result = await _assignments.AssignAsync(strategyId, equipmentIds, await ActorAsync());
        return AssignOutcome(result, strategyId);
    }

    // ==================== Helpers ====================

    private IActionResult AssignOutcome(BulkAssignResultDto result, int strategyId)
    {
        if (result.Errors.Count > 0)
        {
            TempData["Error"] = string.Join(" | ", _l.TranslateAll(result.Errors));
            if (result.Errors.Any(e => e.Code == "Rcm_Err_StrategyNotFound")) return NotFound();
            return RedirectToAction(nameof(Details), new { id = strategyId });
        }

        var parts = new List<string> { T("Rcm_Info_AssignedCount", result.Assigned) };
        if (result.SkippedAlreadyAssigned > 0) parts.Add(T("Rcm_Info_SkippedAlready", result.SkippedAlreadyAssigned));
        if (result.SkippedConflict > 0) parts.Add(T("Rcm_Info_SkippedConflict", result.SkippedConflict));
        if (result.SkippedCriticality > 0) parts.Add(T("Rcm_Info_SkippedCriticality", result.SkippedCriticality));
        if (result.SkippedNotFound > 0) parts.Add(T("Rcm_Info_SkippedNotFound", result.SkippedNotFound));
        TempData["Message"] = string.Join(" · ", parts);
        if (result.Skipped.Count > 0)
            TempData["RcmAssignReport"] = string.Join("\n", result.Skipped.Take(200).Select(s => $"{s.Tag}|{s.Reason}|{s.OtherStrategyId}"));
        return RedirectToAction(nameof(Details), new { id = strategyId });
    }

    private IActionResult Outcome(OpResult result, int id, string successKey, string? successAction = null)
    {
        if (!result.Success)
        {
            if (result.Errors.Any(e => e.Code is "Rcm_Err_StrategyNotFound" or "Rcm_Err_AssignmentNotFound")) return NotFound();
            TempData["Error"] = string.Join(" | ", _l.TranslateAll(result.Errors));
            TempData["RcmErrors"] = string.Join("\n", _l.TranslateAll(result.Errors));
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Message"] = T(successKey);
        return successAction == null
            ? RedirectToAction(nameof(Details), new { id })
            : RedirectToAction(successAction);
    }

    private async Task<IActionResult> FormWithErrorsAsync(int? id, StrategyInputDto input, IEnumerable<RuleError> errors)
    {
        var form = await NewFormAsync(input);
        form.MaintenanceStrategyId = id;
        if (id.HasValue)
        {
            var detail = await _strategyService.GetDetailAsync(id.Value);
            form.Status = detail?.Status ?? "Draft";
            form.VersionNumber = detail?.VersionNumber ?? 1;
            form.WillCreateNewVersion = detail?.Status is RcmCodes.Status.Approved or RcmCodes.Status.Active;
        }
        foreach (var e in errors)
        {
            var msg = _l.Translate(e);
            form.Errors.Add(msg);
            if (!string.IsNullOrEmpty(e.Field)) ModelState.AddModelError(e.Field, msg);
        }
        if (input.FailureModeId.HasValue)
            form.SelectedFailureModeLabel = await _context.FailureModes.Where(m => m.FailureModeId == input.FailureModeId)
                .Select(m => m.Name).FirstOrDefaultAsync();

        // ورودی نامعتبر = 400 (نه 500 و نه 200 با پیام خاموش)
        Response.StatusCode = StatusCodes.Status400BadRequest;
        ViewData["Title"] = id.HasValue ? "Edit Maintenance Strategy" : "New Maintenance Strategy (RCM)";
        ViewData["Section"] = "CMMS Integration";
        return View("Edit", form);
    }

    private async Task<StrategyFormViewModel> NewFormAsync(StrategyInputDto input) => new()
    {
        Input = input,
        Classes = await ClassLookupAsync(),
        Disciplines = await _context.Disciplines.AsNoTracking().Where(d => d.IsActive)
            .OrderBy(d => d.Name).Select(d => new LookupItem { Id = d.DisciplineId, Text = d.Name, Code = d.Code }).ToListAsync()
    };

    private Task<List<LookupItem>> ClassLookupAsync()
        => _context.EquipmentClasses.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new LookupItem { Id = c.EquipmentClassId, Text = c.Name, Code = c.Code }).ToListAsync();

    private string T(string key, params object[] args) => _l == null ? key : _l[key, args].Value;
}
