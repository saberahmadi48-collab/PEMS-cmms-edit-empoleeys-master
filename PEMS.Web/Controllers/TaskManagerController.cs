using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// Task Manager (فضاها، Backlog، Board، Sprint، همه‌ی کارها، پیکربندی فضا). Controller نازک: هر تصمیم دسترسی/اعتبارسنجی
/// در ITaskManagerService/IWorkSpaceService (TaskAccess). دروازه‌ی نقش همان مجوزهای موجود Task.View/Create/Edit؛
/// پیکربندی فضا فقط SystemAdmin. شناسه‌ای که کاربر نمی‌بیند = 404.
/// </summary>
public class TaskManagerController : Controller
{
    private readonly ITaskManagerService _tasks;
    private readonly IWorkSpaceService _spaces;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IStringLocalizer<SharedResource>? _l;

    public TaskManagerController(ITaskManagerService tasks, IWorkSpaceService spaces, ICurrentUserService currentUser,
        IPermissionService permissions, IStringLocalizer<SharedResource>? localizer = null)
    {
        _tasks = tasks;
        _spaces = spaces;
        _currentUser = currentUser;
        _permissions = permissions;
        _l = localizer;
    }

    private Task<TaskActor> ActorAsync() => _tasks.ResolveActorAsync(_currentUser.UserId, _currentUser.Roles, _currentUser.FullName);
    private Task<bool> Can(string action) => _permissions.HasPermissionAsync(_currentUser.Roles, "Task", action);
    private string Text(string key) => _l != null ? _l[key].Value : key;
    private bool WantsJson => string.Equals(Request?.Headers["X-Requested-With"].ToString(), "fetch", StringComparison.Ordinal);

    private IActionResult Back(string? returnUrl, string fallbackAction = nameof(Index), object? routeValues = null) =>
        !string.IsNullOrEmpty(returnUrl) && Url != null && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(fallbackAction, routeValues);

    /// <summary>نتیجه‌ی یکسان: JSON برای fetch (Board/DnD)، Flash + Redirect برای فرم. NotFound = 404 (بدون افشا).</summary>
    private IActionResult Done(TmResult r, string? returnUrl, string okKey = "Tm_Msg_Saved")
    {
        if (WantsJson)
        {
            if (!r.Success && r.ErrorKey == "Tm_Err_NotFound") return NotFound(new { ok = false, error = Text(r.ErrorKey) });
            return Json(new { ok = r.Success, error = r.Success ? null : Text(r.ErrorKey!), id = r.Id });
        }
        if (!r.Success && r.ErrorKey == "Tm_Err_NotFound") return NotFound();
        if (r.Success) TempData["Message"] = Text(okKey);
        else TempData["Error"] = Text(r.ErrorKey!);
        return Back(returnUrl);
    }

