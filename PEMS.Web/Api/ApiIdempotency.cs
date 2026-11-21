using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace PEMS.Web.Api;

/// <summary>A stored first response of a create that carried an <c>Idempotency-Key</c>.</summary>
public sealed record IdempotentResponse(string Fingerprint, int Status, string Location, string ETag, object Body);

/// <summary>
/// Idempotency-Key store (chapter 08, rule 7). In memory for this walking skeleton: a replay within 24 hours on the same server returns the
/// first response; the durable store comes with the definition store API (#434).
/// </summary>
public sealed class ApiIdempotencyStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private readonly IMemoryCache _cache;

    public ApiIdempotencyStore(IMemoryCache cache) => _cache = cache;

    public static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public bool TryGet(string user, string key, out IdempotentResponse? stored) => _cache.TryGetValue(CacheKey(user, key), out stored);

    public void Save(string user, string key, IdempotentResponse response) => _cache.Set(CacheKey(user, key), response, Lifetime);

    private static string CacheKey(string user, string key) => "idem:" + user + ":" + key;
}
