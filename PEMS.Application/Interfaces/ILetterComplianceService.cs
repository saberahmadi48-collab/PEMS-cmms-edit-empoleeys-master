namespace PEMS.Application.Interfaces;

/// <summary>
/// انطباق و نگهداری مکاتبات: «چه کسی دید»، گزارش حسابرسی یک نامه (همه‌ی رویدادهای ثبت‌شده در یک جدول زمانی)، و آرشیو خودکار نامه‌های
/// بی‌فعالیت. هیچ‌کدام داده‌ی نامه را تغییر نمی‌دهند جز آرشیو (Status=Archived، بدون حذف فیزیکی).
/// </summary>
public interface ILetterComplianceService
{
    Task<List<LetterViewLogEntryDto>> GetViewLogAsync(int letterId);

    Task<LetterComplianceReportDto?> BuildComplianceReportAsync(int letterId);

    // نامه‌های ارسال‌شده/بسته‌شده که در years سال اخیر هیچ فعالیتی نداشته‌اند (و پاسخ‌خواهِ بی‌پاسخ نیستند) آرشیو می‌شوند؛ تعداد آرشیوشده را برمی‌گرداند
    Task<int> ArchiveInactiveAsync(DateTime? now = null, int years = 2);
}

public class LetterViewLogEntryDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime FirstViewedDate { get; set; }
    public DateTime LastViewedDate { get; set; }
    public int ViewCount { get; set; }
}

public class LetterComplianceRowDto
{
    public DateTime Timestamp { get; set; }
    public string Actor { get; set; } = "";
    // Created | Version | Review | ApprovalStep | Recipient | Read | Referral | View | Audit
    public string Category { get; set; } = "";
    public string Event { get; set; } = "";
    public string? Detail { get; set; }
}

public class LetterComplianceReportDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
    public string ConfidentialityLevel { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<LetterComplianceRowDto> Rows { get; set; } = new();
}
