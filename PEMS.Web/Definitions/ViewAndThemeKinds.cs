using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

/// <summary>Shared checks of the small kinds (SCRUM-451): a text is an object with both languages, a name is a short plain identifier.</summary>
internal static class KindChecks
{
    public static bool IsText(JsonNode? node)
        => node is JsonObject o && o.Count > 0 && o.All(p => p.Key is "en" or "fa" && p.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)v))
           && o.ContainsKey("en") && o.ContainsKey("fa");

    public static void UnknownProperties(JsonObject node, string at, HashSet<string> allowed, List<DefinitionFieldError> errors)
    {
        foreach (var p in node) if (!allowed.Contains(p.Key)) errors.Add(new(at + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"\"{p.Key}\" is not a property here."));
    }

    public static string? Str(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;
}

/// <summary>
/// A list view as data (SCRUM-451): the <c>ViewDescriptor</c> shape without the id (the key of the definition is the id).
/// <code>{ "columns": [ { "key": "name", "header": { "en": "Name", "fa": "نام" }, "align": "start", "sortable": true, "kind": "text" } ], "emptyText": { "en": "No rows", "fa": "ردیفی نیست" } }</code>
/// <c>kind</c> is auto, text, number, date or datetime; <c>trueText</c> and <c>falseText</c> are the texts of a yes/no column.
/// </summary>
public sealed class ViewDefinitionKind : IDefinitionKind
{
    private const int MaxColumns = 60;
    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal) { "columns", "emptyText" };
    private static readonly HashSet<string> ColumnProperties = new(StringComparer.Ordinal) { "key", "header", "align", "sortable", "kind", "trueText", "falseText" };
    private static readonly HashSet<string> Alignments = new(StringComparer.Ordinal) { "start", "center", "end" };
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "auto", "text", "number", "date", "datetime" };
    private static readonly Regex ColumnKey = new("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.Compiled);

    public string Key => "view";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "invalid-view", "Def_Err_View", "A view is an object.")];
        KindChecks.UnknownProperties(root, "", Properties, errors);
        if (root["emptyText"] is { } empty && !KindChecks.IsText(empty)) errors.Add(new("/emptyText", "missing-translation", "Def_Err_Translation", "A text needs both fa and en."));
        if (root["columns"] is not JsonArray columns || columns.Count == 0) { errors.Add(new("/columns", "required", "Def_Err_Required", "A view needs at least one column.")); return errors; }
        if (columns.Count > MaxColumns) errors.Add(new("/columns", "too-many", "Def_Err_Limit", $"A view has at most {MaxColumns} columns."));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            var at = $"/columns/{i}";
            if (columns[i] is not JsonObject column) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "A column is an object.")); continue; }
            KindChecks.UnknownProperties(column, at, ColumnProperties, errors);
            var key = KindChecks.Str(column["key"]);
            if (key is null || !ColumnKey.IsMatch(key)) errors.Add(new(at + "/key", "invalid-key", "Def_Err_Key", "A column key is the name of a property of the rows (letters, digits, underscore)."));
            else if (!keys.Add(key)) errors.Add(new(at + "/key", "duplicate", "Def_Err_Duplicate", $"The column \"{key}\" is used twice."));
            if (!KindChecks.IsText(column["header"])) errors.Add(new(at + "/header", column["header"] is null ? "required" : "missing-translation", column["header"] is null ? "Def_Err_Required" : "Def_Err_Translation", "A header needs both fa and en."));
            if (column["align"] is { } align && !(KindChecks.Str(align) is { } a && Alignments.Contains(a))) errors.Add(new(at + "/align", "invalid-value", "Def_Err_Enum", "align is start, center or end."));
            if (column["kind"] is { } kind && !(KindChecks.Str(kind) is { } k && Kinds.Contains(k))) errors.Add(new(at + "/kind", "invalid-value", "Def_Err_Enum", "kind is auto, text, number, date or datetime."));
            if (column["sortable"] is { } sortable && !(sortable is JsonValue sv && sv.GetValueKind() is JsonValueKind.True or JsonValueKind.False)) errors.Add(new(at + "/sortable", "wrong-type", "Def_Err_Type", "sortable is true or false."));
            foreach (var text in new[] { "trueText", "falseText" })
                if (column[text] is { } t && !KindChecks.IsText(t)) errors.Add(new(at + "/" + text, "missing-translation", "Def_Err_Translation", "A text needs both fa and en."));
        }
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson) => [];
}

/// <summary>
/// A theme as data (SCRUM-451): design tokens (CSS custom properties) of the light or the dark scheme. Only <c>--pems-*</c> names and plain values (a colour, a length, a number, a <c>var(--pems-*)</c>)
/// are accepted, so a theme cannot carry script, an address or a rule. <code>{ "mode": "dark", "tokens": { "--pems-accent": "#d98c2b", "--pems-radius": "6px" } }</code>
/// </summary>
public sealed class ThemeDefinitionKind : IDefinitionKind
{
    private const int MaxTokens = 200;
    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal) { "mode", "tokens" };
    private static readonly Regex TokenName = new("^--pems-[a-z0-9]+(-[a-z0-9]+){0,5}$", RegexOptions.Compiled);
    private static readonly Regex Hex = new("^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    private static readonly Regex Functional = new(@"^(rgb|rgba|hsl|hsla)\(\s*[0-9.%\s,/-]{1,40}\)$", RegexOptions.Compiled);
    private static readonly Regex Variable = new(@"^var\(--pems-[a-z0-9-]{1,40}\)$", RegexOptions.Compiled);
    private static readonly Regex Lengths = new(@"^-?[0-9]{1,4}(\.[0-9]{1,3})?(px|rem|em|%|ms|s)?( -?[0-9]{1,4}(\.[0-9]{1,3})?(px|rem|em|%|ms|s)?){0,3}$", RegexOptions.Compiled);

    public string Key => "theme";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "invalid-theme", "Def_Err_Theme", "A theme is an object.")];
        KindChecks.UnknownProperties(root, "", Properties, errors);
        if (!(KindChecks.Str(root["mode"]) is "light" or "dark")) errors.Add(new("/mode", "invalid-value", "Def_Err_Enum", "mode is light or dark."));
        if (root["tokens"] is not JsonObject tokens || tokens.Count == 0) { errors.Add(new("/tokens", "required", "Def_Err_Required", "A theme needs at least one token.")); return errors; }
        if (tokens.Count > MaxTokens) errors.Add(new("/tokens", "too-many", "Def_Err_Limit", $"A theme has at most {MaxTokens} tokens."));
        foreach (var (name, value) in tokens)
        {
            if (!TokenName.IsMatch(name)) errors.Add(new("/tokens/" + name, "invalid-name", "Def_Err_Key", "A token name is --pems- and lower-case words joined by hyphens."));
            var text = KindChecks.Str(value)?.Trim();
            if (text is null || !(Hex.IsMatch(text) || Functional.IsMatch(text) || Variable.IsMatch(text) || Lengths.IsMatch(text)))
                errors.Add(new("/tokens/" + name, "invalid-value", "Def_Err_ThemeValue", "A token value is a colour (#hex, rgb, hsl), a length or number, or var(--pems-name)."));
        }
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson) => [];
}
