using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Expressions;
using PEMS.Application.Grids;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// The entity layer of the definitions (SCRUM-437, SCRUM-342, the waves). A list, an option list, a create, an edit, a delete and an activate/deactivate toggle of a master-data entity are JSON only: the
// entity is registered once with the permissions and roles that guard it and the properties a definition may read and write, and the generic provider and handlers below do the rest. There are no
// per-entity handlers: what differs between entities is data (the catalog entry and the definition files), not code. A definition can only reach what the catalog registers, and the catalog's permissions
// and roles are checked by the code on every read and write (a definition cannot widen access).

/// <summary>A registered entity: its CLR type, who may read and write it, and the properties a definition may use.</summary>
public sealed record EntityRegistration(string Name, Type ClrType, string KeyProperty, string? ReadPermission, string? CreatePermission, string? EditPermission, string? DeletePermission,
    IReadOnlyCollection<string>? ReadRoles, IReadOnlyCollection<string>? WriteRoles, IReadOnlySet<string> Readable, IReadOnlySet<string> Writable, Action<object>? BeforeCreate);

/// <summary>Fluent registration of one entity: <c>.Add&lt;Department&gt;("DepartmentId", e => e.Create("Module.Create").Roles(["Admin"]).Readable("Code","Name").Writable("Code","Name"))</c>.</summary>
public sealed class EntityBuilder<T> where T : class
{
    internal string? ReadPermission, CreatePermission, EditPermission, DeletePermission;
    internal IReadOnlyCollection<string>? ReadRoles, WriteRoles;
    internal readonly List<string> ReadableNames = [], WritableNames = [];
    internal Action<T>? Hook;

    /// <summary>"Module.Action" needed to read; null = any signed-in user.</summary>
    public EntityBuilder<T> Read(string? permission) { ReadPermission = permission; return this; }
    public EntityBuilder<T> Create(string? permission) { CreatePermission = permission; return this; }
    public EntityBuilder<T> Edit(string? permission) { EditPermission = permission; return this; }
    public EntityBuilder<T> Delete(string? permission) { DeletePermission = permission; return this; }
    /// <summary>Roles that may read (all), and roles that may write (create, edit, delete); null = no role restriction beyond the permission.</summary>
    public EntityBuilder<T> Roles(string[]? read, string[]? write) { ReadRoles = read; WriteRoles = write; return this; }
    public EntityBuilder<T> Readable(params string[] names) { ReadableNames.AddRange(names); return this; }
    public EntityBuilder<T> Writable(params string[] names) { WritableNames.AddRange(names); return this; }
    /// <summary>A technical step for a row before it is saved (a normalized name the framework needs); not a business rule.</summary>
    public EntityBuilder<T> BeforeCreate(Action<T> hook) { Hook = hook; return this; }
}

public sealed class EntityCatalog
{
    private readonly Dictionary<string, EntityRegistration> _entities = new(StringComparer.Ordinal);

    public EntityCatalog Add<T>(string keyProperty, Action<EntityBuilder<T>> configure) where T : class
    {
        var b = new EntityBuilder<T>();
        configure(b);
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in b.ReadableNames.Concat(b.WritableNames).Append(keyProperty))
            if (!props.Contains(name)) throw new InvalidOperationException($"{typeof(T).Name} has no property '{name}'.");
        var readable = b.ReadableNames.Append(keyProperty).ToHashSet(StringComparer.Ordinal);
        var entityName = typeof(T).Name.Split('`')[0];
        _entities[entityName] = new EntityRegistration(entityName, typeof(T), keyProperty, b.ReadPermission, b.CreatePermission, b.EditPermission, b.DeletePermission, b.ReadRoles, b.WriteRoles,
            readable, b.WritableNames.ToHashSet(StringComparer.Ordinal), b.Hook is null ? null : o => b.Hook((T)o));
        return this;
    }

    public EntityRegistration? Get(string? name) => name is not null && _entities.TryGetValue(name, out var e) ? e : null;
    public IReadOnlyCollection<string> Names => _entities.Keys;

    /// <summary>The entities of the product that definitions may use: one registration per entity; the definition files do the rest (see tools/definitions).</summary>
    public static EntityCatalog Default() => EntityCatalogDefaults.Build();

    internal static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>The access check shared by every generic provider and handler: the catalog's permission and roles, never the definition's say-so.</summary>
internal static class EntityAccess
{
    public static async Task<bool> CanReadAsync(IServiceProvider scope, EntityRegistration e, IReadOnlyCollection<string> roles, CancellationToken ct)
        => (e.ReadRoles is null || e.ReadRoles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase))) && await scope.GetRequiredService<IDefinitionAccess>().CanOpenAsync(e.ReadPermission, roles, ct);

    public static async Task<bool> CanWriteAsync(IServiceProvider scope, EntityRegistration e, string? permission, IReadOnlyCollection<string> roles, CancellationToken ct)
        => (e.WriteRoles is null || e.WriteRoles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase))) && await scope.GetRequiredService<IDefinitionAccess>().CanOpenAsync(permission, roles, ct);
}

internal static class EntityJson
{
    public static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;

    public static IReadOnlyList<string> Names(JsonNode? n) => n is JsonArray a ? a.Select(x => Str(x) ?? "").ToList() : [];

    public static bool Supported(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsEnum || t == typeof(TimeSpan) || t == typeof(string) || t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(bool) || t == typeof(DateTime);
    }

    public sealed class NotConvertible;

    /// <summary>A form value or a default converted to the type of a property: null for empty, <see cref="NotConvertible"/> when it does not fit.</summary>
    public static object? Convert(Type type, JsonNode? json) => json is null ? null : Convert(type, json is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : json.ToJsonString());

