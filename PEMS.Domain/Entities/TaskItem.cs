using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// وظیفه‌ی مستقل (طبق نمونه‌ی کارتابل مرجع Pargar/Fonix: "اقدام وظایف" و "پیگیری وظایف") -
/// برخلاف Referral که همیشه به یک نامه وصل است، این می‌تواند کاملاً مستقل باشد یا اختیاری
/// به یک نامه/مدرک/هر موجودیت دیگری وصل شود (RelatedType/RelatedId، همان الگوی Notification).
/// </summary>
public class TaskItem : BaseEntity
{
    [Key]
    public int TaskItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    // کسی که باید اقدام کند ("اقدام وظایف" از دید او)
    public int? AssignedToEmployeeId { get; set; }
    public Employee? AssignedToEmployee { get; set; }

    // کسی که وظیفه را محول کرده و پیگیری می‌کند ("پیگیری وظایف" از دید او)
    public int? AssignedByApplicationUserId { get; set; }
    public string? AssignedByName { get; set; }

    public DateTime? DueDate { get; set; }

    // Low, Normal, High, Urgent
    public string Priority { get; set; } = "Normal";

    // Open, InProgress, Completed, Cancelled
    public string Status { get; set; } = "Open";

    // پیوند اختیاری به هر موجودیت دیگر (نامه، مدرک، VDR و ...) - طبق همان الگوی Notification
    public string? RelatedType { get; set; }
    public int? RelatedId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? CompletedDate { get; set; }

    public string? CompletionNote { get; set; }

    // ======================================================================================
    // Task Manager (افزایشی؛ همه nullable/پیش‌فرض‌دار تا وظایف قدیمی و اقدامات جلسه دست‌نخورده بمانند).
    // وظیفه‌ی بدون WorkSpace همان «وظیفه‌ی شخصی/قدیمی» است: فقط محول‌کننده، مسئول و ناظرها آن را می‌بینند
    // و وضعیتش همان رشته‌ی Status قدیمی با گذارهای مجاز ثابت (TaskItemStatuses) است.
    // وظیفه‌ی داخل WorkSpace: کلید یکتا و تغییرناپذیر {کلید فضا}-{شماره}، وضعیت از طرح وضعیت همان فضا
    // (WorkItemStatusId)؛ رشته‌ی Status همیشه با دسته‌ی وضعیت هم‌گام می‌ماند تا کارهای من/شمارنده‌ها کار کنند.
    // ======================================================================================

    public int? WorkSpaceId { get; set; }
    public WorkSpace? WorkSpace { get; set; }

    /// <summary>کلید یکتا و تغییرناپذیر (مثلاً MNT-12)؛ فقط برای وظایف داخل فضا.</summary>
    public string? ItemKey { get; set; }

    public int? WorkItemTypeId { get; set; }
    public WorkItemType? WorkItemType { get; set; }

    public int? WorkItemStatusId { get; set; }
    public WorkItemStatus? WorkItemStatus { get; set; }

    /// <summary>گزارش‌دهنده (کارمند ایجادکننده)؛ برای وظایف قدیمی خالی است و AssignedByApplicationUserId مرجع است.</summary>
    public int? ReporterEmployeeId { get; set; }
    public Employee? ReporterEmployee { get; set; }

    public DateTime? StartDate { get; set; }

    public int? ParentTaskItemId { get; set; }
    public TaskItem? ParentTaskItem { get; set; }

    public int? SprintId { get; set; }
    public Sprint? Sprint { get; set; }

    /// <summary>ترتیب در Backlog (کوچک‌تر = بالاتر).</summary>
    public double Rank { get; set; }

    public decimal? StoryPoints { get; set; }

    /// <summary>false = بایگانی‌شده (حذف نرم؛ تاریخچه و نظرها می‌مانند).</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? ModifiedDate { get; set; }
}

/// <summary>وضعیت‌های وظیفه‌ی قدیمی/شخصی و گذارهای مجاز آن‌ها (رشته‌ی آزاد دیگر پذیرفته نمی‌شود).</summary>
public static class TaskItemStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Open, InProgress, Completed, Cancelled };

    /// <summary>گذارهای مجاز. بازگشایی از Completed/Cancelled فقط به Open.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        [Open] = new[] { InProgress, Completed, Cancelled },
        [InProgress] = new[] { Open, Completed, Cancelled },
        [Completed] = new[] { Open },
        [Cancelled] = new[] { Open },
    };

    public static bool IsAllowed(string from, string to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsClosed(string status) => status is Completed or Cancelled;
}

public static class TaskItemPriorities
{
    public static readonly string[] All = { "Low", "Normal", "High", "Urgent" };
}
