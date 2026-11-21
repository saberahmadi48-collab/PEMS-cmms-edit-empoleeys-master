using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;

namespace PEMS.Web.Definitions;

// SCRUM-589 (the sprint goal: a screen is data). A component definition is a named, parameterized fragment of component instances stored in the database: the list page, the details page, a header block
// are definitions, not code. A page uses one as a whole-page template (`"template": "entity-list", "args": {...}`) or an instance uses one inline (`"component": "def:key", "options": {args}`).
// Only the registered PEMS.UI components can render (the code owns the vocabulary); a definition only arranges them. Parameters are typed and checked; `${name}` in a text is the value of the parameter
// (a text that is only the placeholder keeps the type of the value: a list stays a list).

/// <summary>
/// The component definition kind: <c>{ "parameters": { "id": { "type": "string", "required": true }, "texts": { "type": "object", "default": {...} } },
/// "regions": { "content": [ instance ], "toolbar": [ instance ] } }</c> (a page template) or <c>{ "parameters": {...}, "instances": [ instance ] }</c> (an inline component); exactly one of regions and instances.
/// </summary>
public sealed class ComponentDefinitionKind(IPemsComponentRegistry registry) : IDefinitionKind
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "parameters", "regions", "instances" };
    private static readonly HashSet<string> AllowedInstance = new(StringComparer.Ordinal) { "id", "component", "options", "visible", "bindings", "if", "each", "as" };
    private static readonly HashSet<string> AllowedParameter = new(StringComparer.Ordinal) { "type", "required", "default", "description" };
    public static readonly string[] Types = ["string", "number", "boolean", "object", "array", "text", "any"];
    internal static readonly Regex Placeholder = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\}", RegexOptions.Compiled);

    public string Key => "component";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "wrong-type", "Def_Err_Type", "A component definition is an object.")];
        foreach (var p in root) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a component definition."));

        var declared = new HashSet<string>(StringComparer.Ordinal);
        if (root["parameters"] is { } ps)
        {
            if (ps is not JsonObject po) errors.Add(new("/parameters", "wrong-type", "Def_Err_Type", "parameters is an object of parameter descriptions."));
            else
                foreach (var (name, node) in po)
                {
                    var at = "/parameters/" + name;
                    if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) { errors.Add(new(at, "invalid-name", "Def_Err_Name", "A parameter name is letters, digits and underscore.")); continue; }
                    declared.Add(name);
                    if (node is not JsonObject d) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "A parameter is an object (type, required, default).")); continue; }
                    foreach (var p in d) if (!AllowedParameter.Contains(p.Key)) errors.Add(new(at + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a parameter."));
                    var type = d["type"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String ? (string?)tv : null;
                    if (type is null || !Types.Contains(type)) errors.Add(new(at + "/type", "invalid-type", "Def_Err_Type", "type is one of " + string.Join(", ", Types) + "."));
                    else if (d["default"] is { } def && !ComponentTemplate.Fits(type, def)) errors.Add(new(at + "/default", "wrong-type", "Def_Err_Type", $"The default is not a {type}."));
                    if (d["required"] is { } req && !(req is JsonValue rv && rv.GetValueKind() is JsonValueKind.True or JsonValueKind.False)) errors.Add(new(at + "/required", "wrong-type", "Def_Err_Type", "required is true or false."));
                }
        }

        var hasRegions = root["regions"] is not null;
        var hasInstances = root["instances"] is not null;
        if (hasRegions == hasInstances) { errors.Add(new("", "required", "Def_Err_Required", "A component definition has either regions (a page template) or instances (an inline component).")); return errors; }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        if (root["regions"] is JsonObject regions)
            foreach (var (region, list) in regions) CheckList(list, "/regions/" + region, declared, ids, ref count, limits, errors);
        else if (hasRegions) errors.Add(new("/regions", "wrong-type", "Def_Err_Type", "regions is an object of instance lists."));
        if (hasInstances) CheckList(root["instances"], "/instances", declared, ids, ref count, limits, errors);
        return errors;
    }

    private void CheckList(JsonNode? list, string at, HashSet<string> declared, HashSet<string> ids, ref int count, DefinitionLimits limits, List<DefinitionFieldError> errors)
    {
        if (list is not JsonArray items) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "An instance list is an array.")); return; }
        for (var i = 0; i < items.Count; i++)
        {
            var path = $"{at}/{i}";
            if (++count > limits.MaxInstancesPerPage) { errors.Add(new(at, "too-many-instances", "Def_Err_TooMany", $"At most {limits.MaxInstancesPerPage} component instances.")); return; }
            if (items[i] is not JsonObject inst) { errors.Add(new(path, "wrong-type", "Def_Err_Type", "A component instance is an object.")); continue; }
            foreach (var p in inst) if (!AllowedInstance.Contains(p.Key)) errors.Add(new(path + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a component instance."));
            var id = inst["id"] is JsonValue idv && idv.GetValueKind() == JsonValueKind.String ? (string?)idv : null;
            if (string.IsNullOrWhiteSpace(id)) errors.Add(new(path + "/id", "required", "Def_Err_Required", "An instance needs an id."));
            else if (!ids.Add(id)) errors.Add(new(path + "/id", "duplicate", "Def_Err_Duplicate", $"The instance id '{id}' is used twice."));
            var component = inst["component"] is JsonValue cv && cv.GetValueKind() == JsonValueKind.String ? (string?)cv : null;
            if (string.IsNullOrWhiteSpace(component)) errors.Add(new(path + "/component", "required", "Def_Err_Required", "An instance needs a component key."));
            else if (!ComponentTemplate.IsDefinitionRef(component) && !registry.Keys.Contains(component, StringComparer.OrdinalIgnoreCase))
                errors.Add(new(path + "/component", "unknown-component", "Def_Err_UnknownComponent", $"'{component}' is not a registered component (use def:key for a component definition)."));
            if (inst["options"] is { } o && o is not JsonObject) errors.Add(new(path + "/options", "wrong-type", "Def_Err_Type", "The options are an object."));

            // `each`: "${list}" repeats the instance once per item of an array parameter, the item is ${as.property} (as defaults to item)
            var inScope = new HashSet<string>(declared, StringComparer.Ordinal);
            if (inst["each"] is { } each)
            {
                var m = each is JsonValue ev && ev.GetValueKind() == JsonValueKind.String ? Placeholder.Match((string)ev!) : null;
                if (m is null || !m.Success || m.Length != ((string)each!).Length || m.Groups[1].Value.Contains('.') || !declared.Contains(m.Groups[1].Value))
                    errors.Add(new(path + "/each", "invalid", "Def_Err_Each", "each is the placeholder of an array parameter, such as ${tiles}."));
                inScope.Add(inst["as"] is JsonValue av && av.GetValueKind() == JsonValueKind.String ? (string)av! : "item");
            }
            else if (inst["as"] is not null) errors.Add(new(path + "/as", "overlap", "Def_Err_Overlap", "as needs each."));
            foreach (var (pointer, name) in Placeholders(inst, path))
                if (!inScope.Contains(name.Split('.')[0]) && pointer != path + "/each") errors.Add(new(pointer, "unknown-parameter", "Def_Err_Parameter", $"'${{{name}}}' is not a declared parameter."));
        }
    }

    private static IEnumerable<(string Pointer, string Name)> Placeholders(JsonNode? node, string at)
    {
        switch (node)
        {
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                foreach (Match m in Placeholder.Matches((string)v!)) yield return (at, m.Groups[1].Value);
                break;
            case JsonObject o:
                foreach (var (k, child) in o) foreach (var x in Placeholders(child, at + "/" + k)) yield return x;
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) foreach (var x in Placeholders(a[i], at + "/" + i)) yield return x;
                break;
        }
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson)
    {
        var refs = new List<DefinitionRef>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return refs;
        foreach (var inst in Instances(root))
            if (inst["component"] is JsonValue cv && cv.GetValueKind() == JsonValueKind.String)
                refs.Add(ComponentTemplate.IsDefinitionRef((string)cv!) ? new DefinitionRef("component-definition", ComponentTemplate.KeyOf((string)cv!), "renders") : new DefinitionRef("component", (string)cv!, "renders"));
        return refs;
    }

    internal static IEnumerable<JsonObject> Instances(JsonObject root)
    {
        if (root["instances"] is JsonArray ia) foreach (var i in ia.OfType<JsonObject>()) yield return i;
        if (root["regions"] is JsonObject regions)
            foreach (var (_, list) in regions) if (list is JsonArray a) foreach (var i in a.OfType<JsonObject>()) yield return i;
    }
}

