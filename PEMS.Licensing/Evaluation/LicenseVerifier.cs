using PEMS.Licensing.Crypto;
using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;

namespace PEMS.Licensing.Evaluation;

public sealed record VerificationContext(TrustedKeySet Keys, string InstallId, string InstallPub, FingerprintInfo CurrentFingerprint);

public sealed record VerifiedLicence(LicensePayload Payload, FingerprintMatch Match, string Hash);

/// <summary>
/// Signature, binding and deployment checks for a .pemslic (design 5.3). Used unchanged by the startup check, the
/// periodic job, the update guard and the upload page: one engine, five call sites (design 7.1).
/// Time validity is not decided here but by <see cref="LicenseEvaluator"/>, which uses the tamper-guarded clock.
/// </summary>
public static class LicenseVerifier
{
    public static LicenseResult<VerifiedLicence> VerifyBinding(string? text, VerificationContext ctx)
    {
        var parsed = SignedFileCodec.Parse(text, FileFormats.Licence);
        if (!parsed.Ok) return LicenseResult<VerifiedLicence>.Fail(parsed.Failure, parsed.Detail);
        var file = parsed.Value!;

        if (file.Envelope.Signature.Alg != FileFormats.AlgVendor)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.UnknownAlgorithm, file.Envelope.Signature.Alg);
        if (!ctx.Keys.TryGet(file.Envelope.Signature.KeyId, out var key))
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.UnknownKey, file.Envelope.Signature.KeyId);
        if (!Base64Url.TryDecode(key.PublicKey, out var publicKey) ||
            !Ed25519.Verify(publicKey, file.SignedMessage, file.SignatureBytes))
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.BadSignature);

        LicensePayload? payload;
        try { payload = LicenseJson.FromBytes<LicensePayload>(file.PayloadBytes); }
        catch (System.Text.Json.JsonException) { payload = null; }
        if (payload?.Binding is null || payload.Validity is null || payload.Grant is null)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.BadEncoding, "payload");

        if (payload.Crit is { Count: > 0 })
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.UnsupportedCriticalField, string.Join(",", payload.Crit));
        if (key.RetiredAfter.HasValue && payload.IssuedUtc > key.RetiredAfter.Value)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.KeyRetired, key.KeyId);
        if (payload.SigningKeyId != key.KeyId)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.UnknownKey, payload.SigningKeyId);

        if (payload.Binding.InstallId != ctx.InstallId ||
            payload.Binding.InstallPubHash != InstallKeyVerifier.PublicKeyHash(ctx.InstallPub))
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.InstallMismatch);

        if (!DeploymentAllowed(payload.Binding.Deployment, ctx.CurrentFingerprint.Virtualization))
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.DeploymentNotAllowed, ctx.CurrentFingerprint.Virtualization);

        var match = FingerprintComparer.Compare(payload.Binding.Fingerprint, ctx.CurrentFingerprint);
        return LicenseResult<VerifiedLicence>.Success(new VerifiedLicence(payload, match, Hash(file.PayloadBytes)));
    }

    /// <summary>Everything of <see cref="VerifyBinding"/> plus: answers a request we made, is newer than the current licence, fingerprint is not invalid.</summary>
    public static LicenseResult<VerifiedLicence> VerifyForUpload(
        string? text, VerificationContext ctx, IReadOnlyList<PendingRequest> pending, LicensePayload? current, DateTimeOffset effectiveNow)
    {
        var result = VerifyBinding(text, ctx);
        if (!result.Ok) return result;
        var v = result.Value!;

        var request = pending.FirstOrDefault(p => p.Nonce == v.Payload.RequestNonce);
        if (request is null)
            return LicenseResult<VerifiedLicence>.Fail(pending.Count == 0 ? LicenseFailure.NonceUnknown : LicenseFailure.NonceMismatch);
        if (request.RequestHash != v.Payload.RequestHash)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.RequestMismatch);
        if (v.Match.Result == FingerprintResult.Invalid)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.FingerprintInvalid);
        if (current is not null && v.Payload.IssueSeq <= current.IssueSeq && v.Payload.Binding.InstallId == current.Binding.InstallId)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.NotNewer);
        if (LicenseTypes.IsTimeLimited(v.Payload.Type) && v.Payload.Validity.NotAfter is { } end && effectiveNow >= end)
            return LicenseResult<VerifiedLicence>.Fail(LicenseFailure.Expired);
        return result;
    }

    /// <summary>Physical needs "physical", a detected VM needs "vm", a container needs "container" (policy C5).</summary>
    public static bool DeploymentAllowed(DeploymentInfo deployment, string virtualization)
    {
        var kind = virtualization switch { "none" => "physical", "container" => "container", _ => "vm" };
        return deployment.Allowed.Contains(kind, StringComparer.Ordinal);
    }

    public static string Hash(byte[] payloadBytes)
        => Base64Url.Encode(System.Security.Cryptography.SHA256.HashData(payloadBytes));
}
