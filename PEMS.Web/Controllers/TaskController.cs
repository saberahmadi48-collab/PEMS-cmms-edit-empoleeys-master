using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// وظایف مستقل (طبق نمونه‌ی کارتابل مرجع Pargar/Fonix: "اقدام وظایف" / "پیگیری وظایف") -
/// جدا از گردش‌کار ارجاع نامه، می‌تواند کاملاً مستقل باشد یا به هر موجودیتی وصل شود.
/// Task Manager (فاز ۰): مسیرها و کلیدهای مجوز قبلی حفظ شده‌اند؛ تغییر وضعیت حالا سطح-شیء (فقط درگیرها/Lead)،
/// فقط گذارهای مجاز و با کنترل RowVersion از ITaskManagerService انجام می‌شود. Controller به DbContext دسترسی ندارد.
/// </summary>
public class TaskController : Controller
{
    private readonly ITaskService _service;
    private readonly ITaskManagerService _manager;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IStringLocalizer<SharedResource>? _l;

    public TaskController(ITaskService service, ITaskManagerService manager, ICurrentUserService currentUser,
        IPermissionService permissionService, IStringLocalizer<SharedResource>? localizer = null)
    {
        _service = service;
        _manager = manager;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _l = localizer;
    }

    private Task<TaskActor> ActorAsync() => _manager.ResolveActorAsync(_currentUser.UserId, _currentUser.Roles, _currentUser.FullName);

    private string Text(string key) => _l != null ? _l[key].Value : key;

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول فیلترشده با
    // ToListAsync (بدون Skip/Take) بارگذاری می‌شد.
    // طبق ممیزی مستقل فاز ۶B Wave A (P0): این Action هیچ بررسی مجوزی نداشت - هر کاربر
    // احرازهویت‌شده‌ای (بدون توجه به نقش) می‌توانست وظایف را ببیند. الگوی
    // WorkOrder/WorkRequest/MaterialRequisition/Inspection/Permit عیناً استفاده شده.
    public async Task<IActionResult> Index(string tab = "action", int page = 1, int? pageSize = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "View"))
            return Forbid();

        ViewData["Title"] = "Tasks";
        ViewData["Section"] = "Correspondence Management";
        tab = tab == "followup" ? "followup" : "action";
        ViewBag.Tab = tab;
        ViewBag.CanEdit = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "Edit");
        ViewBag.CanCreate = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "Create");

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);

        if (_currentUser.UserId == null)
        {
            ViewBag.Employees = new List<EligibleAssigneeDto>();
            return View(new PagedResult<TaskItemDto> { Page = page, PageSize = normalizedPageSize });
        }

        var tasks = tab == "followup"
            ? await _service.GetAssignedByMeAsync(_currentUser.UserId.Value, page: page, pageSize: normalizedPageSize)
            : await _service.GetMyActionItemsAsync(_currentUser.UserId.Value, page: page, pageSize: normalizedPageSize);

        ViewBag.Employees = (await _manager.GetEligibleAssigneesAsync(null, await ActorAsync())).Candidates;

        return View(tasks);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, string? description, int? assignedToEmployeeId,
        DateTime? dueDate, string priority)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "Create"))
            return Forbid();

        var result = await _manager.CreateAsync(new WorkItemCreateInput
        {
            Title = title ?? "",
            Description = description,
            AssigneeEmployeeId = assignedToEmployeeId,
            DueDate = dueDate,
            Priority = string.IsNullOrWhiteSpace(priority) ? null : priority
        }, await ActorAsync());

        if (result.Success) TempData["Message"] = Text("Tm_Msg_Created");
        else TempData["Error"] = Text(result.ErrorKey!);
        return RedirectToAction(nameof(Index));
    }

    // طبق ممیزی مستقل فاز ۶B Wave A (P0): تغییر وضعیت یک رکورد موجود است - طبق الگوی پروژه
    // (مثلاً MaterialRequisitionController.ChangeStatus) نیازمند مجوز "Edit" است، نه یک مجوز
    // اختصاصیِ جدید. Task Manager فاز ۰: به‌علاوه‌ی کنترل سطح-شیء و اعتبارسنجی گذار (قبلاً هر کاربر Edit هر شناسه‌ای را
    // به هر رشته‌ای تغییر می‌داد).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int taskItemId, string status, string? completionNote, string tab = "action", string? rowVersion = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "Edit"))
            return Forbid();

        var result = await _manager.TransitionAsync(taskItemId, null, status, completionNote, rowVersion, await ActorAsync());
        if (!result.Success)
        {
            if (result.ErrorKey == "Tm_Err_NotFound") return NotFound();
            if (result.ErrorKey == "Tm_Err_NotAuthorized") return Forbid();
            TempData["Error"] = Text(result.ErrorKey!);
        }
        return RedirectToAction(nameof(Index), new { tab = tab == "followup" ? "followup" : "action" });
    }

    /// <summary>صفحه‌ی جزئیات وظیفه (شخصی یا فضا). وظیفه‌ی غیرقابل‌دید = 404 (وجودش افشا نمی‌شود).</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Task", "View"))
            return Forbid();
        var actor = await ActorAsync();
        var detail = await _manager.GetDetailAsync(id, actor);
        if (detail == null) return NotFound();

        ViewData["Title"] = detail.Item.Key ?? $"#{detail.Item.Id}";
        ViewData["Section"] = "Task Manager";
        ViewBag.History = await _manager.GetHistoryAsync(id, actor) ?? new List<WorkItemHistoryEntryDto>();
        ViewBag.Assignees = detail.CanAssign
            ? await _manager.GetEligibleAssigneesAsync(detail.Item.WorkSpaceId, actor)
            : new AssigneeResolutionDto();
        return View(detail);
    }
}