    public static object? Convert(Type type, string? text)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string)) return string.IsNullOrWhiteSpace(text) ? (type == typeof(string) && Nullable.GetUnderlyingType(type) is null && !IsNullableReference(type) ? "" : null) : text.Trim();
        if (text is "$now" && target == typeof(DateTime)) return DateTime.Now;
        if (text is "$utcnow" && target == typeof(DateTime)) return DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(text)) return Nullable.GetUnderlyingType(type) is null && type.IsValueType ? Activator.CreateInstance(type) : null;
        if (target.IsEnum) return Enum.TryParse(target, text.Trim(), ignoreCase: true, out var e) && Enum.IsDefined(target, e!) ? e : new NotConvertible();
        if (target == typeof(TimeSpan)) return TimeSpan.TryParseExact(FormEngine.NormalizeDigits(text.Trim()), ["hh\\:mm", "hh\\:mm\\:ss"], CultureInfo.InvariantCulture, out var ts) ? ts : new NotConvertible();
        if (target == typeof(bool)) return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? true : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? false : new NotConvertible();
        if (target == typeof(DateTime)) return DateTime.TryParse(FormEngine.NormalizeDigits(text), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : new NotConvertible();
        if (!FormEngine.TryNumber(text, out var n)) return new NotConvertible();
        try { return System.Convert.ChangeType(n, target, CultureInfo.InvariantCulture); } catch (Exception ex) when (ex is OverflowException or InvalidCastException) { return new NotConvertible(); }
    }

    // string? properties are nullable reference types: an empty value is null for them, "" for a required string; the CLR cannot tell, so "required" in the definition decides and an empty string is stored as null
    private static bool IsNullableReference(Type t) => true;

    public static string Show(object? v) => v switch { null => "", DateTime d => d.ToString("s", CultureInfo.InvariantCulture), TimeSpan t => t.ToString("hh\\:mm", CultureInfo.InvariantCulture), bool b => b ? "true" : "false", IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };

    public static Expression<Func<T, bool>> KeyEquals<T>(string keyProperty, object key)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var pi = typeof(T).GetProperty(keyProperty)!;
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(Expression.Property(x, pi), Expression.Constant(System.Convert.ChangeType(key, Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType, CultureInfo.InvariantCulture), pi.PropertyType)), x);
    }

    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>Sets a property; an empty text for a property that is not nullable becomes "" (the framework would refuse null).</summary>
    public static void SetValue(PropertyInfo pi, object row, object? value)
    {
        if (value is null && pi.PropertyType == typeof(string) && Nullability.Create(pi).WriteState == NullabilityState.NotNull) value = "";
        pi.SetValue(row, value);
    }

    public static IReadOnlyDictionary<string, object?> Row(object entity, EntityRegistration e)
        => e.Readable.ToDictionary(n => n, n => entity.GetType().GetProperty(n)!.GetValue(entity), StringComparer.Ordinal);

    public static readonly string[] RuleRoots = ["row", "values", "user"];

    /// <summary>The <c>deny</c> rules of an action: <c>{ when: expression over row, values and user, message }</c>; the first that holds refuses.</summary>
    public static IReadOnlyList<DefinitionFieldError> CheckDeny(JsonNode? deny, EntityRegistration e)
    {
        var errors = new List<DefinitionFieldError>();
        if (deny is null) return errors;
        if (deny is not JsonArray rules) return [new("/deny", "wrong-type", "Def_Err_Type", "deny is a list of rules.")];
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i] is not JsonObject r) { errors.Add(new($"/deny/{i}", "wrong-type", "Def_Err_Type", "A deny rule is an object.")); continue; }
            var when = Str(r["when"]);
            if (string.IsNullOrWhiteSpace(when)) errors.Add(new($"/deny/{i}/when", "required", "Def_Err_Required", "A deny rule needs a condition."));
            else if (!ExpressionCompiler.TryCompile(when, out _, out var ex, RuleRoots)) errors.Add(new($"/deny/{i}/when", ex!.Code, "Def_Err_Expression", ex.Message));
            DataSourceDefinitionKind.CheckLabel(r["message"], $"/deny/{i}/message", errors);
        }
        return errors;
    }

    /// <summary>The first deny rule that holds for this row, with its message; null when none does.</summary>
    public static TranslatableText? Denied(JsonNode? deny, EntityRegistration e, object? entity, IReadOnlyDictionary<string, string?> values, string? userName, IReadOnlyCollection<string> roles)
    {
        if (deny is not JsonArray rules) return null;
        var ctx = new ExpressionContext(new Dictionary<string, object?>
        {
            ["row"] = entity is null ? new Dictionary<string, object?>() : Row(entity, e).ToDictionary(p => p.Key, p => (object?)(p.Value is DateTime d ? d.ToString("s", CultureInfo.InvariantCulture) : p.Value is int i ? (double)i : p.Value is long l ? (double)l : p.Value is decimal m ? (double)m : p.Value)),
            ["values"] = values.ToDictionary(p => p.Key, p => (object?)p.Value),
            ["user"] = new Dictionary<string, object?> { ["name"] = userName, ["roles"] = roles.ToList() }
        }, DateTime.Today);
        foreach (var r in rules.OfType<JsonObject>())
            if (ExpressionCompiler.Compile(Str(r["when"])!, RuleRoots).EvaluateCondition(ctx))
                return r["message"]?.Deserialize<TranslatableText>(PemsJson.Options) ?? TranslatableText.Of(("en", "This is not allowed."), ("fa", "این کار مجاز نیست."));
        return null;
    }
}

