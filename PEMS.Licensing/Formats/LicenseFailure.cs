namespace PEMS.Licensing.Formats;

/// <summary>
/// Distinct reason codes for every rejection (design section 5.1). The UI maps each to a localization key;
/// tests assert the exact code so a negative vector can never pass for the wrong reason.
/// </summary>
public enum LicenseFailure
{
    None = 0,
    UnknownFormat,
    UnknownVersion,
    BadEncoding,
    TooLarge,
    UnknownAlgorithm,
    UnknownKey,
    BadSignature,
    KeyRetired,
    UnsupportedCriticalField,
    NotYetValid,
    Expired,
    NonceMismatch,
    NonceUnknown,
    RequestMismatch,
    InstallMismatch,
    FingerprintInvalid,
    DeploymentNotAllowed,
    NotNewer,
    UnsafeArchive,
    NoLicenceInArchive,
    SealInvalid,
}

/// <summary>Result of reading or verifying a licensing file: either a value or one failure code.</summary>
public sealed class LicenseResult<T>
{
    public bool Ok => Failure == LicenseFailure.None;
    public LicenseFailure Failure { get; }
    public T? Value { get; }
    public string? Detail { get; }

    private LicenseResult(LicenseFailure failure, T? value, string? detail)
    {
        Failure = failure;
        Value = value;
        Detail = detail;
    }

    public static LicenseResult<T> Success(T value) => new(LicenseFailure.None, value, null);
    public static LicenseResult<T> Fail(LicenseFailure failure, string? detail = null) => new(failure, default, detail);
}
