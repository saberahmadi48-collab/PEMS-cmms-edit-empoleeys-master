using System.Text.Json;
using System.Text.Json.Serialization;

namespace PEMS.Licensing.Formats;

/// <summary>Serializer settings shared by every licensing file (snake_case, UTC instants, nulls written).</summary>
public static class LicenseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static byte[] ToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? FromBytes<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Options);
}

public static class RequestTypes
{
    public const string Activation = "activation";
    public const string Renewal = "renewal";
    public const string Refresh = "refresh";
    public const string Transfer = "transfer";
    public const string Restore = "restore";

    public static readonly string[] All = { Activation, Renewal, Refresh, Transfer, Restore };
}

public static class LicenseTypes
{
    public const string Lifetime = "lifetime";
    public const string Term = "term";
    public const string Trial = "trial";
    public const string Demo = "demo";

    public static readonly string[] All = { Lifetime, Term, Trial, Demo };

    public static bool IsTimeLimited(string type) => type != Lifetime;
}

public sealed record ProductInfo(string Name, string Version, string BuildDate);

public sealed record InstallInfo(string InstallId, DateTimeOffset InstallTimeUtc, string InstallPub, string InstallKeyAlg, string KeyProtection);

public sealed record SignalEntry(string Id, string H);

public sealed record FingerprintInfo(int Scheme, string Platform, string Virtualization, string FpHash, List<SignalEntry> Signals);

public sealed record SealInfo(long Counter, string AuditHead, string State);

public sealed record CustomerInfo(string CustomerRef, string OrderRef);

public sealed record EulaInfo(string Version, DateTimeOffset AcceptedUtc);

/// <summary>Payload of a .pemsreq file (design section 5.2).</summary>
public sealed record RequestPayload(
    string RequestType,
    string Nonce,
    DateTimeOffset CreatedUtc,
    ProductInfo Product,
    InstallInfo Install,
    FingerprintInfo Fingerprint,
    SealInfo Seal,
    CustomerInfo Customer,
    string? PreviousLicenceId,
    string? Reason,
    EulaInfo? Eula);

public sealed record LicenceCustomer(string CustomerRef, string DisplayName);

public sealed record DeploymentInfo(string Platform, string Virtualization, List<string> Allowed);

public sealed record BindingInfo(string InstallId, string InstallPubHash, FingerprintInfo Fingerprint, DeploymentInfo Deployment);

public sealed record GrantInfo(string Edition, List<string> Modules, Dictionary<string, int> Limits);

public sealed record ValidityInfo(DateTimeOffset NotBefore, DateTimeOffset? NotAfter);

public sealed record MaintenanceInfo(DateTimeOffset? UpdatesUntil);

/// <summary>Payload of a .pemslic file (design section 5.3). There is deliberately no user-count field (decision C3).</summary>
public sealed record LicensePayload(
    string LicenceId,
    long IssueSeq,
    DateTimeOffset IssuedUtc,
    string SigningKeyId,
    string RequestNonce,
    string RequestHash,
    LicenceCustomer Customer,
    BindingInfo Binding,
    GrantInfo Grant,
    string Type,
    ValidityInfo Validity,
    MaintenanceInfo Maintenance,
    int TransferCount,
    string? Supersedes,
    List<string> Crit);
