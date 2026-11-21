using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Persistence.Context;

namespace PEMS.Web.Definitions;

/// <summary>One aggregate column: its name in the row, the function (count, sum, avg, min, max) and the index of the value it reads (null for count).</summary>
public sealed record AggregateMeasure(string Name, string Function, int? ValueIndex);

/// <summary>
/// SCRUM-592: aggregates of a registered entity as a data source definition: <c>{ "kind": "grid", "provider": "entity-aggregate", "parameters": { "entity": "WorkOrder", "groupBy": "Status",
/// "measures": [ { "name": "orders", "fn": "count" }, { "name": "hours", "fn": "sum", "property": "EstimatedHours" } ], "where": { "IsActive": true }, "maxRows": 50000 } }</c>. Same rights as the entity grid
/// (the entity's read permission and roles), same paging and sorting contract; the rows are read as the group column and the value columns only, at most <c>maxRows</c> of them, and grouped here.
/// </summary>
public sealed class EntityAggregateProvider(IServiceScopeFactory scopes, EntityCatalog catalog) : IGridDataProvider
{
    public const int DefaultMaxRows = 50000;
    public const int AbsoluteMaxRows = 200000;
    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal) { "count", "sum", "avg", "min", "max" };
    private static readonly HashSet<Type> Numbers = [typeof(int), typeof(long), typeof(short), typeof(byte), typeof(decimal), typeof(double), typeof(float)];

    public string Key => "entity-aggregate";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var entity = catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is null) return [new("/entity", "unknown-entity", "Def_Err_UnknownEntity", $"parameters.entity must be one of: {string.Join(", ", catalog.Names)}.")];
        var errors = new List<DefinitionFieldError>();
        if (EntityJson.Str(parameters!["groupBy"]) is not { } groupBy || !entity.Readable.Contains(groupBy)) errors.Add(new("/groupBy", "not-readable", "Def_Err_NotReadable", $"groupBy must be a readable property of {entity.Name}."));
        if (parameters["maxRows"] is { } mr && !(mr is JsonValue mv && mv.TryGetValue<int>(out var rows) && rows is >= 1 and <= AbsoluteMaxRows)) errors.Add(new("/maxRows", "range", "Def_Err_Range", $"maxRows is a whole number from 1 to {AbsoluteMaxRows}."));
        if (parameters["measures"] is not JsonArray measures || measures.Count is 0 or > 10) errors.Add(new("/measures", "required", "Def_Err_Required", "measures is a list of 1 to 10 aggregates."));
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < measures.Count; i++)
            {
                if (measures[i] is not JsonObject m) { errors.Add(new($"/measures/{i}", "wrong-type", "Def_Err_Type", "A measure is an object.")); continue; }
                var name = EntityJson.Str(m["name"]);
                var fn = EntityJson.Str(m["fn"]);
                if (string.IsNullOrWhiteSpace(name)) errors.Add(new($"/measures/{i}/name", "required", "Def_Err_Required", "A measure needs a name (the column key)."));
                else if (!names.Add(name) || name == EntityCatalog.Camel(EntityJson.Str(parameters["groupBy"]) ?? "")) errors.Add(new($"/measures/{i}/name", "duplicate", "Def_Err_Duplicate", $"The column name '{name}' is used twice."));
                if (fn is null || !Functions.Contains(fn)) { errors.Add(new($"/measures/{i}/fn", "unknown-function", "Def_Err_Type", "fn is count, sum, avg, min or max.")); continue; }
                if (fn == "count") continue;
                var prop = entity.ClrType.GetProperty(EntityJson.Str(m["property"]) ?? "");
                var type = prop is null ? null : Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (prop is null || !entity.Readable.Contains(prop.Name) || type is null || !Numbers.Contains(type))
                    errors.Add(new($"/measures/{i}/property", "not-numeric", "Def_Err_NotReadable", $"{fn} reads a readable number property of {entity.Name}."));
            }
        }
        if (parameters["where"] is JsonObject where)
            foreach (var (name, v) in where)
                if (!entity.Readable.Contains(name) || EntityJson.Convert(entity.ClrType.GetProperty(name)!.PropertyType, v) is EntityJson.NotConvertible)
                    errors.Add(new($"/where/{name}", "not-readable", "Def_Err_NotReadable", $"'{name}' must be a readable property with a fitting value."));
        return errors;
    }

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var entity = catalog.Get(request.Parameters.GetValueOrDefault("entity")) ?? throw new GridQueryException("unknown entity");
        using var scope = ActionScope.Open(scopes);
        if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, entity, request.Roles, ct)) throw new UnauthorizedAccessException();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();

        var groupBy = request.Parameters.GetValueOrDefault("groupBy") ?? throw new GridQueryException("groupBy is missing");
        var measureNodes = (JsonNode.Parse(request.Parameters.GetValueOrDefault("measures") ?? "[]") as JsonArray ?? []).OfType<JsonObject>().ToList();
        var valueProperties = measureNodes.Where(m => EntityJson.Str(m["fn"]) != "count").Select(m => EntityJson.Str(m["property"])!).Distinct(StringComparer.Ordinal).ToList();
        var measures = measureNodes.Select(m => new AggregateMeasure(EntityJson.Str(m["name"])!, EntityJson.Str(m["fn"])!,
            EntityJson.Str(m["fn"]) == "count" ? null : valueProperties.IndexOf(EntityJson.Str(m["property"])!) + 1)).ToList();
        var where = (JsonNode.Parse(request.Parameters.GetValueOrDefault("where") ?? "{}") as JsonObject)?.ToDictionary(w => w.Key, w => w.Value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text : w.Value?.ToJsonString(), StringComparer.Ordinal) ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        var maxRows = int.TryParse(request.Parameters.GetValueOrDefault("maxRows"), CultureInfo.InvariantCulture, out var configured) ? Math.Clamp(configured, 1, AbsoluteMaxRows) : DefaultMaxRows;

        var load = typeof(EntityAggregateProvider).GetMethod(nameof(Load), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType);
        var rows = await (Task<List<object?[]>>)load.Invoke(null, [db, groupBy, valueProperties, where, maxRows, ct])!;
        var grouped = Aggregate(rows, EntityCatalog.Camel(groupBy), measures);
        return Shape(grouped, request.Grid);
    }

    /// <summary>Reads the group column and the value columns of the rows that pass the fixed filters, at most <paramref name="maxRows"/> of them.</summary>
    private static async Task<List<object?[]>> Load<T>(PEMSDbContext db, string groupBy, IReadOnlyList<string> valueProperties, IReadOnlyDictionary<string, string?> where, int maxRows, CancellationToken ct) where T : class
    {
        var x = Expression.Parameter(typeof(T), "x");
        Expression? predicate = null;
        foreach (var (name, raw) in where)
        {
            var property = typeof(T).GetProperty(name)!;
            var value = EntityJson.Convert(property.PropertyType, raw);
            var equal = Expression.Equal(Expression.Property(x, property), Expression.Constant(value, property.PropertyType));
            predicate = predicate is null ? equal : Expression.AndAlso(predicate, equal);
        }
        var columns = new List<Expression> { Expression.Convert(Expression.Property(x, groupBy), typeof(object)) };
        columns.AddRange(valueProperties.Select(v => (Expression)Expression.Convert(Expression.Property(x, v), typeof(object))));
        var selector = Expression.Lambda<Func<T, object?[]>>(Expression.NewArrayInit(typeof(object), columns), x);
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        if (predicate is not null) query = query.Where(Expression.Lambda<Func<T, bool>>(predicate, x));
        return await query.Take(maxRows).Select(selector).ToListAsync(ct);
    }

    private static decimal? Number(object? value) => value is null ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    /// <summary>Groups the rows by their first column and computes the measures; the group column keeps the key under <paramref name="groupName"/>. A group without a key (null) is one group with an empty key.</summary>
    public static List<Dictionary<string, object?>> Aggregate(IEnumerable<object?[]> rows, string groupName, IReadOnlyList<AggregateMeasure> measures)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var group in rows.GroupBy(r => r[0]?.ToString() ?? "", StringComparer.Ordinal))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal) { [groupName] = group.First()[0] is { } key ? key : "" };
            foreach (var m in measures)
            {
                if (m.Function == "count") { row[m.Name] = group.Count(); continue; }
                var values = group.Select(r => Number(r[m.ValueIndex!.Value])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                decimal? value = null;
                if (values.Count > 0)
                    value = m.Function switch { "sum" => values.Sum(), "avg" => Math.Round(values.Average(), 6), "min" => values.Min(), "max" => values.Max(), _ => (decimal?)null };
                row[m.Name] = value;
            }
            result.Add(row);
        }
        return result;
    }

    /// <summary>Sorting (by the group or a measure column; the group ascending when nothing is asked) and the page of the standard grid request; the total is the number of groups.</summary>
    public static (IReadOnlyList<object> Items, int Total) Shape(List<Dictionary<string, object?>> rows, GridRequest grid)
    {
        IEnumerable<Dictionary<string, object?>> ordered = rows;
        IOrderedEnumerable<Dictionary<string, object?>>? sorted = null;
        foreach (var sort in grid.Sort)
        {
            object? Key(Dictionary<string, object?> r) => r.TryGetValue(sort.Field, out var v) ? v : null;
            sorted = sorted is null
                ? (sort.Descending ? rows.OrderByDescending(Key, ValueComparer.Instance) : rows.OrderBy(Key, ValueComparer.Instance))
                : (sort.Descending ? sorted.ThenByDescending(Key, ValueComparer.Instance) : sorted.ThenBy(Key, ValueComparer.Instance));
        }
        if (sorted is not null) ordered = sorted;
        else if (rows.Count > 0) { var first = rows[0].Keys.First(); ordered = rows.OrderBy(r => r[first], ValueComparer.Instance); }
        var page = ordered.Skip((grid.Page - 1) * grid.PageSize).Take(grid.PageSize).Cast<object>().ToList();
        return (page, rows.Count);
    }

    /// <summary>Numbers compare as numbers, everything else as text; null sorts first.</summary>
    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();
        public int Compare(object? a, object? b)
        {
            if (a is null || b is null) return a is null ? (b is null ? 0 : -1) : 1;
            if (a is IConvertible && b is IConvertible && decimal.TryParse(Convert.ToString(a, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
                && decimal.TryParse(Convert.ToString(b, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var y)) return x.CompareTo(y);
            return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
