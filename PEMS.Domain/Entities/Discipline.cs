namespace PEMS.Domain.Entities;

public class Discipline
{
    public int DisciplineId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    // طبق باگ واقعی: فایل‌های MDR/VDR واقعی معمولاً کد کوتاه‌تری (PR، ME، EL...) یا نام کامل
    // استفاده می‌کنند، نه لزوماً همان Code داخلی سیستم (PROC، MECH...). این‌جا چند کد/نام رایج
    // جایگزین با کاما جدا نگه‌داری می‌شود تا تطبیق هنگام Import بی‌صدا شکست نخورد.
    public string? ImportAliases { get; set; }

    // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۳ (Communication/Numbering): کد
    // تک‌حرفی دپارتمان/فرستنده که در شماره‌ی ترانسمیتال (TV-X-UU-AAAA-NNNN) به‌عنوان X استفاده
    // می‌شود. این جدول دپارتمان مستقل از جدول دیسیپلین سند (Appendix 1) است و تناظر یک‌به‌یک
    // رسمی در سند نیامده - نگاشت زیر بر مبنای تأیید کاربر ثبت شده. مقدار NULL یعنی برای این
    // دیسیپلین نگاشت رسمی مشخصی وجود ندارد و کد باید هنگام ثبت ترانسمیتال دستی انتخاب شود.
    public string? TransmittalCode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    /// <summary>Concurrency token for the REST API (ETag / If-Match, SCRUM-435).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }
}