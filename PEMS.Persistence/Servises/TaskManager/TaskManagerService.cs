using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services.TaskManager;

/// <summary>
/// پیاده‌سازی Task Manager روی TaskItem. همه‌ی تصمیم‌های دسترسی در TaskAccess؛ همه‌ی تغییرات از SaveChanges (AuditLog
/// عمومی هر Insert/Update را با مقدار قبل/بعد ثبت می‌کند) و حذف‌ها نرم‌اند. کلید وظیفه با NumberingService و در همان
/// تراکنش درج ساخته می‌شود (بدون حفره و تکرار).
/// </summary>
public class TaskManagerService : ITaskManagerService
{
    public const string NumberingType = "WorkItem";
    private const int MaxTitle = 300;
    private const int MaxDescription = 8000;
    private const int MaxComment = 5000;

    private readonly PEMSDbContext _ctx;
    private readonly IPermissionService _permissions;
    private readonly INumberingService _numbering;
    private readonly INotificationService? _notifications;

    public TaskManagerService(PEMSDbContext ctx, IPermissionService permissions, INumberingService numbering, INotificationService? notifications = null)
    {
        _ctx = ctx;
        _permissions = permissions;
        _numbering = numbering;
        _notifications = notifications;
    }

    private Task<TaskAccess> AccessAsync(TaskActor actor) => TaskAccess.LoadAsync(_ctx, _permissions, actor);

    public async Task<TaskActor> ResolveActorAsync(int? userId, IReadOnlyList<string> roles, string? displayName)
    {
        int? employeeId = null;
        if (userId is int uid)
            employeeId = await _ctx.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        return new TaskActor(userId, employeeId, roles, displayName);
    }

    // ==================================================================================== reads

    public async Task<List<WorkSpaceDto>> GetAccessibleSpacesAsync(TaskActor actor)
    {
        var a = await AccessAsync(actor);
        if (!a.RoleView || a.MemberSpaces.Count == 0) return new List<WorkSpaceDto>();
        var ids = a.MemberSpaces.ToList();
        var spaces = await _ctx.WorkSpaces.AsNoTracking().Where(s => ids.Contains(s.WorkSpaceId) && s.IsActive)
            .OrderBy(s => s.Key)
            .Select(s => new WorkSpaceDto
            {
                Id = s.WorkSpaceId, Key = s.Key, Name = s.Name, NameFa = s.NameFa, Description = s.Description,
                OwnerDepartmentId = s.OwnerDepartmentId, OwnerDepartmentName = s.OwnerDepartment != null ? s.OwnerDepartment.Name : null,
                IsActive = s.IsActive,
                MemberCount = s.Members.Count(m => m.IsActive),
                OpenItemCount = _ctx.TaskItems.Count(t => t.WorkSpaceId == s.WorkSpaceId && t.IsActive && t.Status != TaskItemStatuses.Completed && t.Status != TaskItemStatuses.Cancelled)
            }).ToListAsync();
        foreach (var s in spaces) { s.IsMember = true; s.IsLead = a.LeadSpaces.Contains(s.Id); }
        return spaces;
    }

    public async Task<WorkSpaceViewDto?> GetSpaceViewAsync(string key, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        return await BuildSpaceViewAsync(key, a);
    }

    private async Task<WorkSpaceViewDto?> BuildSpaceViewAsync(string key, TaskAccess a)
    {
        if (!a.RoleView || string.IsNullOrWhiteSpace(key)) return null;
        var space = await _ctx.WorkSpaces.AsNoTracking().Include(s => s.OwnerDepartment)
            .FirstOrDefaultAsync(s => s.Key == key && s.IsActive);
        if (space == null || !a.MemberSpaces.Contains(space.WorkSpaceId)) return null;

        var statuses = await _ctx.WorkItemStatuses.AsNoTracking().Where(s => s.WorkSpaceId == space.WorkSpaceId && s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.WorkItemStatusId)
            .Select(s => new WorkItemStatusDto { Id = s.WorkItemStatusId, Name = s.Name, NameFa = s.NameFa, Category = s.Category, SortOrder = s.SortOrder })
            .ToListAsync();
        var types = await _ctx.WorkItemTypes.AsNoTracking().Where(t => t.WorkSpaceId == space.WorkSpaceId && t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.WorkItemTypeId)
            .Select(t => new WorkItemTypeDto { Id = t.WorkItemTypeId, Name = t.Name, NameFa = t.NameFa, Icon = t.Icon, IsSubtask = t.IsSubtask })
            .ToListAsync();
        var transitions = await _ctx.WorkItemStatusTransitions.AsNoTracking().Where(t => t.WorkSpaceId == space.WorkSpaceId)
            .Select(t => new { t.FromStatusId, t.ToStatusId }).ToListAsync();

        return new WorkSpaceViewDto
        {
            Space = new WorkSpaceDto
            {
                Id = space.WorkSpaceId, Key = space.Key, Name = space.Name, NameFa = space.NameFa, Description = space.Description,
                OwnerDepartmentId = space.OwnerDepartmentId, OwnerDepartmentName = space.OwnerDepartment?.Name, IsActive = space.IsActive,
                IsMember = true, IsLead = a.IsLeadOf(space.WorkSpaceId)
            },
            Statuses = statuses,
            Types = types,
            Sprints = await SprintsAsync(space.WorkSpaceId, includeClosed: false),
            Transitions = transitions.GroupBy(t => t.FromStatusId).ToDictionary(g => g.Key, g => g.Select(x => x.ToStatusId).ToList()),
            CanCreate = a.CanCreateIn(space.WorkSpaceId),
            CanManageSprints = a.CanRank(space.WorkSpaceId),
            CanRank = a.CanRank(space.WorkSpaceId)
        };
    }

    private async Task<List<SprintDto>> SprintsAsync(int workSpaceId, bool includeClosed)
    {
        var sprints = await _ctx.Sprints.AsNoTracking()
            .Where(s => s.WorkSpaceId == workSpaceId && (includeClosed || s.State != SprintStates.Closed))
            .OrderBy(s => s.State == SprintStates.Active ? 0 : 1).ThenBy(s => s.StartDate ?? DateTime.MaxValue).ThenBy(s => s.SprintId)
            .ToListAsync();
        var ids = sprints.Select(s => s.SprintId).ToList();
        var counts = await _ctx.TaskItems.AsNoTracking().Where(t => t.SprintId != null && ids.Contains(t.SprintId.Value) && t.IsActive)
            .GroupBy(t => new { t.SprintId, t.Status })
            .Select(g => new { g.Key.SprintId, g.Key.Status, N = g.Count() }).ToListAsync();
        return sprints.Select(s => new SprintDto
        {
            Id = s.SprintId, Name = s.Name, Goal = s.Goal, StartDate = s.StartDate, EndDate = s.EndDate, State = s.State,
            ToDoCount = counts.Where(c => c.SprintId == s.SprintId && c.Status == TaskItemStatuses.Open).Sum(c => c.N),
            InProgressCount = counts.Where(c => c.SprintId == s.SprintId && c.Status == TaskItemStatuses.InProgress).Sum(c => c.N),
            DoneCount = counts.Where(c => c.SprintId == s.SprintId && TaskItemStatuses.IsClosed(c.Status)).Sum(c => c.N),
        }).ToList();
    }

