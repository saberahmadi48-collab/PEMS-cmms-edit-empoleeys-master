namespace PEMS.Application.DTOs;

public enum AdminResetStatus
{
    Success,
    SelfResetNotAllowed,
    ActorNotAdmin,
    TargetNotFound,
    TargetNotAdmin,
    PasswordRejected,

    /// <summary>رمز عوض شد ولی ممیزی/ابطال نشست شکست خورد؛ رمز موقت برگردانده نمی‌شود و باید ریست تکرار شود.</summary>
    AuditFailed
}

public sealed class AdminResetResult
{
    public AdminResetStatus Status { get; init; }

    /// <summary>فقط در موفقیت پر است و فقط همین یک بار نمایش داده می‌شود؛ هیچ‌جا ذخیره یا لاگ نمی‌شود.</summary>
    public string? TemporaryPassword { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public string? TargetUserName { get; init; }

    public bool Succeeded => Status == AdminResetStatus.Success;
}
