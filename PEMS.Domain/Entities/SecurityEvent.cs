using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// رویداد امنیتی بازیابی حساب مدیر (SCRUM-357 / ۳۵۳.۴). برای هر تلاش بازیابی - موفق یا ناموفق - و از هر سه مسیر
/// (ریست توسط ادمین دوم، کد بازیابی یکبارمصرف، break-glass روی سرور) یک ردیف ثبت می‌شود.
/// فقط‌افزودنی (append-only): ردیف هرگز ویرایش یا حذف نمی‌شود؛ تنها استثنا <see cref="ForwardedToSiemAt"/> است که
/// ارسال‌کننده‌ی SIEM (SCRUM-212) پس از تحویل رویداد پر می‌کند (null = هنوز ارسال نشده).
/// AuditLog عمومی جایگزین این جدول نیست: فقط از تغییر موجودیت‌ها ساخته می‌شود، IP و نتیجه ندارد و تلاش ناموفق
/// یا مسیرهای بدون کاربر وارد‌شده (کد بازیابی، break-glass) را اصلاً ثبت نمی‌کند.
/// هیچ رمز، کد بازیابی، توکن یا هشی در این جدول نوشته نمی‌شود.
/// </summary>
public class SecurityEvent
{
    [Key]
    public int SecurityEventId { get; set; }

    /// <summary>نوع رویداد؛ مقادیر ثابت در <c>RecoveryEventTypes</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>روش بازیابی؛ مقادیر ثابت در <c>RecoveryMethods</c>.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Success | Failure</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>دلیل شکست (کد کوتاه، بدون راز)؛ برای موفق null.</summary>
    public string? FailureReason { get; set; }

    /// <summary>انجام‌دهنده؛ برای کد بازیابی و break-glass (بدون ورود) null است.</summary>
    public int? ActorUserId { get; set; }

    public string? ActorUserName { get; set; }

    /// <summary>حساب مدیری که بازیابی روی آن انجام شد (یا تلاش شد)؛ ممکن است ناشناخته باشد.</summary>
    public int? TargetUserId { get; set; }

    public string? TargetUserName { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>زمان وقوع (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>زمان تحویل به SIEM (SCRUM-212)؛ null = در صف ارسال.</summary>
    public DateTime? ForwardedToSiemAt { get; set; }
}