/// <summary>
/// Grid rows of a registered entity: <c>{ "kind": "grid", "provider": "entity", "parameters": { "entity": "Department", "fields": ["Code", "Name"], "search": ["Code", "Name"], "sort": ["-Name"],
/// "counts": [{ "name": "employees", "entity": "Employee", "foreignKey": "DepartmentId", "where": { "IsActive": true } }] } }</c>. <c>counts</c> adds a column with the number of related rows
/// (the related entity is registered too and read with its own rules).
/// </summary>
public sealed class EntityGridProvider(IServiceScopeFactory scopes, EntityCatalog catalog) : IGridDataProvider
{
    public string Key => "entity";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = new List<DefinitionFieldError>();
        var entity = catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is null) return [new("/entity", "unknown-entity", "Def_Err_UnknownEntity", $"parameters.entity must be one of: {string.Join(", ", catalog.Names)}.")];
        var fields = EntityJson.Names(parameters!["fields"]);
        if (fields.Count == 0) errors.Add(new("/fields", "required", "Def_Err_Required", "A grid needs the list of fields it exposes."));
        foreach (var f in fields.Concat(EntityJson.Names(parameters["search"])).Distinct())
            if (!entity.Readable.Contains(f)) errors.Add(new("/fields", "not-readable", "Def_Err_NotReadable", $"'{f}' is not a readable property of {entity.Name}."));
        foreach (var s in EntityJson.Names(parameters["search"]))
            if (entity.ClrType.GetProperty(s)?.PropertyType != typeof(string)) errors.Add(new("/search", "not-text", "Def_Err_Type", $"'{s}' is not a text property; only text properties are searched."));
        foreach (var s in EntityJson.Names(parameters["sort"]))
            if (!fields.Contains(s.TrimStart('-'))) errors.Add(new("/sort", "not-exposed", "Def_Err_NotReadable", $"The default sort '{s}' must be one of the fields."));
        if (parameters["where"] is JsonObject fixedWhere)
            foreach (var (name, v) in fixedWhere)
                if (!entity.Readable.Contains(name) || EntityJson.Convert(entity.ClrType.GetProperty(name)!.PropertyType, v) is EntityJson.NotConvertible) errors.Add(new($"/where/{name}", "not-readable", "Def_Err_NotReadable", $"'{name}' is not a readable property of {entity.Name} or the value does not fit."));
        var presetNames = new HashSet<string>(StringComparer.Ordinal);
        if (parameters["presets"] is JsonObject presets)
            foreach (var (pname, pv) in presets)
            {
                presetNames.Add(pname);
                if (pv is not JsonObject preset) { errors.Add(new($"/presets/{pname}", "wrong-type", "Def_Err_Type", "A preset is an object { left, op, right }.")); continue; }
                foreach (var side in new[] { "left", "right" })
                {
                    var prop = entity.ClrType.GetProperty(EntityJson.Str(preset[side]) ?? "");
                    var type = prop is null ? null : Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (prop is null || !entity.Readable.Contains(prop.Name) || type is null || !(type == typeof(int) || type == typeof(long) || type == typeof(decimal) || type == typeof(double) || type == typeof(DateTime)))
                        errors.Add(new($"/presets/{pname}/{side}", "not-readable", "Def_Err_NotReadable", $"preset {side} must be a readable number or date property of {entity.Name}."));
                }
                if (!(EntityJson.Str(preset["op"]) is "lt" or "lte" or "gt" or "gte" or "eq" or "ne")) errors.Add(new($"/presets/{pname}/op", "wrong-op", "Def_Err_Type", "op is lt, lte, gt, gte, eq or ne."));
            }
        foreach (var a in EntityJson.Names(parameters["apply"]))
            if (!presetNames.Contains(a)) errors.Add(new("/apply", "unknown-preset", "Def_Err_Unknown", $"'{a}' is not a preset of this data source."));
        if (parameters["counts"] is JsonArray counts)
            for (var i = 0; i < counts.Count; i++)
            {
                if (counts[i] is not JsonObject c) { errors.Add(new($"/counts/{i}", "wrong-type", "Def_Err_Type", "A count is an object.")); continue; }
                var related = catalog.Get(EntityJson.Str(c["entity"]));
                var fk = EntityJson.Str(c["foreignKey"]);
                if (string.IsNullOrWhiteSpace(EntityJson.Str(c["name"]))) errors.Add(new($"/counts/{i}/name", "required", "Def_Err_Required", "A count needs a name (the column key)."));
                if (related is null) { errors.Add(new($"/counts/{i}/entity", "unknown-entity", "Def_Err_UnknownEntity", "The related entity is not registered.")); continue; }
                if (fk is null || !related.Readable.Contains(fk)) errors.Add(new($"/counts/{i}/foreignKey", "not-readable", "Def_Err_NotReadable", $"'{fk}' is not a readable property of {related.Name}."));
                if (c["where"] is JsonObject where)
                    foreach (var (name, v) in where)
                        if (!related.Readable.Contains(name) || EntityJson.Convert(related.ClrType.GetProperty(name)!.PropertyType, v) is EntityJson.NotConvertible) errors.Add(new($"/counts/{i}/where/{name}", "not-readable", "Def_Err_NotReadable", $"'{name}' is not a readable property of {related.Name} or the value does not fit."));
            }
        if (parameters["lookups"] is JsonArray lookups)
            for (var i = 0; i < lookups.Count; i++)
            {
                if (lookups[i] is not JsonObject l) { errors.Add(new($"/lookups/{i}", "wrong-type", "Def_Err_Type", "A lookup is an object.")); continue; }
                var target = catalog.Get(EntityJson.Str(l["entity"]));
                var fk = EntityJson.Str(l["foreignKey"]);
                if (string.IsNullOrWhiteSpace(EntityJson.Str(l["name"]))) errors.Add(new($"/lookups/{i}/name", "required", "Def_Err_Required", "A lookup needs a name (the column key)."));
                if (target is null) { errors.Add(new($"/lookups/{i}/entity", "unknown-entity", "Def_Err_UnknownEntity", "The looked-up entity is not registered.")); continue; }
                if (fk is null || !entity.Readable.Contains(fk)) errors.Add(new($"/lookups/{i}/foreignKey", "not-readable", "Def_Err_NotReadable", $"'{fk}' is not a readable property of {entity.Name}."));
                if (!target.Readable.Contains(EntityJson.Str(l["label"]) ?? "")) errors.Add(new($"/lookups/{i}/label", "not-readable", "Def_Err_NotReadable", $"'{EntityJson.Str(l["label"])}' is not a readable property of {target.Name}."));
            }
        return errors;
    }

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var entity = catalog.Get(request.Parameters.GetValueOrDefault("entity")) ?? throw new GridQueryException("unknown entity");
        using var scope = scopes.CreateScope();
        if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, entity, request.Roles, ct)) throw new UnauthorizedAccessException();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        string[] Names(string key) => (JsonNode.Parse(request.Parameters.GetValueOrDefault(key) ?? "[]") as JsonArray ?? []).Select(x => EntityJson.Str(x) ?? "").ToArray();
        var fields = Names("fields");
        var grid = request.Grid.Sort.Count == 0 && Names("sort").Length > 0
            ? request.Grid with { Sort = Names("sort").Select(s => new GridSort(EntityCatalog.Camel(s.TrimStart('-')), s.StartsWith('-'))).ToList() } : request.Grid;
        var method = typeof(EntityGridProvider).GetMethod(nameof(Query), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType);
        var (items, total) = await (Task<(List<Dictionary<string, object?>>, int)>)method.Invoke(null, [db, grid, fields, Names("search").ToHashSet(StringComparer.Ordinal), entity, request.Parameters.GetValueOrDefault("where"), request.Parameters.GetValueOrDefault("presets"), Names("apply"), ct])!;

        // related counts: one grouped query per count for the rows of this page only
        if (JsonNode.Parse(request.Parameters.GetValueOrDefault("counts") ?? "[]") is JsonArray counts && counts.Count > 0 && items.Count > 0)
            foreach (var c in counts.OfType<JsonObject>())
            {
                var related = catalog.Get(EntityJson.Str(c["entity"]))!;
                if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, related, request.Roles, ct)) { foreach (var row in items) row[EntityJson.Str(c["name"])!] = null; continue; }
                var keyName = EntityCatalog.Camel(entity.KeyProperty);
                var keys = items.Select(r => r[keyName]!).ToList();
                var cm = typeof(EntityGridProvider).GetMethod(nameof(Counts), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(related.ClrType);
                var map = await (Task<Dictionary<string, int>>)cm.Invoke(null, [db, keys, EntityJson.Str(c["foreignKey"])!, c["where"] as JsonObject, ct])!;
                foreach (var row in items) row[EntityJson.Str(c["name"])!] = map.GetValueOrDefault(EntityJson.Show(row[keyName]));
            }
        // lookups: the label of a related row (a department name for a department id), one query per lookup for the ids of this page
        if (JsonNode.Parse(request.Parameters.GetValueOrDefault("lookups") ?? "[]") is JsonArray lookups && lookups.Count > 0 && items.Count > 0)
            foreach (var l in lookups.OfType<JsonObject>())
            {
                var target = catalog.Get(EntityJson.Str(l["entity"]))!;
                var column = EntityJson.Str(l["name"])!;
                if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, target, request.Roles, ct)) { foreach (var row in items) row[column] = null; continue; }
                var fkName = EntityCatalog.Camel(EntityJson.Str(l["foreignKey"])!);
                var ids = items.Select(r => r.GetValueOrDefault(fkName)).Where(v => v is not null).Distinct().ToList();
                var lm = typeof(EntityGridProvider).GetMethod(nameof(Labels), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(target.ClrType);
                var labels = await (Task<Dictionary<string, string>>)lm.Invoke(null, [db, ids!, target.KeyProperty, EntityJson.Str(l["label"])!, ct])!;
                foreach (var row in items) row[column] = row.GetValueOrDefault(fkName) is { } v ? labels.GetValueOrDefault(EntityJson.Show(v)) : null;
            }
        return (items.Cast<object>().ToList(), total);
    }

    private static async Task<Dictionary<string, string>> Labels<T>(PEMSDbContext db, List<object> ids, string keyProperty, string labelProperty, CancellationToken ct) where T : class
    {
        var x = Expression.Parameter(typeof(T), "x");
        var key = typeof(T).GetProperty(keyProperty)!;
        var keyType = Nullable.GetUnderlyingType(key.PropertyType) ?? key.PropertyType;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(keyType))!;
        foreach (var id in ids) list.Add(System.Convert.ChangeType(id, keyType, CultureInfo.InvariantCulture));
        Expression member = Expression.Property(x, key);
        if (key.PropertyType != keyType) member = Expression.Property(member, "Value");
        var predicate = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [keyType], Expression.Constant(list), member);
        var rows = await db.Set<T>().AsNoTracking().Where(Expression.Lambda<Func<T, bool>>(predicate, x)).ToListAsync(ct);
        var label = typeof(T).GetProperty(labelProperty)!;
        return rows.ToDictionary(r => EntityJson.Show(key.GetValue(r)), r => EntityJson.Show(label.GetValue(r)));
    }

    private static async Task<(List<Dictionary<string, object?>> Items, int Total)> Query<T>(PEMSDbContext db, GridRequest request, string[] fields, HashSet<string> search, EntityRegistration entity, string? whereJson, string? presetsJson, string[] apply, CancellationToken ct) where T : class
    {
        var gridFields = new GridFields<T>();
        var add = typeof(GridFields<T>).GetMethod("Add")!;
        foreach (var name in fields.Append(entity.KeyProperty).Distinct())
        {
            var pi = typeof(T).GetProperty(name)!;
            var p = Expression.Parameter(typeof(T), "x");
            add.MakeGenericMethod(pi.PropertyType).Invoke(gridFields, [EntityCatalog.Camel(name), Expression.Lambda(Expression.Property(p, pi), p), search.Contains(name)]);
        }
        gridFields.TieBreak(EntityCatalog.Camel(entity.KeyProperty));
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        // fixed filters of the data source (where) and the presets it always applies (apply): both are definition data
        var x = Expression.Parameter(typeof(T), "x");
        Expression? fixedPredicate = null;
        if (JsonNode.Parse(whereJson ?? "{}") is JsonObject where)
            foreach (var (name, v) in where)
            {
                var pi = typeof(T).GetProperty(name)!;
                var eq = Expression.Equal(Expression.Property(x, pi), Expression.Constant(EntityJson.Convert(pi.PropertyType, v), pi.PropertyType));
                fixedPredicate = fixedPredicate is null ? eq : Expression.AndAlso(fixedPredicate, eq);
            }
        if (apply.Length > 0 && JsonNode.Parse(presetsJson ?? "{}") is JsonObject presets)
            foreach (var name in apply)
            {
                var p = (JsonObject)presets[name]!;
                var left = typeof(T).GetProperty(EntityJson.Str(p["left"])!)!; var right = typeof(T).GetProperty(EntityJson.Str(p["right"])!)!;
                var common = typeof(Nullable<>).MakeGenericType(Nullable.GetUnderlyingType(left.PropertyType) ?? left.PropertyType);
                Expression l = Expression.Convert(Expression.Property(x, left), common), r = Expression.Convert(Expression.Property(x, right), common);
                Expression cmp = EntityJson.Str(p["op"]) switch { "lt" => Expression.LessThan(l, r), "lte" => Expression.LessThanOrEqual(l, r), "gt" => Expression.GreaterThan(l, r), "gte" => Expression.GreaterThanOrEqual(l, r), "eq" => Expression.Equal(l, r), _ => Expression.NotEqual(l, r) };
                // a missing value on either side never matches (SQL comparison semantics, made explicit)
                cmp = Expression.AndAlso(Expression.AndAlso(Expression.Property(l, "HasValue"), Expression.Property(r, "HasValue")), cmp);
                fixedPredicate = fixedPredicate is null ? cmp : Expression.AndAlso(fixedPredicate, cmp);
            }
        if (fixedPredicate is not null) query = query.Where(Expression.Lambda<Func<T, bool>>(fixedPredicate, x));
        var total = await query.ApplyGridFilters(request, gridFields).CountAsync(ct);
        var rows = await query.ApplyGridFilters(request, gridFields).ApplyGridSort(request, gridFields).Paged(request).ToListAsync(ct);
        var props = fields.Append(entity.KeyProperty).Distinct().Select(f => (Name: EntityCatalog.Camel(f), Prop: typeof(T).GetProperty(f)!)).ToList();
        var items = rows.Select(r =>
        {
            var d = new Dictionary<string, object?>();
            foreach (var (name, prop) in props) d[name] = prop.GetValue(r) switch { DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture), TimeSpan ts => ts.ToString("hh\\:mm", CultureInfo.InvariantCulture), Enum en => en.ToString(), var v => v };
            return d;
        }).ToList();
        return (items, total);
    }

    private static async Task<Dictionary<string, int>> Counts<T>(PEMSDbContext db, List<object> keys, string foreignKey, JsonObject? where, CancellationToken ct) where T : class
    {
        var x = Expression.Parameter(typeof(T), "x");
        var fk = typeof(T).GetProperty(foreignKey)!;
        var fkType = Nullable.GetUnderlyingType(fk.PropertyType) ?? fk.PropertyType;
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(fkType))!;
        foreach (var k in keys) list.Add(System.Convert.ChangeType(k, fkType, CultureInfo.InvariantCulture));
        Expression member = Expression.Property(x, fk);
        if (fk.PropertyType != fkType) member = Expression.Property(member, "Value");
        Expression predicate = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [fkType], Expression.Constant(list), member);
        if (fk.PropertyType != fkType) predicate = Expression.AndAlso(Expression.Property(Expression.Property(x, fk), "HasValue"), predicate);
        if (where is not null)
            foreach (var (name, v) in where)
            {
                var pi = typeof(T).GetProperty(name)!;
                predicate = Expression.AndAlso(predicate, Expression.Equal(Expression.Property(x, pi), Expression.Constant(EntityJson.Convert(pi.PropertyType, v), pi.PropertyType)));
            }
        var rows = await db.Set<T>().AsNoTracking().Where(Expression.Lambda<Func<T, bool>>(predicate, x)).Select(Expression.Lambda<Func<T, object>>(Expression.Convert(member, typeof(object)), x)).ToListAsync(ct);
        return rows.GroupBy(r => EntityJson.Show(r)).ToDictionary(g => g.Key, g => g.Count());
    }
}

