using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-775: ONE generic way to READ through a service, as operation.invoke is the one way to WRITE. A service method marked [PemsOperation(model, action)] that returns a list, a text list or one object is a data
// source for a definition: grid rows ("operation" grid provider) or choices ("operation-options" provider). Parameters: model, action, args (parameter name to literal:text or context:userName|roles|employeeId|culture),
// path (a property path to a list inside the answer, for example "PendingMigrations"), and for options value and label (property names; a list of text needs neither). No provider and no controller per page.

/// <summary>Calls a read operation for a data source: checks its permission and roles, builds the arguments from the context and the literals, and gives back the answer as plain rows.</summary>
public sealed class OperationReader(IServiceScopeFactory scopes, OperationCatalog catalog)
{
    public IReadOnlyList<DefinitionFieldError> Validate(JsonObject? parameters)
    {
        var model = EntityJson.Str(parameters?["model"]);
        if (catalog.Get(model, EntityJson.Str(parameters?["action"])) is not { } op)
            return [new("/model", "unknown-operation", "Def_Err_UnknownOperation", $"parameters.model and parameters.action must name a registered operation (models: {string.Join(", ", catalog.Models)}).")];
        var errors = new List<DefinitionFieldError>();
        var args = parameters!["args"] as JsonObject ?? [];
        var names = op.Method.GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, source) in args)
            if (!names.Contains(name)) errors.Add(new("/args/" + name, "unknown-parameter", "Def_Err_UnknownField", $"'{name}' is not a parameter of {op.Name}."));
            else if (!IsSource(EntityJson.Str(source) ?? "")) errors.Add(new("/args/" + name, "wrong-source", "Def_Err_Type", "A source of a read operation is literal:text, param:name (a value typed in a column filter of the grid) or context:employeeId|roles|userName|culture."));
        foreach (var p in op.Method.GetParameters())
            if (p.ParameterType != typeof(CancellationToken) && !Simple(p.ParameterType) && !(IsRoles(p) && EntityJson.Str(args[p.Name!]) == "context:roles"))
                errors.Add(new("/operation", "unsupported-parameter", "Def_Err_Type", $"The parameter '{p.Name}' of {op.Name} must be a simple value (give it a literal or a context source) or the roles of the user (context:roles)."));
        return errors;
    }

    private static bool Simple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(bool) || t == typeof(DateTime) || t.IsEnum;
    }

    private static bool IsRoles(ParameterInfo p) => p.Name!.EndsWith("Roles", StringComparison.OrdinalIgnoreCase) && p.ParameterType != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(p.ParameterType);
    private static bool IsSource(string s) => s.StartsWith("literal:") || s.StartsWith("param:") && s.Length > 6 || s.StartsWith("context:") && s[8..] is "employeeId" or "roles" or "userName" or "culture";

    /// <summary>What a call of a read operation answered: Allowed false when the user may not run it, NoInput when a required argument was not given (the service is not called), else the raw answer.</summary>
    public sealed record Invoked(bool Allowed, bool NoInput, object? Answer);

    /// <summary>Calls the operation (permission, roles, arguments from the context, the literals and the query) and gives back the raw answer: the one place a read answer is produced for rows, choices and files.</summary>
    public async Task<Invoked> InvokeAsync(IReadOnlyDictionary<string, string?> parameters, string culture, IReadOnlyCollection<string> roles, string? userName, CancellationToken ct, IReadOnlyDictionary<string, string?>? query = null)
    {
        var op = catalog.Get(parameters.GetValueOrDefault("model"), parameters.GetValueOrDefault("action")) ?? throw new InvalidOperationException("unknown operation");
        using var scope = scopes.CreateScope();
        if (!await scope.ServiceProvider.GetRequiredService<IDefinitionAccess>().CanOpenAsync(op.Permission, roles, ct)
            || op.Roles.Count > 0 && !roles.Any(r => op.Roles.Contains(r, StringComparer.OrdinalIgnoreCase))) return new(false, false, null);

        var args = parameters.GetValueOrDefault("args") is { } json ? JsonNode.Parse(json) as JsonObject ?? [] : [];
        var values = new List<object?>();
        foreach (var p in op.Method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) { values.Add(ct); continue; }
            var source = EntityJson.Str(args[p.Name!]);
            if (IsRoles(p)) { values.Add(roles.ToList()); continue; }
            if (source is null) { values.Add(p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null ? Activator.CreateInstance(p.ParameterType) : null); continue; }
            if (source.StartsWith("param:") && !p.IsOptional && string.IsNullOrWhiteSpace(query?.GetValueOrDefault(source[6..]))) return new(true, true, null);      // nothing typed yet: no rows, the service is not called
            var text = source.StartsWith("literal:") ? source[8..] : source.StartsWith("param:") ? query?.GetValueOrDefault(source[6..]) : source[8..] switch { "userName" => userName, "culture" => culture, "roles" => string.Join(",", roles), _ => null };
            values.Add(EntityJson.Convert(p.ParameterType, text) is var c && c is EntityJson.NotConvertible ? throw new InvalidOperationException($"'{p.Name}' is not valid.") : c);
        }
        var service = scope.ServiceProvider.GetRequiredService(op.ServiceType);
        var task = op.Method.Invoke(service, values.ToArray())!;
        object? answer = task;
        if (task is Task t)
        {
            await t;
            answer = t.GetType().IsGenericType ? t.GetType().GetProperty("Result")!.GetValue(t) : null;
        }
        return new(true, false, answer);
    }

    /// <summary>The rows of the answer: one dictionary per item (properties by name, first letter lower-case); a text list becomes rows with a "value" column. Null when the user may not run the operation.</summary>
    public async Task<IReadOnlyList<IDictionary<string, object?>>?> ReadAsync(IReadOnlyDictionary<string, string?> parameters, string culture, IReadOnlyCollection<string> roles, string? userName, CancellationToken ct, IReadOnlyDictionary<string, string?>? query = null)
    {
        var invoked = await InvokeAsync(parameters, culture, roles, userName, ct, query);
        if (!invoked.Allowed) return null;
        if (invoked.NoInput) return [];
        var answer = invoked.Answer;
        foreach (var step in (parameters.GetValueOrDefault("path") ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
            answer = answer?.GetType().GetProperty(step)?.GetValue(answer);
        if (answer is null) return [];
        IEnumerable<object?> items = answer is string ? [answer] : answer is IEnumerable e ? e.Cast<object?>() : [answer];
        return items.Select(Row).ToList();
    }

    private static IDictionary<string, object?> Row(object? item)
    {
        if (item is null or string || item.GetType().IsPrimitive || item is decimal) return new Dictionary<string, object?> { ["value"] = item };
        return item.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && Show(p.PropertyType))
            .ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.GetValue(item), StringComparer.Ordinal);
    }

    private static bool Show(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t.IsPrimitive || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t.IsEnum;
    }
}

