namespace PEMS.Application.DTOs;

/// <summary>
/// نتیجه‌ی تولید مجموعه‌ی کد بازیابی. <see cref="Codes"/> فقط همین یک بار برگردانده می‌شود؛ هیچ‌جا ذخیره یا لاگ نمی‌شود.
/// </summary>
public sealed class RecoveryCodeSetResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> Codes { get; init; } = Array.Empty<string>();
    public DateTime GeneratedAtUtc { get; init; }
    public string? UserName { get; init; }
}

public sealed record RecoveryCodeRedeemRequest(string? UserName, string? Code, string? NewPassword, string? IpAddress);

public enum RecoveryCodeRedeemStatus
{
    Success,

    /// <summary>نام کاربری/کد نادرست، کد مصرف‌شده یا باطل، یا حساب غیرمدیر - همه یک پیام عمومی دارند.</summary>
    Rejected,

    /// <summary>حساب به‌خاطر تلاش‌های ناموفق قفل است.</summary>
    LockedOut,

    /// <summary>کد درست بود ولی رمز جدید با سیاست رمز سازگار نیست؛ کد مصرف نشده است.</summary>
    PasswordRejected
}

public sealed class RecoveryCodeRedeemResult
{
    public RecoveryCodeRedeemStatus Status { get; init; }
    public IReadOnlyList<string> PasswordErrors { get; init; } = Array.Empty<string>();
    public int RemainingCodes { get; init; }
    public bool Succeeded => Status == RecoveryCodeRedeemStatus.Success;
}
