using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Persistence.Context;

namespace PEMS.Web.Definitions;

// SCRUM-449, 450, 451, 455: the definition workbench reads definitions as grid rows, so its list, details, usage and storage pages are stored pages over these data sources (entity-list, record-view,
// entity-chart): nothing about the workbench is a compiled screen. Each data source definition names a permission (Definitions.View); the providers only shape rows.

internal static class WorkbenchRows
{
    /// <summary>A title of a definition (a language map or a plain string) in the language of the request.</summary>
    public static string Title(string titleJson, string culture)
    {
        try
        {
            using var doc = JsonDocument.Parse(titleJson);
            if (doc.RootElement.ValueKind == JsonValueKind.String) return doc.RootElement.GetString() ?? "";
            foreach (var k in new[] { culture, "fa", "en" })
                if (doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) return v.GetString()!;
        }
        catch (JsonException) { }
        return "";
    }

    public static string FilterValue(GridDataRequest request, string field)
        => request.Grid.Filters.FirstOrDefault(f => f.Field == field && f.Op == "eq")?.Value ?? "";
}

/// <summary>The definitions of the store as rows: <c>ref</c> (appKey:kind:key, the id of a row), key, kind, appKey, title, origin, publishedVersion, hasDraft, isCustomized, revision, createdAt. Filters kind and appKey; the search matches key and title.</summary>
public sealed class DefinitionsGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definitions";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var kind = WorkbenchRows.FilterValue(request, "kind");
        var appKey = WorkbenchRows.FilterValue(request, "appKey");
        var result = await definitions.ListAsync(new DefinitionListQuery(kind.Length == 0 ? null : kind, appKey.Length == 0 ? null : appKey, request.Grid.Search, null, [], Math.Max(1, request.Grid.Page), request.Grid.PageSize), ct);
        IReadOnlyList<object> rows = result.Items.Select(d => (object)new Dictionary<string, object?>
        {
            ["ref"] = $"{d.AppKey}:{d.Kind}:{d.Key}", ["key"] = d.Key, ["kind"] = d.Kind, ["appKey"] = d.AppKey, ["title"] = WorkbenchRows.Title(d.TitleJson, request.Culture),
            ["origin"] = d.Origin.ToString(), ["publishedVersion"] = d.PublishedVersion, ["hasDraft"] = d.HasDraft, ["isCustomized"] = d.IsCustomized, ["revision"] = d.Revision,
            ["createdAt"] = d.CreatedAtUtc.ToString("s", System.Globalization.CultureInfo.InvariantCulture)
        }).ToList();
        return (rows, result.Total);
    }
}

/// <summary>
/// One row per registered definition kind (SCRUM-451, 455): <c>kind</c>, <c>owner</c> (where the kind lives), <c>definitions</c>, <c>published</c>, <c>drafts</c>, <c>versions</c>, <c>payloadBytes</c> (published payloads). The list of
/// kinds, their counts and their size on disk are one data source: the kind catalog and the storage report.
/// </summary>
public sealed class DefinitionKindsGridProvider(IServiceScopeFactory scopes, IEnumerable<IDefinitionKind> kinds, DefinitionOwnership ownership) : IGridDataProvider
{
    public string Key => "definition-kinds";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var perKind = await db.Definitions.AsNoTracking().GroupBy(d => d.Kind)
            .Select(g => new { Kind = g.Key, Definitions = g.Count(), Published = g.Count(d => d.PublishedVersionId != null) }).ToListAsync(ct);
        var versions = await db.DefinitionVersions.AsNoTracking().GroupBy(v => v.Definition!.Kind)
            .Select(g => new { Kind = g.Key, Versions = g.Count(), Drafts = g.Count(v => v.Status == PEMS.Domain.Entities.Definitions.DefinitionStatus.Draft || v.Status == PEMS.Domain.Entities.Definitions.DefinitionStatus.Validated) }).ToListAsync(ct);
        var bytes = await db.Definitions.AsNoTracking().Where(d => d.PublishedVersionId != null).GroupBy(d => d.Kind).Select(g => new { Kind = g.Key, Bytes = g.Sum(d => d.PublishedVersion!.PayloadJson.Length) }).ToListAsync(ct);

