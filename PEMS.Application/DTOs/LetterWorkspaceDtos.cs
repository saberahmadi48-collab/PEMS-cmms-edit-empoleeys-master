namespace PEMS.Application.DTOs;

/// <summary>
/// داشبورد اختصاصی مکاتبات (Phase 2). همه‌ی اعداد از داده‌ی واقعی و با همان قانون محرمانگی فهرست محاسبه می‌شوند؛
/// کاربر بدون مجوز مشاهده‌ی محرمانه، نامه‌ی محرمانه‌ی دیگران را در هیچ شمارنده‌ای نمی‌بیند.
/// </summary>
public class LetterWorkspaceDashboardDto
{
    public int TotalActive { get; set; }
    public int OpenLetters { get; set; }
    public int ReceivedLast30Days { get; set; }
    public int IssuedLast30Days { get; set; }

    public int OpenReferrals { get; set; }
    public int OverdueReferrals { get; set; }
    public int OverdueResponses { get; set; }

    // فقط برای بازبین (Approve) معنا دارد؛ برای بقیه صفر و در UI پنهان است
    public int AwaitingApproval { get; set; }
    public int MyDrafts { get; set; }
    public int MyReturnedForRevision { get; set; }
    public int MyOpenReferrals { get; set; }

    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> ByPriority { get; set; } = new();

    // کلیدها: 0-7 | 8-30 | 31-90 | 90+ (روز از تاریخ نامه، فقط نامه‌های باز)
    public List<LetterAgingBucketDto> Aging { get; set; } = new();

    // معوق‌ترین موارد (ارجاع یا پاسخ معوق)، حداکثر ۱۰ مورد
    public List<LetterAttentionItemDto> NeedsAttention { get; set; } = new();
}

public class LetterAgingBucketDto
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
}

public class LetterAttentionItemDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    // OverdueReferral | OverdueResponse
    public string Reason { get; set; } = "";
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
}

public class LetterTemplateDto
{
    public int LetterTemplateId { get; set; }
    public string Name { get; set; } = "";
    // null = همه‌ی انواع
    public string? LetterType { get; set; }
    public string Subject { get; set; } = "";
    public string? Body { get; set; }
    public string Priority { get; set; } = "Normal";
    public string ConfidentialityLevel { get; set; } = "Internal";
    public bool IsActive { get; set; } = true;
}
