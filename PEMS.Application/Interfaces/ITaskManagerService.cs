using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// Task Manager روی همان TaskItem. هر عملیات با TaskActor (هویت سرور) و دسترسی سطح-شیء کنترل می‌شود: دانستن شناسه
/// دسترسی نمی‌دهد. مجوزهای نقش موجود (Task.View/Create/Edit) دروازه‌ی اول‌اند و قواعد سطح-شیء (مسئول/محول‌کننده/
/// گزارش‌دهنده/ناظر/عضو فضا/Lead) دروازه‌ی دوم. وضعیت‌های فضا فقط تغییر وضعیت عادی‌اند؛ تأیید رسمی فقط با
/// PEMS.WorkflowPlatform. هیچ مسئولی به‌صورت خودکار/پیش‌فرض انتخاب نمی‌شود.
/// </summary>
public interface ITaskManagerService
{
    /// <summary>TaskActor از کاربر احرازشده‌ی سرور؛ EmployeeId از ApplicationUser.EmployeeId (بدون حدس).</summary>
    Task<TaskActor> ResolveActorAsync(int? userId, IReadOnlyList<string> roles, string? displayName);

    Task<List<WorkSpaceDto>> GetAccessibleSpacesAsync(TaskActor actor);
    Task<WorkSpaceViewDto?> GetSpaceViewAsync(string key, TaskActor actor);
    Task<BacklogDto?> GetBacklogAsync(string key, TaskActor actor);
    Task<BoardDto?> GetBoardAsync(string key, TaskActor actor);
    Task<PagedResult<WorkItemListItemDto>> QueryAsync(WorkItemQuery query, TaskActor actor);
    Task<WorkItemDetailDto?> GetDetailAsync(int id, TaskActor actor);
    Task<List<WorkItemHistoryEntryDto>?> GetHistoryAsync(int id, TaskActor actor);

    /// <summary>مسئولان مجاز: وظیفه‌ی شخصی = کارمندان فعال؛ وظیفه‌ی فضا = اعضای فعال همان فضا.</summary>
    Task<AssigneeResolutionDto> GetEligibleAssigneesAsync(int? workSpaceId, TaskActor actor);

    Task<TmResult> CreateAsync(WorkItemCreateInput input, TaskActor actor);
    Task<TmResult> UpdateAsync(WorkItemUpdateInput input, TaskActor actor);
    Task<TmResult> AssignAsync(int id, int? assigneeEmployeeId, string? rowVersion, TaskActor actor);

    /// <summary>وظیفه‌ی فضا: statusId؛ وظیفه‌ی شخصی: legacyStatus. گذار باید در طرح وضعیت مجاز باشد.</summary>
    Task<TmResult> TransitionAsync(int id, int? statusId, string? legacyStatus, string? note, string? rowVersion, TaskActor actor);

    Task<TmResult> SetArchivedAsync(int id, bool archived, TaskActor actor);
    Task<TmResult> MoveRankAsync(int id, bool up, TaskActor actor);
    Task<TmResult> ReorderAsync(int workSpaceId, int? sprintId, IReadOnlyList<int> orderedIds, TaskActor actor);
    Task<TmResult> SetSprintAsync(int id, int? sprintId, TaskActor actor);

    /// <summary>action: assign | status | sprint | archive. هر مورد جداگانه مجوز می‌گیرد.</summary>
    Task<BulkResultDto> BulkAsync(IReadOnlyList<int> ids, string action, string? value, TaskActor actor);

    Task<TmResult> AddCommentAsync(int id, string body, TaskActor actor);
    Task<TmResult> DeleteCommentAsync(int commentId, TaskActor actor);
    Task<TmResult> AddLinkAsync(int sourceId, string linkType, string targetKeyOrId, TaskActor actor);
    Task<TmResult> RemoveLinkAsync(int linkId, TaskActor actor);
    Task<TmResult> SetWatchingAsync(int id, bool watch, TaskActor actor);
}

/// <summary>
/// پیکربندی فضاها (فقط SystemAdmin: فضا، اعضا، وضعیت‌ها، گذارها، انواع) و Sprintها (فقط Lead همان فضا).
/// </summary>
public interface IWorkSpaceService
{
    Task<List<WorkSpaceDto>> GetAllForAdminAsync(TaskActor actor);
    Task<WorkSpaceAdminDto?> GetAdminAsync(int id, TaskActor actor);
    Task<TmResult> SaveSpaceAsync(WorkSpaceInput input, TaskActor actor);
    Task<TmResult> AddMemberAsync(int workSpaceId, int employeeId, string role, TaskActor actor);
    Task<TmResult> SetMemberAsync(int memberId, string role, bool isActive, TaskActor actor);
    Task<TmResult> AddStatusAsync(int workSpaceId, string name, string? nameFa, string category, TaskActor actor);
    Task<TmResult> UpdateStatusAsync(int statusId, string name, string? nameFa, string category, int sortOrder, bool isActive, TaskActor actor);
    Task<TmResult> SetTransitionAsync(int workSpaceId, int fromStatusId, int toStatusId, bool allowed, TaskActor actor);
    Task<TmResult> AddTypeAsync(int workSpaceId, string name, string? nameFa, string icon, bool isSubtask, TaskActor actor);
    Task<TmResult> SetTypeActiveAsync(int typeId, bool isActive, TaskActor actor);
    Task<List<(int Id, string Name)>> GetDepartmentsAsync();
    Task<List<EligibleAssigneeDto>> GetActiveEmployeesAsync();

    Task<TmResult> CreateSprintAsync(int workSpaceId, string name, string? goal, DateTime? start, DateTime? end, TaskActor actor);
    Task<TmResult> UpdateSprintAsync(int sprintId, string name, string? goal, DateTime? start, DateTime? end, TaskActor actor);
    Task<TmResult> StartSprintAsync(int sprintId, TaskActor actor);
    /// <summary>وظایف ناتمام به Sprint مقصد (Future همان فضا) یا Backlog (null) منتقل می‌شوند.</summary>
    Task<TmResult> CompleteSprintAsync(int sprintId, int? moveUnfinishedToSprintId, TaskActor actor);
}
