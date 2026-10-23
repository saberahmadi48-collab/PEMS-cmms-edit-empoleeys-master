namespace PEMS.Application.DTOs;

public class LetterDto
{
    public int LetterId { get; set; }
    public string LetterType { get; set; } = "";
    public string LetterNumber { get; set; } = "";
    public DateTime LetterDate { get; set; }
    public string? Sender { get; set; }
    public string? Receiver { get; set; }
    public string Subject { get; set; } = "";
    public string Priority { get; set; } = "Normal";
    public string ConfidentialityLevel { get; set; } = "Internal";
    public string? ProjectName { get; set; }
    public string? DisciplineName { get; set; }
    public int? RelatedDocumentId { get; set; }
    public string? RelatedDocumentNo { get; set; }
    public int? RelatedMRId { get; set; }
    public string? RelatedMRNumber { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public int AttachmentsCount { get; set; }
    public int OpenReferralsCount { get; set; }
    public bool HasOverdueReferral { get; set; }
    public bool IsStarred { get; set; }

    // برای بیننده‌ی جاری گیرنده است و هنوز نخوانده؛ نامه‌ی پیش‌ازارسال هرگز «نخوانده» نیست
    public bool IsUnread { get; set; }
    // چند کلمه‌ی اول متن ساده‌ی نامه برای نمایش کنار موضوع در فهرست (در خروجی Excel/CSV پر نمی‌شود)
    public string? Snippet { get; set; }

    // مشتق‌شده (بدون تغییر Schema): نزدیک‌ترین مهلت پاسخ در بین ارجاع‌های باز، و آخرین فعالیت روی نامه
    public DateTime? NextResponseDue { get; set; }

    // پاسخ‌خواهی سطح نامه (Phase 2). Answered/Overdue محاسبه‌شده‌اند، نه ذخیره‌شده.
    public bool ResponseRequired { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public bool ResponseAnswered { get; set; }
    public bool ResponseOverdue { get; set; }

    // بازطراحی مکاتبات داخلی
    public string? Category { get; set; }
    public string? ReferenceNumber { get; set; }
    public int? SenderEmployeeId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderTitle { get; set; }
    public int? SenderDepartmentId { get; set; }
    public string? SenderDepartmentName { get; set; }
    public bool ApprovalRequired { get; set; }
    public DateTime? ArchivedDate { get; set; }
    public DateTime LastActivityDate { get; set; }

    // To / Cc از دید کاربر جاری (اگر گیرنده‌ی این نامه باشد) - طبق نمونه‌ی کارتابل مرجع
    // ("نامه" در برابر "رونوشت")
    public string? MyRecipientType { get; set; }

    public List<TagDto> Tags { get; set; } = new();

    public string? SignedByName { get; set; }
    public DateTime? SignedDate { get; set; }

    // طبق بند ۲۹ (Concurrency): برای Round-trip در فرم ویرایش/ارسال‌مجدد، تا EF بتواند ویرایش
    // هم‌زمان را تشخیص دهد
    public byte[]? RowVersion { get; set; }

    public int? RegisteredById { get; set; }

    public string? ReturnedByName { get; set; }
    public DateTime? ReturnedDate { get; set; }
    public string? ReturnReason { get; set; }

    public string? RejectedByName { get; set; }
    public DateTime? RejectedDate { get; set; }
    public string? RejectReason { get; set; }

    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedDate { get; set; }

    public DateTime? IssuedDate { get; set; }
    public DateTime? ClosedDate { get; set; }

    public int RevisionCount { get; set; }
}

public class LetterReviewDto
{
    public int LetterReviewId { get; set; }
    public string? ReviewerName { get; set; }
    public string Decision { get; set; } = "";
    public string? Comment { get; set; }
    public DateTime CreatedDate { get; set; }

    // طبق «PEMS Correspondence Workspace» بند ۱۷: نمایش نسخه‌ی قبلی (همان‌که بازبین دیده) در کنار
    // فرم ویرایش «Resubmit» - فقط برای Decision=ReturnedForRevision پر می‌شود
    public string? PreviousSubjectSnapshot { get; set; }
    public string? PreviousBodySnapshot { get; set; }
}

public class LetterRecipientDto
{
    public int LetterRecipientId { get; set; }
    public int? EmployeeId { get; set; }
    // To | Cc | Bcc
    public string RecipientType { get; set; } = "To";
    public string DisplayName { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime? ReadDate { get; set; }
}

public class LetterReferralDto
{
    public int LetterReferralId { get; set; }
    public int LetterId { get; set; }
    public string? ReferredToEmployeeName { get; set; }
    public string? ReferredToDepartmentName { get; set; }
    public string? Instructions { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Pending";
    public bool IsRead { get; set; }
    public DateTime? ReadDate { get; set; }
    public string? ReferredByName { get; set; }
    public DateTime ReferredDate { get; set; }
    public string? ResponseNote { get; set; }
    public bool IsOverdue { get; set; }
}

public class LetterAttachmentDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
}

public class LetterDetailDto
{
    public LetterDto Letter { get; set; } = new();
    public string? Body { get; set; }
    public List<LetterReferralDto> Referrals { get; set; } = new();
    public List<LetterAttachmentDto> Attachments { get; set; } = new();
    public List<LetterRecipientDto> Recipients { get; set; } = new();
    public List<LetterApprovalStepDto> ApprovalSteps { get; set; } = new();
    public List<LetterReviewDto> Reviews { get; set; } = new();
}

public class LetterDashboardDto
{
    public int PendingLetters { get; set; }
    public int OverdueReferrals { get; set; }
    public int MyOpenReferrals { get; set; }
    public int RecentlyReceived { get; set; }
    public int RecentlySent { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByType { get; set; } = new();
}

public class RelatedLetterDto
{
    public int LetterId { get; set; }
    public string LetterType { get; set; } = "";
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime LetterDate { get; set; }
    public string Status { get; set; } = "";
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? LinkUrl { get; set; }
    public string? RelatedType { get; set; }
    public int? RelatedId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? DismissedDate { get; set; }
    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// فیلترهای پیشرفته‌ی فهرست مکاتبات. Overdue «محاسبه‌شده» است (ارجاع باز با مهلت گذشته) و ذخیره نمی‌شود.
/// </summary>
public class LetterAdvancedFilter
{
    public string? Priority { get; set; }
    public bool? HasAttachments { get; set; }
    public bool? HasReferrals { get; set; }
    public bool OverdueReferralOnly { get; set; }
    // پاسخ‌خواهی سطح نامه که مهلتش گذشته و هنوز پاسخ صادر نشده (محاسبه‌شده، Phase 2)
    public bool OverdueResponseOnly { get; set; }
    // فقط نامه‌هایی که بیننده گیرنده‌ی آن‌هاست و هنوز نخوانده
    public bool UnreadOnly { get; set; }
    // جستجوی یکجا در شماره، موضوع، فرستنده و گیرنده
    public string? Query { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Priority) && !HasAttachments.HasValue
        && !HasReferrals.HasValue && !OverdueReferralOnly && !OverdueResponseOnly && !UnreadOnly && string.IsNullOrWhiteSpace(Query);
}

/// <summary>شمارنده‌ی تب‌های پوشه (فقط نامه‌هایی که بیننده اجازه‌ی دیدنشان را دارد).</summary>
public class LetterFolderCountsDto
{
    public int Draft { get; set; }
    public int PendingApproval { get; set; }
    public int ReturnedForRevision { get; set; }
    public int Approved { get; set; }
    public int Starred { get; set; }
}

/// <summary>نتیجه‌ی عملیات گروهی: هر نامه جداگانه ارزیابی می‌شود و دلیل رد هر کدام برگردانده می‌شود.</summary>
public class LetterBulkResult
{
    public int Succeeded { get; set; }
    public List<LetterBulkFailure> Failed { get; set; } = new();
}

public class LetterBulkFailure
{
    public int LetterId { get; set; }
    public string? LetterNumber { get; set; }
    public string Reason { get; set; } = "";
}