/// <summary>The options of a field from a registered entity: <c>{ "kind": "provider", "provider": "entity-options", "parameters": { "entity": "MaterialCategory", "value": "MaterialCategoryId", "label": "Name", "active": "IsActive" } }</c>.</summary>
public sealed class EntityOptionsProvider(IServiceScopeFactory scopes, EntityCatalog catalog) : IDataSourceProvider
{
    public string Key => "entity-options";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var entity = catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is null) return [new("/entity", "unknown-entity", "Def_Err_UnknownEntity", $"parameters.entity must be one of: {string.Join(", ", catalog.Names)}.")];
        var errors = new List<DefinitionFieldError>();
        foreach (var k in new[] { "value", "label" })
            if (!entity.Readable.Contains(EntityJson.Str(parameters![k]) ?? "")) errors.Add(new("/" + k, "not-readable", "Def_Err_NotReadable", $"parameters.{k} must be a readable property of {entity.Name}."));
        if (parameters!["active"] is not null && entity.ClrType.GetProperty(EntityJson.Str(parameters["active"]) ?? "")?.PropertyType != typeof(bool)) errors.Add(new("/active", "not-bool", "Def_Err_Type", "parameters.active must be a bool property."));
        return errors;
    }

    public async Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct)
    {
        var entity = catalog.Get(request.Parameters.GetValueOrDefault("entity")) ?? throw new InvalidOperationException("unknown entity");
        using var scope = scopes.CreateScope();
        if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, entity, request.Roles, ct)) return [];
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var method = typeof(EntityOptionsProvider).GetMethod(nameof(Load), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType);
        return await (Task<IReadOnlyList<FieldOption>>)method.Invoke(null, [db, request.Parameters.GetValueOrDefault("value")!, request.Parameters.GetValueOrDefault("label")!, request.Parameters.GetValueOrDefault("active"), ct])!;
    }

    private static async Task<IReadOnlyList<FieldOption>> Load<T>(PEMSDbContext db, string value, string label, string? active, CancellationToken ct) where T : class
    {
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        if (!string.IsNullOrEmpty(active))
        {
            var p = Expression.Parameter(typeof(T), "x");
            query = query.Where(Expression.Lambda<Func<T, bool>>(Expression.Property(p, active), p));
        }
        var vp = typeof(T).GetProperty(value)!; var lp = typeof(T).GetProperty(label)!;
        var rows = await query.Take(2000).ToListAsync(ct);
        return rows.Select(r => new FieldOption { Value = System.Convert.ToString(vp.GetValue(r), CultureInfo.InvariantCulture) ?? "", Label = TranslatableText.Literal(System.Convert.ToString(lp.GetValue(r), CultureInfo.InvariantCulture)) })
            .OrderBy(o => o.Label.ToString(), StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Loads the current values of one row for an edit form: the form's action names the entity and maps form fields to properties; the values come back by form field name.</summary>
public sealed class EntityRecordLoader(IServiceScopeFactory scopes, EntityCatalog catalog, OperationReader? operations = null)
{
    /// <summary>Null when the row does not exist or the user may not read it.</summary>
    public async Task<IReadOnlyDictionary<string, string?>?> LoadAsync(JsonObject? actionParameters, string id, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        // an operation-backed action (operation.invoke) names its source in "load": { entity, map, defaults } (SCRUM-645); an entity action names it at the top
        var source = actionParameters?["load"] as JsonObject ?? actionParameters;
        if (source?["operation"] is JsonObject loadOp && operations is not null)
        {
            // SCRUM-782: the values of the form come from a read operation of a service (its first row); the form id is the parameter param:id
            var read = new Dictionary<string, string?> { ["model"] = EntityJson.Str(loadOp["model"]), ["action"] = EntityJson.Str(loadOp["action"]), ["args"] = (loadOp["args"] as JsonObject)?.ToJsonString() };
            var rows = await operations.ReadAsync(read, "en", roles, null, ct, new Dictionary<string, string?> { ["id"] = id });
            if (rows is null || rows.Count == 0) return null;
            return rows[0].ToDictionary(kv => kv.Key, kv => kv.Value is bool flag ? (flag ? "true" : "false") : (string?)System.Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);
        }
        var entity = catalog.Get(EntityJson.Str(source?["entity"]));
        if (entity is null || source!["map"] is not JsonObject map) return null;
        using var scope = scopes.CreateScope();
        if (!await EntityAccess.CanReadAsync(scope.ServiceProvider, entity, roles, ct)) return null;
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var method = typeof(EntityRecordLoader).GetMethod(nameof(Find), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType);
        var row = await (Task<object?>)method.Invoke(null, [db, entity.KeyProperty, id, ct])!;
        // a settings row that does not exist yet (created on first save) shows the defaults of the definition
        if (row is null && source["defaults"] is JsonObject defaults) return map.ToDictionary(m => m.Key, m => defaults[m.Key] is { } d ? EntityJson.Show(EntityJson.Convert(typeof(string), d)) : null, StringComparer.Ordinal);
        if (row is null) return null;
        return map.ToDictionary(m => m.Key, m => (string?)EntityJson.Show(entity.ClrType.GetProperty(EntityJson.Str(m.Value)!)!.GetValue(row)), StringComparer.Ordinal);
    }

    private static async Task<object?> Find<T>(PEMSDbContext db, string keyProperty, string id, CancellationToken ct) where T : class
    {
        if (!EntityJson.Supported(typeof(T).GetProperty(keyProperty)!.PropertyType) || EntityJson.Convert(typeof(T).GetProperty(keyProperty)!.PropertyType, id) is EntityJson.NotConvertible) return null;
        return await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(EntityJson.KeyEquals<T>(keyProperty, id), ct);
    }
}

/// <summary>
/// The write handlers of the entity layer: <c>entity.create</c>, <c>entity.update</c>, <c>entity.delete</c> and <c>entity.toggle</c> share one parameter vocabulary:
/// <c>entity</c>; <c>map</c> (form field to property, create and update); <c>required</c>; <c>defaults</c> (create; a literal, <c>"$now"</c> or <c>"$utcnow"</c>); <c>unique</c> (<c>{ fields, message, ignoreEmpty, symmetric, where }</c>: no other row
/// may have the same values, text compared without case, an empty value skipped when <c>ignoreEmpty</c>; <c>symmetric</c> with two fields makes (A, B) and (B, A) the same pair; <c>where</c> limits the rows compared, e.g. only active ones); <c>deny</c> (<c>{ when, message }</c>: an expression over <c>row</c> (the current row), <c>values</c> and
/// <c>user</c>; the first that holds refuses); <c>property</c> (toggle: the bool property); <c>deactivate</c> (delete: a bool property set to false instead of removing the row); <c>redirect</c> (create: a path with
/// <c>{Property}</c> placeholders of the new row); <c>validate</c> (create and update, SCRUM-774: <c>{ model, action, args }</c>, a <c>[PemsOperation]</c> that runs before the write with the form values, usually
/// the validation of the service that owns the rule; a refusal shows the service message and nothing is written). The values of the form carry <c>id</c> for update, delete and toggle.
/// </summary>
public abstract class EntityWriteAction(IServiceScopeFactory scopes, EntityCatalog catalog, string key) : IActionHandler
{
    public string Key => key;
    public IReadOnlyCollection<string>? AllowedRoles => null;
    protected IServiceScopeFactory Scopes => scopes;
    protected EntityCatalog Catalog => catalog;

    protected abstract string? PermissionOf(EntityRegistration e);
    protected virtual bool NeedsMap => true;

    protected static TranslatableText Msg(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    public virtual IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var entity = catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is null) return [new("/entity", "unknown-entity", "Def_Err_UnknownEntity", $"parameters.entity must be one of: {string.Join(", ", catalog.Names)}.")];
        var errors = new List<DefinitionFieldError>();
        if (NeedsMap)
        {
            if (parameters!["map"] is not JsonObject map || map.Count == 0) errors.Add(new("/map", "required", "Def_Err_Required", "map is required: form field to property."));
            else foreach (var (field, prop) in map)
            {
                var name = EntityJson.Str(prop) ?? "";
                var pi = entity.ClrType.GetProperty(name);
                if (!entity.Writable.Contains(name) || pi is null || !EntityJson.Supported(pi.PropertyType)) errors.Add(new("/map/" + field, "not-writable", "Def_Err_NotWritable", $"'{name}' is not a writable property of {entity.Name}."));
            }
        }
        foreach (var r in EntityJson.Names(parameters!["required"])) if (!entity.Writable.Contains(r)) errors.Add(new("/required", "not-writable", "Def_Err_NotWritable", $"'{r}' is not a writable property."));
        if (parameters["defaults"] is JsonObject defaults)
            foreach (var (name, v) in defaults)
            {
                var pi = entity.ClrType.GetProperty(name);
                if (pi is null || !EntityJson.Supported(pi.PropertyType)) errors.Add(new("/defaults/" + name, "not-writable", "Def_Err_NotWritable", $"'{name}' is not a settable property of {entity.Name}."));
                else if (EntityJson.Convert(pi.PropertyType, v) is EntityJson.NotConvertible) errors.Add(new("/defaults/" + name, "wrong-type", "Def_Err_Type", $"The default of '{name}' does not fit its type."));
            }
        if (parameters["unique"] is JsonArray unique)
            for (var i = 0; i < unique.Count; i++)
            {
                if (unique[i] is not JsonObject u) { errors.Add(new($"/unique/{i}", "wrong-type", "Def_Err_Type", "A unique rule is an object.")); continue; }
                foreach (var f in EntityJson.Names(u["fields"])) if (entity.ClrType.GetProperty(f) is null) errors.Add(new($"/unique/{i}/fields", "unknown-field", "Def_Err_UnknownField", $"'{f}' is not a property of {entity.Name}."));
                if (u["symmetric"] is not null && EntityJson.Names(u["fields"]).Count != 2) errors.Add(new($"/unique/{i}/symmetric", "wrong-fields", "Def_Err_Type", "symmetric needs exactly two fields."));
                if (u["where"] is JsonObject uw)
                    foreach (var (wn, wv) in uw)
                    {
                        var wp = entity.ClrType.GetProperty(wn);
                        if (wp is null || !EntityJson.Supported(wp.PropertyType) || EntityJson.Convert(wp.PropertyType, wv is JsonValue wj && wj.TryGetValue<string>(out var wt) ? wt : wv!.ToJsonString()) is EntityJson.NotConvertible)
                            errors.Add(new($"/unique/{i}/where/{wn}", "wrong-type", "Def_Err_Type", $"The value of '{wn}' does not fit its type."));
                    }
                DataSourceDefinitionKind.CheckLabel(u["message"], $"/unique/{i}/message", errors);
            }
        errors.AddRange(EntityJson.CheckDeny(parameters["deny"], entity));
        if (parameters["validate"] is { } validate)
        {
            if (validate is not JsonObject v || string.IsNullOrWhiteSpace(EntityJson.Str(v["model"])) || string.IsNullOrWhiteSpace(EntityJson.Str(v["action"])))
                errors.Add(new("/validate", "wrong-validate", "Def_Err_Type", "validate is an object { model, action, args }: a service operation that runs before the write."));
        }
        return errors;
    }

    /// <summary>SCRUM-774: runs the named operation with the values of the form before the row is written; null = it passed (or none is named).</summary>
    protected async Task<ActionOutcome?> ValidateWithOperationAsync(JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        if (p["validate"] is not JsonObject validate) return null;
        using var scope = ActionScope.Open(scopes);
        var operations = scope.ServiceProvider.GetService<OperationCatalog>();
        if (operations is null || operations.Get(EntityJson.Str(validate["model"]), EntityJson.Str(validate["action"])) is null) return ActionOutcome.Refused(Msg("The action is not set up.", "کنش درست تنظیم نشده است."));
        var outcome = await new OperationInvokeAction(scopes, operations).ExecuteAsync(invocation with { Parameters = validate }, ct);
        return outcome.Ok ? null : outcome;
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var p = invocation.Parameters;
        var entity = catalog.Get(EntityJson.Str(p?["entity"]));
        if (entity is null) return ActionOutcome.Refused(Msg("The action is not set up.", "کنش درست تنظیم نشده است."));
        using var scope = ActionScope.Open(scopes);
        if (!await EntityAccess.CanWriteAsync(scope.ServiceProvider, entity, PermissionOf(entity), invocation.Roles, ct)) return ActionOutcome.Refused(Msg("You may not do this.", "اجازه‌ی انجام این کار را ندارید."));
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        return await RunAsync(db, entity, p!, invocation, ct);
    }

    protected abstract Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct);

    /// <summary>Applies the mapped form values to a row; the first value that does not fit refuses on its field.</summary>
    protected static ActionOutcome? Apply(EntityRegistration entity, JsonObject p, ActionInvocation invocation, object row)
    {
        var map = (JsonObject)p["map"]!;
        foreach (var (field, prop) in map)
        {
            var pi = entity.ClrType.GetProperty(EntityJson.Str(prop)!)!;
            var value = EntityJson.Convert(pi.PropertyType, invocation.Values.GetValueOrDefault(field));
            if (value is EntityJson.NotConvertible) return ActionOutcome.Refused(field, Msg("The value is not valid.", "مقدار معتبر نیست."));
            if (EntityConstraints.Check(pi, field, invocation.Values.GetValueOrDefault(field)) is { } invalid) return invalid;   // SCRUM-373: the declared length and range, checked by the one validator
            EntityJson.SetValue(pi, row, value);
        }
        foreach (var name in EntityJson.Names(p["required"]))
        {
            var field = map.FirstOrDefault(m => EntityJson.Str(m.Value) == name).Key;
            var v = entity.ClrType.GetProperty(name)!.GetValue(row);
            // a required value type (an int key) is missing when nothing was posted, not when it is 0
            var missing = field is not null ? string.IsNullOrWhiteSpace(invocation.Values.GetValueOrDefault(field)) : v is null || v is string s && string.IsNullOrWhiteSpace(s);
            if (missing) return ActionOutcome.Refused(field ?? name, Msg("This field is required.", "این فیلد الزامی است."));
        }
        return null;
    }

    protected static async Task<ActionOutcome?> CheckUniqueAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, object row, CancellationToken ct)
    {
        if (p["unique"] is not JsonArray unique) return null;
        var map = p["map"] as JsonObject ?? [];
        foreach (var u in unique.OfType<JsonObject>())
        {
            var fields = EntityJson.Names(u["fields"]);
            if (u["ignoreEmpty"] is JsonValue ie && ie.TryGetValue<bool>(out var skip) && skip
                && fields.Any(f => entity.ClrType.GetProperty(f)!.GetValue(row) is null or "")) continue;
            var symmetric = u["symmetric"] is JsonValue sy && sy.TryGetValue<bool>(out var pair) && pair && fields.Count == 2;
            var scope = (u["where"] as JsonObject)?.Select(w => (w.Key, entity.ClrType.GetProperty(w.Key)!.PropertyType, w.Value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text : w.Value!.ToJsonString())).ToList() ?? [];
            var exists = await (Task<bool>)typeof(EntityWriteAction).GetMethod(nameof(Exists), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType).Invoke(null, [db, row, fields, entity.KeyProperty, symmetric, scope, ct])!;
            if (!exists) continue;
            var message = u["message"]?.Deserialize<TranslatableText>(PemsJson.Options) ?? Msg("This already exists.", "این مورد از قبل وجود دارد.");
            var field = map.FirstOrDefault(m => fields.Contains(EntityJson.Str(m.Value) ?? "")).Key;
            return field is null ? ActionOutcome.Refused(message) : ActionOutcome.Refused(field, message);
        }
        return null;
    }

    private static async Task<bool> Exists<T>(PEMSDbContext db, object row, IReadOnlyList<string> fields, string keyProperty, bool symmetric, List<(string Name, Type Type, string Raw)> scope, CancellationToken ct) where T : class
    {
        var x = Expression.Parameter(typeof(T), "x");
        Expression Same(string column, string valueOf)
        {
            var pi = typeof(T).GetProperty(column)!;
            var left = Expression.Property(x, pi);
            var value = typeof(T).GetProperty(valueOf)!.GetValue(row);
            return pi.PropertyType == typeof(string)
                ? Expression.Equal(Expression.Call(Expression.Coalesce(left, Expression.Constant("")), typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!), Expression.Constant((value as string ?? "").Trim().ToLowerInvariant()))
                : Expression.Equal(left, Expression.Constant(value, pi.PropertyType));
        }
        Expression? all = null;
        foreach (var name in fields)
        {
            var eq = Same(name, name);
            all = all is null ? eq : Expression.AndAlso(all, eq);
        }
        if (all is null) return false;
        // a pair is the same pair in either order: (A, B) and (B, A)
        if (symmetric) all = Expression.OrElse(all, Expression.AndAlso(Same(fields[0], fields[1]), Same(fields[1], fields[0])));
        // only the rows the rule names (for example the active ones) are compared
        foreach (var (name, type, raw) in scope)
        {
            var pi = typeof(T).GetProperty(name)!;
            all = Expression.AndAlso(all, Expression.Equal(Expression.Property(x, pi), Expression.Constant(EntityJson.Convert(type, raw), pi.PropertyType)));
        }
        // an edit must not collide with itself
        var key = typeof(T).GetProperty(keyProperty)!;
        var self = key.GetValue(row);
        var isNew = self is null || System.Convert.ToDouble(self, CultureInfo.InvariantCulture) == 0;
        if (!isNew) all = Expression.AndAlso(all, Expression.NotEqual(Expression.Property(x, key), Expression.Constant(self, key.PropertyType)));
        return await db.Set<T>().AnyAsync(Expression.Lambda<Func<T, bool>>(all, x), ct);
    }

    protected static async Task<object?> FindAsync(PEMSDbContext db, EntityRegistration entity, string? id, CancellationToken ct)
        => string.IsNullOrWhiteSpace(id) ? null : await (Task<object?>)typeof(EntityWriteAction).GetMethod(nameof(Find), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity.ClrType).Invoke(null, [db, entity.KeyProperty, id, ct])!;

    private static async Task<object?> Find<T>(PEMSDbContext db, string keyProperty, string id, CancellationToken ct) where T : class
    {
        var type = typeof(T).GetProperty(keyProperty)!.PropertyType;
        return EntityJson.Convert(type, id) is EntityJson.NotConvertible or null ? null : await db.Set<T>().FirstOrDefaultAsync(EntityJson.KeyEquals<T>(keyProperty, id), ct);
    }

    protected static ActionOutcome NotFound() => ActionOutcome.Refused(Msg("The record no longer exists.", "رکورد دیگر وجود ندارد."));
}