        var rows = kinds.Select(k => k.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).Select(k =>
        {
            var c = perKind.FirstOrDefault(x => x.Kind == k); var v = versions.FirstOrDefault(x => x.Kind == k); var b = bytes.FirstOrDefault(x => x.Kind == k);
            return (object)new Dictionary<string, object?>
            {
                ["kind"] = k, ["owner"] = ownership.OwnerOf(k).ToString(), ["definitions"] = c?.Definitions ?? 0, ["published"] = c?.Published ?? 0,
                ["drafts"] = v?.Drafts ?? 0, ["versions"] = v?.Versions ?? 0, ["payloadBytes"] = b?.Bytes ?? 0
            };
        }).ToList();
        return (rows, rows.Count);
    }
}

/// <summary>Where a definition is used (SCRUM-450): the definitions whose published version points at the kind and key of the filters <c>toKind</c> and <c>toKey</c>: <c>fromKind</c>, <c>fromKey</c>, <c>role</c>.</summary>
public sealed class DefinitionUsagesGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-usages";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var toKind = WorkbenchRows.FilterValue(request, "toKind");
        var toKey = WorkbenchRows.FilterValue(request, "toKey");
        if (toKind.Length == 0 || toKey.Length == 0) return ([], 0);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var query = db.DefinitionReferences.AsNoTracking()
            .Where(r => r.ToKind == toKind && r.ToKey == toKey && r.FromVersion!.Definition!.PublishedVersionId == r.FromVersionId)
            .Select(r => new { Kind = r.FromVersion!.Definition!.Kind, Key = r.FromVersion.Definition.Key, r.Role });
        var total = await query.CountAsync(ct);
        var page = Math.Max(1, request.Grid.Page);
        var rows = await query.OrderBy(x => x.Kind).ThenBy(x => x.Key).Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).ToListAsync(ct);
        return (rows.Select(x => (object)new Dictionary<string, object?> { ["fromKind"] = x.Kind, ["fromKey"] = x.Key, ["role"] = x.Role, ["ref"] = x.Kind + ":" + x.Key }).ToList(), total);
    }
}

/// <summary>
/// The heaviest definitions (SCRUM-455): one row per definition with <c>ref</c>, <c>kind</c>, <c>key</c>, <c>versions</c>, <c>publishedBytes</c>, <c>totalBytes</c> (every version), ordered by total size, so growth is visible where it
/// happens. Versions are kept forever; nothing here deletes.
/// </summary>
public sealed class DefinitionLargestGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-largest";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var rows = await db.DefinitionVersions.AsNoTracking().GroupBy(v => new { v.Definition!.Kind, v.Definition.Key, v.Definition.AppKey })
            .Select(g => new { g.Key.Kind, g.Key.Key, g.Key.AppKey, Versions = g.Count(), TotalBytes = g.Sum(v => v.PayloadJson.Length) })
            .OrderByDescending(x => x.TotalBytes).Take(Math.Max(1, request.Grid.PageSize)).ToListAsync(ct);
        IReadOnlyList<object> items = rows.Select(x => (object)new Dictionary<string, object?>
            { ["ref"] = $"{x.AppKey}:{x.Kind}:{x.Key}", ["kind"] = x.Kind, ["key"] = x.Key, ["versions"] = x.Versions, ["totalBytes"] = x.TotalBytes }).ToList();
        return (items, items.Count);
    }
}

/// <summary>
/// Growth of the definition store per month (SCRUM-786): one row per month of version creation with <c>month</c> (yyyy-MM), <c>versions</c> created and <c>bytes</c> of their payloads, newest first. Read only; nothing here deletes.
/// </summary>
public sealed class DefinitionGrowthGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-growth";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var rows = await db.DefinitionVersions.AsNoTracking().GroupBy(v => new { v.CreatedAtUtc.Year, v.CreatedAtUtc.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Versions = g.Count(), Bytes = g.Sum(v => v.PayloadJson.Length) })
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(Math.Max(1, request.Grid.PageSize)).ToListAsync(ct);
        IReadOnlyList<object> items = rows.Select(x => (object)new Dictionary<string, object?>
            { ["month"] = $"{x.Year:0000}-{x.Month:00}", ["versions"] = x.Versions, ["bytes"] = x.Bytes }).ToList();
        return (items, items.Count);
    }
}

