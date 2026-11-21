using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PEMS.Application.Definitions;

// SCRUM-576 (render plans) and SCRUM-574 (hydrated results per permission set): the two caches of the definition pipeline. Both are measured first (SCRUM-577: stage timings and counts) and both are safe by construction:
// a plan is immutable and keyed by the content hash of the definition version, a hydrated result is keyed by (definition hash, permission set, culture, scope) and is never row-level data.

/// <summary>One definition version compiled for the pipeline: parsed once, never changed; every request works on its own copy (<see cref="Instantiate"/>).</summary>
public sealed record RenderPlan(string Kind, string Key, string ContentHash, JsonNode Template)
{
    public JsonNode Instantiate() => Template.DeepClone();
}

/// <summary>
/// SCRUM-576: definitions are compiled into <see cref="RenderPlan"/>s once per content hash instead of being parsed on every request. A new version (a new hash) replaces the plan in one atomic step, so a request
/// sees either the old plan or the new one; a version that does not compile keeps the last good plan, so an invalid edit never takes a page down.
/// </summary>
public sealed class RenderPlanCache
{
    private sealed record Entry(string Hash, RenderPlan Plan);

    private readonly ConcurrentDictionary<(string Kind, string Key, string AppKey), Entry> _plans = new();
    private long _compiles;

    /// <summary>How many plans were compiled (diagnostics: after warm-up this stays flat).</summary>
    public long Compiles => Interlocked.Read(ref _compiles);

    public int Count => _plans.Count;

    /// <summary>The plan of this version; the last good plan when this version does not compile; null when there is none at all.</summary>
    public RenderPlan? Get(PublishedDefinition published)
    {
        var id = (published.Kind, published.Key, published.AppKey);
        if (_plans.TryGetValue(id, out var current) && current.Hash == published.ContentHash) return current.Plan;
        try
        {
            var template = JsonNode.Parse(published.PayloadJson) ?? throw new JsonException("The payload is empty.");
            var plan = new RenderPlan(published.Kind, published.Key, published.ContentHash, template);
            _plans[id] = new Entry(published.ContentHash, plan);         // one assignment: the swap is atomic
            Interlocked.Increment(ref _compiles);
            return plan;
        }
        catch (JsonException)
        {
            return _plans.TryGetValue(id, out var lastGood) ? lastGood.Plan : null;
        }
    }

    public void Clear() => _plans.Clear();
}

/// <summary>
/// SCRUM-574: hydrated results (option lists, lookups) shared by the users of one permission set. The key is (definition content hash, the sorted roles, culture, scope); a changed definition has a new hash, a changed
/// permission set has other roles, and an entry lives only for its time to live, so a data change shows within that delay. A result that depends on the user (row-level rules, "my items") must not be cached: a provider opts in with
/// <c>CacheSeconds</c> and only when its rows do not depend on the user beyond the roles.
/// </summary>
public sealed class HydrationCache(TimeProvider? time = null)
{
    private sealed record Entry(object Value, DateTimeOffset Expires);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Share of lookups answered from the cache (0 to 1; 0 when nothing was asked).</summary>
    public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);

    /// <summary>The permission set as a short stable hash: the same roles in any order and case are the same set.</summary>
    public static string PermissionSet(IEnumerable<string> roles)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', roles.Select(r => r.Trim().ToLowerInvariant()).Where(r => r.Length > 0).Distinct().OrderBy(r => r, StringComparer.Ordinal)))))[..16];

    public static string Key(string contentHash, IEnumerable<string> roles, string culture, string scope)
        => string.Join('|', contentHash, PermissionSet(roles), culture.ToLowerInvariant(), scope);

    /// <summary>The cached value, or the one <paramref name="factory"/> makes (kept for <paramref name="ttl"/>). A zero or negative time to live never caches.</summary>
    public async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory) where T : class
    {
        if (ttl <= TimeSpan.Zero) return await factory();
        var now = _time.GetUtcNow();
        if (_entries.TryGetValue(key, out var hit) && hit.Expires > now)
        {
            Interlocked.Increment(ref _hits);
            return (T)hit.Value;
        }
        Interlocked.Increment(ref _misses);
        var value = await factory();
        _entries[key] = new Entry(value, now + ttl);
        return value;
    }

    public void Clear() => _entries.Clear();

    /// <summary>Drops the entries of one definition hash (a definition change or an explicit invalidation).</summary>
    public int Evict(string contentHash)
    {
        var removed = 0;
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(contentHash + "|", StringComparison.Ordinal)))
            if (_entries.TryRemove(key, out _)) removed++;
        return removed;
    }
}
