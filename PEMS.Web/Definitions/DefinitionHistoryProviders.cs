using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

// SCRUM-784 (gap of SCRUM-449) and SCRUM-789: the version history of a definition, the difference between two versions and the verdict of publishing a retired version again are rows of data sources, so the
// history page is a stored page over them (no compiled screen). Rights: the data sources name Definitions.View; the providers only shape rows.

/// <summary>One difference between two payloads: a JSON Pointer, what happened (<c>added</c>, <c>removed</c>, <c>changed</c>) and both values as text.</summary>
public sealed record PayloadDifference(string Pointer, string Change, string? From, string? To);

/// <summary>The JSON-aware difference of two payloads (SCRUM-784): objects are compared by property and arrays by position, so a text in fa or en shows as its own pointer. Pure.</summary>
public static class PayloadDiff
{
    public const int MaxRows = 500;

    public static IReadOnlyList<PayloadDifference> Compare(string fromJson, string toJson)
    {
        var rows = new List<PayloadDifference>();
        Walk(Parse(fromJson), Parse(toJson), "", rows);
        return rows;
    }

    private static JsonNode? Parse(string json) { try { return JsonNode.Parse(json); } catch (JsonException) { return null; } }

    private static string Show(JsonNode? node) => node is null ? "" : node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString();

    private static void Walk(JsonNode? from, JsonNode? to, string pointer, List<PayloadDifference> rows)
    {
        if (rows.Count >= MaxRows) return;
        switch (from, to)
        {
            case (JsonObject fo, JsonObject to2):
                foreach (var key in fo.Select(p => p.Key).Concat(to2.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
                    Walk(fo.TryGetPropertyValue(key, out var a) ? a : null, to2.TryGetPropertyValue(key, out var b) ? b : null, pointer + "/" + key.Replace("~", "~0").Replace("/", "~1"), rows);
                break;
            case (JsonArray fa, JsonArray ta):
                for (var i = 0; i < Math.Max(fa.Count, ta.Count); i++) Walk(i < fa.Count ? fa[i] : null, i < ta.Count ? ta[i] : null, pointer + "/" + i, rows);
                break;
            default:
                if (from is null && to is null) return;
                if (from is null) rows.Add(new PayloadDifference(pointer, "added", null, Show(to)));
                else if (to is null) rows.Add(new PayloadDifference(pointer, "removed", Show(from), null));
                else if (!JsonNode.DeepEquals(from, to)) rows.Add(new PayloadDifference(pointer, "changed", Show(from), Show(to)));
                break;
        }
    }
}

internal static class HistoryRows
{
    /// <summary>The id of the definition of <c>appKey:kind:key</c>, or null.</summary>
    public static async Task<int?> FindAsync(IDefinitionService definitions, string reference, CancellationToken ct)
    {
        var parts = (reference ?? "").Split(':', 3);
        if (parts.Length != 3) return null;
        var page = await definitions.ListAsync(new DefinitionListQuery(parts[1], parts[0], parts[2], null, [], 1, 20), ct);
        return page.Items.FirstOrDefault(d => d.AppKey == parts[0] && d.Kind == parts[1] && d.Key == parts[2])?.Id;
    }
}

/// <summary>
/// The versions of one definition (filter <c>ref</c> = <c>appKey:kind:key</c>), newest first (SCRUM-784, 789): <c>versionRef</c> (the row id, <c>ref:number</c>), <c>number</c>, <c>status</c>, <c>hash</c>, <c>summary</c>, <c>createdAt</c>,
/// <c>publishedAt</c>, <c>publishedBy</c>, <c>retiredAt</c> and <c>verdict</c> with <c>reason</c>: for a retired or published version whether it can be published again against today's definitions (allowed, warnings or blocked and why),
/// for an open draft whether it can be published.
/// </summary>
public sealed class DefinitionVersionsGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-versions";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var reference = WorkbenchRows.FilterValue(request, "ref");
        if (reference.Length == 0) return ([], 0);
        using var scope = scopes.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var impact = scope.ServiceProvider.GetRequiredService<IDefinitionImpact>();
        var id = await HistoryRows.FindAsync(definitions, reference, ct);
        if (id is null) return ([], 0);
        var detail = await definitions.GetAsync(id.Value, ct);
        if (!detail.Success) return ([], 0);

        var rows = new List<object>();
        foreach (var v in detail.Value!.Versions.OrderByDescending(v => v.Number))
        {
            var verdict = "";
            var reason = "";
            if (v.Number != detail.Value.Summary.PublishedVersion && v.Status is PEMS.Domain.Entities.Definitions.DefinitionStatus.Retired or PEMS.Domain.Entities.Definitions.DefinitionStatus.Published)
            {
                var report = await impact.PrecheckRollbackAsync(id.Value, v.Number, ct);
                (verdict, reason) = (report.Verdict, report.Text());
            }
            else if (v.Status is PEMS.Domain.Entities.Definitions.DefinitionStatus.Draft or PEMS.Domain.Entities.Definitions.DefinitionStatus.Validated)
            {
                var report = await impact.PrecheckPublishAsync(id.Value, ct);
                (verdict, reason) = (report.Verdict, report.Text());
            }
            rows.Add(new Dictionary<string, object?>
            {
                ["versionRef"] = reference + ":" + v.Number, ["ref"] = reference, ["number"] = v.Number, ["status"] = v.Status.ToString(), ["hash"] = v.ContentHash.Length > 12 ? v.ContentHash[..12] : v.ContentHash,
                ["summary"] = v.ChangeSummary ?? "", ["previewUrl"] = $"/preview/definition?ref={Uri.EscapeDataString(reference)}&version={v.Number}", ["createdAt"] = v.CreatedAtUtc.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
                ["publishedAt"] = v.PublishedAtUtc?.ToString("s", System.Globalization.CultureInfo.InvariantCulture) ?? "", ["publishedBy"] = v.PublishedBy ?? "",
                ["retiredAt"] = v.RetiredAtUtc?.ToString("s", System.Globalization.CultureInfo.InvariantCulture) ?? "", ["verdict"] = verdict, ["reason"] = reason,
            });
        }
        return (rows, rows.Count);
    }
}

