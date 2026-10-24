namespace PEMS.Application.DTOs;

/// <summary>
/// کاربر جاری برای همه‌ی تصمیم‌های Task Manager. هویت فقط از کوکی/زمینه‌ی سرور ساخته می‌شود، هرگز از ورودی فرم.
/// </summary>
public sealed record TaskActor(int? UserId, int? EmployeeId, IReadOnlyList<string> Roles, string? DisplayName)
{
    public const string SystemAdminRole = "SystemAdmin";
    public bool IsSystemAdmin => Roles.Contains(SystemAdminRole);
}

/// <summary>نتیجه‌ی عملیات؛ ErrorKey کلید بومی‌سازی (Tm_Err_*) است.</summary>
public sealed record TmResult(bool Success, string? ErrorKey = null, int? Id = null, string? Detail = null)
{
    public static TmResult Ok(int? id = null) => new(true, null, id);
    public static TmResult Fail(string key, string? detail = null) => new(false, key, null, detail);
}

public sealed class WorkSpaceDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string? Description { get; set; }
    public int? OwnerDepartmentId { get; set; }
    public string? OwnerDepartmentName { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int OpenItemCount { get; set; }
    public bool IsLead { get; set; }
    public bool IsMember { get; set; }
}

public sealed class WorkSpaceMemberDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class WorkItemStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string Category { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WorkItemTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string Icon { get; set; } = "";
    public bool IsSubtask { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SprintDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Goal { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string State { get; set; } = "";
    public int ToDoCount { get; set; }
    public int InProgressCount { get; set; }
    public int DoneCount { get; set; }
    public int ItemCount => ToDoCount + InProgressCount + DoneCount;
}

/// <summary>یک فضا با پیکربندی و دسترسی‌های کاربر جاری (برای صفحه‌های Backlog/Board/ایجاد).</summary>
public sealed class WorkSpaceViewDto
{
    public WorkSpaceDto Space { get; set; } = new();
    public List<WorkItemStatusDto> Statuses { get; set; } = new();
    public List<WorkItemTypeDto> Types { get; set; } = new();
    public List<SprintDto> Sprints { get; set; } = new();
    /// <summary>گذارهای مجاز (FromStatusId → ToStatusIds) برای اعتبارسنجی سمت کاربر؛ مرجع نهایی سرور است.</summary>
    public Dictionary<int, List<int>> Transitions { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanManageSprints { get; set; }
    public bool CanRank { get; set; }
}

public sealed class WorkItemListItemDto
{
    public int Id { get; set; }
    public string? Key { get; set; }
    public string Title { get; set; } = "";
    public int? WorkSpaceId { get; set; }
    public string? WorkSpaceKey { get; set; }
    public int? TypeId { get; set; }
    public string? TypeName { get; set; }
    public string? TypeNameFa { get; set; }
    public string? TypeIcon { get; set; }
    public int? StatusId { get; set; }
    public string Status { get; set; } = "";
    public string? StatusName { get; set; }
    public string? StatusNameFa { get; set; }
    public string StatusCategory { get; set; } = "";
    public string Priority { get; set; } = "Normal";
    public int? AssigneeEmployeeId { get; set; }
    public string? AssigneeName { get; set; }
    public string? ReporterName { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public bool IsOverdue { get; set; }
    public int? SprintId { get; set; }
    public decimal? StoryPoints { get; set; }
    public double Rank { get; set; }
    public int? ParentId { get; set; }
    public string? ParentKey { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? RowVersion { get; set; }
    public int? ReporterEmployeeId { get; set; }
    public int? AssignedByUserId { get; set; }
    /// <summary>کاربر جاری می‌تواند وضعیت را تغییر دهد (فقط راهنمای UI؛ سرور دوباره بررسی می‌کند).</summary>
    public bool CanTransition { get; set; }
}

public sealed class WorkItemLinkDto
{
    public int Id { get; set; }
    public string LinkType { get; set; } = "";
    /// <summary>Outward = این وظیفه منبع است؛ Inward = این وظیفه مقصد است.</summary>
    public bool Outward { get; set; }
    public int OtherId { get; set; }
    /// <summary>false = کاربر وظیفه‌ی طرف دیگر را نمی‌بیند؛ فقط «مورد محدود» نمایش داده می‌شود (بدون کلید و عنوان).</summary>
    public bool OtherVisible { get; set; }
    public string? OtherKey { get; set; }
    public string? OtherTitle { get; set; }
    public string? OtherStatusCategory { get; set; }
    public bool CanRemove { get; set; }
}

public sealed class WorkItemCommentDto
{
    public int Id { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public bool IsDeleted { get; set; }
    public bool CanDelete { get; set; }
}

public sealed class WorkItemHistoryEntryDto
{
    public DateTime Timestamp { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string? Changes { get; set; }
}

public sealed class WorkItemStatusOptionDto
{
    /// <summary>برای وظیفه‌ی فضا شناسه‌ی وضعیت؛ برای وظیفه‌ی شخصی null و Value رشته‌ی وضعیت قدیمی است.</summary>
    public int? StatusId { get; set; }
    public string Value { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string Category { get; set; } = "";
}

public sealed class WorkItemDetailDto
{
    public WorkItemListItemDto Item { get; set; } = new();
    public string? Description { get; set; }
    public string? AssignedByName { get; set; }
    public int? ReporterEmployeeId { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? CompletionNote { get; set; }
    public string? RelatedType { get; set; }
    public int? RelatedId { get; set; }
    public string? WorkSpaceName { get; set; }
    public string? WorkSpaceNameFa { get; set; }
    public string? ParentTitle { get; set; }
    public string? SprintName { get; set; }
    public bool IsLegacy { get; set; }
    public List<WorkItemListItemDto> Subtasks { get; set; } = new();
    public List<WorkItemLinkDto> Links { get; set; } = new();
    public List<WorkItemCommentDto> Comments { get; set; } = new();
    public List<string> Watchers { get; set; } = new();
    public List<WorkItemStatusOptionDto> AllowedTransitions { get; set; } = new();
    public List<WorkItemTypeDto> Types { get; set; } = new();
    public List<SprintDto> OpenSprints { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool CanTransition { get; set; }
    public bool CanAssign { get; set; }
    public bool CanComment { get; set; }
    public bool CanArchive { get; set; }
    public bool CanLink { get; set; }
    public bool IsWatching { get; set; }
}

public sealed class WorkItemCreateInput
{
    public int? WorkSpaceId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? TypeId { get; set; }
    public string? Priority { get; set; }
    public int? AssigneeEmployeeId { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public int? ParentId { get; set; }
    public int? SprintId { get; set; }
    public decimal? StoryPoints { get; set; }
}

public sealed class WorkItemUpdateInput
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? TypeId { get; set; }
    public string? Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public int? ParentId { get; set; }
    public decimal? StoryPoints { get; set; }
    public string? RowVersion { get; set; }
}

/// <summary>فیلتر بسته و اعتبارسنجی‌شده (بدون زبان پرس‌وجوی آزاد). همه‌ی نتایج به دسترسی کاربر محدودند.</summary>
public sealed class WorkItemQuery
{
    public int? WorkSpaceId { get; set; }
    public string? Text { get; set; }
    /// <summary>me | unassigned | {EmployeeId}</summary>
    public string? Assignee { get; set; }
    /// <summary>ToDo | InProgress | Done | NotDone</summary>
    public string? StatusCategory { get; set; }
    public string? Priority { get; set; }
    public int? TypeId { get; set; }
    /// <summary>null = همه؛ 0 = Backlog (بدون Sprint)؛ n = همان Sprint</summary>
    public int? SprintId { get; set; }
    public bool IncludeArchived { get; set; }
    /// <summary>rank | due | priority | created | updated | key</summary>
    public string? Sort { get; set; }
    public bool Descending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class EligibleAssigneeDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>نتیجه‌ی حل مسئولان مجاز؛ State همان واژگان Enterprise Actor Resolution (RESOLVED / NO_CONFIGURATION / NO_ELIGIBLE_ACTOR).</summary>
public sealed class AssigneeResolutionDto
{
    public string State { get; set; } = "RESOLVED";
    public List<EligibleAssigneeDto> Candidates { get; set; } = new();
}

public sealed class BulkResultDto
{
    public int Succeeded { get; set; }
    public List<string> Failed { get; set; } = new();
}

/// <summary>Backlog یک فضا: پنل Sprintهای باز + Backlog (بدون Sprint).</summary>
public sealed class BacklogDto
{
    public WorkSpaceViewDto View { get; set; } = new();
    public List<(SprintDto Sprint, List<WorkItemListItemDto> Items)> SprintPanels { get; set; } = new();
    public List<WorkItemListItemDto> BacklogItems { get; set; } = new();
}

public sealed class BoardColumnDto
{
    public WorkItemStatusDto Status { get; set; } = new();
    public List<WorkItemListItemDto> Items { get; set; } = new();
}

public sealed class BoardDto
{
    public WorkSpaceViewDto View { get; set; } = new();
    public SprintDto? ActiveSprint { get; set; }
    public List<BoardColumnDto> Columns { get; set; } = new();
}

public sealed class WorkSpaceInput
{
    public int? Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string? Description { get; set; }
    public int? OwnerDepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WorkSpaceAdminDto
{
    public WorkSpaceDto Space { get; set; } = new();
    public List<WorkSpaceMemberDto> Members { get; set; } = new();
    public List<WorkItemStatusDto> Statuses { get; set; } = new();
    public List<WorkItemTypeDto> Types { get; set; } = new();
    public HashSet<(int From, int To)> Transitions { get; set; } = new();
}