public sealed class ComponentTemplateException(string message) : Exception(message);

/// <summary>The expansion of a component definition for given arguments: parameters are checked, defaults filled, <c>${name}</c> replaced. Pure, no I/O.</summary>
public static class ComponentTemplate
{
    public const string Prefix = "def:";
    public static bool IsDefinitionRef(string component) => component.StartsWith(Prefix, StringComparison.Ordinal);
    public static string KeyOf(string component) => component[Prefix.Length..];

    public static bool Fits(string type, JsonNode value) => type switch
    {
        "string" => value is JsonValue v && v.GetValueKind() == JsonValueKind.String,
        "number" => value is JsonValue v && v.GetValueKind() == JsonValueKind.Number,
        "boolean" => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "text" => value is JsonValue sv && sv.GetValueKind() == JsonValueKind.String || value is JsonObject o && o.All(p => p.Value is JsonValue pv && pv.GetValueKind() == JsonValueKind.String),
        _ => true
    };

    /// <summary>Resolves the arguments against the declared parameters: required ones must be present and fit their type, the others take their default.</summary>
    public static Dictionary<string, JsonNode?> Bind(JsonObject definition, JsonObject? args)
    {
        var bound = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var declared = definition["parameters"] as JsonObject ?? new JsonObject();
        if (args is not null)
            foreach (var (name, _) in args)
                if (!declared.ContainsKey(name)) throw new ComponentTemplateException($"'{name}' is not a parameter of this component.");
        foreach (var (name, spec) in declared)
        {
            var type = (string?)(spec as JsonObject)?["type"] ?? "any";
            JsonNode? value = args is not null && args.ContainsKey(name) ? args[name] : null;
            if (value is null) value = (spec as JsonObject)?["default"];
            if (value is null)
            {
                if ((spec as JsonObject)?["required"] is JsonValue rv && rv.GetValueKind() == JsonValueKind.True) throw new ComponentTemplateException($"The parameter '{name}' is required.");
                bound[name] = null;
                continue;
            }
            if (!Fits(type, value)) throw new ComponentTemplateException($"The parameter '{name}' must be a {type}.");
            bound[name] = value.DeepClone();
        }
        return bound;
    }

