using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Licensing.Seal;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Licensing;

/// <summary>Database copy of the seal (S-DB, licensing design 4.2). Opens its own scope so the singleton licence manager can use it.</summary>
public sealed class DbSealStore : ISealStore
{
    private readonly IServiceScopeFactory _scopes;

    public DbSealStore(IServiceScopeFactory scopes) => _scopes = scopes;

    public string Name => "database";

    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        return await db.Set<LicenseSealRecord>().AsNoTracking().Select(x => x.Bundle).FirstOrDefaultAsync(ct);
    }

    public async Task WriteAsync(string bundleJson, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var row = await db.Set<LicenseSealRecord>().FirstOrDefaultAsync(ct);
        if (row is null) db.Set<LicenseSealRecord>().Add(new LicenseSealRecord { Bundle = bundleJson, UpdatedAtUtc = DateTime.UtcNow });
        else { row.Bundle = bundleJson; row.UpdatedAtUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Database mirror of the event chain; the file sink is authoritative so this can lag while the database is not ready.</summary>
public sealed class DbEventSink : ILicenseEventSink
{
    private readonly IServiceScopeFactory _scopes;

    public DbEventSink(IServiceScopeFactory scopes) => _scopes = scopes;

    public async Task<LicenseEventEntry?> LastAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var e = await db.Set<LicenseEventRecord>().AsNoTracking().OrderByDescending(x => x.Seq).FirstOrDefaultAsync(ct);
        return e is null ? null : new LicenseEventEntry(e.Seq, new DateTimeOffset(DateTime.SpecifyKind(e.UtcAt, DateTimeKind.Utc)), e.Type, e.DataHash, e.PrevHash, e.EntryHash);
    }

    public async Task AppendAsync(LicenseEventEntry entry, CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        db.Set<LicenseEventRecord>().Add(new LicenseEventRecord
        {
            Seq = entry.Seq, UtcAt = entry.Utc.UtcDateTime, Type = entry.Type, DataHash = entry.DataHash, PrevHash = entry.PrevHash, EntryHash = entry.EntryHash,
        });
        await db.SaveChangesAsync(ct);
    }
}