public sealed class EntityCreateAction(IServiceScopeFactory scopes, EntityCatalog catalog) : EntityWriteAction(scopes, catalog, "entity.create")
{
    protected override string? PermissionOf(EntityRegistration e) => e.CreatePermission;

    public override IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = base.ValidateParameters(parameters).ToList();
        if (parameters?["redirect"] is { } r && (EntityJson.Str(r) is not { } s || !s.StartsWith('/') || s.StartsWith("//"))) errors.Add(new("/redirect", "invalid-redirect", "Def_Err_Redirect", "redirect is a path of this site."));
        return errors;
    }

    protected override async Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        var row = Activator.CreateInstance(entity.ClrType)!;
        var applied = Apply(entity, p, invocation, row);
        if (applied is not null) return applied;
        if (p["defaults"] is JsonObject defaults)
            foreach (var (name, v) in defaults) { var pi = entity.ClrType.GetProperty(name)!; EntityJson.SetValue(pi, row, EntityJson.Convert(pi.PropertyType, v)); }
        var denied = EntityJson.Denied(p["deny"], entity, null, invocation.Values, invocation.UserName, invocation.Roles);
        if (denied is not null) return ActionOutcome.Refused(denied);
        var unique = await CheckUniqueAsync(db, entity, p, row, ct);
        if (unique is not null) return unique;
        var validated = await ValidateWithOperationAsync(p, invocation, ct);
        if (validated is not null) return validated;
        entity.BeforeCreate?.Invoke(row);
        db.Add(row);
        await db.SaveChangesAsync(ct);
        var redirect = EntityJson.Str(p["redirect"]);
        if (redirect is not null)
            foreach (var name in entity.Readable)
                redirect = redirect.Replace("{" + name + "}", Uri.EscapeDataString(EntityJson.Show(entity.ClrType.GetProperty(name)!.GetValue(row))), StringComparison.Ordinal);
        // SCRUM-594: what the row has now (the new key included) is returned, so a following step or the redirect of the action can use it as {Id}
        var returned = entity.Readable.ToDictionary(name => name, name => (string?)EntityJson.Show(entity.ClrType.GetProperty(name)!.GetValue(row)), StringComparer.Ordinal);
        return ActionOutcome.Done(null, redirect, returned);
    }
}

