using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services.TaskManager;

/// <summary>
/// پیکربندی فضاها فقط با SystemAdmin (فضا، اعضا، طرح وضعیت، گذارها، انواع). Sprintها با Lead همان فضا.
/// هیچ عضو/واحد/مسئولی ساخته یا حدس زده نمی‌شود: اعضا فقط از کارمندان فعال موجود و واحد فقط از Departmentهای موجود.
/// </summary>
public class WorkSpaceService : IWorkSpaceService
{
    private static readonly Regex KeyPattern = new("^[A-Z][A-Z0-9]{1,9}$", RegexOptions.Compiled);
    private static readonly Regex IconPattern = new("^bi-[a-z0-9-]{1,40}$", RegexOptions.Compiled);

    private readonly PEMSDbContext _ctx;
    private readonly IPermissionService _permissions;

    public WorkSpaceService(PEMSDbContext ctx, IPermissionService permissions)
    {
        _ctx = ctx;
        _permissions = permissions;
    }

    // ==================================================================================== admin reads

    public async Task<List<WorkSpaceDto>> GetAllForAdminAsync(TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return new List<WorkSpaceDto>();
        return await _ctx.WorkSpaces.AsNoTracking().OrderBy(s => s.Key)
            .Select(s => new WorkSpaceDto
            {
                Id = s.WorkSpaceId, Key = s.Key, Name = s.Name, NameFa = s.NameFa, Description = s.Description,
                OwnerDepartmentId = s.OwnerDepartmentId, OwnerDepartmentName = s.OwnerDepartment != null ? s.OwnerDepartment.Name : null,
                IsActive = s.IsActive,
                MemberCount = s.Members.Count(m => m.IsActive),
                OpenItemCount = _ctx.TaskItems.Count(t => t.WorkSpaceId == s.WorkSpaceId && t.IsActive && t.Status != TaskItemStatuses.Completed && t.Status != TaskItemStatuses.Cancelled)
            }).ToListAsync();
    }

    public async Task<WorkSpaceAdminDto?> GetAdminAsync(int id, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return null;
        var s = await _ctx.WorkSpaces.AsNoTracking().Include(x => x.OwnerDepartment).FirstOrDefaultAsync(x => x.WorkSpaceId == id);
        if (s == null) return null;
        return new WorkSpaceAdminDto
        {
            Space = new WorkSpaceDto
            {
                Id = s.WorkSpaceId, Key = s.Key, Name = s.Name, NameFa = s.NameFa, Description = s.Description,
                OwnerDepartmentId = s.OwnerDepartmentId, OwnerDepartmentName = s.OwnerDepartment?.Name, IsActive = s.IsActive
            },
            Members = await _ctx.WorkSpaceMembers.AsNoTracking().Where(m => m.WorkSpaceId == id)
                .OrderByDescending(m => m.IsActive).ThenBy(m => m.Employee!.FirstName).ThenBy(m => m.Employee!.LastName)
                .Select(m => new WorkSpaceMemberDto
                {
                    Id = m.WorkSpaceMemberId, EmployeeId = m.EmployeeId, Name = m.Employee!.FirstName + " " + m.Employee.LastName,
                    Role = m.Role, IsActive = m.IsActive && m.Employee.IsActive
                }).ToListAsync(),
            Statuses = await _ctx.WorkItemStatuses.AsNoTracking().Where(x => x.WorkSpaceId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.WorkItemStatusId)
                .Select(x => new WorkItemStatusDto { Id = x.WorkItemStatusId, Name = x.Name, NameFa = x.NameFa, Category = x.Category, SortOrder = x.SortOrder, IsActive = x.IsActive })
                .ToListAsync(),
            Types = await _ctx.WorkItemTypes.AsNoTracking().Where(x => x.WorkSpaceId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.WorkItemTypeId)
                .Select(x => new WorkItemTypeDto { Id = x.WorkItemTypeId, Name = x.Name, NameFa = x.NameFa, Icon = x.Icon, IsSubtask = x.IsSubtask, IsActive = x.IsActive })
                .ToListAsync(),
            Transitions = (await _ctx.WorkItemStatusTransitions.AsNoTracking().Where(x => x.WorkSpaceId == id)
                .Select(x => new { x.FromStatusId, x.ToStatusId }).ToListAsync())
                .Select(x => (x.FromStatusId, x.ToStatusId)).ToHashSet()
        };
    }

