using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// بازیابی break-glass روی سرور (SCRUM-356 / ۳۵۳.۳): وقتی فایل درخواست در پوشه‌ی تعریف‌شده قرار گیرد، صفحه‌ی بازیابی ۱۵ دقیقه باز است.
/// محدودیت شبکه (localhost یا شبکه‌ی مدیریتی) در لایه‌ی وب اعمال می‌شود؛ ممیزی/ابطال نشست/اعلان از <see cref="IRecoveryEventService"/> می‌آید.
/// </summary>
public interface IBreakGlassService
{
    /// <summary>وضعیت پنجره؛ فایل درخواست منقضی‌شده همین‌جا حذف می‌شود.</summary>
    BreakGlassWindow GetWindow();

    /// <summary>مدیرهای فعال قابل بازیابی (فقط وقتی پنجره باز است یا توکن معتبر است صدا زده می‌شود).</summary>
    Task<IReadOnlyList<BreakGlassAdminDto>> GetAdminsAsync(CancellationToken ct = default);

    /// <summary>
    /// یک مدیر را ریست می‌کند: رمز موقت تصادفی (یک‌بار نمایش، ۲۴ ساعته)، تغییر اجباری رمز، ریست MFA، سپس ممیزی/ابطال نشست/اعلان.
    /// بدون فایل درخواست معتبر، فقط با <paramref name="vendorToken"/> معتبر (اگر روشن باشد). پس از موفقیت فایل درخواست حذف
    /// (یا توکن مصرف‌شده علامت‌گذاری) می‌شود تا یک بار بیشتر استفاده نشود.
    /// </summary>
    Task<BreakGlassResetResult> ResetAsync(int targetUserId, string? vendorToken, string? ipAddress, CancellationToken ct = default);

    /// <summary>ثبت تلاش رد‌شده از شبکه‌ی غیرمجاز (فقط وقتی پنجره باز است، تا صفحه‌ی بسته جدول را پر نکند).</summary>
    Task RecordBlockedNetworkAsync(string? ipAddress, CancellationToken ct = default);
}
