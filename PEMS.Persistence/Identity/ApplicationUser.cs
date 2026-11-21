using Microsoft.AspNetCore.Identity;

namespace PEMS.Persistence.Identity;

/// <summary>
/// کاربر سیستم بر پایه‌ی ASP.NET Core Identity (جدول AspNetUsers).
/// این کلاس مستقل از Entity قدیمی PEMS.Domain.Entities.User است و آن را جایگزین نمی‌کند؛
/// جدول قدیمی دست‌نخورده باقی می‌ماند تا Migration های موجود خراب نشوند.
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;

    // لینک اختیاری به رکورد Employee (اطلاعات سازمانی کارمند)
    public int? EmployeeId { get; set; }

    // لینک اختیاری به Discipline (برای فیلتر کردن مدارک/فعالیت‌های مرتبط با کاربر)
    public int? DisciplineId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? LastLoginDate { get; set; }

    // طبق درخواست: کاربرانی که با رمز پیش‌فرض ساخته می‌شوند (مثلاً هنگام ساخت خودکار از
    // Employee) باید در اولین ورود مجبور به تغییر رمز شوند.
    public bool MustChangePassword { get; set; } = false;

    /// <summary>
    /// SCRUM-354: انقضای رمز موقتِ صادرشده در ریست حساب مدیر (UTC). فقط وقتی با MustChangePassword=true همراه باشد معنا دارد؛
    /// بعد از این زمان ورود با همان رمز موقت رد می‌شود و باید مدیر دیگری دوباره ریست کند. null = رمز موقت ندارد (یا قبلاً تغییر داده شده).
    /// </summary>
    public DateTime? TemporaryPasswordExpiresAt { get; set; }

    /// <summary>
    /// SCRUM-354: بعد از ریست حساب مدیر، passkeyها و کلید authenticator پاک شده‌اند و کاربر باید MFA را دوباره ثبت کند.
    /// اجبار واقعی ثبت‌نام در ورود با MFA (SCRUM-188 / SCRUM-206) اعمال می‌شود؛ تا آن زمان این پرچم فقط وضعیت را نگه می‌دارد.
    /// </summary>
    public bool MfaEnrollmentRequired { get; set; }
}
