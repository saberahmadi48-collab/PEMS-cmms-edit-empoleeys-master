using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Seal;

public sealed record PendingRequest(string Nonce, string RequestType, DateTimeOffset CreatedUtc, string RequestHash);

/// <summary>The signed install seal (design 4.2). Everything here is covered by the install-key signature.</summary>
public sealed record SealRecord(
    int SealVersion,
    string InstallId,
    DateTimeOffset InstallTimeUtc,
    string InstallPub,
    string InstallKeyAlg,
    string KeyProtection,
    long Counter,
    DateTimeOffset LastSeenUtc,
    string AuditHead,
    string? LicenceId,
    string? LicenceHash,
    FingerprintInfo FpEnrol,
    string State,
    bool RestoreSuspected,
    DateTimeOffset? RestoreDetectedUtc,
    EulaInfo? Eula,
    List<PendingRequest> PendingRequests);

/// <summary>What one store holds: the signed seal envelope plus the active licence text (which verifies itself).</summary>
public sealed record SealBundle(string Seal, string? Licence);

/// <summary>One persistence place of the seal (database, protected file). Implementations must not throw for "nothing stored".</summary>
public interface ISealStore
{
    string Name { get; }
    Task<string?> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(string bundleJson, CancellationToken ct = default);
}
