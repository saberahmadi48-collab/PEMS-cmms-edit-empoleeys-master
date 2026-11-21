namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-04: سرویس شماره‌گذاری مرکزی، جایگزین الگوی ناامن
/// Count()+1 که در چند کنترلر مستقل (و ناهماهنگ با هم) تکرار شده بود. هیچ Controller/Service
/// دیگری نباید مستقیماً شماره تولید کند - همه از اینجا می‌گیرند.
/// </summary>
public interface INumberingService
{
    /// <summary>
    /// شماره بعدی را برای یک نوع سند (مثلاً "WorkOrder") به‌صورت اتمیک برمی‌گرداند.
    /// scopeKey دامنه‌ی شمارش را جدا می‌کند - مثلاً سال جاری برای WorkOrder، یا شماره
    /// تجهیز برای MaintenancePlan. اگر داده نشود، بسته به نوع سند پیش‌فرض محاسبه می‌شود.
    /// </summary>
    Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null);

    /// <summary>
    /// همان شمارنده‌ی اتمیک، ولی فقط مقدار عددی (برای کلیدهایی که قالبشان را خود ماژول می‌سازد، مثل کلید وظیفه‌ی
    /// Task Manager «{کلید فضا}-{n}»). پیاده‌سازی پیش‌فرض فقط برای سازگاری پیاده‌سازی‌های قدیمی/جعلی است.
    /// </summary>
    Task<int> GetNextValueAsync(string documentType, string scopeKey) =>
        throw new NotSupportedException("GetNextValueAsync is not supported by this numbering implementation.");

    /// <summary>
    /// SCRUM-40: شماره‌ی بعدی با الگوی مدیر سامانه (NumberingScheme) اگر برای این نوع سند فعال باشد؛ وگرنه قالب داخلی.
    /// context کدهای پروژه/دیسیپلین را برای توکن‌های {PROJECT}/{DISC} می‌دهد.
    /// </summary>
    Task<string> GetNextNumberAsync(string documentType, PEMS.Application.Numbering.NumberingContext context) =>
        GetNextNumberAsync(documentType);

    /// <summary>
    /// SCRUM-40: شماره‌ی نهایی سند تازه. با الگوی فعال: شماره‌ی خودکار، مگر الگو ورود دستی را مجاز کرده و شماره‌ای وارد شده باشد.
    /// بدون الگوی فعال: همان شماره‌ی واردشده (Trim‌شده؛ خالی بودنش را سرویس سند مثل قبل رد می‌کند).
    /// </summary>
    Task<string> ResolveNumberAsync(string documentType, string? enteredNumber, int? projectId = null, int? disciplineId = null) =>
        Task.FromResult(enteredNumber?.Trim() ?? string.Empty);
}