    /// <summary>A copy of the node with every <c>${name}</c> replaced; a property whose whole value is an unset optional parameter is dropped.</summary>
    public static JsonNode? Substitute(JsonNode? node, IReadOnlyDictionary<string, JsonNode?> values)
    {
        switch (node)
        {
            case null: return null;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var text = (string)v!;
                var whole = ComponentDefinitionKind.Placeholder.Match(text);
                if (whole.Success && whole.Length == text.Length) return Resolve(values, whole.Groups[1].Value)?.DeepClone();
                return JsonValue.Create(ComponentDefinitionKind.Placeholder.Replace(text, m =>
                {
                    var x = Resolve(values, m.Groups[1].Value);
                    if (x is null) return "";
                    return x is JsonValue xv && xv.GetValueKind() == JsonValueKind.String ? (string)xv! : x is JsonValue ? x.ToJsonString().Trim('"') : throw new ComponentTemplateException($"'${{{m.Groups[1].Value}}}' is inside a text but is not a simple value.");
                }));
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (k, child) in o)
                {
                    var replaced = Substitute(child, values);
                    if (replaced is null && child is JsonValue) continue;   // an unset optional parameter: the property is absent
                    copy[k] = replaced;
                }
                return copy;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var child in a)
                {
                    // `"each": "${list}"` repeats the instance for every item of the array parameter; the id gets the position (SCRUM-601)
                    if (child is JsonObject rep && rep.ContainsKey("each"))
                    {
                        var source = Substitute(rep["each"], values) as JsonArray;
                        var name = rep["as"] is JsonValue nv && nv.GetValueKind() == JsonValueKind.String ? (string)nv! : "item";
                        var template = (JsonObject)rep.DeepClone();
                        template.Remove("each"); template.Remove("as");
                        for (var i = 0; source is not null && i < source.Count; i++)
                        {
                            var scoped = new Dictionary<string, JsonNode?>(values, StringComparer.Ordinal) { [name] = source[i]?.DeepClone() };
                            if (Substitute(template, scoped) is JsonObject made)
                            {
                                if (made["id"] is JsonValue idv && idv.GetValueKind() == JsonValueKind.String) made["id"] = (string)idv! + "-" + (i + 1);
                                list.Add(made);
                            }
                        }
                        continue;
                    }
                    // `"if": "${name}"` on an instance keeps it only when the parameter is set (and not false, not empty): an optional part of a template
                    if (child is JsonObject inst && inst.ContainsKey("if"))
                    {
                        var keep = Substitute(inst["if"], values);
                        if (keep is null || (keep is JsonValue kv && (kv.GetValueKind() == JsonValueKind.False || (kv.GetValueKind() == JsonValueKind.String && string.IsNullOrEmpty((string)kv!))))) continue;
                        var withoutIf = (JsonObject)inst.DeepClone();
                        withoutIf.Remove("if");
                        var r2 = Substitute(withoutIf, values);
                        if (r2 is not null) list.Add(r2);
                        continue;
                    }
                    var r = Substitute(child, values);
                    if (r is not null) list.Add(r);
                }
                return list;
            default:
                return node.DeepClone();
        }
    }

    /// <summary>A value by its name or a dotted path into an object value (tile.label).</summary>
    private static JsonNode? Resolve(IReadOnlyDictionary<string, JsonNode?> values, string path)
    {
        var parts = path.Split('.');
        if (!values.TryGetValue(parts[0], out var node)) return null;
        for (var i = 1; i < parts.Length && node is not null; i++) node = (node as JsonObject)?[parts[i]];
        return node;
    }

    /// <summary>The regions of a page template for the arguments.</summary>
    public static JsonObject ExpandRegions(JsonObject definition, JsonObject? args)
    {
        var values = Bind(definition, args);
        return (JsonObject?)Substitute(definition["regions"] ?? new JsonObject(), values) ?? new JsonObject();
    }

    /// <summary>The instances of an inline component for the arguments; child ids are prefixed with the id of the instance that uses the component, so two uses never collide.</summary>
    public static JsonArray ExpandInstances(JsonObject definition, JsonObject? args, string idPrefix)
    {
        var values = Bind(definition, args);
        var list = (JsonArray?)Substitute(definition["instances"] ?? new JsonArray(), values) ?? [];
        foreach (var inst in list.OfType<JsonObject>()) inst["id"] = idPrefix + "." + (string?)inst["id"];
        return list;
    }
}
