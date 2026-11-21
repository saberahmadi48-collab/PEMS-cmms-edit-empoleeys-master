using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Definitions;

/// <summary>SCRUM-591: the grants of a definition from the database (a small indexed table, read once per opened definition).</summary>
public sealed class EfDefinitionGrantStore(PEMSDbContext db) : IDefinitionGrantStore
{
    public async Task<IReadOnlyList<DefinitionGrantRecord>> ForAsync(string kind, string key, CancellationToken ct = default)
        => await db.DefinitionGrants.AsNoTracking().Where(g => g.Kind == kind && g.DefinitionKey == key)
            .Select(g => new DefinitionGrantRecord(g.Kind, g.DefinitionKey, g.RoleName, g.Effect)).ToListAsync(ct);
}