public sealed class EntityUpdateAction(IServiceScopeFactory scopes, EntityCatalog catalog) : EntityWriteAction(scopes, catalog, "entity.update")
{
    protected override string? PermissionOf(EntityRegistration e) => e.EditPermission;

    protected override async Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        var row = await FindAsync(db, entity, invocation.Values.GetValueOrDefault("id"), ct);
        if (row is null) return NotFound();
        var denied = EntityJson.Denied(p["deny"], entity, row, invocation.Values, invocation.UserName, invocation.Roles);
        if (denied is not null) return ActionOutcome.Refused(denied);
        var applied = Apply(entity, p, invocation, row);
        if (applied is not null) { db.Entry(row).State = EntityState.Unchanged; return applied; }
        var unique = await CheckUniqueAsync(db, entity, p, row, ct);
        if (unique is not null) { db.Entry(row).Reload(); return unique; }
        var validated = await ValidateWithOperationAsync(p, invocation, ct);
        if (validated is not null) { db.Entry(row).Reload(); return validated; }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ActionOutcome.Refused(Msg("Someone else changed this record at the same time; reload and try again.", "رکورد هم‌زمان توسط کاربر دیگری تغییر کرد؛ دوباره بارگذاری کنید.")); }
        return ActionOutcome.Done();
    }
}

