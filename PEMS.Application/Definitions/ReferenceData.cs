using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PEMS.Application.Definitions;

// SCRUM-516: reference data (lookups, templates, numbering schemes) that the product ships. A list has an owner (platform or application) and so has every row; a shipped row is identified by a stable key.
// The update rule is pure and proven by tests: what the product ships is added and updated by us, what the customer owns or edited is never overwritten (a difference is reported as an upstream change),
// and a row the product no longer ships is retired (inactive), never deleted. The table of lists with their owner is data (one definition per list), confirmed by the owner before a list is moved.

/// <summary>The owner of a list or a row: the platform ships and may update it; the application (the customer) owns it and nothing of ours overwrites it.</summary>
public static class DataOwners
{
    public const string Platform = "platform";
    public const string Application = "application";
    public static bool IsOwner(string? owner) => owner is Platform or Application;
}

/// <summary>The <c>reference-data</c> definition: <c>{ "list": "unit-of-measure", "owner": "platform", "rows": [ { "key": "kg", "values": { "name": "Kilogram" } } ] }</c>.</summary>
public sealed class ReferenceDataKind : IDefinitionKind
{
    public const int MaxRows = 5000;
    private static readonly Regex KeyShape = new("^[A-Za-z0-9]+([._-][A-Za-z0-9]+)*$", RegexOptions.Compiled);

    public string Key => "reference-data";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "wrong-type", "Def_Err_Type", "A reference data list is an object.")];
        foreach (var p in root) if (p.Key is not ("list" or "owner" or "rows")) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a reference data list."));

        if (Str(root["list"]) is not { } list || !KeyShape.IsMatch(list) || list.Length > 80) errors.Add(new("/list", "invalid-key", "Def_Err_Key", "list is the key of the list (letters, digits and . _ -)."));
        if (!DataOwners.IsOwner(Str(root["owner"]))) errors.Add(new("/owner", "unknown-owner", "Def_Err_Type", "owner is platform or application."));
        if (root["rows"] is not JsonArray rows || rows.Count == 0) { errors.Add(new("/rows", "required", "Def_Err_Required", "A list ships at least one row.")); return errors; }
        if (rows.Count > MaxRows) errors.Add(new("/rows", "too-many", "Def_Err_Range", $"A list definition has at most {MaxRows} rows."));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var at = $"/rows/{i}";
            if (rows[i] is not JsonObject row) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "A row is an object.")); continue; }
            foreach (var p in row) if (p.Key is not ("key" or "owner" or "values")) errors.Add(new($"{at}/{p.Key}", "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a row."));
            if (Str(row["key"]) is not { } key || !KeyShape.IsMatch(key) || key.Length > 80) errors.Add(new($"{at}/key", "invalid-key", "Def_Err_Key", "A row has a stable key (letters, digits and . _ -)."));
            else if (!keys.Add(key)) errors.Add(new($"{at}/key", "duplicate", "Def_Err_Duplicate", $"The row key '{key}' is used twice."));
            if (row["owner"] is { } owner && !DataOwners.IsOwner(Str(owner))) errors.Add(new($"{at}/owner", "unknown-owner", "Def_Err_Type", "owner is platform or application."));
            if (row["values"] is not JsonObject values || values.Count == 0) errors.Add(new($"{at}/values", "required", "Def_Err_Required", "A row has values."));
            else foreach (var v in values) if (v.Value is not null && !(v.Value is JsonValue)) errors.Add(new($"{at}/values/{v.Key}", "wrong-type", "Def_Err_Type", "A value is text, a number, true or false."));
        }
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson) => [];

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>The rows of a valid list definition, each with the list's owner unless it names its own. Values are written as text.</summary>
    public static IReadOnlyList<ShippedRow> Rows(string payloadJson)
    {
        var root = (JsonObject)JsonNode.Parse(payloadJson)!;
        var listOwner = Str(root["owner"]) ?? DataOwners.Platform;
        var result = new List<ShippedRow>();
        foreach (var row in ((JsonArray)root["rows"]!).OfType<JsonObject>())
        {
            var values = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (var v in (JsonObject)row["values"]!) values[v.Key] = v.Value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text : v.Value?.ToJsonString();
            result.Add(new ShippedRow(Str(row["key"])!, Str(row["owner"]) ?? listOwner, values));
        }
        return result;
    }
}

public sealed record ShippedRow(string Key, string Owner, IReadOnlyDictionary<string, string?> Values);

/// <summary>A row as stored: <see cref="ShippedHash"/> is the fingerprint of the values the product last wrote (null for a row that was not written by the product); <see cref="IsShipped"/> is false for a row the customer created.</summary>
public sealed record StoredRow(string Key, string Owner, IReadOnlyDictionary<string, string?> Values, string? ShippedHash, bool IsActive, bool IsShipped);

public enum RowAction
{
    Insert,
    /// <summary>The product's row changed and the stored row is still the product's: update it (and its fingerprint).</summary>
    Update,
    Unchanged,
    /// <summary>The row is the customer's (application-owned or edited): left as it is, the difference is reported.</summary>
    UpstreamChanged,
    /// <summary>The product no longer ships the row: made inactive, never deleted.</summary>
    Retire,
}

public sealed record RowPlan(string Key, RowAction Action, IReadOnlyList<string> ChangedFields);

public static class ReferenceDataSync
{
    /// <summary>The fingerprint of a row's values (key order does not matter).</summary>
    public static string Fingerprint(IReadOnlyDictionary<string, string?> values)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values.OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value))))) [..16].ToLowerInvariant();

    public static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string?> shipped, IReadOnlyDictionary<string, string?> stored)
        => shipped.Keys.Union(stored.Keys).Where(k => !string.Equals(shipped.GetValueOrDefault(k), stored.GetValueOrDefault(k), StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The plan of one list. Nothing the customer owns or edited is ever written: an application-owned row, a row whose values differ from what the product last wrote, and a row with no record of what the product wrote are
    /// only reported when they differ. A row not shipped any more is retired when it was shipped and is still active; the customer's own rows are never touched.
    /// </summary>
    public static IReadOnlyList<RowPlan> Plan(IEnumerable<ShippedRow> shipped, IEnumerable<StoredRow> stored)
    {
        var current = stored.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var plan = new List<RowPlan>();
        var shippedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in shipped)
        {
            shippedKeys.Add(s.Key);
            if (!current.TryGetValue(s.Key, out var r)) { plan.Add(new RowPlan(s.Key, RowAction.Insert, s.Values.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())); continue; }

            var differences = Differences(s.Values, r.Values);
            if (differences.Count == 0) { plan.Add(new RowPlan(s.Key, RowAction.Unchanged, [])); continue; }

            var customerOwns = r.Owner == DataOwners.Application || !r.IsShipped;
            var edited = r.ShippedHash is null || Fingerprint(r.Values) != r.ShippedHash;
            plan.Add(new RowPlan(s.Key, customerOwns || edited ? RowAction.UpstreamChanged : RowAction.Update, differences));
        }

        foreach (var r in current.Values.Where(r => !shippedKeys.Contains(r.Key) && r.IsShipped && r.IsActive && r.Owner == DataOwners.Platform))
            plan.Add(new RowPlan(r.Key, RowAction.Retire, []));
        return plan;
    }
}
