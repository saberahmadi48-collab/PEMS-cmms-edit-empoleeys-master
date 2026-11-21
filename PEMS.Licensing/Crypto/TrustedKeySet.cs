using System.Reflection;
using System.Text.Json;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Crypto;

/// <summary>One vendor public key the product accepts. <see cref="RetiredAfter"/> ends trust in licences issued after that instant.</summary>
public sealed record TrustedKey(string KeyId, string PublicKey, DateTimeOffset? RetiredAfter = null);

/// <summary>The embedded set of vendor public keys (design 5.3). Rotation adds a key through a signed update, never through a setting.</summary>
public sealed class TrustedKeySet
{
    private readonly Dictionary<string, TrustedKey> _keys;

    public TrustedKeySet(IEnumerable<TrustedKey> keys)
        => _keys = keys.ToDictionary(k => k.KeyId, StringComparer.Ordinal);

    public IReadOnlyCollection<TrustedKey> Keys => _keys.Values;

    public bool TryGet(string keyId, out TrustedKey key) => _keys.TryGetValue(keyId, out key!);

    /// <summary>Keys compiled into this assembly (empty until the real public key exists, plan row #107).</summary>
    public static TrustedKeySet LoadEmbedded()
    {
        using var stream = typeof(TrustedKeySet).Assembly.GetManifestResourceStream("PEMS.Licensing.trusted-keys.json")
            ?? throw new InvalidOperationException("Embedded trusted-keys.json is missing.");
        var doc = JsonSerializer.Deserialize<TrustedKeyFile>(stream, LicenseJson.Options);
        return new TrustedKeySet(doc?.Keys ?? new List<TrustedKey>());
    }

    private sealed record TrustedKeyFile(List<TrustedKey> Keys);
}