/// <summary>
/// The health of the definition store (SCRUM-786): rows <c>metric</c>, <c>value</c>, <c>note</c>: the policy (<c>Definitions:RetainRetiredVersions</c>, only <c>forever</c>), the total size of every version, the warning size
/// (<c>Definitions:StorageWarnMb</c>, default 1024) and whether it is exceeded, and the cache counters of this process (hits, misses, stamp reads, evictions).
/// </summary>
public sealed class DefinitionHealthGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public const int DefaultWarnMb = 1024;
    public string Key => "definition-health";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>The warning size in MB from the setting; a missing or invalid value is the default.</summary>
    public static int WarnMb(string? setting) => int.TryParse(setting, out var mb) && mb > 0 ? mb : DefaultWarnMb;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var total = await db.DefinitionVersions.AsNoTracking().SumAsync(v => (long)v.PayloadJson.Length, ct);
        var warnMb = WarnMb(config["Definitions:StorageWarnMb"]);
        var exceeded = total > warnMb * 1024L * 1024L;
        var hits = PEMS.Persistence.Services.DefinitionCacheStats.Hits;
        var misses = PEMS.Persistence.Services.DefinitionCacheStats.Misses;
        var rate = hits + misses == 0 ? "" : $"{100.0 * hits / (hits + misses):0.0}%";
        object Row(string metric, object? value, string note = "") => new Dictionary<string, object?> { ["metric"] = metric, ["value"] = value?.ToString() ?? "", ["note"] = note };
        IReadOnlyList<object> items =
        [
            Row("retainRetiredVersions", config["Definitions:RetainRetiredVersions"] ?? "forever", "retired versions are kept forever; nothing deletes a version"),
            Row("totalBytes", total, "payload size of every version"),
            Row("warnMb", warnMb, "Definitions:StorageWarnMb"),
            Row("warning", exceeded ? "yes" : "no", exceeded ? $"the store is above {warnMb} MB: plan the archive decision (ADR-0010 section 5)" : ""),
            Row("cacheHits", hits, "this process, since start"),
            Row("cacheMisses", misses, rate),
            Row("stampReads", PEMS.Persistence.Services.DefinitionCacheStats.StampReads),
            Row("evictions", PEMS.Persistence.Services.DefinitionCacheStats.Evictions, "publishes seen by this process"),
        ];
        return (items, items.Count);
    }
}

/// <summary>
/// Missing translations of published definitions (SCRUM-419, 609): every language map (an object with <c>en</c> or <c>fa</c>) of a published form, page or menu whose other language is empty or absent is one row: <c>ref</c>,
/// <c>kind</c>, <c>key</c>, <c>path</c> in the payload, <c>missing</c> (the language), <c>shown</c> (the text a user sees through the fallback). Filter <c>kind</c> narrows to one kind.
/// </summary>
public sealed class DefinitionTranslationsGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    private static readonly string[] Languages = ["en", "fa"];
    public string Key => "definition-translations";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var kind = WorkbenchRows.FilterValue(request, "kind");
        var query = db.Definitions.AsNoTracking().Where(d => d.PublishedVersionId != null && (kind == "" ? d.Kind == "form" || d.Kind == "page" || d.Kind == "menu" : d.Kind == kind));
        var published = await query.Select(d => new { d.Kind, d.Key, d.AppKey, Payload = d.PublishedVersion!.PayloadJson }).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var d in published)
        {
            try
            {
                using var doc = JsonDocument.Parse(d.Payload);
                Walk(doc.RootElement, "", (path, missing, shown) => rows.Add(new Dictionary<string, object?>
                    { ["ref"] = $"{d.AppKey}:{d.Kind}:{d.Key}", ["kind"] = d.Kind, ["key"] = d.Key, ["path"] = path, ["missing"] = missing, ["shown"] = shown }));
            }
            catch (JsonException) { }
        }
        var page = Math.Max(1, request.Grid.Page);
        return (rows.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).ToList(), rows.Count);
    }

    private static void Walk(JsonElement e, string path, Action<string, string, string> found)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var isMap = e.EnumerateObject().Any(p => Languages.Contains(p.Name)) && e.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String);
            if (isMap)
            {
                foreach (var lang in Languages)
                {
                    var has = e.TryGetProperty(lang, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString());
                    if (!has)
                    {
                        var other = Languages.First(l => l != lang);
                        found(path.Length == 0 ? "/" : path, lang, e.TryGetProperty(other, out var o) ? o.GetString() ?? "" : "");
                    }
                }
                return;
            }
            foreach (var p in e.EnumerateObject()) Walk(p.Value, path + "/" + p.Name, found);
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in e.EnumerateArray()) Walk(item, path + "/" + i++, found);
        }
    }
}