public sealed class EntityDeleteAction(IServiceScopeFactory scopes, EntityCatalog catalog) : EntityWriteAction(scopes, catalog, "entity.delete")
{
    protected override string? PermissionOf(EntityRegistration e) => e.DeletePermission;
    protected override bool NeedsMap => false;

    public override IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = base.ValidateParameters(parameters).ToList();
        var entity = Catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is not null && parameters!["deactivate"] is { } d && (!entity.Writable.Contains(EntityJson.Str(d) ?? "") || entity.ClrType.GetProperty(EntityJson.Str(d)!)?.PropertyType != typeof(bool)))
            errors.Add(new("/deactivate", "not-writable", "Def_Err_NotWritable", "deactivate must be a writable bool property."));
        return errors;
    }

    protected override async Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        var row = await FindAsync(db, entity, invocation.Values.GetValueOrDefault("id"), ct);
        if (row is null) return NotFound();
        var denied = EntityJson.Denied(p["deny"], entity, row, invocation.Values, invocation.UserName, invocation.Roles);
        if (denied is not null) return ActionOutcome.Refused(denied);
        if (EntityJson.Str(p["deactivate"]) is { } flag) entity.ClrType.GetProperty(flag)!.SetValue(row, false);
        else db.Remove(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return ActionOutcome.Refused(Msg("This record is in use and cannot be deleted.", "این رکورد در حال استفاده است و حذف نمی‌شود.")); }
        return ActionOutcome.Done();
    }
}