    public async Task<BacklogDto?> GetBacklogAsync(string key, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var view = await BuildSpaceViewAsync(key, a);
        if (view == null) return null;
        var spaceId = view.Space.Id;

        var items = await Project(a.Visible(_ctx.TaskItems.AsNoTracking(), _ctx)
                .Where(t => t.WorkSpaceId == spaceId && t.IsActive))
            .OrderBy(t => t.Rank).ThenBy(t => t.Id).ToListAsync();
        Finish(items, a);

        var result = new BacklogDto { View = view };
        foreach (var sprint in view.Sprints)
            result.SprintPanels.Add((sprint, items.Where(i => i.SprintId == sprint.Id).ToList()));
        // Backlog: بدون Sprint و هنوز تمام‌نشده
        result.BacklogItems = items.Where(i => i.SprintId == null && i.StatusCategory != WorkItemStatusCategories.Done).ToList();
        return result;
    }

    public async Task<BoardDto?> GetBoardAsync(string key, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var view = await BuildSpaceViewAsync(key, a);
        if (view == null) return null;
        var spaceId = view.Space.Id;
        var active = view.Sprints.FirstOrDefault(s => s.State == SprintStates.Active);

        // با Sprint فعال فقط موارد همان Sprint (Scrum)؛ بدون Sprint فعال همه‌ی موارد فعال فضا (حالت Kanban برای واحدهای بدون Sprint)
        var q = a.Visible(_ctx.TaskItems.AsNoTracking(), _ctx).Where(t => t.WorkSpaceId == spaceId && t.IsActive);
        if (active != null) q = q.Where(t => t.SprintId == active.Id);
        var items = await Project(q).OrderBy(t => t.Rank).ThenBy(t => t.Id).ToListAsync();
        Finish(items, a);

        return new BoardDto
        {
            View = view,
            ActiveSprint = active,
            Columns = view.Statuses.Select(s => new BoardColumnDto { Status = s, Items = items.Where(i => i.StatusId == s.Id).ToList() }).ToList()
        };
    }

    public async Task<PagedResult<WorkItemListItemDto>> QueryAsync(WorkItemQuery query, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var q = a.Visible(_ctx.TaskItems.AsNoTracking(), _ctx);

        if (!query.IncludeArchived) q = q.Where(t => t.IsActive);
        if (query.WorkSpaceId.HasValue) q = q.Where(t => t.WorkSpaceId == query.WorkSpaceId);
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            q = q.Where(t => t.Title.Contains(text) || (t.ItemKey != null && t.ItemKey.Contains(text)) || (t.Description != null && t.Description.Contains(text)));
        }
        if (query.Assignee == "me") q = q.Where(t => t.AssignedToEmployeeId == (actor.EmployeeId ?? -1));
        else if (query.Assignee == "unassigned") q = q.Where(t => t.AssignedToEmployeeId == null);
        else if (int.TryParse(query.Assignee, out var assigneeId)) q = q.Where(t => t.AssignedToEmployeeId == assigneeId);

        q = query.StatusCategory switch
        {
            WorkItemStatusCategories.ToDo => q.Where(t => t.Status == TaskItemStatuses.Open),
            WorkItemStatusCategories.InProgress => q.Where(t => t.Status == TaskItemStatuses.InProgress),
            WorkItemStatusCategories.Done => q.Where(t => t.Status == TaskItemStatuses.Completed || t.Status == TaskItemStatuses.Cancelled),
            "NotDone" => q.Where(t => t.Status != TaskItemStatuses.Completed && t.Status != TaskItemStatuses.Cancelled),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(query.Priority) && TaskItemPriorities.All.Contains(query.Priority)) q = q.Where(t => t.Priority == query.Priority);
        if (query.TypeId.HasValue) q = q.Where(t => t.WorkItemTypeId == query.TypeId);
        if (query.SprintId == 0) q = q.Where(t => t.SprintId == null);
        else if (query.SprintId.HasValue) q = q.Where(t => t.SprintId == query.SprintId);

