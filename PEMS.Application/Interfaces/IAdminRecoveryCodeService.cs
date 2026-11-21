using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// کدهای بازیابی یکبارمصرف حساب مدیر (SCRUM-355 / ۳۵۳.۲). جادوی راه‌اندازی اولیه (SCRUM-188) بعد از ثبت MFA و پروفایل مدیر
/// هر دو <see cref="GenerateAsync"/> را صدا می‌زنند. ممیزی، ابطال نشست و اعلان بازیابی از <see cref="IRecoveryEventService"/> می‌آید.
/// </summary>
public interface IAdminRecoveryCodeService
{
    /// <summary>
    /// ۱۰ کد جدید برای مدیر فعال <paramref name="userId"/> می‌سازد و همه‌ی کدهای مصرف‌نشده‌ی قبلی را باطل می‌کند.
    /// کدها فقط در همین نتیجه برمی‌گردند؛ فقط هش آن‌ها ذخیره می‌شود.
    /// </summary>
    Task<RecoveryCodeSetResult> GenerateAsync(int userId, CancellationToken ct = default);

    /// <summary>تعداد کدهای هنوز معتبر (مصرف‌نشده و باطل‌نشده).</summary>
    Task<int> GetRemainingCountAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// نام کاربری + یک کد + رمز جدید: کد یک بار مصرف می‌شود، رمز تنظیم و MFA برای ثبت‌نام مجدد ریست می‌شود، سپس ممیزی/ابطال نشست/اعلان.
    /// کد نادرست شمارنده‌ی قفل Identity را بالا می‌برد؛ حساب قفل‌شده حتی با کد درست بازیابی نمی‌شود.
    /// </summary>
    Task<RecoveryCodeRedeemResult> RedeemAsync(RecoveryCodeRedeemRequest request, CancellationToken ct = default);
}
