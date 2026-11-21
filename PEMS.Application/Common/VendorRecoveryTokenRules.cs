using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PEMS.Application.Common;

public enum VendorTokenStatus
{
    Valid,
    Disabled,
    Malformed,
    BadSignature,
    WrongMachine,
    NotYetValid,
    Expired,
    LifetimeTooLong
}

/// <summary>محتوای توکن بازیابی فروشنده. jti شناسه‌ی یکتا برای جلوگیری از استفاده‌ی دوباره است.</summary>
public sealed record VendorTokenPayload(int V, string Jti, string Fp, long Iat, long Exp);

/// <summary>
/// توکن بازیابی امضاشده‌ی فروشنده (SCRUM-356، اختیاری، پیش‌فرض خاموش). قالب: base64url(JSON payload) "." base64url(امضای ECDSA P-256/SHA-256
/// روی بایت‌های همان payload، قالب IEEE P1363). فقط با کلید عمومی فروشنده بررسی می‌شود؛ کلید خصوصی هرگز در محصول نیست.
/// </summary>
public static class VendorRecoveryTokenRules
{
    public const int MaxLifetimeHours = 24;
    public const int MaxTokenLength = 4096;
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static VendorTokenStatus Validate(string? token, BreakGlassOptions options, DateTime nowUtc, out VendorTokenPayload? payload)
    {
        payload = null;
        if (!options.VendorRecoveryEnabled || string.IsNullOrWhiteSpace(options.VendorPublicKey) || string.IsNullOrWhiteSpace(options.MachineFingerprint))
            return VendorTokenStatus.Disabled;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return VendorTokenStatus.Malformed;

        var parts = token.Trim().Split('.');
        if (parts.Length != 2) return VendorTokenStatus.Malformed;

        byte[] body, signature;
        try
        {
            body = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException) { return VendorTokenStatus.Malformed; }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(options.VendorPublicKey), out _);
            if (!ecdsa.VerifyData(body, signature, HashAlgorithmName.SHA256)) return VendorTokenStatus.BadSignature;
        }
        catch (CryptographicException) { return VendorTokenStatus.BadSignature; }
        catch (FormatException) { return VendorTokenStatus.BadSignature; }

        VendorTokenPayload? p;
        try { p = JsonSerializer.Deserialize<VendorTokenPayload>(body, Json); }
        catch (JsonException) { return VendorTokenStatus.Malformed; }
        if (p == null || p.V != 1 || string.IsNullOrWhiteSpace(p.Jti) || string.IsNullOrWhiteSpace(p.Fp)) return VendorTokenStatus.Malformed;

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(p.Fp), Encoding.UTF8.GetBytes(options.MachineFingerprint)))
            return VendorTokenStatus.WrongMachine;

        var issued = DateTimeOffset.FromUnixTimeSeconds(p.Iat).UtcDateTime;
        var expires = DateTimeOffset.FromUnixTimeSeconds(p.Exp).UtcDateTime;
        if (expires <= issued || expires - issued > TimeSpan.FromHours(MaxLifetimeHours)) return VendorTokenStatus.LifetimeTooLong;
        if (nowUtc < issued - ClockSkew) return VendorTokenStatus.NotYetValid;
        if (nowUtc >= expires) return VendorTokenStatus.Expired;

        payload = p;
        return VendorTokenStatus.Valid;
    }

    /// <summary>نام فایل نشانگر مصرف یک توکن (هش jti تا هیچ مقدار ورودی خامی نام فایل نشود).</summary>
    public static string UsedMarkerFileName(string jti) =>
        BreakGlassRules.UsedVendorTokenPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jti))).ToLowerInvariant();

    public static string ToBase64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        return Convert.FromBase64String(b);
    }
}
