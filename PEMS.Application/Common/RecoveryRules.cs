namespace PEMS.Application.Common;

/// <summary>روش‌های بازیابی حساب مدیر (SCRUM-353). مقدار ذخیره‌شده در <c>SecurityEvent.Method</c>.</summary>
public static class RecoveryMethods
{
    /// <summary>۳۵۳.۱ - مدیر دیگر حساب مدیر را ریست می‌کند (رمز موقت + تغییر اجباری + ثبت‌نام مجدد MFA).</summary>
    public const string SecondAdminReset = "SecondAdminReset";

    /// <summary>۳۵۳.۲ - کد بازیابی یکبارمصرف.</summary>
    public const string RecoveryCode = "RecoveryCode";

    /// <summary>۳۵۳.۳ - break-glass: فایل درخواست روی سرور + صفحه‌ی ۱۵ دقیقه‌ای فقط‌محلی.</summary>
    public const string BreakGlass = "BreakGlass";

    /// <summary>۳۵۳.۳ (اختیاری، پیش‌فرض خاموش) - توکن امضاشده‌ی فروشنده.</summary>
    public const string VendorToken = "VendorToken";

    /// <summary>تغییر رمز یک حساب مدیر از فرم ویرایش کاربر در پنل مدیریت (مسیر قدیمی؛ فقط برای ممیزی).</summary>
    public const string AdminConsole = "AdminConsole";

    public static readonly IReadOnlyList<string> All = new[] { SecondAdminReset, RecoveryCode, BreakGlass, VendorToken, AdminConsole };

    public static bool IsKnown(string? method) => method != null && All.Contains(method);

    /// <summary>برچسب فارسی برای اعلان/صفحه‌ی رویدادها.</summary>
    public static string Label(string method) => method switch
    {
        SecondAdminReset => "ریست توسط مدیر دیگر",
        RecoveryCode => "کد بازیابی",
        BreakGlass => "break-glass روی سرور",
        VendorToken => "توکن فروشنده",
        AdminConsole => "ویرایش کاربر در پنل مدیریت",
        _ => method
    };
}

public static class RecoveryEventTypes
{
    /// <summary>تلاش برای بازیابی حساب مدیر (نتیجه در Result: Success | Failure).</summary>
    public const string AdminRecovery = "AdminRecovery";
}

public static class RecoveryResults
{
    public const string Success = "Success";
    public const string Failure = "Failure";
}

/// <summary>
/// کدهای ثابت دلیل شکست. فقط همین مقادیر ذخیره می‌شوند تا هیچ متن آزاد (و در نتیجه هیچ رمز/کدی) به جدول نرسد.
/// </summary>
public static class RecoveryFailureReasons
{
    public const string InvalidCode = "InvalidCode";
    public const string SelfResetNotAllowed = "SelfResetNotAllowed";
    public const string TargetNotAdmin = "TargetNotAdmin";
    public const string ActorNotAdmin = "ActorNotAdmin";
    public const string UnknownTarget = "UnknownTarget";
    public const string NetworkNotAllowed = "NetworkNotAllowed";
    public const string WindowClosed = "WindowClosed";
    public const string RateLimited = "RateLimited";
    public const string TokenInvalid = "TokenInvalid";
    public const string PasswordRejected = "PasswordRejected";
    public const string SessionRevocationFailed = "SessionRevocationFailed";
    public const string Other = "Other";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        InvalidCode, SelfResetNotAllowed, TargetNotAdmin, ActorNotAdmin, UnknownTarget, NetworkNotAllowed, WindowClosed,
        RateLimited, TokenInvalid, PasswordRejected, SessionRevocationFailed, Other
    };

    /// <summary>مقدار ناشناخته (یا متن آزاد) به <see cref="Other"/> تبدیل می‌شود.</summary>
    public static string Normalize(string? reason) => reason != null && Known.Contains(reason) ? reason : Other;
}

/// <summary>قواعد خالص ثبت رویداد بازیابی: کوتاه‌سازی فیلدها و نرمال‌سازی IP.</summary>
public static class RecoveryEventRules
{
    public const int MaxNameLength = 256;
    public const int MaxIpLength = 64;

    public static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>IPv4-mapped IPv6 (::ffff:1.2.3.4) به IPv4 ساده تبدیل می‌شود تا فیلتر/جستجو یکسان باشد.</summary>
    public static string? NormalizeIp(string? ip)
    {
        var t = Trim(ip, MaxIpLength);
        if (t != null && t.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) && t.IndexOf('.') > 0) t = t["::ffff:".Length..];
        return t;
    }
}
