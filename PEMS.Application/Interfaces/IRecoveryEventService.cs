using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// سرویس مشترک هر سه مسیر بازیابی حساب مدیر (SCRUM-357 / ۳۵۳.۴): ریست توسط مدیر دیگر، کد بازیابی، break-glass.
/// مسیرها خودشان رمز/MFA را تغییر می‌دهند؛ این سرویس بعد از آن (یا هنگام شکست) ممیزی، ابطال نشست‌ها و اعلان را یکسان انجام می‌دهد.
/// </summary>
public interface IRecoveryEventService
{
    /// <summary>
    /// بعد از ریست موفق صدا زده می‌شود. به ترتیب: ۱) ابطال همه‌ی نشست‌ها و کوکی‌های ماندگار حساب هدف (SecurityStamp)،
    /// ۲) ثبت SecurityEvent موفق، ۳) اعلان به همه‌ی مدیران فعال (بهترین‌تلاش؛ شکست اعلان ریست را برنمی‌گرداند)، ۴) لاگ ساخت‌یافته‌ی «PEMS.Security».
    /// اگر ابطال نشست شکست بخورد، رویداد شکست ثبت و استثنا پرتاب می‌شود (ریست موفق گزارش نمی‌شود). حساب هدف باید موجود باشد.
    /// </summary>
    Task<RecoveryOutcome> CompleteSuccessAsync(RecoveryAttempt attempt, CancellationToken ct = default);

    /// <summary>تلاش ناموفق: فقط ممیزی (بدون اعلان و بدون ابطال نشست). reason باید یکی از <c>RecoveryFailureReasons</c> باشد، وگرنه «Other» ثبت می‌شود.</summary>
    Task<int> RecordFailureAsync(RecoveryAttempt attempt, string reason, CancellationToken ct = default);

    /// <summary>فهرست رویدادها (جدیدترین اول) برای صفحه‌ی مدیریت. method/result اختیاری.</summary>
    Task<PagedResult<SecurityEventDto>> GetEventsAsync(string? method, string? result, int page, int pageSize, CancellationToken ct = default);
}
