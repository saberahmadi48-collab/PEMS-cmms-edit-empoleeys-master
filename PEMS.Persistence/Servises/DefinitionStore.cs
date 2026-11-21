using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>Counters of the definition cache since the process started (SCRUM-786): shown in the storage report so a cache that does not work is visible. Thread safe, no I/O.</summary>
public static class DefinitionCacheStats
{
    private static long _hits, _misses, _stampReads, _evictions;
    public static long Hits => Interlocked.Read(ref _hits);
    public static long Misses => Interlocked.Read(ref _misses);
    public static long StampReads => Interlocked.Read(ref _stampReads);
    public static long Evictions => Interlocked.Read(ref _evictions);
    internal static void Hit() => Interlocked.Increment(ref _hits);
    internal static void Miss() => Interlocked.Increment(ref _misses);
    internal static void StampRead() => Interlocked.Increment(ref _stampReads);
    internal static void Evict() => Interlocked.Increment(ref _evictions);
}

/// <summary>
/// Runtime read side of the definition store (ADR-0010 section 5): the published version only, cached per (kind, key). A cached entry is trusted while
/// the per-kind stamp is unchanged; the stamp itself is read at most once per second, so another instance sees a publish within about 2 seconds,
/// and the instance that published evicts at once.
/// </summary>
public sealed class DefinitionStore : IDefinitionStore
{
    private static readonly TimeSpan StampLifetime = TimeSpan.FromSeconds(1);
    private readonly PEMSDbContext _db;
    private readonly IMemoryCache _cache;

    public DefinitionStore(PEMSDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private sealed record Entry(long Stamp, PublishedDefinition? Value);

    public async Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
    {
        var stamp = await StampAsync(kind, ct);
        var cacheKey = $"def:{appKey}:{kind}:{key}";
        if (_cache.TryGetValue(cacheKey, out Entry? hit) && hit!.Stamp == stamp) { DefinitionCacheStats.Hit(); return hit.Value; }
        DefinitionCacheStats.Miss();

        var row = await _db.Definitions.AsNoTracking()
            .Where(d => d.AppKey == appKey && d.Kind == kind && d.Key == key && d.PublishedVersionId != null)
            .Select(d => new PublishedDefinition(d.Kind, d.Key, d.AppKey, d.PublishedVersion!.VersionNumber, d.PublishedVersion.PayloadJson, d.PublishedVersion.ContentHash, d.TitleJson))
            .FirstOrDefaultAsync(ct);
        _cache.Set(cacheKey, new Entry(stamp, row), TimeSpan.FromHours(1));
        return row;
    }

    /// <summary>All published definitions of a kind, cached by the same per-kind stamp as a single read (the route table and the entity registry read whole kinds).</summary>
    public async Task<IReadOnlyList<PublishedDefinition>> ListPublishedAsync(string kind, string appKey = "core", CancellationToken ct = default)
    {
        var stamp = await StampAsync(kind, ct);
        var cacheKey = $"deflist:{appKey}:{kind}";
        if (_cache.TryGetValue(cacheKey, out ListEntry? hit) && hit!.Stamp == stamp) { DefinitionCacheStats.Hit(); return hit.Value; }
        DefinitionCacheStats.Miss();

        var rows = await _db.Definitions.AsNoTracking()
            .Where(d => d.AppKey == appKey && d.Kind == kind && d.PublishedVersionId != null)
            .OrderBy(d => d.Key)
            .Select(d => new PublishedDefinition(d.Kind, d.Key, d.AppKey, d.PublishedVersion!.VersionNumber, d.PublishedVersion.PayloadJson, d.PublishedVersion.ContentHash, d.TitleJson))
            .ToListAsync(ct);
        _cache.Set(cacheKey, new ListEntry(stamp, rows), TimeSpan.FromHours(1));
        return rows;
    }

    private sealed record ListEntry(long Stamp, IReadOnlyList<PublishedDefinition> Value);

    private async Task<long> StampAsync(string kind, CancellationToken ct)
    {
        var stampKey = "defstamp:" + kind;
        if (_cache.TryGetValue(stampKey, out long cached)) return cached;
        DefinitionCacheStats.StampRead();
        var value = await _db.DefinitionStamps.AsNoTracking().Where(s => s.Kind == kind).Select(s => s.Value).FirstOrDefaultAsync(ct);
        _cache.Set(stampKey, value, StampLifetime);
        return value;
    }

    /// <summary>Called by the writer after a publish: the next read sees the new stamp immediately.</summary>
    public void Evict(string kind)
    {
        DefinitionCacheStats.Evict();
        _cache.Remove("defstamp:" + kind);
    }
}
