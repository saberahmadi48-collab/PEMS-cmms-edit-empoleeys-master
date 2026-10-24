namespace PEMS.Application.DTOs;

/// <summary>شمارنده‌های پوشه‌های کارتابل (همه با همان قانون محرمانگی فهرست).</summary>
public class CartableCountsDto
{
    public int Inbox { get; set; }
    public int InboxUnread { get; set; }
    public int Shared { get; set; }
    public int Sent { get; set; }
    public int Drafts { get; set; }
    public int Starred { get; set; }
    public int Archive { get; set; }
    public int Trash { get; set; }
    public int All { get; set; }
    public int Delegated { get; set; }
    /// <summary>تعداد نامه‌ی (قابل‌دیدن) هر برچسب، با احتساب زیرمجموعه‌ها.</summary>
    public Dictionary<int, int> TagCounts { get; set; } = new();
}

/// <summary>اندازه‌ی رشته‌ی گفتگو (نامه + پاسخ‌هایش) برای نمای رشته‌ای.</summary>
public record LetterThreadInfo(int RootLetterId, int Count);

public class LetterThreadMemberDto
{
    public int LetterId { get; set; }
    public int? ParentLetterId { get; set; }
    public int Depth { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public string LetterType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? SenderName { get; set; }
    public DateTime LetterDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class LetterTagNodeDto
{
    public int TagId { get; set; }
    public int? ParentTagId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "secondary";
    public bool IsPersonal { get; set; }
    public int SortOrder { get; set; }
    public int Depth { get; set; }
    public int LetterCount { get; set; }
    public bool CanEdit { get; set; }
}

public class TagTreeOpResult
{
    public bool Success { get; set; }
    public string? ErrorKey { get; set; }
    public int TagId { get; set; }
    public static TagTreeOpResult Ok(int id) => new() { Success = true, TagId = id };
    public static TagTreeOpResult Fail(string key) => new() { Success = false, ErrorKey = key };
}

public class CartableOpResult
{
    public bool Success { get; set; }
    public string? ErrorKey { get; set; }
    public int Affected { get; set; }
    public int Id { get; set; }
    public static CartableOpResult Ok(int affected = 1, int id = 0) => new() { Success = true, Affected = affected, Id = id };
    public static CartableOpResult Fail(string key) => new() { Success = false, ErrorKey = key };
}

public class LetterDelegationDto
{
    public int LetterDelegationId { get; set; }
    public int DelegatorUserId { get; set; }
    public string DelegatorName { get; set; } = "";
    public int DelegateUserId { get; set; }
    public string DelegateName { get; set; } = "";
    public string? DelegatePosition { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; }
    public bool IsEffectiveNow { get; set; }
}

public class DelegateCandidateDto
{
    public int UserId { get; set; }
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string? Position { get; set; }
    public string? Department { get; set; }
    /// <summary>Manager | Subordinate | Peer | Other - نسبت سازمانی با درخواست‌کننده از روی چارت سازمانی.</summary>
    public string Relation { get; set; } = "Other";
}

public class LetterRoutingRuleDto
{
    public int LetterRoutingRuleId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? MatchLetterType { get; set; }
    public string? MatchPriority { get; set; }
    public string? MatchCategory { get; set; }
    public int? MatchProjectId { get; set; }
    public int? MatchDisciplineId { get; set; }
    public string? MatchSenderContains { get; set; }
    public string? MatchSubjectContains { get; set; }
    public int? ApplyTagId { get; set; }
    public string? ApplyTagName { get; set; }
    public bool StarLetter { get; set; }
    public int? ReferToEmployeeId { get; set; }
    public string? ReferToName { get; set; }
    public string? ReferInstruction { get; set; }
    public int MatchCount { get; set; }
    public DateTime? LastMatchedDate { get; set; }
}

public class LetterContactGroupDto
{
    public int LetterContactGroupId { get; set; }
    public string Name { get; set; } = "";
    public bool IsPersonal { get; set; }
    public bool CanEdit { get; set; }
    public List<LetterContactMemberDto> Members { get; set; } = new();
}

public class LetterContactMemberDto
{
    public int LetterContactGroupMemberId { get; set; }
    public int? EmployeeId { get; set; }
    public int? CorrespondentPartyId { get; set; }
    public string Name { get; set; } = "";
    public string? Subtitle { get; set; }
}

/// <summary>مخاطب جستجوپذیر: کارمند چارت سازمانی یا طرف‌حساب خارجی.</summary>
public class LetterContactDto
{
    /// <summary>Employee | Party</summary>
    public string Kind { get; set; } = "Employee";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Subtitle { get; set; }
}

public class LetterReminderDto
{
    public int LetterReminderId { get; set; }
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime RemindAt { get; set; }
    public string? Note { get; set; }
    public bool IsDone { get; set; }
    public bool IsDue { get; set; }
}

/// <summary>گزارش شخصی کارتابل + SLA + (برای مدیران) عملکرد زیردستان از چارت سازمانی.</summary>
public class CartableReportDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public int Received { get; set; }
    public int Unread { get; set; }
    public int Sent { get; set; }
    public int OpenReferrals { get; set; }
    public int OverdueReferrals { get; set; }
    public int CompletedReferrals { get; set; }

    // SLA پاسخ‌خواهی نامه‌های دریافتی
    public int ResponseRequired { get; set; }
    public int AnsweredOnTime { get; set; }
    public int AnsweredLate { get; set; }
    public int OpenOverdue { get; set; }
    public int OpenWithinSla { get; set; }
    public double? AvgResponseDays { get; set; }
    public double? AvgReferralDays { get; set; }

    public List<CartableBucketDto> ByDiscipline { get; set; } = new();
    public List<CartableBucketDto> ByMonth { get; set; } = new();
    public List<SubordinateReportRowDto> Subordinates { get; set; } = new();
    public bool HasSubordinates { get; set; }

    public double OnTimePercent => AnsweredOnTime + AnsweredLate + OpenOverdue == 0 ? 100
        : Math.Round(100.0 * AnsweredOnTime / (AnsweredOnTime + AnsweredLate + OpenOverdue), 1);
}

public record CartableBucketDto(string Label, int Count);

public class SubordinateReportRowDto
{
    public int EmployeeId { get; set; }
    public string Name { get; set; } = "";
    public string? Position { get; set; }
    public int Depth { get; set; }
    public int Received { get; set; }
    public int Unread { get; set; }
    public int OpenReferrals { get; set; }
    public int OverdueReferrals { get; set; }
    public int CompletedReferrals { get; set; }
    public double? AvgReferralDays { get; set; }
}

/// <summary>یک رویداد گردش‌کار با نقش اقدام‌کننده نسبت به بیننده (برای فیلتر «من / مدیر / زیردستان»).</summary>
public class WorkflowHistoryEventDto
{
    public DateTime Date { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public string? ActorName { get; set; }
    public int? ActorEmployeeId { get; set; }
    /// <summary>Me | Manager | Subordinate | Other</summary>
    public string ActorRelation { get; set; } = "Other";
}

/// <summary>یک پیام از گردش پیام‌های نامه (نامه‌ی اصلی، پاسخ‌ها و ارجاع‌ها) با متن کامل، به ترتیب زمان.</summary>
public class ConversationMessageDto
{
    public int LetterId { get; set; }
    public int? ParentLetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public DateTime LetterDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? SenderName { get; set; }
    public string? SenderTitle { get; set; }
    public string RecipientsSummary { get; set; } = "";
    public string CcSummary { get; set; } = "";
    /// <summary>Original | Reply | Referral</summary>
    public string Kind { get; set; } = "Original";
    public string Status { get; set; } = "";
    public string LetterType { get; set; } = "";
    public string Priority { get; set; } = "Normal";
    public bool IsCurrent { get; set; }
    public int AttachmentsCount { get; set; }
}