    public async Task<List<(int Id, string Name)>> GetDepartmentsAsync()
    {
        var rows = await _ctx.Departments.AsNoTracking().OrderBy(d => d.Name).Select(d => new { d.DepartmentId, d.Name }).ToListAsync();
        return rows.Select(d => (d.DepartmentId, d.Name ?? "")).ToList();
    }

    public Task<List<EligibleAssigneeDto>> GetActiveEmployeesAsync() =>
        _ctx.Employees.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.EmployeeId)
            .Select(e => new EligibleAssigneeDto { EmployeeId = e.EmployeeId, Name = e.FirstName + " " + e.LastName })
            .ToListAsync();

    // ==================================================================================== admin writes

    public async Task<TmResult> SaveSpaceAsync(WorkSpaceInput input, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 150) return TmResult.Fail("Tm_Err_NameRequired");
        if (input.NameFa?.Length > 150 || input.Description?.Length > 1000) return TmResult.Fail("Tm_Err_TooLong");
        if (input.OwnerDepartmentId is int dept && !await _ctx.Departments.AnyAsync(d => d.DepartmentId == dept))
            return TmResult.Fail("Tm_Err_InvalidDepartment");

        if (input.Id is int id)
        {
            var existing = await _ctx.WorkSpaces.FirstOrDefaultAsync(s => s.WorkSpaceId == id);
            if (existing == null) return TmResult.Fail("Tm_Err_NotFound");
            // Key تغییرناپذیر است (کلید وظایف موجود به آن وابسته است)
            existing.Name = input.Name.Trim();
            existing.NameFa = Clean(input.NameFa);
            existing.Description = Clean(input.Description);
            existing.OwnerDepartmentId = input.OwnerDepartmentId;
            existing.IsActive = input.IsActive;
            await _ctx.SaveChangesAsync();
            return TmResult.Ok(id);
        }

        var key = (input.Key ?? "").Trim().ToUpperInvariant();
        if (!KeyPattern.IsMatch(key)) return TmResult.Fail("Tm_Err_InvalidKey");
        if (await _ctx.WorkSpaces.AnyAsync(s => s.Key == key)) return TmResult.Fail("Tm_Err_KeyExists");

        var space = new WorkSpace
        {
            Key = key, Name = input.Name.Trim(), NameFa = Clean(input.NameFa), Description = Clean(input.Description),
            OwnerDepartmentId = input.OwnerDepartmentId, IsActive = input.IsActive, CreatedDate = DateTime.Now
        };
        _ctx.WorkSpaces.Add(space);
        // شمارنده‌ی کلید همین فضا از ابتدا ساخته می‌شود تا اولین ایجادهای هم‌زمان (داخل تراکنش مشترک) با رقابت درج ردیف شمارنده مواجه نشوند
        if (!await _ctx.NumberingSequences.AnyAsync(n => n.DocumentType == TaskManagerService.NumberingType && n.ScopeKey == key))
            _ctx.NumberingSequences.Add(TaskManagerService.NewSequence(key));
        await _ctx.SaveChangesAsync();
        await SeedDefaultsAsync(space.WorkSpaceId);
        return TmResult.Ok(space.WorkSpaceId);
    }

    /// <summary>طرح پیش‌فرض فضا (قابل ویرایش توسط SystemAdmin): ۴ وضعیت با همه‌ی گذارها و ۵ نوع.</summary>
    private async Task SeedDefaultsAsync(int spaceId)
    {
        var statuses = new[]
        {
            new WorkItemStatus { WorkSpaceId = spaceId, Name = "To Do", NameFa = "برای انجام", Category = WorkItemStatusCategories.ToDo, SortOrder = 1 },
            new WorkItemStatus { WorkSpaceId = spaceId, Name = "In Progress", NameFa = "در حال انجام", Category = WorkItemStatusCategories.InProgress, SortOrder = 2 },
            new WorkItemStatus { WorkSpaceId = spaceId, Name = "In Review", NameFa = "در حال بررسی", Category = WorkItemStatusCategories.InProgress, SortOrder = 3 },
            new WorkItemStatus { WorkSpaceId = spaceId, Name = "Done", NameFa = "انجام‌شده", Category = WorkItemStatusCategories.Done, SortOrder = 4 },
        };
        _ctx.WorkItemStatuses.AddRange(statuses);
        _ctx.WorkItemTypes.AddRange(
            new WorkItemType { WorkSpaceId = spaceId, Name = "Task", NameFa = "وظیفه", Icon = "bi-check2-square", SortOrder = 1 },
            new WorkItemType { WorkSpaceId = spaceId, Name = "Story", NameFa = "داستان کاربر", Icon = "bi-bookmark", SortOrder = 2 },
            new WorkItemType { WorkSpaceId = spaceId, Name = "Bug", NameFa = "اشکال", Icon = "bi-bug", SortOrder = 3 },
            new WorkItemType { WorkSpaceId = spaceId, Name = "Epic", NameFa = "اپیک", Icon = "bi-lightning", SortOrder = 4 },
            new WorkItemType { WorkSpaceId = spaceId, Name = "Subtask", NameFa = "زیروظیفه", Icon = "bi-diagram-2", IsSubtask = true, SortOrder = 5 });
        await _ctx.SaveChangesAsync();

        foreach (var from in statuses)
            foreach (var to in statuses.Where(t => t != from))
                _ctx.WorkItemStatusTransitions.Add(new WorkItemStatusTransition { WorkSpaceId = spaceId, FromStatusId = from.WorkItemStatusId, ToStatusId = to.WorkItemStatusId });
        await _ctx.SaveChangesAsync();
    }

    public async Task<TmResult> AddMemberAsync(int workSpaceId, int employeeId, string role, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (!WorkSpaceMemberRoles.All.Contains(role)) return TmResult.Fail("Tm_Err_InvalidRole");
        if (!await _ctx.WorkSpaces.AnyAsync(s => s.WorkSpaceId == workSpaceId)) return TmResult.Fail("Tm_Err_NotFound");
        if (!await _ctx.Employees.AnyAsync(e => e.EmployeeId == employeeId && e.IsActive)) return TmResult.Fail("Tm_Err_InvalidEmployee");

        var existing = await _ctx.WorkSpaceMembers.FirstOrDefaultAsync(m => m.WorkSpaceId == workSpaceId && m.EmployeeId == employeeId);
        if (existing != null)
        {
            existing.IsActive = true;
            existing.Role = role;
        }
        else
        {
            _ctx.WorkSpaceMembers.Add(new WorkSpaceMember { WorkSpaceId = workSpaceId, EmployeeId = employeeId, Role = role, IsActive = true, CreatedDate = DateTime.Now });
        }
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(workSpaceId);
    }

    public async Task<TmResult> SetMemberAsync(int memberId, string role, bool isActive, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (!WorkSpaceMemberRoles.All.Contains(role)) return TmResult.Fail("Tm_Err_InvalidRole");
        var m = await _ctx.WorkSpaceMembers.FirstOrDefaultAsync(x => x.WorkSpaceMemberId == memberId);
        if (m == null) return TmResult.Fail("Tm_Err_NotFound");
        m.Role = role;
        m.IsActive = isActive;   // غیرفعال‌سازی به‌جای حذف (تاریخچه‌ی عضویت می‌ماند)
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(m.WorkSpaceId);
    }

    public async Task<TmResult> AddStatusAsync(int workSpaceId, string name, string? nameFa, string category, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        var error = ValidateName(name, nameFa) ?? (WorkItemStatusCategories.All.Contains(category) ? null : "Tm_Err_InvalidCategory");
        if (error != null) return TmResult.Fail(error);
        if (!await _ctx.WorkSpaces.AnyAsync(s => s.WorkSpaceId == workSpaceId)) return TmResult.Fail("Tm_Err_NotFound");
        var trimmed = name.Trim();
        if (await _ctx.WorkItemStatuses.AnyAsync(s => s.WorkSpaceId == workSpaceId && s.Name == trimmed)) return TmResult.Fail("Tm_Err_NameExists");
        var order = (await _ctx.WorkItemStatuses.Where(s => s.WorkSpaceId == workSpaceId).MaxAsync(s => (int?)s.SortOrder) ?? 0) + 1;
        _ctx.WorkItemStatuses.Add(new WorkItemStatus { WorkSpaceId = workSpaceId, Name = trimmed, NameFa = Clean(nameFa), Category = category, SortOrder = order });
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(workSpaceId);
    }

    public async Task<TmResult> UpdateStatusAsync(int statusId, string name, string? nameFa, string category, int sortOrder, bool isActive, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        var error = ValidateName(name, nameFa) ?? (WorkItemStatusCategories.All.Contains(category) ? null : "Tm_Err_InvalidCategory");
        if (error != null) return TmResult.Fail(error);
        var s = await _ctx.WorkItemStatuses.FirstOrDefaultAsync(x => x.WorkItemStatusId == statusId);
        if (s == null) return TmResult.Fail("Tm_Err_NotFound");
        var trimmed = name.Trim();
        if (await _ctx.WorkItemStatuses.AnyAsync(x => x.WorkSpaceId == s.WorkSpaceId && x.Name == trimmed && x.WorkItemStatusId != statusId))
            return TmResult.Fail("Tm_Err_NameExists");

        var inUse = await _ctx.TaskItems.AnyAsync(t => t.WorkItemStatusId == statusId && t.IsActive);
        // وضعیتی که وظیفه‌ی فعال دارد نه غیرفعال می‌شود و نه دسته‌اش عوض می‌شود (رشته‌ی Status قدیمی وظایف ناهمگام می‌شد)
        if (inUse && (!isActive || category != s.Category)) return TmResult.Fail("Tm_Err_StatusInUse");
        if (!isActive && s.Category == WorkItemStatusCategories.ToDo &&
            !await _ctx.WorkItemStatuses.AnyAsync(x => x.WorkSpaceId == s.WorkSpaceId && x.IsActive && x.Category == WorkItemStatusCategories.ToDo && x.WorkItemStatusId != statusId))
            return TmResult.Fail("Tm_Err_NoInitialStatus");

        s.Name = trimmed;
        s.NameFa = Clean(nameFa);
        s.Category = category;
        s.SortOrder = Math.Clamp(sortOrder, 0, 999);
        s.IsActive = isActive;
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(s.WorkSpaceId);
    }

    public async Task<TmResult> SetTransitionAsync(int workSpaceId, int fromStatusId, int toStatusId, bool allowed, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (fromStatusId == toStatusId) return TmResult.Fail("Tm_Err_InvalidTransition");
        var count = await _ctx.WorkItemStatuses.CountAsync(s => s.WorkSpaceId == workSpaceId && (s.WorkItemStatusId == fromStatusId || s.WorkItemStatusId == toStatusId));
        if (count != 2) return TmResult.Fail("Tm_Err_InvalidTransition");
        var existing = await _ctx.WorkItemStatusTransitions.FirstOrDefaultAsync(t => t.WorkSpaceId == workSpaceId && t.FromStatusId == fromStatusId && t.ToStatusId == toStatusId);
        if (allowed && existing == null)
            _ctx.WorkItemStatusTransitions.Add(new WorkItemStatusTransition { WorkSpaceId = workSpaceId, FromStatusId = fromStatusId, ToStatusId = toStatusId });
        else if (!allowed && existing != null)
            _ctx.WorkItemStatusTransitions.Remove(existing);
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(workSpaceId);
    }

    public async Task<TmResult> AddTypeAsync(int workSpaceId, string name, string? nameFa, string icon, bool isSubtask, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        var error = ValidateName(name, nameFa);
        if (error != null) return TmResult.Fail(error);
        if (string.IsNullOrWhiteSpace(icon) || !IconPattern.IsMatch(icon.Trim())) return TmResult.Fail("Tm_Err_InvalidIcon");
        if (!await _ctx.WorkSpaces.AnyAsync(s => s.WorkSpaceId == workSpaceId)) return TmResult.Fail("Tm_Err_NotFound");
        var trimmed = name.Trim();
        if (await _ctx.WorkItemTypes.AnyAsync(t => t.WorkSpaceId == workSpaceId && t.Name == trimmed)) return TmResult.Fail("Tm_Err_NameExists");
        var order = (await _ctx.WorkItemTypes.Where(t => t.WorkSpaceId == workSpaceId).MaxAsync(t => (int?)t.SortOrder) ?? 0) + 1;
        _ctx.WorkItemTypes.Add(new WorkItemType { WorkSpaceId = workSpaceId, Name = trimmed, NameFa = Clean(nameFa), Icon = icon.Trim(), IsSubtask = isSubtask, SortOrder = order });
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(workSpaceId);
    }

    public async Task<TmResult> SetTypeActiveAsync(int typeId, bool isActive, TaskActor actor)
    {
        if (!actor.IsSystemAdmin) return TmResult.Fail("Tm_Err_NotAuthorized");
        var t = await _ctx.WorkItemTypes.FirstOrDefaultAsync(x => x.WorkItemTypeId == typeId);
        if (t == null) return TmResult.Fail("Tm_Err_NotFound");
        if (!isActive && !t.IsSubtask &&
            !await _ctx.WorkItemTypes.AnyAsync(x => x.WorkSpaceId == t.WorkSpaceId && x.IsActive && !x.IsSubtask && x.WorkItemTypeId != typeId))
            return TmResult.Fail("Tm_Err_LastType");
        t.IsActive = isActive;   // وظایف موجود نوع خود را نگه می‌دارند؛ فقط برای ایجاد جدید نمایش داده نمی‌شود
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(t.WorkSpaceId);
    }

    // ==================================================================================== sprints (Lead)

    private async Task<bool> IsLeadAsync(int workSpaceId, TaskActor actor)
    {
        var access = await TaskAccess.LoadAsync(_ctx, _permissions, actor);
        return access.CanRank(workSpaceId);
    }

    private static string? ValidateSprint(string name, string? goal, DateTime? start, DateTime? end)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150) return "Tm_Err_NameRequired";
        if (goal?.Length > 1000) return "Tm_Err_TooLong";
        if (start.HasValue && end.HasValue && start.Value.Date > end.Value.Date) return "Tm_Err_StartAfterDue";
        return null;
    }

    public async Task<TmResult> CreateSprintAsync(int workSpaceId, string name, string? goal, DateTime? start, DateTime? end, TaskActor actor)
    {
        if (!await IsLeadAsync(workSpaceId, actor)) return TmResult.Fail("Tm_Err_NotAuthorized");
        var error = ValidateSprint(name, goal, start, end);
        if (error != null) return TmResult.Fail(error);
        var sprint = new Sprint { WorkSpaceId = workSpaceId, Name = name.Trim(), Goal = Clean(goal), StartDate = start, EndDate = end, State = SprintStates.Future, CreatedDate = DateTime.Now };
        _ctx.Sprints.Add(sprint);
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(sprint.SprintId);
    }

    public async Task<TmResult> UpdateSprintAsync(int sprintId, string name, string? goal, DateTime? start, DateTime? end, TaskActor actor)
    {
        var s = await _ctx.Sprints.FirstOrDefaultAsync(x => x.SprintId == sprintId);
        if (s == null) return TmResult.Fail("Tm_Err_NotFound");
        if (!await IsLeadAsync(s.WorkSpaceId, actor)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (s.State == SprintStates.Closed) return TmResult.Fail("Tm_Err_SprintClosed");
        var error = ValidateSprint(name, goal, start, end);
        if (error != null) return TmResult.Fail(error);
        s.Name = name.Trim();
        s.Goal = Clean(goal);
        s.StartDate = start;
        s.EndDate = end;
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(s.SprintId);
    }

    public async Task<TmResult> StartSprintAsync(int sprintId, TaskActor actor)
    {
        var s = await _ctx.Sprints.FirstOrDefaultAsync(x => x.SprintId == sprintId);
        if (s == null) return TmResult.Fail("Tm_Err_NotFound");
        if (!await IsLeadAsync(s.WorkSpaceId, actor)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (s.State != SprintStates.Future) return TmResult.Fail("Tm_Err_SprintState");
        if (await _ctx.Sprints.AnyAsync(x => x.WorkSpaceId == s.WorkSpaceId && x.State == SprintStates.Active))
            return TmResult.Fail("Tm_Err_ActiveSprintExists");
        s.State = SprintStates.Active;
        s.StartDate ??= DateTime.Today;
        try
        {
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // ایندکس یکتای فیلترشده‌ی UX_Sprints_OneActivePerSpace: شروع هم‌زمان دو Sprint
            _ctx.ChangeTracker.Clear();
            return TmResult.Fail("Tm_Err_ActiveSprintExists");
        }
        return TmResult.Ok(s.SprintId);
    }

    public async Task<TmResult> CompleteSprintAsync(int sprintId, int? moveUnfinishedToSprintId, TaskActor actor)
    {
        var s = await _ctx.Sprints.FirstOrDefaultAsync(x => x.SprintId == sprintId);
        if (s == null) return TmResult.Fail("Tm_Err_NotFound");
        if (!await IsLeadAsync(s.WorkSpaceId, actor)) return TmResult.Fail("Tm_Err_NotAuthorized");
        if (s.State != SprintStates.Active) return TmResult.Fail("Tm_Err_SprintState");
        if (moveUnfinishedToSprintId is int targetId &&
            (targetId == sprintId || !await _ctx.Sprints.AnyAsync(x => x.SprintId == targetId && x.WorkSpaceId == s.WorkSpaceId && x.State == SprintStates.Future)))
            return TmResult.Fail("Tm_Err_InvalidSprint");

        var unfinished = await _ctx.TaskItems
            .Where(t => t.SprintId == sprintId && t.IsActive && t.Status != TaskItemStatuses.Completed && t.Status != TaskItemStatuses.Cancelled)
            .OrderBy(t => t.Rank).ToListAsync();
        var baseRank = (await _ctx.TaskItems.Where(t => t.WorkSpaceId == s.WorkSpaceId && t.SprintId == moveUnfinishedToSprintId).MaxAsync(t => (double?)t.Rank) ?? 0);
        foreach (var t in unfinished)
        {
            t.SprintId = moveUnfinishedToSprintId;
            t.Rank = ++baseRank;
            t.ModifiedDate = DateTime.Now;
        }
        // وظایف تمام‌شده در Sprint بسته باقی می‌مانند (سابقه‌ی Sprint)
        s.State = SprintStates.Closed;
        s.CompletedDate = DateTime.Now;
        await _ctx.SaveChangesAsync();
        return TmResult.Ok(s.SprintId);
    }

    private static string? ValidateName(string name, string? nameFa)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Tm_Err_NameRequired";
        if (name.Trim().Length > 100 || nameFa?.Length > 100) return "Tm_Err_TooLong";
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