        var total = await q.CountAsync();
        var page = query.Page < 1 ? 1 : query.Page;
        var size = DataGridPageSize.Normalize(query.PageSize);
        var projected = Project(q);
        var sorted = (query.Sort ?? "updated") switch
        {
            "rank" => query.Descending ? projected.OrderByDescending(t => t.Rank) : projected.OrderBy(t => t.Rank),
            "due" => query.Descending ? projected.OrderByDescending(t => t.DueDate) : projected.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate),
            "priority" => query.Descending
                ? projected.OrderBy(t => t.Priority == "Urgent" ? 0 : t.Priority == "High" ? 1 : t.Priority == "Normal" ? 2 : 3)
                : projected.OrderByDescending(t => t.Priority == "Urgent" ? 0 : t.Priority == "High" ? 1 : t.Priority == "Normal" ? 2 : 3),
            "created" => query.Descending ? projected.OrderByDescending(t => t.CreatedDate) : projected.OrderBy(t => t.CreatedDate),
            "key" => query.Descending ? projected.OrderByDescending(t => t.Key) : projected.OrderBy(t => t.Key),
            _ => projected.OrderByDescending(t => t.ModifiedDate ?? t.CreatedDate)
        };
        var items = await ((IOrderedQueryable<WorkItemListItemDto>)sorted).ThenBy(t => t.Id).Skip((page - 1) * size).Take(size).ToListAsync();
        Finish(items, a);
        return new PagedResult<WorkItemListItemDto> { Items = items, Page = page, PageSize = size, TotalCount = total };
    }

    private IQueryable<WorkItemListItemDto> Project(IQueryable<TaskItem> q) => q.Select(t => new WorkItemListItemDto
    {
        Id = t.TaskItemId,
        Key = t.ItemKey,
        Title = t.Title,
        WorkSpaceId = t.WorkSpaceId,
        WorkSpaceKey = t.WorkSpace != null ? t.WorkSpace.Key : null,
        TypeId = t.WorkItemTypeId,
        TypeName = t.WorkItemType != null ? t.WorkItemType.Name : null,
        TypeNameFa = t.WorkItemType != null ? t.WorkItemType.NameFa : null,
        TypeIcon = t.WorkItemType != null ? t.WorkItemType.Icon : null,
        StatusId = t.WorkItemStatusId,
        Status = t.Status,
        StatusName = t.WorkItemStatus != null ? t.WorkItemStatus.Name : null,
        StatusNameFa = t.WorkItemStatus != null ? t.WorkItemStatus.NameFa : null,
        StatusCategory = t.WorkItemStatus != null ? t.WorkItemStatus.Category : "",
        Priority = t.Priority,
        AssigneeEmployeeId = t.AssignedToEmployeeId,
        AssigneeName = t.AssignedToEmployee != null ? t.AssignedToEmployee.FirstName + " " + t.AssignedToEmployee.LastName : null,
        ReporterName = t.ReporterEmployee != null ? t.ReporterEmployee.FirstName + " " + t.ReporterEmployee.LastName : t.AssignedByName,
        DueDate = t.DueDate,
        StartDate = t.StartDate,
        SprintId = t.SprintId,
        StoryPoints = t.StoryPoints,
        Rank = t.Rank,
        ParentId = t.ParentTaskItemId,
        ParentKey = t.ParentTaskItem != null ? t.ParentTaskItem.ItemKey : null,
        IsActive = t.IsActive,
        CreatedDate = t.CreatedDate,
        ModifiedDate = t.ModifiedDate,
        RowVersion = t.RowVersion != null ? Convert.ToBase64String(t.RowVersion) : null,
        ReporterEmployeeId = t.ReporterEmployeeId,
        AssignedByUserId = t.AssignedByApplicationUserId
    });

    private static void Finish(IEnumerable<WorkItemListItemDto> items, TaskAccess? access = null)
    {
        foreach (var i in items)
        {
            if (access != null) i.CanTransition = access.CanTransition(i);
            if (string.IsNullOrEmpty(i.StatusCategory))
                i.StatusCategory = i.Status switch
                {
                    TaskItemStatuses.InProgress => WorkItemStatusCategories.InProgress,
                    TaskItemStatuses.Completed or TaskItemStatuses.Cancelled => WorkItemStatusCategories.Done,
                    _ => WorkItemStatusCategories.ToDo
                };
            i.IsOverdue = i.DueDate.HasValue && i.DueDate.Value.Date < DateTime.Today && i.StatusCategory != WorkItemStatusCategories.Done && i.IsActive;
        }
    }

    public async Task<WorkItemDetailDto?> GetDetailAsync(int id, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == id);
        if (t == null || !a.CanView(t)) return null;

        var item = await Project(_ctx.TaskItems.AsNoTracking().Where(x => x.TaskItemId == id)).SingleAsync();
        Finish(new[] { item }, a);

        var detail = new WorkItemDetailDto
        {
            Item = item,
            Description = t.Description,
            AssignedByName = t.AssignedByName,
            ReporterEmployeeId = t.ReporterEmployeeId,
            CompletedDate = t.CompletedDate,
            CompletionNote = t.CompletionNote,
            RelatedType = t.RelatedType,
            RelatedId = t.RelatedId,
            IsLegacy = t.WorkSpaceId == null,
            CanEdit = a.CanEdit(t),
            CanTransition = a.CanTransition(t),
            CanAssign = a.CanAssign(t),
            CanComment = a.CanComment(t),
            CanArchive = a.CanArchive(t),
            CanLink = a.CanEdit(t),
            IsWatching = a.IsWatching(t.TaskItemId)
        };

        if (t.WorkSpaceId is int spaceId)
        {
            var space = await _ctx.WorkSpaces.AsNoTracking().FirstAsync(s => s.WorkSpaceId == spaceId);
            detail.WorkSpaceName = space.Name;
            detail.WorkSpaceNameFa = space.NameFa;
            detail.Types = await _ctx.WorkItemTypes.AsNoTracking().Where(x => x.WorkSpaceId == spaceId && x.IsActive).OrderBy(x => x.SortOrder)
                .Select(x => new WorkItemTypeDto { Id = x.WorkItemTypeId, Name = x.Name, NameFa = x.NameFa, Icon = x.Icon, IsSubtask = x.IsSubtask }).ToListAsync();
            detail.OpenSprints = await SprintsAsync(spaceId, includeClosed: false);
            if (t.SprintId is int sprintId)
                detail.SprintName = await _ctx.Sprints.AsNoTracking().Where(s => s.SprintId == sprintId).Select(s => s.Name).FirstOrDefaultAsync();

            var allowedTo = await _ctx.WorkItemStatusTransitions.AsNoTracking()
                .Where(x => x.WorkSpaceId == spaceId && x.FromStatusId == t.WorkItemStatusId)
                .Select(x => x.ToStatusId).ToListAsync();
            detail.AllowedTransitions = await _ctx.WorkItemStatuses.AsNoTracking()
                .Where(s => s.WorkSpaceId == spaceId && s.IsActive && allowedTo.Contains(s.WorkItemStatusId))
                .OrderBy(s => s.SortOrder)
                .Select(s => new WorkItemStatusOptionDto { StatusId = s.WorkItemStatusId, Value = s.WorkItemStatusId.ToString(), Name = s.Name, NameFa = s.NameFa, Category = s.Category })
                .ToListAsync();
        }
        else
        {
            detail.AllowedTransitions = (TaskItemStatuses.Transitions.TryGetValue(t.Status, out var targets) ? targets : Array.Empty<string>())
                .Select(s => new WorkItemStatusOptionDto { Value = s, Name = s, Category = s switch
                {
                    TaskItemStatuses.InProgress => WorkItemStatusCategories.InProgress,
                    TaskItemStatuses.Completed or TaskItemStatuses.Cancelled => WorkItemStatusCategories.Done,
                    _ => WorkItemStatusCategories.ToDo
                } }).ToList();
        }

        if (t.ParentTaskItemId is int parentId)
        {
            var parent = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == parentId);
            if (parent != null && a.CanView(parent)) detail.ParentTitle = parent.Title;
            else { detail.Item.ParentKey = null; }   // مورد محدود: کلید و عنوان والد نمایش داده نمی‌شود
        }

        var subtasks = await Project(a.Visible(_ctx.TaskItems.AsNoTracking(), _ctx).Where(x => x.ParentTaskItemId == id && x.IsActive))
            .OrderBy(x => x.Rank).ThenBy(x => x.Id).ToListAsync();
        Finish(subtasks, a);
        detail.Subtasks = subtasks;

        var links = await _ctx.WorkItemLinks.AsNoTracking()
            .Where(l => l.SourceTaskItemId == id || l.TargetTaskItemId == id)
            .Select(l => new { l.WorkItemLinkId, l.LinkType, l.SourceTaskItemId, l.TargetTaskItemId }).ToListAsync();
        var otherIds = links.Select(l => l.SourceTaskItemId == id ? l.TargetTaskItemId : l.SourceTaskItemId).Distinct().ToList();
        var others = await _ctx.TaskItems.AsNoTracking().Where(x => otherIds.Contains(x.TaskItemId)).ToListAsync();
        foreach (var l in links)
        {
            var otherId = l.SourceTaskItemId == id ? l.TargetTaskItemId : l.SourceTaskItemId;
            var other = others.FirstOrDefault(o => o.TaskItemId == otherId);
            var visible = other != null && a.CanView(other);
            detail.Links.Add(new WorkItemLinkDto
            {
                Id = l.WorkItemLinkId, LinkType = l.LinkType, Outward = l.SourceTaskItemId == id, OtherId = visible ? otherId : 0,
                OtherVisible = visible, OtherKey = visible ? other!.ItemKey : null, OtherTitle = visible ? other!.Title : null,
                OtherStatusCategory = visible ? (other!.Status == TaskItemStatuses.InProgress ? WorkItemStatusCategories.InProgress
                    : TaskItemStatuses.IsClosed(other.Status) ? WorkItemStatusCategories.Done : WorkItemStatusCategories.ToDo) : null,
                CanRemove = a.CanEdit(t)
            });
        }

        detail.Comments = await _ctx.WorkItemComments.AsNoTracking().Where(c => c.TaskItemId == id)
            .OrderByDescending(c => c.CreatedDate).ThenByDescending(c => c.WorkItemCommentId)
            .Select(c => new WorkItemCommentDto
            {
                Id = c.WorkItemCommentId, AuthorName = c.AuthorName ?? "", Body = c.IsDeleted ? "" : c.Body, CreatedDate = c.CreatedDate,
                IsDeleted = c.IsDeleted, CanDelete = !c.IsDeleted && actor.UserId != null && c.AuthorUserId == actor.UserId
            }).ToListAsync();

        detail.Watchers = await _ctx.WorkItemWatchers.AsNoTracking().Where(w => w.TaskItemId == id)
            .OrderBy(w => w.Employee!.FirstName).Select(w => w.Employee!.FirstName + " " + w.Employee.LastName).ToListAsync();
        return detail;
    }

    public async Task<List<WorkItemHistoryEntryDto>?> GetHistoryAsync(int id, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == id);
        if (t == null || !a.CanView(t)) return null;
        var key = id.ToString();
        return await _ctx.AuditLogs.AsNoTracking()
            .Where(l => l.EntityName == nameof(TaskItem) && l.EntityKeyValue == key)
            .OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.AuditLogId)
            .Take(200)
            .Select(l => new WorkItemHistoryEntryDto { Timestamp = l.Timestamp, UserName = l.UserName, Action = l.Action, Changes = l.ChangedFieldsSummary })
            .ToListAsync();
    }

    public async Task<AssigneeResolutionDto> GetEligibleAssigneesAsync(int? workSpaceId, TaskActor actor)
    {
        if (workSpaceId == null)
        {
            var all = await _ctx.Employees.AsNoTracking().Where(e => e.IsActive)
                .OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.EmployeeId)
                .Select(e => new EligibleAssigneeDto { EmployeeId = e.EmployeeId, Name = e.FirstName + " " + e.LastName }).ToListAsync();
            return new AssigneeResolutionDto { State = all.Count == 0 ? "NO_ELIGIBLE_ACTOR" : "RESOLVED", Candidates = all };
        }

        var space = await _ctx.WorkSpaces.AsNoTracking().FirstOrDefaultAsync(s => s.WorkSpaceId == workSpaceId && s.IsActive);
        if (space == null) return new AssigneeResolutionDto { State = "NO_CONFIGURATION" };
        var hasMembers = await _ctx.WorkSpaceMembers.AnyAsync(m => m.WorkSpaceId == space.WorkSpaceId && m.IsActive);
        if (space.OwnerDepartmentId == null && !hasMembers) return new AssigneeResolutionDto { State = "NO_CONFIGURATION" };

        var ids = await EligibleEmployeeIdsQuery(space).ToListAsync();
        var candidates = await _ctx.Employees.AsNoTracking().Where(e => ids.Contains(e.EmployeeId))
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.EmployeeId)
            .Select(e => new EligibleAssigneeDto { EmployeeId = e.EmployeeId, Name = e.FirstName + " " + e.LastName }).ToListAsync();
        return new AssigneeResolutionDto { State = candidates.Count == 0 ? "NO_ELIGIBLE_ACTOR" : "RESOLVED", Candidates = candidates };
    }

    /// <summary>کارمندان فعالِ عضو صریح فعال یا کارمندان فعالِ واحد مالک فضا. هیچ جایگزین/پیش‌فرض دیگری.</summary>
    private IQueryable<int> EligibleEmployeeIdsQuery(WorkSpace space)
    {
        var spaceId = space.WorkSpaceId;
        var dept = space.OwnerDepartmentId;
        return _ctx.Employees.Where(e => e.IsActive &&
                ((dept != null && e.DepartmentId == dept) ||
                 _ctx.WorkSpaceMembers.Any(m => m.WorkSpaceId == spaceId && m.IsActive && m.EmployeeId == e.EmployeeId)))
            .Select(e => e.EmployeeId);
    }

    private async Task<string?> CheckAssigneeAsync(int? workSpaceId, int? assigneeEmployeeId)
    {
        if (assigneeEmployeeId == null) return null;
        if (workSpaceId == null)
            return await _ctx.Employees.AnyAsync(e => e.EmployeeId == assigneeEmployeeId && e.IsActive) ? null : "Tm_Err_AssigneeNotEligible";
        var space = await _ctx.WorkSpaces.AsNoTracking().FirstOrDefaultAsync(s => s.WorkSpaceId == workSpaceId);
        if (space == null) return "Tm_Err_NotFound";
        return await EligibleEmployeeIdsQuery(space).AnyAsync(e => e == assigneeEmployeeId) ? null : "Tm_Err_AssigneeNotEligible";
    }

    // ==================================================================================== writes

    private static string? ValidateFields(string title, string? description, string? priority, DateTime? start, DateTime? due, decimal? points)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Tm_Err_TitleRequired";
        if (title.Trim().Length > MaxTitle) return "Tm_Err_TitleTooLong";
        if (description != null && description.Length > MaxDescription) return "Tm_Err_DescriptionTooLong";
        if (priority != null && !TaskItemPriorities.All.Contains(priority)) return "Tm_Err_InvalidPriority";
        if (start.HasValue && due.HasValue && start.Value.Date > due.Value.Date) return "Tm_Err_StartAfterDue";
        if (points.HasValue && (points < 0 || points > 1000)) return "Tm_Err_InvalidPoints";
        return null;
    }

    public async Task<TmResult> CreateAsync(WorkItemCreateInput input, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        if (!a.RoleCreate) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (actor.UserId == null) return TmResult.Fail("Tm_Err_NotAuthorized");
        var error = ValidateFields(input.Title, input.Description, input.Priority, input.StartDate, input.DueDate, input.StoryPoints);
        if (error != null) return TmResult.Fail(error);

        var task = new TaskItem
        {
            Title = input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Priority = input.Priority ?? "Normal",
            DueDate = input.DueDate,
            StartDate = input.StartDate,
            StoryPoints = input.StoryPoints,
            AssignedByApplicationUserId = actor.UserId,
            AssignedByName = actor.DisplayName,
            ReporterEmployeeId = actor.EmployeeId,
            Status = TaskItemStatuses.Open,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        WorkSpace? space = null;
        if (input.WorkSpaceId is int spaceId)
        {
            space = await _ctx.WorkSpaces.FirstOrDefaultAsync(s => s.WorkSpaceId == spaceId && s.IsActive);
            if (space == null) return TmResult.Fail("Tm_Err_NotFound");
            if (!a.CanCreateIn(spaceId)) return TmResult.Fail("Tm_Err_NotAuthorized");

            var type = input.TypeId is int typeId
                ? await _ctx.WorkItemTypes.FirstOrDefaultAsync(t => t.WorkItemTypeId == typeId && t.WorkSpaceId == spaceId && t.IsActive)
                : await _ctx.WorkItemTypes.Where(t => t.WorkSpaceId == spaceId && t.IsActive && !t.IsSubtask).OrderBy(t => t.SortOrder).ThenBy(t => t.WorkItemTypeId).FirstOrDefaultAsync();
            if (type == null) return TmResult.Fail("Tm_Err_InvalidType");

            var initial = await _ctx.WorkItemStatuses.Where(s => s.WorkSpaceId == spaceId && s.IsActive && s.Category == WorkItemStatusCategories.ToDo)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.WorkItemStatusId).FirstOrDefaultAsync();
            if (initial == null) return TmResult.Fail("Tm_Err_NoInitialStatus");

            if (input.ParentId is int parentId)
            {
                var parentError = await CheckParentAsync(a, spaceId, null, parentId);
                if (parentError != null) return TmResult.Fail(parentError);
            }
            else if (type.IsSubtask) return TmResult.Fail("Tm_Err_SubtaskNeedsParent");

            if (input.SprintId is int sprintId && !await _ctx.Sprints.AnyAsync(s => s.SprintId == sprintId && s.WorkSpaceId == spaceId && s.State != SprintStates.Closed))
                return TmResult.Fail("Tm_Err_InvalidSprint");

            task.WorkSpaceId = spaceId;
            task.WorkItemTypeId = type.WorkItemTypeId;
            task.WorkItemStatusId = initial.WorkItemStatusId;
            task.Status = WorkItemStatusCategories.LegacyStatus(initial.Category);
            task.ParentTaskItemId = input.ParentId;
            task.SprintId = input.SprintId;
            task.Rank = (await _ctx.TaskItems.Where(t => t.WorkSpaceId == spaceId).MaxAsync(t => (double?)t.Rank) ?? 0) + 1;
        }
        else if (input.TypeId != null || input.ParentId != null || input.SprintId != null || input.StoryPoints != null)
        {
            return TmResult.Fail("Tm_Err_PersonalTaskFields");
        }

        var assigneeError = await CheckAssigneeAsync(task.WorkSpaceId, input.AssigneeEmployeeId);
        if (assigneeError != null) return TmResult.Fail(assigneeError);
        task.AssignedToEmployeeId = input.AssigneeEmployeeId;

        if (space != null) await EnsureSequenceAsync(space.Key);

        await InTransactionAsync(async () =>
        {
            if (space != null)
            {
                var n = await _numbering.GetNextValueAsync(NumberingType, space.Key);
                task.ItemKey = $"{space.Key}-{n}";
            }
            _ctx.TaskItems.Add(task);
            await _ctx.SaveChangesAsync();
            AddWatcher(task.TaskItemId, actor.EmployeeId);
            AddWatcher(task.TaskItemId, task.AssignedToEmployeeId);
            await _ctx.SaveChangesAsync();
        });

        if (task.AssignedToEmployeeId is int assignee && assignee != actor.EmployeeId)
            await NotifyEmployeesAsync(new[] { assignee }, "وظیفه‌ی جدید به شما محول شد / A task was assigned to you", task);
        return TmResult.Ok(task.TaskItemId);
    }

    internal static NumberingSequence NewSequence(string spaceKey) => new()
    {
        DocumentType = NumberingType, ScopeKey = spaceKey, CurrentValue = 0, Prefix = "WI", PadWidth = 1, IncludeScopeInFormat = true, CreatedDate = DateTime.Now
    };

    /// <summary>
    /// NumberingService در تراکنش مشترک رقابت «اولین درج ردیف شمارنده» را به فراخوان برمی‌گرداند؛ ردیف پیش از تراکنش ساخته می‌شود
    /// (فضاهای جدید آن را هنگام ساخت دارند؛ این فقط برای فضاهای قدیمی/دستی است). درج هم‌زمان دیگر = ردیف موجود است.
    /// </summary>
    private async Task EnsureSequenceAsync(string spaceKey)
    {
        if (await _ctx.NumberingSequences.AnyAsync(n => n.DocumentType == NumberingType && n.ScopeKey == spaceKey)) return;
        var row = NewSequence(spaceKey);
        _ctx.NumberingSequences.Add(row);
        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            _ctx.Entry(row).State = EntityState.Detached;
            if (!await _ctx.NumberingSequences.AsNoTracking().AnyAsync(n => n.DocumentType == NumberingType && n.ScopeKey == spaceKey)) throw;
        }
    }

    private async Task InTransactionAsync(Func<Task> work)
    {
        if (!_ctx.Database.IsRelational())
        {
            await work();
            return;
        }
        var strategy = _ctx.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _ctx.Database.BeginTransactionAsync();
            await work();
            await tx.CommitAsync();
        });
    }

    private void AddWatcher(int taskItemId, int? employeeId)
    {
        if (employeeId is not int emp) return;
        if (_ctx.WorkItemWatchers.Local.Any(w => w.TaskItemId == taskItemId && w.EmployeeId == emp)) return;
        if (_ctx.WorkItemWatchers.Any(w => w.TaskItemId == taskItemId && w.EmployeeId == emp)) return;
        _ctx.WorkItemWatchers.Add(new WorkItemWatcher { TaskItemId = taskItemId, EmployeeId = emp, CreatedDate = DateTime.Now });
    }

    private async Task<string?> CheckParentAsync(TaskAccess a, int spaceId, int? selfId, int parentId)
    {
        if (selfId == parentId) return "Tm_Err_InvalidParent";
        var parent = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.TaskItemId == parentId);
        if (parent == null || parent.WorkSpaceId != spaceId || !parent.IsActive || !a.CanView(parent)) return "Tm_Err_InvalidParent";
        // بدون چرخه: هیچ جدِ والد نباید خود وظیفه باشد
        var cursor = parent.ParentTaskItemId;
        for (var depth = 0; cursor != null && depth < 50; depth++)
        {
            if (cursor == selfId) return "Tm_Err_InvalidParent";
            cursor = await _ctx.TaskItems.AsNoTracking().Where(t => t.TaskItemId == cursor).Select(t => t.ParentTaskItemId).FirstOrDefaultAsync();
        }
        return null;
    }

    private async Task<(TaskItem? Task, TaskAccess Access, string? Error)> LoadForWriteAsync(int id, TaskActor actor, string? rowVersion)
    {
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.FirstOrDefaultAsync(x => x.TaskItemId == id);
        if (t == null || !a.CanView(t)) return (null, a, "Tm_Err_NotFound");
        if (!string.IsNullOrEmpty(rowVersion))
        {
            try { _ctx.Entry(t).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion); }
            catch (FormatException) { return (null, a, "Tm_Err_Concurrency"); }
        }
        return (t, a, null);
    }

    private async Task<TmResult> SaveAsync(int id)
    {
        try
        {
            await _ctx.SaveChangesAsync();
            return TmResult.Ok(id);
        }
        catch (DbUpdateConcurrencyException)
        {
            _ctx.ChangeTracker.Clear();
            return TmResult.Fail("Tm_Err_Concurrency");
        }
    }

    public async Task<TmResult> UpdateAsync(WorkItemUpdateInput input, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(input.Id, actor, input.RowVersion);
        if (t == null) return TmResult.Fail(error!);
        if (!a.CanEdit(t)) return TmResult.Fail("Tm_Err_NotAuthorized");
        var validation = ValidateFields(input.Title, input.Description, input.Priority, input.StartDate, input.DueDate, input.StoryPoints);
        if (validation != null) return TmResult.Fail(validation);

        if (t.WorkSpaceId is int spaceId)
        {
            if (input.TypeId is int typeId && typeId != t.WorkItemTypeId)
            {
                var type = await _ctx.WorkItemTypes.FirstOrDefaultAsync(x => x.WorkItemTypeId == typeId && x.WorkSpaceId == spaceId && x.IsActive);
                if (type == null) return TmResult.Fail("Tm_Err_InvalidType");
                if (type.IsSubtask && (input.ParentId ?? t.ParentTaskItemId) == null) return TmResult.Fail("Tm_Err_SubtaskNeedsParent");
                t.WorkItemTypeId = typeId;
            }
            if (input.ParentId != t.ParentTaskItemId)
            {
                if (input.ParentId is int parentId)
                {
                    var parentError = await CheckParentAsync(a, spaceId, t.TaskItemId, parentId);
                    if (parentError != null) return TmResult.Fail(parentError);
                }
                t.ParentTaskItemId = input.ParentId;
            }
            t.StoryPoints = input.StoryPoints;
        }
        else if (input.TypeId != null || input.ParentId != null || input.StoryPoints != null)
        {
            return TmResult.Fail("Tm_Err_PersonalTaskFields");
        }

        t.Title = input.Title.Trim();
        t.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        t.Priority = input.Priority ?? t.Priority;
        t.DueDate = input.DueDate;
        t.StartDate = input.StartDate;
        t.ModifiedDate = DateTime.Now;
        return await SaveAsync(t.TaskItemId);
    }

    public async Task<TmResult> AssignAsync(int id, int? assigneeEmployeeId, string? rowVersion, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(id, actor, rowVersion);
        if (t == null) return TmResult.Fail(error!);
        if (!a.CanAssign(t)) return TmResult.Fail("Tm_Err_NotAuthorized");
        var assigneeError = await CheckAssigneeAsync(t.WorkSpaceId, assigneeEmployeeId);
        if (assigneeError != null) return TmResult.Fail(assigneeError);
        if (t.AssignedToEmployeeId == assigneeEmployeeId) return TmResult.Ok(id);

        t.AssignedToEmployeeId = assigneeEmployeeId;
        t.ModifiedDate = DateTime.Now;
        AddWatcher(t.TaskItemId, assigneeEmployeeId);
        var result = await SaveAsync(id);
        if (result.Success && assigneeEmployeeId is int emp && emp != actor.EmployeeId)
            await NotifyEmployeesAsync(new[] { emp }, "وظیفه‌ای به شما محول شد / A task was assigned to you", t);
        return result;
    }

    public async Task<TmResult> TransitionAsync(int id, int? statusId, string? legacyStatus, string? note, string? rowVersion, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(id, actor, rowVersion);
        if (t == null) return TmResult.Fail(error!);
        if (!a.CanTransition(t)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (note != null && note.Length > 2000) return TmResult.Fail("Tm_Err_NoteTooLong");

        var wasClosed = TaskItemStatuses.IsClosed(t.Status);
        if (t.WorkSpaceId is int spaceId)
        {
            if (statusId is not int target) return TmResult.Fail("Tm_Err_InvalidTransition");
            if (target == t.WorkItemStatusId) return TmResult.Ok(id);
            var status = await _ctx.WorkItemStatuses.FirstOrDefaultAsync(s => s.WorkItemStatusId == target && s.WorkSpaceId == spaceId && s.IsActive);
            if (status == null) return TmResult.Fail("Tm_Err_InvalidTransition");
            var allowed = await _ctx.WorkItemStatusTransitions.AnyAsync(x => x.WorkSpaceId == spaceId && x.FromStatusId == t.WorkItemStatusId && x.ToStatusId == target);
            if (!allowed) return TmResult.Fail("Tm_Err_InvalidTransition");
            t.WorkItemStatusId = target;
            t.Status = WorkItemStatusCategories.LegacyStatus(status.Category);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(legacyStatus) || !TaskItemStatuses.All.Contains(legacyStatus)) return TmResult.Fail("Tm_Err_InvalidTransition");
            if (legacyStatus == t.Status) return TmResult.Ok(id);
            if (!TaskItemStatuses.IsAllowed(t.Status, legacyStatus)) return TmResult.Fail("Tm_Err_InvalidTransition");
            t.Status = legacyStatus;
        }

        var isClosed = TaskItemStatuses.IsClosed(t.Status);
        if (isClosed && !wasClosed) { t.CompletedDate = DateTime.Now; t.CompletionNote = string.IsNullOrWhiteSpace(note) ? t.CompletionNote : note.Trim(); }
        if (!isClosed && wasClosed) t.CompletedDate = null;
        t.ModifiedDate = DateTime.Now;

        var result = await SaveAsync(id);
        if (!result.Success) return result;

        if (isClosed && _notifications != null)
            await _notifications.ResolveRelatedAsync(null, "TaskItem", id);
        var interested = new[] { t.ReporterEmployeeId }.Where(e => e is int x && x != actor.EmployeeId).Select(e => e!.Value).ToList();
        if (interested.Count > 0) await NotifyEmployeesAsync(interested, "وضعیت وظیفه تغییر کرد / Task status changed", t);
        return result;
    }

    public async Task<TmResult> SetArchivedAsync(int id, bool archived, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(id, actor, null);
        if (t == null) return TmResult.Fail(error!);
        if (!a.CanArchive(t)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (t.IsActive == !archived) return TmResult.Ok(id);
        t.IsActive = !archived;
        t.ModifiedDate = DateTime.Now;
        return await SaveAsync(id);
    }

    public async Task<TmResult> MoveRankAsync(int id, bool up, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(id, actor, null);
        if (t == null) return TmResult.Fail(error!);
        if (t.WorkSpaceId is not int spaceId || !a.CanRank(spaceId)) return TmResult.Fail("Tm_Err_NotAuthorized");

        var bucket = await _ctx.TaskItems.Where(x => x.WorkSpaceId == spaceId && x.IsActive && x.SprintId == t.SprintId)
            .OrderBy(x => x.Rank).ThenBy(x => x.TaskItemId).ToListAsync();
        var index = bucket.FindIndex(x => x.TaskItemId == id);
        var target = up ? index - 1 : index + 1;
        if (index < 0 || target < 0 || target >= bucket.Count) return TmResult.Ok(id);
        (bucket[index], bucket[target]) = (bucket[target], bucket[index]);
        for (var i = 0; i < bucket.Count; i++) bucket[i].Rank = i + 1;
        return await SaveAsync(id);
    }

    public async Task<TmResult> ReorderAsync(int workSpaceId, int? sprintId, IReadOnlyList<int> orderedIds, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        if (!a.CanRank(workSpaceId)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (orderedIds.Count == 0 || orderedIds.Count > 500 || orderedIds.Distinct().Count() != orderedIds.Count) return TmResult.Fail("Tm_Err_StaleOrder");
        var moved = await _ctx.TaskItems.Where(x => orderedIds.Contains(x.TaskItemId)).ToListAsync();
        // همه باید فعال و در همین فضا/Sprint باشند؛ در غیر این صورت صفحه کهنه است
        if (moved.Count != orderedIds.Count || moved.Any(x => x.WorkSpaceId != workSpaceId || !x.IsActive || x.SprintId != sprintId))
            return TmResult.Fail("Tm_Err_StaleOrder");
        // فقط جایگاه‌های همین موارد بین خودشان جابه‌جا می‌شود (موارد نمایش‌داده‌نشده مثل Done جای خود را نگه می‌دارند)
        var slots = moved.Select(x => x.Rank).OrderBy(r => r).ToList();
        for (var i = 1; i < slots.Count; i++) if (slots[i] <= slots[i - 1]) slots[i] = slots[i - 1] + 0.001;
        for (var i = 0; i < orderedIds.Count; i++) moved.First(x => x.TaskItemId == orderedIds[i]).Rank = slots[i];
        return await SaveAsync(0);
    }

    public async Task<TmResult> SetSprintAsync(int id, int? sprintId, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(id, actor, null);
        if (t == null) return TmResult.Fail(error!);
        if (t.WorkSpaceId is not int spaceId || !a.CanRank(spaceId) || !t.IsActive) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (sprintId is int sid && !await _ctx.Sprints.AnyAsync(s => s.SprintId == sid && s.WorkSpaceId == spaceId && s.State != SprintStates.Closed))
            return TmResult.Fail("Tm_Err_InvalidSprint");
        if (t.SprintId == sprintId) return TmResult.Ok(id);
        t.SprintId = sprintId;
        t.Rank = (await _ctx.TaskItems.Where(x => x.WorkSpaceId == spaceId && x.SprintId == sprintId).MaxAsync(x => (double?)x.Rank) ?? 0) + 1;
        t.ModifiedDate = DateTime.Now;
        return await SaveAsync(id);
    }

    public async Task<BulkResultDto> BulkAsync(IReadOnlyList<int> ids, string action, string? value, TaskActor actor)
    {
        var result = new BulkResultDto();
        foreach (var id in ids.Distinct().Take(200))
        {
            TmResult r = action switch
            {
                "assign" => await AssignAsync(id, int.TryParse(value, out var emp) ? emp : null, null, actor),
                "status" => await BulkTransitionAsync(id, value, actor),
                "sprint" => await SetSprintAsync(id, int.TryParse(value, out var sid) && sid > 0 ? sid : null, actor),
                "archive" => await SetArchivedAsync(id, true, actor),
                _ => TmResult.Fail("Tm_Err_InvalidAction")
            };
            if (r.Success) result.Succeeded++;
            else
            {
                var key = await _ctx.TaskItems.AsNoTracking().Where(t => t.TaskItemId == id).Select(t => t.ItemKey).FirstOrDefaultAsync();
                result.Failed.Add($"{key ?? "#" + id}: {r.ErrorKey}");
            }
        }
        return result;
    }

    private async Task<TmResult> BulkTransitionAsync(int id, string? value, TaskActor actor)
    {
        var isSpaceTask = await _ctx.TaskItems.AsNoTracking().Where(t => t.TaskItemId == id).Select(t => t.WorkSpaceId != null).FirstOrDefaultAsync();
        return isSpaceTask
            ? await TransitionAsync(id, int.TryParse(value, out var s) ? s : null, null, null, null, actor)
            : await TransitionAsync(id, null, value, null, null, actor);
    }

    public async Task<TmResult> AddCommentAsync(int id, string body, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == id);
        if (t == null || !a.CanView(t)) return TmResult.Fail("Tm_Err_NotFound");
        if (!a.CanComment(t) || actor.UserId == null) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (string.IsNullOrWhiteSpace(body)) return TmResult.Fail("Tm_Err_CommentRequired");
        if (body.Length > MaxComment) return TmResult.Fail("Tm_Err_CommentTooLong");

        // متن ساده ذخیره می‌شود و در View به‌صورت HTML-encoded نمایش داده می‌شود (هیچ HTML کاربر رندر نمی‌شود)
        var comment = new WorkItemComment
        {
            TaskItemId = id, AuthorEmployeeId = actor.EmployeeId, AuthorUserId = actor.UserId, AuthorName = actor.DisplayName,
            Body = body.Trim(), CreatedDate = DateTime.Now
        };
        _ctx.WorkItemComments.Add(comment);
        AddWatcher(id, actor.EmployeeId);
        await _ctx.SaveChangesAsync();

        var audience = await _ctx.WorkItemWatchers.AsNoTracking().Where(w => w.TaskItemId == id).Select(w => w.EmployeeId).ToListAsync();
        if (t.AssignedToEmployeeId is int assignee) audience.Add(assignee);
        audience = audience.Distinct().Where(e => e != actor.EmployeeId).ToList();
        if (audience.Count > 0) await NotifyEmployeesAsync(audience, "نظر جدید روی وظیفه / New comment on a task", t);
        return TmResult.Ok(comment.WorkItemCommentId);
    }

    public async Task<TmResult> DeleteCommentAsync(int commentId, TaskActor actor)
    {
        var comment = await _ctx.WorkItemComments.FirstOrDefaultAsync(c => c.WorkItemCommentId == commentId);
        if (comment == null || comment.IsDeleted) return TmResult.Fail("Tm_Err_NotFound");
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.AsNoTracking().FirstAsync(x => x.TaskItemId == comment.TaskItemId);
        if (!a.CanView(t)) return TmResult.Fail("Tm_Err_NotFound");
        if (actor.UserId == null || comment.AuthorUserId != actor.UserId || !a.RoleEdit) return TmResult.Fail("Tm_Err_NotAuthorized");
        comment.IsDeleted = true;   // حذف نرم؛ متن در AuditLog (Update) باقی می‌ماند
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(comment.TaskItemId);
    }

    public async Task<TmResult> AddLinkAsync(int sourceId, string linkType, string targetKeyOrId, TaskActor actor)
    {
        var (t, a, error) = await LoadForWriteAsync(sourceId, actor, null);
        if (t == null) return TmResult.Fail(error!);
        if (!a.CanEdit(t)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (!WorkItemLinkTypes.All.Contains(linkType)) return TmResult.Fail("Tm_Err_InvalidLinkType");
        if (string.IsNullOrWhiteSpace(targetKeyOrId)) return TmResult.Fail("Tm_Err_LinkTargetNotFound");

        var trimmed = targetKeyOrId.Trim();
        var target = int.TryParse(trimmed.TrimStart('#'), out var targetId)
            ? await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == targetId)
            : await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.ItemKey == trimmed.ToUpperInvariant());
        // مقصدی که کاربر نمی‌بیند همان «یافت نشد» است (وجودش افشا نمی‌شود)
        if (target == null || !a.CanView(target)) return TmResult.Fail("Tm_Err_LinkTargetNotFound");
        if (target.TaskItemId == sourceId) return TmResult.Fail("Tm_Err_SelfLink");
        if (await _ctx.WorkItemLinks.AnyAsync(l => l.SourceTaskItemId == sourceId && l.LinkType == linkType && l.TargetTaskItemId == target.TaskItemId))
            return TmResult.Fail("Tm_Err_LinkExists");

        _ctx.WorkItemLinks.Add(new WorkItemLink { SourceTaskItemId = sourceId, LinkType = linkType, TargetTaskItemId = target.TaskItemId, CreatedDate = DateTime.Now });
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(sourceId);
    }

    public async Task<TmResult> RemoveLinkAsync(int linkId, TaskActor actor)
    {
        var link = await _ctx.WorkItemLinks.FirstOrDefaultAsync(l => l.WorkItemLinkId == linkId);
        if (link == null) return TmResult.Fail("Tm_Err_NotFound");
        var a = await AccessAsync(actor);
        var source = await _ctx.TaskItems.AsNoTracking().FirstAsync(x => x.TaskItemId == link.SourceTaskItemId);
        var target = await _ctx.TaskItems.AsNoTracking().FirstAsync(x => x.TaskItemId == link.TargetTaskItemId);
        // از هر دو سمت قابل حذف است، به شرط حق ویرایش آن سمت
        if (!a.CanEdit(source) && !a.CanEdit(target)) return TmResult.Fail("Tm_Err_NotAuthorized");
        _ctx.WorkItemLinks.Remove(link);   // پیوند داده‌ی رابطه است نه تاریخچه؛ حذف در AuditLog ثبت می‌شود
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(link.SourceTaskItemId);
    }

    public async Task<TmResult> SetWatchingAsync(int id, bool watch, TaskActor actor)
    {
        var a = await AccessAsync(actor);
        var t = await _ctx.TaskItems.AsNoTracking().FirstOrDefaultAsync(x => x.TaskItemId == id);
        if (t == null || !a.CanView(t)) return TmResult.Fail("Tm_Err_NotFound");
        if (actor.EmployeeId is not int emp) return TmResult.Fail("Tm_Err_NoEmployee");
        var existing = await _ctx.WorkItemWatchers.FirstOrDefaultAsync(w => w.TaskItemId == id && w.EmployeeId == emp);
        if (watch && existing == null) _ctx.WorkItemWatchers.Add(new WorkItemWatcher { TaskItemId = id, EmployeeId = emp, CreatedDate = DateTime.Now });
        else if (!watch && existing != null) _ctx.WorkItemWatchers.Remove(existing);
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(id);
    }

    private async Task NotifyEmployeesAsync(IReadOnlyCollection<int> employeeIds, string title, TaskItem t)
    {
        if (_notifications == null || employeeIds.Count == 0) return;
        var users = await _ctx.Users.AsNoTracking().Where(u => u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value))
            .Select(u => u.Id).ToListAsync();
        var message = (t.ItemKey != null ? t.ItemKey + " · " : "") + t.Title;
        foreach (var userId in users.Distinct())
            await _notifications.CreateAsync(userId, title, message, $"/Task/Details/{t.TaskItemId}", "TaskItem", t.TaskItemId);
    }
}