    // ==================================================================================== pages

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await Can("View")) return Forbid();
        var actor = await ActorAsync();
        ViewData["Title"] = Text("Tm_Title");
        ViewData["Section"] = "Task Manager";
        ViewBag.IsAdmin = actor.IsSystemAdmin;
        ViewBag.HasEmployee = actor.EmployeeId != null;
        return View(await _tasks.GetAccessibleSpacesAsync(actor));
    }

    [HttpGet]
    public async Task<IActionResult> Space(string key, string view = "backlog")
    {
        if (!await Can("View")) return Forbid();
        var actor = await ActorAsync();
        view = view == "board" ? "board" : "backlog";
        ViewBag.ViewMode = view;

        if (view == "board")
        {
            var board = await _tasks.GetBoardAsync(key, actor);
            if (board == null) return NotFound();
            ViewData["Title"] = board.View.Space.Name;
            ViewData["Section"] = "Task Manager";
            ViewBag.Assignees = await _tasks.GetEligibleAssigneesAsync(board.View.Space.Id, actor);
            ViewBag.SpaceView = board.View;
            return View("Board", board);
        }

        var backlog = await _tasks.GetBacklogAsync(key, actor);
        if (backlog == null) return NotFound();
        ViewData["Title"] = backlog.View.Space.Name;
        ViewData["Section"] = "Task Manager";
        ViewBag.Assignees = await _tasks.GetEligibleAssigneesAsync(backlog.View.Space.Id, actor);
        ViewBag.SpaceView = backlog.View;
        return View("Backlog", backlog);
    }

    /// <summary>همه‌ی کارهای قابل‌دید کاربر با فیلتر بسته (همچنین «کارهای من» با assignee=me).</summary>
    [HttpGet]
    public async Task<IActionResult> AllWork([FromQuery] WorkItemQuery query)
    {
        if (!await Can("View")) return Forbid();
        var actor = await ActorAsync();
        ViewData["Title"] = Text(query.Assignee == "me" ? "Tm_MyTasks" : "Tm_AllWork");
        ViewData["Section"] = "Task Manager";
        ViewBag.Query = query;
        var spaces = await _tasks.GetAccessibleSpacesAsync(actor);
        ViewBag.Spaces = spaces;
        ViewBag.CanEdit = await Can("Edit");
        // با فیلتر یک فضا، نوار گروهی همان مقادیر فضا (وضعیت/مسئول/Sprint) را پیشنهاد می‌دهد؛ بدون آن فقط بایگانی
        var space = spaces.FirstOrDefault(s => s.Id == query.WorkSpaceId);
        if (space != null)
        {
            var view = await _tasks.GetSpaceViewAsync(space.Key, actor);
            ViewBag.SpaceView = view;
            ViewBag.Types = view?.Types;
            ViewBag.Assignees = await _tasks.GetEligibleAssigneesAsync(space.Id, actor);
        }
        return View(await _tasks.QueryAsync(query, actor));
    }

    // ==================================================================================== item writes

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkItemCreateInput input, bool createAnother = false, string? returnUrl = null)
    {
        if (!await Can("Create")) return Forbid();
        var r = await _tasks.CreateAsync(input, await ActorAsync());
        if (WantsJson) return Done(r, returnUrl, "Tm_Msg_Created");
        if (!r.Success) { TempData["Error"] = Text(r.ErrorKey!); return Back(returnUrl); }
        TempData["Message"] = Text("Tm_Msg_Created");
        if (createAnother) return Back(AppendQuery(returnUrl, "create=1"));
        return RedirectToAction("Details", "Task", new { id = r.Id });
    }

    private static string? AppendQuery(string? url, string q) =>
        string.IsNullOrEmpty(url) ? null : url + (url.Contains('?') ? "&" : "?") + q;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(WorkItemUpdateInput input, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.UpdateAsync(input, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, int? assigneeEmployeeId, string? rowVersion, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.AssignAsync(id, assigneeEmployeeId, rowVersion, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(int id, int? statusId, string? legacyStatus, string? note, string? rowVersion, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.TransitionAsync(id, statusId, legacyStatus, note, rowVersion, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id, bool archived = true, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.SetArchivedAsync(id, archived, await ActorAsync()), returnUrl, archived ? "Tm_Msg_Archived" : "Tm_Msg_Restored");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rank(int id, bool up, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.MoveRankAsync(id, up, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(int workSpaceId, int? sprintId, [FromForm] List<int> ids)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.ReorderAsync(workSpaceId, sprintId, ids ?? new List<int>(), await ActorAsync()), null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSprint(int id, int? sprintId, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.SetSprintAsync(id, sprintId > 0 ? sprintId : null, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk([FromForm] List<int> ids, string bulkAction, string? value, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        if (ids == null || ids.Count == 0)
        {
            TempData["Error"] = Text("Tm_Err_NothingSelected");
            return Back(returnUrl);
        }
        var r = await _tasks.BulkAsync(ids, bulkAction, value, await ActorAsync());
        if (r.Succeeded > 0) TempData["Message"] = string.Format(Text("Tm_Msg_BulkDone"), r.Succeeded);
        if (r.Failed.Count > 0)
            TempData["Error"] = string.Format(Text("Tm_Msg_BulkFailed"), r.Failed.Count) + " " +
                string.Join("; ", r.Failed.Take(5).Select(f => { var parts = f.Split(": ", 2); return parts.Length == 2 ? parts[0] + ": " + Text(parts[1]) : f; }));
        return Back(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Comment(int id, string body, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.AddCommentAsync(id, body ?? "", await ActorAsync()), returnUrl, "Tm_Msg_CommentAdded");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int commentId, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.DeleteCommentAsync(commentId, await ActorAsync()), returnUrl, "Tm_Msg_CommentDeleted");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLink(int id, string linkType, string target, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.AddLinkAsync(id, linkType ?? "", target ?? "", await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLink(int linkId, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _tasks.RemoveLinkAsync(linkId, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Watch(int id, bool watch, string? returnUrl = null)
    {
        if (!await Can("View")) return Forbid();
        return Done(await _tasks.SetWatchingAsync(id, watch, await ActorAsync()), returnUrl);
    }

    // ==================================================================================== sprints (Lead)

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSprint(int workSpaceId, string name, string? goal, DateTime? startDate, DateTime? endDate, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _spaces.CreateSprintAsync(workSpaceId, name ?? "", goal, startDate, endDate, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSprint(int sprintId, string name, string? goal, DateTime? startDate, DateTime? endDate, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _spaces.UpdateSprintAsync(sprintId, name ?? "", goal, startDate, endDate, await ActorAsync()), returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSprint(int sprintId, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _spaces.StartSprintAsync(sprintId, await ActorAsync()), returnUrl, "Tm_Msg_SprintStarted");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteSprint(int sprintId, int? moveTo, string? returnUrl = null)
    {
        if (!await Can("Edit")) return Forbid();
        return Done(await _spaces.CompleteSprintAsync(sprintId, moveTo > 0 ? moveTo : null, await ActorAsync()), returnUrl, "Tm_Msg_SprintCompleted");
    }

    // ==================================================================================== admin (SystemAdmin)

    [HttpGet]
    public async Task<IActionResult> Admin()
    {
        var actor = await ActorAsync();
        if (!actor.IsSystemAdmin) return Forbid();
        ViewData["Title"] = Text("Tm_Admin_Title");
        ViewData["Section"] = "Administration";
        ViewBag.Departments = await _spaces.GetDepartmentsAsync();
        return View(await _spaces.GetAllForAdminAsync(actor));
    }

    [HttpGet]
    public async Task<IActionResult> AdminSpace(int id)
    {
        var actor = await ActorAsync();
        if (!actor.IsSystemAdmin) return Forbid();
        var model = await _spaces.GetAdminAsync(id, actor);
        if (model == null) return NotFound();
        ViewData["Title"] = model.Space.Name;
        ViewData["Section"] = "Administration";
        ViewBag.Departments = await _spaces.GetDepartmentsAsync();
        ViewBag.Employees = await _spaces.GetActiveEmployeesAsync();
        return View(model);
    }

    private async Task<IActionResult> AdminDone(Func<TaskActor, Task<TmResult>> op, int? spaceId)
    {
        var actor = await ActorAsync();
        if (!actor.IsSystemAdmin) return Forbid();
        var r = await op(actor);
        if (r.Success) TempData["Message"] = Text("Tm_Msg_Saved");
        else if (r.ErrorKey == "Tm_Err_NotFound") return NotFound();
        else TempData["Error"] = Text(r.ErrorKey!);
        var target = r.Success && spaceId == null ? r.Id : spaceId;
        return target is int sid ? RedirectToAction(nameof(AdminSpace), new { id = sid }) : RedirectToAction(nameof(Admin));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveSpace(WorkSpaceInput input) => AdminDone(a => _spaces.SaveSpaceAsync(input, a), input.Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddMember(int workSpaceId, int employeeId, string role) =>
        AdminDone(a => _spaces.AddMemberAsync(workSpaceId, employeeId, role ?? "", a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SetMember(int workSpaceId, int memberId, string role, bool isActive) =>
        AdminDone(a => _spaces.SetMemberAsync(memberId, role ?? "", isActive, a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddStatus(int workSpaceId, string name, string? nameFa, string category) =>
        AdminDone(a => _spaces.AddStatusAsync(workSpaceId, name ?? "", nameFa, category ?? "", a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> EditStatus(int workSpaceId, int statusId, string name, string? nameFa, string category, int sortOrder, bool isActive) =>
        AdminDone(a => _spaces.UpdateStatusAsync(statusId, name ?? "", nameFa, category ?? "", sortOrder, isActive, a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SetTransition(int workSpaceId, int fromStatusId, int toStatusId, bool allowed) =>
        AdminDone(a => _spaces.SetTransitionAsync(workSpaceId, fromStatusId, toStatusId, allowed, a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> AddType(int workSpaceId, string name, string? nameFa, string icon, bool isSubtask = false) =>
        AdminDone(a => _spaces.AddTypeAsync(workSpaceId, name ?? "", nameFa, icon ?? "", isSubtask, a), workSpaceId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SetTypeActive(int workSpaceId, int typeId, bool isActive) =>
        AdminDone(a => _spaces.SetTypeActiveAsync(typeId, isActive, a), workSpaceId);
}
