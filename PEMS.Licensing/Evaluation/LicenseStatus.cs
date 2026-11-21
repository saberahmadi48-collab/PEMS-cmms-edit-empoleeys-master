namespace PEMS.Licensing.Evaluation;

/// <summary>Declaration order is severity order: the evaluator keeps the greatest state any finding raises.</summary>
public enum LicenseState
{
    /// <summary><c>Licensing:Enforce</c> is false: nothing is checked and nothing is blocked.</summary>
    NotEnforced,
    Active,
    ActiveWarning,
    /// <summary>Read-only: reads work, writes are refused (design 7.2).</summary>
    Restricted,
    /// <summary>No usable licence or seal: only the always-free surfaces open.</summary>
    ActivationRequired,
}

/// <summary>Why the state is what it is. Codes are stable: the UI maps each to a localization key, tests assert them.</summary>
public static class LicenseReasons
{
    public const string NoLicence = "no_licence";
    public const string SealInvalid = "seal_invalid";
    public const string RestoreSuspected = "restore_suspected";
    public const string RestoreGraceExpired = "restore_grace_expired";
    public const string FingerprintInvalid = "fingerprint_invalid";
    public const string FingerprintDegraded = "fingerprint_degraded";
    public const string FingerprintDrift = "fingerprint_drift";
    public const string NotYetValid = "not_yet_valid";
    public const string Expired = "expired";
    public const string Expiring = "expiring";
    public const string ClockBackward = "clock_backward";
    public const string LicenceUnverifiable = "licence_unverifiable";
}

public sealed record LicenseStatus(
    LicenseState State,
    IReadOnlyList<string> Reasons,
    string? LicenceId,
    string? LicenceType,
    string? Edition,
    DateTimeOffset? ExpiresUtc,
    int? DaysLeft,
    DateTimeOffset? UpdatesUntil,
    string? InstallId,
    string? FingerprintShort)
{
    public static LicenseStatus NotEnforced { get; } =
        new(LicenseState.NotEnforced, Array.Empty<string>(), null, null, null, null, null, null, null, null);

    /// <summary>Read-only restricted mode and activation-required both refuse writes (design 7.2).</summary>
    public bool WritesAllowed => State is LicenseState.NotEnforced or LicenseState.Active or LicenseState.ActiveWarning;
}
