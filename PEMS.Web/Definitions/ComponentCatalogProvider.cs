using PEMS.UI.Manifest;

namespace PEMS.Web.Definitions;

// SCRUM-340: the component catalog is data too. Every registered component describes itself (key, category, icon, options schema, a sample); this provider serves those descriptions as grid rows,
// so the catalog list and its details are stored pages over a data source (entity-list and entity-details), not a compiled host. A designer reads the same rows to build its palette.

/// <summary>Grid rows of the registered components: <c>key, component, category, icon, nameKey, parameters, sample, options</c>. Read-only; any signed-in user may read it (it is the vocabulary of the product, no customer data).</summary>
public sealed class ComponentCatalogGridProvider(ComponentManifestCatalog catalog) : IGridDataProvider
{
    public string Key => "components";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var rows = catalog.All().Select(m => new Dictionary<string, object?>
        {
            ["key"] = m.Key,
            ["component"] = m.Component,
            ["category"] = m.Category,
            ["icon"] = m.Icon,
            ["nameKey"] = m.NameKey,
            ["parameters"] = string.Join(", ", m.Parameters.Select(p => p.Name + (p.Required ? "*" : ""))),
            ["sample"] = m.Sample?.ToJsonString(),
            ["options"] = m.Options.ToJsonString()
        }).ToList();

        IEnumerable<Dictionary<string, object?>> query = rows;
        if (!string.IsNullOrWhiteSpace(request.Grid.Search))
        {
            var q = request.Grid.Search.Trim();
            query = query.Where(r => ((string)r["key"]!).Contains(q, StringComparison.OrdinalIgnoreCase) || ((string)r["category"]!).Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var f in request.Grid.Filters)
        {
            var field = f.Field; var value = f.Value;
            if (!rows[0].ContainsKey(field)) continue;
            query = f.Op == "eq" ? query.Where(r => string.Equals(r[field]?.ToString(), value, StringComparison.OrdinalIgnoreCase)) : query.Where(r => (r[field]?.ToString() ?? "").Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        var list = query.ToList();
        var sort = request.Grid.Sort.FirstOrDefault(s => rows[0].ContainsKey(s.Field));
        list = (sort is null ? list.OrderBy(r => (string)r["key"]!, StringComparer.Ordinal) : sort.Descending ? list.OrderByDescending(r => r[sort.Field]?.ToString(), StringComparer.Ordinal) : list.OrderBy(r => r[sort.Field]?.ToString(), StringComparer.Ordinal)).ToList();
        var page = Math.Max(1, request.Grid.Page);
        var items = list.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).Cast<object>().ToList();
        return Task.FromResult(((IReadOnlyList<object>)items, list.Count));
    }
}