/// <summary>The difference between two versions (filters <c>ref</c>, <c>from</c> and <c>to</c>, the version numbers): <c>pointer</c>, <c>change</c> (added, removed, changed), <c>from</c> and <c>to</c> as text, in the order of the payload.</summary>
public sealed class DefinitionDiffGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-diff";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var reference = WorkbenchRows.FilterValue(request, "ref");
        if (reference.Length == 0 || !int.TryParse(WorkbenchRows.FilterValue(request, "from"), out var from) || !int.TryParse(WorkbenchRows.FilterValue(request, "to"), out var to)) return ([], 0);
        using var scope = scopes.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var id = await HistoryRows.FindAsync(definitions, reference, ct);
        if (id is null) return ([], 0);
        var a = await definitions.GetVersionAsync(id.Value, from, ct);
        var b = await definitions.GetVersionAsync(id.Value, to, ct);
        if (!a.Success || !b.Success) return ([], 0);
        var rows = PayloadDiff.Compare(a.Value!.PayloadJson, b.Value!.PayloadJson);
        var page = Math.Max(1, request.Grid.Page);
        IReadOnlyList<object> items = rows.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize)
            .Select(r => (object)new Dictionary<string, object?> { ["pointer"] = r.Pointer, ["change"] = r.Change, ["from"] = r.From ?? "", ["to"] = r.To ?? "" }).ToList();
        return (items, rows.Count);
    }
}

/// <summary>The packages run on this installation (SCRUM-814), newest first: <c>id</c>, <c>kind</c>, <c>version</c>, <c>result</c> (applied or failed), <c>summary</c>, <c>backup</c>, <c>by</c>, <c>at</c>, <c>hash</c> (first 12 characters).</summary>
public sealed class MigratorHistoryGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "migrator-history";
    public IReadOnlyCollection<string>? AllowedRoles => ["SystemAdmin"];

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMS.Persistence.Context.PEMSDbContext>();
        var total = await db.AppliedPackages.AsNoTracking().CountAsync(ct);
        var page = Math.Max(1, request.Grid.Page);
        var rows = await db.AppliedPackages.AsNoTracking().OrderByDescending(p => p.AppliedAtUtc).Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).ToListAsync(ct);
        return (rows.Select(p => (object)new Dictionary<string, object?>
        {
            ["id"] = p.AppliedPackageId, ["kind"] = p.Kind, ["version"] = p.Version ?? "", ["result"] = p.Succeeded ? "applied" : "failed", ["summary"] = p.Summary ?? "", ["backup"] = p.BackupPoint ?? "",
            ["by"] = p.AppliedBy ?? "", ["at"] = p.AppliedAtUtc.ToString("s", System.Globalization.CultureInfo.InvariantCulture), ["hash"] = p.PackageHash.Length > 12 ? p.PackageHash[..12] : p.PackageHash,
        }).ToList(), total);
    }
}