/// <summary>
/// The parts of one definition as rows for the designers (SCRUM-439, 604, 606, 607, 608): filter <c>ref</c> (appKey:kind:key) and <c>pointer</c> (a JSON Pointer to a list or an object, default /fields). The open draft is read when
/// there is one, otherwise the published version. A list gives one row per item (<c>index</c>, <c>summary</c>, <c>json</c>), an object one row per property (<c>index</c> is the property name).
/// </summary>
public sealed class DefinitionPayloadGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-payload";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var reference = WorkbenchRows.FilterValue(request, "ref");
        var pointer = WorkbenchRows.FilterValue(request, "pointer");
        if (pointer.Length == 0) pointer = "/fields";
        var parts = reference.Split(':', 3);
        if (parts.Length != 3) return ([], 0);
        using var scope = scopes.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var found = (await definitions.ListAsync(new DefinitionListQuery(parts[1], parts[0], parts[2], null, [], 1, 20), ct)).Items.FirstOrDefault(d => d.Kind == parts[1] && d.Key == parts[2] && d.AppKey == parts[0]);
        if (found is null) return ([], 0);

        string? payload = null;
        var draft = await definitions.GetDraftAsync(found.Id, ct);
        if (draft.Success) payload = draft.Value!.PayloadJson;
        else if (found.PublishedVersion is { } v && (await definitions.GetVersionAsync(found.Id, v, ct)) is { Success: true } published) payload = published.Value!.PayloadJson;
        if (payload is null) return ([], 0);

        System.Text.Json.Nodes.JsonNode? node;
        try { node = System.Text.Json.Nodes.JsonNode.Parse(payload); } catch (JsonException) { return ([], 0); }
        foreach (var segment in pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Replace("~1", "/").Replace("~0", "~")))
            node = node switch
            {
                System.Text.Json.Nodes.JsonObject o => o[segment],
                System.Text.Json.Nodes.JsonArray a when int.TryParse(segment, out var i) && i >= 0 && i < a.Count => a[i],
                _ => null
            };
        var rows = new List<object>();
        if (node is System.Text.Json.Nodes.JsonArray array)
            for (var i = 0; i < array.Count; i++) rows.Add(Row(i.ToString(), array[i], reference, pointer));
        else if (node is System.Text.Json.Nodes.JsonObject obj)
            foreach (var p in obj) rows.Add(Row(p.Key, p.Value, reference, pointer));
        var page = Math.Max(1, request.Grid.Page);
        return (rows.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).ToList(), rows.Count);
    }

    private static object Row(string index, System.Text.Json.Nodes.JsonNode? item, string reference, string pointer)
    {
        string summary = item is System.Text.Json.Nodes.JsonObject o
            ? (o["name"] ?? o["key"] ?? o["id"] ?? o["component"] ?? o["title"])?.ToString() ?? ""
            : item?.ToString() ?? "";
        return new Dictionary<string, object?> { ["rowId"] = reference + "|" + pointer + "|" + index, ["index"] = index, ["summary"] = summary, ["json"] = item?.ToJsonString() ?? "null" };
    }
}