/// <summary>Grid rows from a read operation (parameters: model, action, args, path). The standard grid query (quick search over the text columns, equality filters, sort, paging) is applied to the rows.</summary>
public sealed class OperationGridProvider(OperationReader reader) : IGridDataProvider
{
    public string Key => "operation";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters) => reader.Validate(parameters);

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var typed = request.Grid.Filters.GroupBy(f => f.Field).ToDictionary(g => g.Key, g => (string?)g.First().Value);      // the column filters are the parameters of the operation
        // batch 5: the text typed in the quick search of the grid is the argument "param:search" (a semantic or full-text search of the service); the service has already searched, so the rows are not filtered again by their text
        var searchGoesToOperation = request.Parameters.GetValueOrDefault("args") is { } argsJson && argsJson.Contains("\"param:search\"", StringComparison.Ordinal);
        if (searchGoesToOperation && !string.IsNullOrWhiteSpace(request.Grid.Search)) typed["search"] = request.Grid.Search.Trim();
        var rows = await reader.ReadAsync(request.Parameters, request.Culture, request.Roles, request.UserName, ct, typed) ?? throw new UnauthorizedAccessException();
        IEnumerable<IDictionary<string, object?>> query = rows;
        if (!searchGoesToOperation && !string.IsNullOrWhiteSpace(request.Grid.Search))
        {
            var q = request.Grid.Search.Trim();
            query = query.Where(r => r.Values.Any(v => v is string s && s.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        var argumentFilters = ArgumentParameters(request.Parameters.GetValueOrDefault("args"));   // filters the service already took as arguments (a page passes them in the url of the grid)
        foreach (var f in request.Grid.Filters.Where(f => !argumentFilters.Contains(f.Field)))
        {
            var field = f.Field; var value = f.Value;
            query = f.Op == "eq" ? query.Where(r => string.Equals(Text(r, field), value, StringComparison.OrdinalIgnoreCase)) : query.Where(r => (Text(r, field) ?? "").Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        // batch 5: fixed equality filters of the definition (<c>"where": { "key": "ByDiscipline" }</c>): one operation answers several charts, each source keeps the rows of one stable key
        if (request.Parameters.GetValueOrDefault("where") is { } whereJson && JsonNode.Parse(whereJson) is JsonObject where)
            foreach (var (field, expected) in where) { var f2 = field; var v2 = EntityJson.Str(expected); query = query.Where(r => string.Equals(Text(r, f2), v2, StringComparison.OrdinalIgnoreCase)); }
        var list = query.ToList();
        if (request.Grid.Sort.FirstOrDefault(s => list.Count > 0 && list[0].ContainsKey(s.Field)) is { } sort)
            list = (sort.Descending ? list.OrderByDescending(r => r[sort.Field], Comparer<object?>.Create(Compare)) : list.OrderBy(r => r[sort.Field], Comparer<object?>.Create(Compare))).ToList();
        var page = Math.Max(1, request.Grid.Page);
        return (list.Skip((page - 1) * request.Grid.PageSize).Take(request.Grid.PageSize).Cast<object>().ToList(), list.Count);
    }

    /// <summary>The names after <c>param:</c> in the arguments of the operation (the filters and the search the service handles itself).</summary>
    private static HashSet<string> ArgumentParameters(string? argsJson)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (argsJson is null || JsonNode.Parse(argsJson) is not JsonObject args) return names;
        foreach (var (_, source) in args) if (EntityJson.Str(source) is { } text && text.StartsWith("param:", StringComparison.Ordinal)) names.Add(text[6..]);
        return names;
    }

    private static string? Text(IDictionary<string, object?> row, string field) => row.TryGetValue(field, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    private static int Compare(object? a, object? b)
        => a is IComparable ca && b is not null && a.GetType() == b.GetType() ? ca.CompareTo(b) : string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Choices from a read operation (parameters: model, action, args, path, and value and label unless the answer is a list of text).</summary>
public sealed class OperationOptionsProvider(OperationReader reader) : IDataSourceProvider
{
    public string Key => "operation-options";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters) => reader.Validate(parameters);

    public async Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct)
    {
        var rows = await reader.ReadAsync(request.Parameters, request.Culture, request.Roles, request.UserName, ct) ?? [];
        var value = request.Parameters.GetValueOrDefault("value") ?? "value";
        var label = request.Parameters.GetValueOrDefault("label") ?? value;
        string Show(IDictionary<string, object?> r, string name) => r.TryGetValue(name, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
        return rows.Select(r => new FieldOption { Value = Show(r, value), Label = TranslatableText.Literal(Show(r, label)) }).ToList();
    }
}

/// <summary>
/// SCRUM-6b: the file source of a definition (a data source of kind provider with the provider <c>operation-file</c>; parameters: model, action, args with param:x from the query, disposition inline or attachment). It names a read
/// operation whose answer is a file (an object with Bytes, FileName and ContentType: a label image, a PDF, a workbook) or any object (answered as JSON, for the scripts of a page); it is served by <c>GET /x/{key}</c>
/// (FileHostController). The operation keeps its permission and roles; the data source may name a permission too. No controller per file.
/// </summary>
public sealed class OperationFileProvider(OperationReader reader) : IDataSourceProvider
{
    public string Key => "operation-file";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
        => reader.Validate(parameters).Concat(EntityJson.Str(parameters?["disposition"]) is null or "inline" or "attachment" ? [] : [new DefinitionFieldError("/disposition", "wrong-value", "Def_Err_Type", "disposition is inline or attachment.")]).ToList();

    /// <summary>A file source has no choices; it is read through <c>GET /x/{key}</c>.</summary>
    public Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct) => Task.FromResult<IReadOnlyList<FieldOption>>([]);
}
