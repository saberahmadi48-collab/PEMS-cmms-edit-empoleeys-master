using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// حساب‌های مدیر (SCRUM-354 / ۳۵۳.۱): محافظ آخرین مدیر فعال و ریست حساب یک مدیر توسط مدیر دیگر.
/// ممیزی، ابطال نشست و اعلان ریست از <see cref="IRecoveryEventService"/> (SCRUM-357) می‌آید.
/// </summary>
public interface IAdminAccountService
{
    /// <summary>
    /// true اگر تغییر پیشنهادی کاربر <paramref name="userId"/> آخرین مدیرِ فعال را از بین ببرد (غیرفعال‌سازی یا خروج از نقش SystemAdmin).
    /// فقط وقتی true می‌شود که کاربر الان مدیر فعال باشد، پس از تغییر دیگر مدیر فعال نباشد، و مدیر فعال دیگری وجود نداشته باشد.
    /// </summary>
    Task<bool> WouldLeaveNoActiveAdminAsync(int userId, bool willBeActive, bool willBeAdmin, CancellationToken ct = default);

    /// <summary>
    /// مدیر <paramref name="actorUserId"/> حساب مدیر دیگر را ریست می‌کند: رمز موقت تصادفی (یک‌بار نمایش، ۲۴ ساعته)، تغییر اجباری رمز،
    /// باز کردن قفل، ریست MFA (passkeyها + کلید authenticator + پرچم ثبت‌نام مجدد)، سپس ممیزی/ابطال نشست/اعلان. ریست خود ممکن نیست.
    /// </summary>
    Task<AdminResetResult> ResetAdminAsync(int actorUserId, int targetUserId, string? ipAddress, CancellationToken ct = default);
}