public sealed class EntityToggleAction(IServiceScopeFactory scopes, EntityCatalog catalog) : EntityWriteAction(scopes, catalog, "entity.toggle")
{
    protected override string? PermissionOf(EntityRegistration e) => e.EditPermission;
    protected override bool NeedsMap => false;

    public override IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = base.ValidateParameters(parameters).ToList();
        var entity = Catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is not null && (!entity.Writable.Contains(EntityJson.Str(parameters!["property"]) ?? "") || entity.ClrType.GetProperty(EntityJson.Str(parameters["property"]) ?? "")?.PropertyType != typeof(bool)))
            errors.Add(new("/property", "not-writable", "Def_Err_NotWritable", "property must be a writable bool property."));
        return errors;
    }

    protected override async Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        var row = await FindAsync(db, entity, invocation.Values.GetValueOrDefault("id"), ct);
        if (row is null) return NotFound();
        var denied = EntityJson.Denied(p["deny"], entity, row, invocation.Values, invocation.UserName, invocation.Roles);
        if (denied is not null) return ActionOutcome.Refused(denied);
        var pi = entity.ClrType.GetProperty(EntityJson.Str(p["property"])!)!;
        pi.SetValue(row, !(bool)pi.GetValue(row)!);
        await db.SaveChangesAsync(ct);
        return ActionOutcome.Done();
    }
}
