namespace PEMS.Application.DTOs;

/// <summary>
/// یک تلاش بازیابی حساب مدیر. هیچ رمز/کد/توکنی در آن نیست - فقط هویت‌ها، روش و IP.
/// ActorUserId برای مسیرهای بدون ورود (کد بازیابی، break-glass) null است.
/// </summary>
public sealed record RecoveryAttempt(
    string Method,
    int? TargetUserId,
    string? TargetUserName,
    int? ActorUserId = null,
    string? ActorUserName = null,
    string? IpAddress = null);

public sealed class RecoveryOutcome
{
    public int SecurityEventId { get; init; }
    public int AdminsNotified { get; init; }
}

public sealed class SecurityEventDto
{
    public int SecurityEventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? ActorUserName { get; init; }
    public string? TargetUserName { get; init; }
    public string? IpAddress { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public bool Forwarded { get; init; }
}
