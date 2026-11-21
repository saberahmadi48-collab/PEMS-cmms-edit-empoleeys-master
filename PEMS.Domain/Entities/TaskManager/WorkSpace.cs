using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// فضای کاری Task Manager (معادل Space/Project در Jira): صف کار، Backlog، Board و پیشوند کلید وظایف یک واحد سازمانی.
/// دسترسی: اعضای صریح (WorkSpaceMember، توسط SystemAdmin تعریف می‌شوند) + در صورت تنظیم OwnerDepartmentId،
/// کارمندان فعالی که Employee.DepartmentId آن‌ها همان واحد است. هیچ رابطه‌ی سازمانی‌ای حدس زده نمی‌شود و هیچ
/// مدیر/ادمینی به‌صورت ضمنی عضو نیست. Key پس از ساخت تغییرناپذیر است (کلید وظایف موجود به آن وابسته است).
/// </summary>
public class WorkSpace : BaseEntity
{
    public int WorkSpaceId { get; set; }

    /// <summary>پیشوند کلید وظایف (۲ تا ۱۰ حرف بزرگ/رقم، مثلاً MNT)؛ یکتا و تغییرناپذیر.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public string? Description { get; set; }

    /// <summary>واحد سازمانی مالک (اختیاری)؛ کارمندان همین واحد عضو ضمنی‌اند.</summary>
    public int? OwnerDepartmentId { get; set; }
    public Department? OwnerDepartment { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public ICollection<WorkSpaceMember> Members { get; set; } = new List<WorkSpaceMember>();
    public ICollection<WorkItemStatus> Statuses { get; set; } = new List<WorkItemStatus>();
    public ICollection<WorkItemType> Types { get; set; } = new List<WorkItemType>();
}

/// <summary>عضویت صریح در فضا. Lead = مدیریت اجرایی فضا (Sprint، ارجاع، بایگانی)؛ تعریف عضو فقط با SystemAdmin.</summary>
public class WorkSpaceMember : BaseEntity
{
    public int WorkSpaceMemberId { get; set; }
    public int WorkSpaceId { get; set; }
    public WorkSpace? WorkSpace { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Member | Lead</summary>
    public string Role { get; set; } = WorkSpaceMemberRoles.Member;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

public static class WorkSpaceMemberRoles
{
    public const string Member = "Member";
    public const string Lead = "Lead";
    public static readonly string[] All = { Member, Lead };
}

/// <summary>نوع وظیفه در یک فضا (Task، Bug، Story، Epic، Subtask و ...).</summary>
public class WorkItemType : BaseEntity
{
    public int WorkItemTypeId { get; set; }
    public int WorkSpaceId { get; set; }
    public WorkSpace? WorkSpace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    /// <summary>کلاس Bootstrap Icons (اعتبارسنجی‌شده).</summary>
    public string Icon { get; set; } = "bi-check2-square";
    public bool IsSubtask { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// وضعیت در طرح وضعیت یک فضا. فقط برای «تغییر وضعیت عادی» است: بدون بازیگر، شرط، Hook یا تأیید؛ تأیید رسمی فقط از
/// PEMS.WorkflowPlatform (تصمیم معماری DR-TM-02 - این جدول موتور گردش‌کار دوم نیست).
/// </summary>
public class WorkItemStatus : BaseEntity
{
    public int WorkItemStatusId { get; set; }
    public int WorkSpaceId { get; set; }
    public WorkSpace? WorkSpace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    /// <summary>ToDo | InProgress | Done</summary>
    public string Category { get; set; } = WorkItemStatusCategories.ToDo;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class WorkItemStatusCategories
{
    public const string ToDo = "ToDo";
    public const string InProgress = "InProgress";
    public const string Done = "Done";
    public static readonly string[] All = { ToDo, InProgress, Done };

    /// <summary>هم‌گام‌سازی رشته‌ی Status قدیمی برای کارهای من/شمارنده‌ها.</summary>
    public static string LegacyStatus(string category) => category switch
    {
        InProgress => TaskItemStatuses.InProgress,
        Done => TaskItemStatuses.Completed,
        _ => TaskItemStatuses.Open
    };
}

/// <summary>گذار مجاز بین دو وضعیت در یک فضا (اعتبارسنجی سمت سرور هر تغییر وضعیت).</summary>
public class WorkItemStatusTransition
{
    public int WorkItemStatusTransitionId { get; set; }
    public int WorkSpaceId { get; set; }
    public int FromStatusId { get; set; }
    public WorkItemStatus? FromStatus { get; set; }
    public int ToStatusId { get; set; }
    public WorkItemStatus? ToStatus { get; set; }
}

/// <summary>Sprint یک فضا. در هر فضا حداکثر یک Sprint فعال (ایندکس یکتای فیلترشده + سرویس).</summary>
public class Sprint : BaseEntity
{
    public int SprintId { get; set; }
    public int WorkSpaceId { get; set; }
    public WorkSpace? WorkSpace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>Future | Active | Closed</summary>
    public string State { get; set; } = SprintStates.Future;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? CompletedDate { get; set; }
}

public static class SprintStates
{
    public const string Future = "Future";
    public const string Active = "Active";
    public const string Closed = "Closed";
}

/// <summary>نظر روی وظیفه؛ متن HTML پاک‌سازی‌شده سمت سرور. حذف نرم (IsDeleted) تا تاریخچه بماند.</summary>
public class WorkItemComment : BaseEntity
{
    public int WorkItemCommentId { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }
    public int? AuthorEmployeeId { get; set; }
    public int? AuthorUserId { get; set; }
    public string? AuthorName { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
}

/// <summary>پیوند نوع‌دار بین دو وظیفه (Blocks، RelatesTo، Duplicates). پیوند به رکوردهای ماژول‌های دیگر همان RelatedType/RelatedId قبلی است.</summary>
public class WorkItemLink : BaseEntity
{
    public int WorkItemLinkId { get; set; }
    public int SourceTaskItemId { get; set; }
    public TaskItem? SourceTaskItem { get; set; }
    public string LinkType { get; set; } = WorkItemLinkTypes.RelatesTo;
    public int TargetTaskItemId { get; set; }
    public TaskItem? TargetTaskItem { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

public static class WorkItemLinkTypes
{
    public const string Blocks = "Blocks";
    public const string RelatesTo = "RelatesTo";
    public const string Duplicates = "Duplicates";
    public static readonly string[] All = { Blocks, RelatesTo, Duplicates };
}

/// <summary>ناظر وظیفه (مخاطب اعلان‌ها). ناظر بودن دسترسی دیدن همان وظیفه را می‌دهد، نه بیشتر.</summary>
public class WorkItemWatcher
{
    public int WorkItemWatcherId { get; set; }
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