/// <summary>The operations of the product as rows for the action designer picker (SCRUM-606): <c>ref</c> (Model.Action), <c>model</c>, <c>action</c>, <c>permission</c>, <c>arguments</c> (the names the operation takes).</summary>
public sealed class OperationCatalogGridProvider(OperationCatalog catalog) : IGridDataProvider
{
    public string Key => "operation-catalog";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var search = (request.Grid.Search ?? "").Trim();
        var all = catalog.Describe().Where(o => search.Length == 0 || o.Model.Contains(search, StringComparison.OrdinalIgnoreCase) || o.Action.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        var page = Math.Max(1, request.Grid.Page);
        IReadOnlyList<object> rows = all.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize)
            .Select(o => (object)new Dictionary<string, object?> { ["ref"] = o.Model + "." + o.Action, ["model"] = o.Model, ["action"] = o.Action, ["permission"] = o.Permission ?? "", ["arguments"] = string.Join(", ", o.Arguments) }).ToList();
        return Task.FromResult((rows, all.Count));
    }
}

/// <summary>
/// The arguments of one operation as rows for the action designer (SCRUM-811): filter <c>ref</c> = <c>Model.Action</c>; one row per argument with <c>argument</c>, <c>source</c> (the source the designer suggests: <c>context:userName</c>
/// for <c>actor</c> and <c>userName</c>, <c>context:roles</c> for <c>roles</c>, else <c>field:name</c> of the same name) and the <c>json</c> of the whole arguments object, so an action is written without guessing a name.
/// </summary>
public sealed class OperationArgumentsGridProvider(OperationCatalog catalog) : IGridDataProvider
{
    public string Key => "operation-arguments";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>The source a designer suggests for an argument by its name.</summary>
    public static string Suggest(string argument) => argument switch
    {
        "actor" or "userName" or "performedBy" => "context:userName",
        "roles" => "context:roles",
        "employeeId" => "context:employeeId",
        "culture" => "context:culture",
        _ => "field:" + argument,
    };

    public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var reference = WorkbenchRows.FilterValue(request, "ref");
        var dot = reference.IndexOf('.');
        var operation = dot <= 0 ? null : catalog.Describe().FirstOrDefault(o => o.Model == reference[..dot] && o.Action == reference[(dot + 1)..]);
        if (operation is null) return Task.FromResult(((IReadOnlyList<object>)[], 0));
        var json = "{ " + string.Join(", ", operation.Arguments.Select(a => $"\"{a}\": \"{Suggest(a)}\"")) + " }";
        IReadOnlyList<object> rows = operation.Arguments.Select(a => (object)new Dictionary<string, object?> { ["argument"] = a, ["source"] = Suggest(a), ["json"] = json }).ToList();
        return Task.FromResult((rows, rows.Count));
    }
}

