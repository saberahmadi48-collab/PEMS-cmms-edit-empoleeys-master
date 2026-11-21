using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-40: الگوی شماره‌گذاری یک نوع سند که مدیر سامانه تعیین می‌کند (مثلاً «PO-{YYYY}-{SEQ}»). وقتی فعال است، NumberingService
/// شماره را از روی همین الگو می‌سازد و شمارنده‌ی اتمیک NumberingSequence را به‌ازای دامنه‌ی الگو (سال بازنشانی، پروژه، دیسیپلین)
/// افزایش می‌دهد؛ وقتی نیست، همان قالب‌های داخلی قبلی می‌ماند. هر ویرایش Version را یکی بالا می‌برد و در AuditLog ثبت می‌شود.
/// </summary>
public class NumberingScheme : BaseEntity
{
    public int NumberingSchemeId { get; set; }

    /// <summary>همان کلید NumberingService (مثل "PurchaseOrder").</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>متن ثابت و توکن‌ها: {YYYY} {YY} {PROJECT} {DISC} {SEQ}؛ {SEQ} دقیقاً یک بار.</summary>
    public string Pattern { get; set; } = string.Empty;

    public int PadWidth { get; set; } = 4;

    /// <summary><see cref="NumberingCalendars"/></summary>
    public string Calendar { get; set; } = NumberingCalendars.Jalali;

    /// <summary><see cref="NumberingResetPeriods"/></summary>
    public string ResetPeriod { get; set; } = NumberingResetPeriods.Yearly;

    public bool IsActive { get; set; } = true;

    /// <summary>اگر روشن باشد کاربر می‌تواند شماره‌ی دلخواه (یکتا) وارد کند؛ خالی بماند، شماره خودکار است.</summary>
    public bool AllowManualEntry { get; set; }

    public int Version { get; set; } = 1;

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? ModifiedDate { get; set; }
}

public static class NumberingCalendars
{
    public const string Jalali = "Jalali";
    public const string Gregorian = "Gregorian";
    public static readonly IReadOnlyList<string> All = new[] { Jalali, Gregorian };
}

public static class NumberingResetPeriods
{
    public const string Never = "Never";
    public const string Yearly = "Yearly";
    public static readonly IReadOnlyList<string> All = new[] { Never, Yearly };
}
