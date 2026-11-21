using System.Security.Cryptography;
using PEMS.Licensing.Crypto;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;

namespace PEMS.Licensing;

public sealed record BuiltRequest(string FileText, PendingRequest Pending, RequestPayload Payload);

/// <summary>Builds and signs a .pemsreq with the install key (design 5.2).</summary>
public static class RequestBuilder
{
    public static BuiltRequest Build(
        string requestType, SealRecord seal, FingerprintInfo fingerprint, IInstallSigner signer, DateTimeOffset now,
        string customerRef, string orderRef, string? previousLicenceId, string? reason, string productVersion, string buildDate)
    {
        if (!RequestTypes.All.Contains(requestType)) throw new ArgumentOutOfRangeException(nameof(requestType));
        if (requestType != RequestTypes.Activation && string.IsNullOrWhiteSpace(previousLicenceId) && seal.LicenceId is null)
            previousLicenceId = null;   // nothing to supersede yet; the vendor sees an activation-like request

        var nonce = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
        var payload = new RequestPayload(
            requestType, nonce, now,
            new ProductInfo("PEMS", productVersion, buildDate),
            new InstallInfo(seal.InstallId, seal.InstallTimeUtc, seal.InstallPub, seal.InstallKeyAlg, seal.KeyProtection),
            fingerprint,
            new SealInfo(seal.Counter, seal.AuditHead, seal.State),
            new CustomerInfo(Trim(customerRef, 64), Trim(orderRef, 64)),
            previousLicenceId ?? seal.LicenceId,
            reason is null ? null : Trim(reason, 500),
            seal.Eula);

        var bytes = LicenseJson.ToBytes(payload);
        var text = SignedFileCodec.Create(FileFormats.Request, bytes, FileFormats.AlgInstall, "inst-" + seal.InstallId, signer.Sign);
        var pending = new PendingRequest(nonce, requestType, now, LicenseVerifier_Hash(bytes));
        return new BuiltRequest(text, pending, payload);
    }

    private static string LicenseVerifier_Hash(byte[] payloadBytes) => Evaluation.LicenseVerifier.Hash(payloadBytes);

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
