using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>The read-only impact checks of the definition store (SCRUM-450, 457, 459); see <see cref="IDefinitionImpact"/>.</summary>
public sealed class DefinitionImpactService(PEMSDbContext db, IEnumerable<IDefinitionKind> kinds) : IDefinitionImpact
{
    private readonly IReadOnlyDictionary<string, IDefinitionKind> _kinds = kinds.ToDictionary(k => k.Key, StringComparer.Ordinal);

    public async Task<IReadOnlyList<DefinitionUsage>> UsedByAsync(string kind, string key, CancellationToken ct = default)
    {
        // a reference of a page to a component definition is stored as "component-definition"; the store kind is "component"
        var refKind = kind == "component" ? "component-definition" : kind;
        var rows = await db.DefinitionReferences.AsNoTracking()
            .Where(r => (r.ToKind == kind || r.ToKind == refKind) && r.ToKey == key && r.FromVersion!.Definition!.PublishedVersionId == r.FromVersionId)
            .Select(r => new { r.FromVersion!.Definition!.Kind, r.FromVersion.Definition.Key, r.Role })
            .ToListAsync(ct);
        return rows.Select(x => new DefinitionUsage(x.Kind, x.Key, x.Role)).Distinct().OrderBy(u => u.Kind, StringComparer.Ordinal).ThenBy(u => u.Key, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<DefinitionTarget>> ReferencesAsync(int id, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, ct);
        var current = def?.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.PublishedVersionId);
        if (def == null || current == null || !_kinds.TryGetValue(def.Kind, out var kind)) return [];
        var targets = new List<DefinitionTarget>();
        foreach (var r in kind.References(current.PayloadJson).Distinct())
        {
            var storeKind = r.ToKind == "component-definition" ? "component" : r.ToKind;
            // the same scope as the check before publish: interface components and code routes are not definitions of the store
            var owned = r.ToKind != "component" && r.Role is not ("menu-link" or "routes-to") && _kinds.ContainsKey(storeKind);
            var exists = !owned || await db.Definitions.AsNoTracking().AnyAsync(d => d.Kind == storeKind && d.Key == r.ToKey && d.PublishedVersionId != null, ct);
            targets.Add(new DefinitionTarget(storeKind, r.ToKey, r.Role, exists));
        }
        return targets.OrderBy(t => t.Kind, StringComparer.Ordinal).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
    }

    public async Task<ImpactReport> PrecheckPublishAsync(int id, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, ct);
        if (def == null) return ImpactReport.From([new ImpactItem("blocked", "/", "The definition does not exist.")]);
        var draft = def.Versions.Where(v => v.Status is DefinitionStatus.Draft or DefinitionStatus.Validated).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (draft == null) return ImpactReport.From([new ImpactItem("blocked", "/", "There is no draft to publish.")]);
        return await CheckAsync(def, draft.PayloadJson, ct);
    }

    public async Task<ImpactReport> PrecheckRollbackAsync(int id, int toVersion, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, ct);
        if (def == null) return ImpactReport.From([new ImpactItem("blocked", "/", "The definition does not exist.")]);
        var target = def.Versions.FirstOrDefault(v => v.VersionNumber == toVersion && v.Status is DefinitionStatus.Published or DefinitionStatus.Retired);
        if (target == null) return ImpactReport.From([new ImpactItem("blocked", "/toVersion", "That version does not exist or was never published.")]);
        return await CheckAsync(def, target.PayloadJson, ct);
    }

    private async Task<Definition?> LoadAsync(int id, CancellationToken ct)
        => await db.Definitions.AsNoTracking().Include(d => d.Versions).FirstOrDefaultAsync(d => d.DefinitionId == id, ct);

    private async Task<ImpactReport> CheckAsync(Definition def, string payloadJson, CancellationToken ct)
    {
        var items = new List<ImpactItem>();
        if (_kinds.TryGetValue(def.Kind, out var kind))
        {
            foreach (var r in kind.References(payloadJson).Distinct())
            {
                // interface components come from the registry and menu or route links may point at code routes: only definitions the store owns are checked
                if (r.ToKind == "component" || r.Role is "menu-link" or "routes-to") continue;
                var storeKind = r.ToKind == "component-definition" ? "component" : r.ToKind;
                if (!_kinds.ContainsKey(storeKind)) continue;
                var published = await db.Definitions.AsNoTracking().AnyAsync(d => d.Kind == storeKind && d.Key == r.ToKey && d.PublishedVersionId != null, ct);
                if (!published) items.Add(new ImpactItem("blocked", $"/references/{storeKind}/{r.ToKey}", $"The {storeKind} '{r.ToKey}' ({r.Role}) does not exist or is not published."));
            }
        }
        var current = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.PublishedVersionId);
        if (current != null) items.AddRange(DefinitionCompatibility.Analyze(def.Kind, current.PayloadJson, payloadJson));
        return ImpactReport.From(items);
    }
}
