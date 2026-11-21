namespace PEMS.Application.DTOs;

public sealed class BreakGlassWindow
{
    /// <summary>فایل درخواست معتبر (کمتر از ۱۵ دقیقه) وجود دارد.</summary>
    public bool IsOpen { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>مسیر توکن فروشنده روشن است (صفحه بدون فایل هم برای وارد کردن توکن باز است).</summary>
    public bool VendorTokenAccepted { get; init; }

    /// <summary>صفحه‌ی break-glass اصلاً باید پاسخ بدهد (پنجره باز یا مسیر توکن روشن).</summary>
    public bool IsReachable => IsOpen || VendorTokenAccepted;
}

public sealed class BreakGlassAdminDto
{
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}

public enum BreakGlassResetStatus
{
    Success,
    WindowClosed,
    TokenInvalid,
    TargetNotFound,
    TargetNotAdmin,
    PasswordRejected,
    AuditFailed
}

public sealed class BreakGlassResetResult
{
    public BreakGlassResetStatus Status { get; init; }

    /// <summary>فقط در موفقیت پر است و فقط یک بار نمایش داده می‌شود؛ هیچ‌جا ذخیره یا لاگ نمی‌شود.</summary>
    public string? TemporaryPassword { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string? TargetUserName { get; init; }
    public bool Succeeded => Status == BreakGlassResetStatus.Success;
}