/// <summary>
/// The items of a menu definition as an indented tree (SCRUM-813): filter <c>ref</c> = <c>appKey:menu:key</c> (the published version, or the draft with <c>draft</c> = <c>1</c>); rows <c>id</c> (<c>ref|/items|index</c>, the row id of the
/// designer move buttons), <c>key</c>, <c>title</c> (indented by depth), <c>parent</c>, <c>order</c>, <c>target</c>.
/// </summary>
public sealed class MenuTreeGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "menu-tree";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>The items in tree order (children after their parent, siblings by order then key) with their depth; an item whose parent does not exist is shown at the top level. Pure.</summary>
    public static IReadOnlyList<(int Index, JsonElement Item, int Depth)> Walk(JsonElement items)
    {
        var list = items.EnumerateArray().Select((e, i) => (Index: i, Item: e)).ToList();
        string? KeyOf(JsonElement e) => e.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
        string? ParentOf(JsonElement e) => e.TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        int OrderOf(JsonElement e) => e.TryGetProperty("order", out var o) && o.TryGetInt32(out var n) ? n : 0;
        var keys = list.Select(x => KeyOf(x.Item)).Where(k => k != null).ToHashSet(StringComparer.Ordinal);
        var result = new List<(int, JsonElement, int)>();
        var seen = new HashSet<int>();
        void Add(string? parent, int depth)
        {
            foreach (var x in list.Where(x => (parent is null ? ParentOf(x.Item) is null || !keys.Contains(ParentOf(x.Item)!) : ParentOf(x.Item) == parent)).OrderBy(x => OrderOf(x.Item)).ThenBy(x => KeyOf(x.Item), StringComparer.Ordinal))
            {
                if (!seen.Add(x.Index) || depth > 8) continue;
                result.Add((x.Index, x.Item, depth));
                if (KeyOf(x.Item) is { } k) Add(k, depth + 1);
            }
        }
        Add(null, 0);
        return result;
    }

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var reference = WorkbenchRows.FilterValue(request, "ref");
        if (reference.Length == 0) return ([], 0);
        using var scope = scopes.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionService>();
        var id = await HistoryRows.FindAsync(definitions, reference, ct);
        if (id is null) return ([], 0);
        var payload = WorkbenchRows.FilterValue(request, "draft") == "1" ? await definitions.GetDraftAsync(id.Value, ct) : null;
        if (payload is null || !payload.Success)
        {
            var detail = await definitions.GetAsync(id.Value, ct);
            if (!detail.Success || detail.Value!.Summary.PublishedVersion is not { } published) return ([], 0);
            payload = await definitions.GetVersionAsync(id.Value, published, ct);
            if (!payload.Success) return ([], 0);
        }
        using var doc = JsonDocument.Parse(payload.Value!.PayloadJson);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return ([], 0);
        string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var rows = Walk(items).Select(w =>
        {
            var title = w.Item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.Object ? (t.TryGetProperty(request.Culture, out var tl) && tl.ValueKind == JsonValueKind.String ? tl.GetString() : t.TryGetProperty("en", out var te) ? te.GetString() : "") : Text(w.Item, "titleKey");
            var target = Text(w.Item, "page") is { Length: > 0 } pg ? "/p/" + pg : Text(w.Item, "form") is { Length: > 0 } fm ? "/f/" + fm : Text(w.Item, "url");
            return (object)new Dictionary<string, object?>
            {
                ["id"] = reference + "|/items|" + w.Index, ["key"] = Text(w.Item, "key"), ["title"] = new string('\u00A0', w.Depth * 4) + title, ["parent"] = Text(w.Item, "parent"),
                ["order"] = w.Item.TryGetProperty("order", out var o) && o.TryGetInt32(out var n) ? n : 0, ["target"] = target, ["depth"] = w.Depth,
            };
        }).ToList();
        return (rows, rows.Count);
    }
}

/// <summary>
/// Who may design and who may publish (SCRUM-813): one row per role with <c>role</c>, <c>view</c>, <c>create</c>, <c>edit</c>, <c>approve</c> (yes or no, from the permission matrix through the same access check the pages use) and
/// <c>separation</c>: <c>one person can design and publish</c> when the role holds both Edit and Approve (the two-person rule, SCRUM-808, is what stops the author publishing a protected definition), <c>designs only</c>, <c>publishes only</c> or empty.
/// </summary>
public sealed class DefinitionSodGridProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "definition-sod";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>The separation note of a role from what it may do. Pure.</summary>
    public static string Separation(bool edit, bool approve) => edit && approve ? "one person can design and publish" : edit ? "designs only" : approve ? "publishes only" : "";

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IDefinitionAccess>();
        var roles = await db.Roles.AsNoTracking().Select(r => r.Name!).OrderBy(n => n).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var role in roles)
        {
            async Task<bool> Can(string action) => await access.CanOpenAsync("Definitions." + action, [role], ct);
            bool view = await Can("View"), create = await Can("Create"), edit = await Can("Edit"), approve = await Can("Approve");
            if (!view && !create && !edit && !approve) continue;
            string YesNo(bool b) => b ? "yes" : "no";
            rows.Add(new Dictionary<string, object?> { ["role"] = role, ["view"] = YesNo(view), ["create"] = YesNo(create), ["edit"] = YesNo(edit), ["approve"] = YesNo(approve), ["separation"] = Separation(edit, approve) });
        }
        return (rows, rows.Count);
    }
}
