using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services.TaskManager;

/// <summary>
/// تنها نقطه‌ی تصمیم دسترسی Task Manager. دو دروازه: (۱) مجوز نقش موجود Task.View/Create/Edit؛ (۲) سطح-شیء:
///  * دیدن: عضو فضای وظیفه (عضو صریح فعال یا کارمند فعالِ واحد مالک فضا) یا درگیر در وظیفه (مسئول، گزارش‌دهنده،
///    محول‌کننده) یا ناظر. وظیفه‌ی شخصی (بدون فضا) فقط برای درگیرها و ناظرها.
///  * ویرایش/تغییر وضعیت/پیوند: درگیرها یا Lead فضا. ارجاع و بایگانی: گزارش‌دهنده/محول‌کننده یا Lead.
///  * رتبه/Sprint: فقط Lead فضا.
/// SystemAdmin هیچ دسترسی ضمنی به وظایف ندارد (فقط پیکربندی فضاها) - تصمیم سیاستی «دید مدیریتی کل سازمان» باز است.
/// </summary>
internal sealed class TaskAccess
{
    public TaskActor Actor { get; }
    public bool RoleView { get; }
    public bool RoleCreate { get; }
    public bool RoleEdit { get; }
    public HashSet<int> MemberSpaces { get; }
    public HashSet<int> LeadSpaces { get; }
    private readonly HashSet<int> _watched;

    private TaskAccess(TaskActor actor, bool view, bool create, bool edit, HashSet<int> members, HashSet<int> leads, HashSet<int> watched)
    {
        Actor = actor; RoleView = view; RoleCreate = create; RoleEdit = edit; MemberSpaces = members; LeadSpaces = leads; _watched = watched;
    }

    private int Emp => Actor.EmployeeId ?? -1;
    private int Uid => Actor.UserId ?? -1;

    public static async Task<TaskAccess> LoadAsync(PEMSDbContext ctx, IPermissionService permissions, TaskActor actor)
    {
        var view = await permissions.HasPermissionAsync(actor.Roles, "Task", "View");
        var create = await permissions.HasPermissionAsync(actor.Roles, "Task", "Create");
        var edit = await permissions.HasPermissionAsync(actor.Roles, "Task", "Edit");

        var members = new HashSet<int>();
        var leads = new HashSet<int>();
        var watched = new HashSet<int>();
        if (actor.EmployeeId is int emp)
        {
            var employee = await ctx.Employees.AsNoTracking().Where(e => e.EmployeeId == emp)
                .Select(e => new { e.IsActive, e.DepartmentId }).FirstOrDefaultAsync();
            if (employee is { IsActive: true })
            {
                var explicitRows = await ctx.WorkSpaceMembers.AsNoTracking()
                    .Where(m => m.EmployeeId == emp && m.IsActive && m.WorkSpace!.IsActive)
                    .Select(m => new { m.WorkSpaceId, m.Role }).ToListAsync();
                foreach (var r in explicitRows)
                {
                    members.Add(r.WorkSpaceId);
                    if (r.Role == WorkSpaceMemberRoles.Lead) leads.Add(r.WorkSpaceId);
                }
                var implicitSpaces = await ctx.WorkSpaces.AsNoTracking()
                    .Where(s => s.IsActive && s.OwnerDepartmentId == employee.DepartmentId)
                    .Select(s => s.WorkSpaceId).ToListAsync();
                members.UnionWith(implicitSpaces);
            }
            watched = (await ctx.WorkItemWatchers.AsNoTracking().Where(w => w.EmployeeId == emp).Select(w => w.TaskItemId).ToListAsync()).ToHashSet();
        }
        return new TaskAccess(actor, view, create, edit, members, leads, watched);
    }

    /// <summary>فیلتر پرس‌وجو: فقط وظایفی که کاربر اجازه‌ی دیدنشان را دارد (بدون Task.View هیچ).</summary>
    public IQueryable<TaskItem> Visible(IQueryable<TaskItem> q, PEMSDbContext ctx)
    {
        if (!RoleView) return q.Where(_ => false);
        var spaces = MemberSpaces.ToList();
        var emp = Emp;
        var uid = Uid;
        return q.Where(t =>
            (t.WorkSpaceId != null && spaces.Contains(t.WorkSpaceId.Value)) ||
            (emp > 0 && (t.AssignedToEmployeeId == emp || t.ReporterEmployeeId == emp)) ||
            (uid > 0 && t.AssignedByApplicationUserId == uid) ||
            (emp > 0 && ctx.WorkItemWatchers.Any(w => w.TaskItemId == t.TaskItemId && w.EmployeeId == emp)));
    }

    public bool IsInvolved(TaskItem t) =>
        (Emp > 0 && (t.AssignedToEmployeeId == Emp || t.ReporterEmployeeId == Emp)) ||
        (Uid > 0 && t.AssignedByApplicationUserId == Uid);

    public bool IsOwner(TaskItem t) =>
        (Emp > 0 && t.ReporterEmployeeId == Emp) || (Uid > 0 && t.AssignedByApplicationUserId == Uid);

    public bool IsLead(TaskItem t) => t.WorkSpaceId is int s && LeadSpaces.Contains(s);
    public bool IsLeadOf(int workSpaceId) => LeadSpaces.Contains(workSpaceId);

    public bool CanView(TaskItem t) => RoleView &&
        ((t.WorkSpaceId is int s && MemberSpaces.Contains(s)) || IsInvolved(t) || _watched.Contains(t.TaskItemId));

    public bool CanEdit(TaskItem t) => RoleEdit && t.IsActive && (IsInvolved(t) || IsLead(t));
    public bool CanTransition(TaskItem t) => RoleEdit && t.IsActive && (IsInvolved(t) || IsLead(t));
    public bool CanAssign(TaskItem t) => RoleEdit && t.IsActive && (IsOwner(t) || IsLead(t));
    public bool CanArchive(TaskItem t) => RoleEdit && (IsOwner(t) || IsLead(t));
    public bool CanComment(TaskItem t) => RoleEdit && CanView(t);
    public bool CanCreateIn(int workSpaceId) => RoleCreate && MemberSpaces.Contains(workSpaceId);
    public bool CanRank(int workSpaceId) => RoleEdit && LeadSpaces.Contains(workSpaceId);
    public bool IsWatching(int taskItemId) => _watched.Contains(taskItemId);

    /// <summary>همان قاعده‌ی CanTransition روی ردیف فهرست (برای نمایش کنترل‌ها).</summary>
    public bool CanTransition(WorkItemListItemDto i) => RoleEdit && i.IsActive &&
        ((Emp > 0 && (i.AssigneeEmployeeId == Emp || i.ReporterEmployeeId == Emp)) || (Uid > 0 && i.AssignedByUserId == Uid) ||
         (i.WorkSpaceId is int s && LeadSpaces.Contains(s)));
}
