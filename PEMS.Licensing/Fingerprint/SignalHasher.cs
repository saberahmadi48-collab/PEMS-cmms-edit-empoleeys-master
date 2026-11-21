using System.Security.Cryptography;
using System.Text;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Fingerprint;

/// <summary>Turns raw serials into hashes (design 3.1) and drops values that carry no identity.</summary>
public static class SignalHasher
{
    private static readonly string[] Placeholders =
    {
        "to be filled by o.e.m.", "default string", "none", "n/a", "unknown", "not specified", "not applicable",
        "system serial number", "system product name", "0", "123456789", "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
    };

    /// <summary>Lower-cases and trims; returns null for empty, all-zero, all-F or placeholder values.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim().ToLowerInvariant();
        if (Placeholders.Contains(v)) return null;
        var digits = v.Replace("-", "").Replace(":", "");
        if (digits.Length > 0 && (digits.All(c => c == '0') || digits.All(c => c == 'f'))) return null;
        return v;
    }

    /// <summary>First 16 bytes of HMAC-SHA256 with a public per-signal key: domain separation, not secrecy.</summary>
    public static string Hash(string signalId, string normalizedValue)
    {
        var key = Encoding.UTF8.GetBytes("PEMS-FP-V1:" + signalId);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalizedValue));
        return Base64Url.Encode(mac.AsSpan(0, 16));
    }

    /// <summary>Composite hash over the sorted signal ids and hashes (design 3.3).</summary>
    public static string FingerprintHash(IEnumerable<SignalEntry> signals)
    {
        var text = string.Join("\n", signals.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id + ":" + s.H));
        return Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
