using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;

namespace PEMS.Web.Definitions;

/// <summary>The kinds that can be stored today (ADR-0011). More kinds are added by registering another <see cref="IDefinitionKind"/>.</summary>
public static class DefinitionKindExtensions
{
    public static IServiceCollection AddPemsDefinitionKinds(this IServiceCollection services)
    {
        services.AddSingleton<IDefinitionKind, FormDefinitionKind>();
        services.AddSingleton<IDefinitionKind, PageDefinitionKind>();
        services.AddSingleton<IDefinitionKind, LayoutDefinitionKind>();
        // SCRUM-451: the list view (columns, empty text) and the theme (design tokens) as definitions
        services.AddSingleton<IDefinitionKind, ViewDefinitionKind>();
        services.AddSingleton<IDefinitionKind, ThemeDefinitionKind>();
        // SCRUM-437: data sources and actions as definitions; the providers and handlers are registered code, named by the definitions
        services.AddSingleton<IDefinitionKind, DataSourceDefinitionKind>();
        services.AddSingleton<IDefinitionKind, ActionDefinitionKind>();
        // SCRUM-768: the route table (a URL pattern to a generic host), asked only when no controller answered
        services.AddSingleton<IDefinitionKind, RouteDefinitionKind>();
        // SCRUM-589: components as definitions (a parameterized fragment of registered components; a page template or an inline component)
        services.AddSingleton<IDefinitionKind, ComponentDefinitionKind>();
        // SCRUM-590: the menu as a definition (shipped by the migrator feed, synced into the sidebar table with its ownership rule)
        services.AddSingleton<IDefinitionKind, PEMS.Web.Navigation.MenuDefinitionKind>();
        // SCRUM-516: shipped reference data (lookups, templates, numbering) with an owner per list and per row
        services.AddSingleton<IDefinitionKind, PEMS.Application.Definitions.ReferenceDataKind>();
        services.AddScoped<RouteTable>();
        // posted values of a stored form are cleaned before use: rich-text HTML sanitized, undeclared names dropped (SCRUM-59)
        services.AddScoped<FormValueSanitizer>();
        return services;
    }

    internal static string Pointer(string jsonPath)
    {
        // "$.fields[0].name" -> "/fields/0/name"
        var p = jsonPath.TrimStart('$').Replace("[", "/").Replace("]", "").Replace('.', '/');
        return p.Length == 0 ? "" : (p.StartsWith('/') ? p : "/" + p);
    }
}

/// <summary>A form is the <see cref="FormDefinition"/> record as JSON; unknown properties are rejected.</summary>
public sealed class FormDefinitionKind : IDefinitionKind
{
    private static readonly JsonSerializerOptions Strict = new(PemsJson.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public string Key => "form";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        FormDefinition? form;
        try { form = JsonSerializer.Deserialize<FormDefinition>(payloadJson, Strict); }
        catch (JsonException ex)
        {
            return new[] { new DefinitionFieldError(DefinitionKindExtensions.Pointer(ex.Path ?? "$"), "invalid-form", "Def_Err_Form", ex.Message) };
        }
        if (form is null) return new[] { new DefinitionFieldError("", "invalid-form", "Def_Err_Form", "The form is empty.") };

        var errors = new List<DefinitionFieldError>();
        if (form.Fields.Count == 0) errors.Add(new("/fields", "required", "Def_Err_FormFields", "A form needs at least one field."));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < form.Fields.Count; i++)
            if (!seen.Add(form.Fields[i].Name))
                errors.Add(new($"/fields/{i}/name", "duplicate", "Def_Err_FormFieldDuplicate", $"The field name '{form.Fields[i].Name}' is used twice."));
        errors.AddRange(FormEngine.CheckDefinition(form));   // SCRUM-578: conditions, rules, messages and options are checked when the definition is loaded
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson)
    {
        try
        {
            var form = JsonSerializer.Deserialize<FormDefinition>(payloadJson, Strict);
            if (form is null) return Array.Empty<DefinitionRef>();
            var refs = form.Fields.Where(f => !string.IsNullOrEmpty(f.OptionsFrom)).Select(f => new DefinitionRef("data-source", f.OptionsFrom!, "options")).ToList();
            if (!string.IsNullOrEmpty(form.ActionKey)) refs.Add(new DefinitionRef("action", form.ActionKey, "submits"));
            return refs;
        }
        catch (JsonException) { return Array.Empty<DefinitionRef>(); }
    }
}

/// <summary>
/// A page is a layout of regions holding component instances (ADR-0011 point 1 and 2): every instance names a registered component and its options
/// are checked against that component's manifest (#433).
/// </summary>
public sealed class PageDefinitionKind : IDefinitionKind
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "layout", "regions", "route", "permission", "template", "args" };
    /// <summary>The variables an expression of a page can read: the signed-in user, the request (culture, route values), the hydrated values of the page and the current item.</summary>
    internal static readonly string[] PageExpressionRoots = ["user", "request", "values", "item", "params"];

    private static readonly HashSet<string> AllowedInstance = new(StringComparer.Ordinal) { "id", "component", "options", "visible", "bindings" };

    private readonly IPemsComponentRegistry _registry;
    private readonly ComponentManifestCatalog _catalog;

    public PageDefinitionKind(IPemsComponentRegistry registry, ComponentManifestCatalog catalog)
    {
        _registry = registry;
        _catalog = catalog;
    }

    public string Key => "page";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        var root = (JsonObject)JsonNode.Parse(payloadJson)!;
        foreach (var p in root) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a page."));
        if (root["layout"] is not JsonValue lv || lv.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace((string?)lv))
            errors.Add(new("/layout", "required", "Def_Err_Required", "A page needs a layout key."));
        if (root["route"] is { } route && !(route is JsonValue rv && rv.GetValueKind() == JsonValueKind.String)) errors.Add(new("/route", "wrong-type", "Def_Err_Type", "The route must be text."));
        if (root["permission"] is { } perm && !(perm is JsonValue pv && pv.GetValueKind() == JsonValueKind.String)) errors.Add(new("/permission", "wrong-type", "Def_Err_Type", "The permission must be text."));

        // SCRUM-589: a page may be a template (a component definition that supplies the regions) with its arguments; its own regions are added after the template's
        var template = root["template"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String ? (string?)tv : null;
        if (root["template"] is not null && string.IsNullOrWhiteSpace(template)) errors.Add(new("/template", "wrong-type", "Def_Err_Type", "template is the key of a component definition."));
        if (root["args"] is { } args && args is not JsonObject) errors.Add(new("/args", "wrong-type", "Def_Err_Type", "args is an object."));
        if (root["args"] is not null && template is null) errors.Add(new("/args", "overlap", "Def_Err_Overlap", "args needs a template."));
        if (root["regions"] is null && template is not null) return errors;
        if (root["regions"] is not JsonObject regions) { errors.Add(new("/regions", "required", "Def_Err_Required", "A page needs regions (an object of instance lists) or a template.")); return errors; }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var (region, list) in regions)
        {
            if (list is not JsonArray items) { errors.Add(new($"/regions/{region}", "wrong-type", "Def_Err_Type", "A region is a list of component instances.")); continue; }
            for (var i = 0; i < items.Count; i++)
            {
                var at = $"/regions/{region}/{i}";
                if (++count > limits.MaxInstancesPerPage) { errors.Add(new("/regions", "too-many-instances", "Def_Err_TooMany", $"A page may hold at most {limits.MaxInstancesPerPage} component instances.")); return errors; }
                if (items[i] is not JsonObject inst) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "A component instance is an object.")); continue; }
                foreach (var p in inst) if (!AllowedInstance.Contains(p.Key)) errors.Add(new(at + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a component instance."));

                var id = inst["id"] is JsonValue idv && idv.GetValueKind() == JsonValueKind.String ? (string?)idv : null;
                if (string.IsNullOrWhiteSpace(id)) errors.Add(new(at + "/id", "required", "Def_Err_Required", "An instance needs an id."));
                else if (!ids.Add(id)) errors.Add(new(at + "/id", "duplicate", "Def_Err_Duplicate", $"The instance id '{id}' is used twice on this page."));

                var component = inst["component"] is JsonValue cv && cv.GetValueKind() == JsonValueKind.String ? (string?)cv : null;
                if (string.IsNullOrWhiteSpace(component)) { errors.Add(new(at + "/component", "required", "Def_Err_Required", "An instance needs a component key.")); continue; }
                if (ComponentTemplate.IsDefinitionRef(component)) continue;   // a component definition (SCRUM-589): its arguments are checked when it is expanded
                if (!_registry.Keys.Contains(component, StringComparer.OrdinalIgnoreCase)) { errors.Add(new(at + "/component", "unknown-component", "Def_Err_UnknownComponent", $"'{component}' is not a registered component.")); continue; }

                // visible is an expression (ADR-0015): it must parse and read only the variables a page offers; checked at load, not at run time
                if (inst["visible"] is { } visible)
                {
                    if (visible is JsonValue vv && vv.GetValueKind() == JsonValueKind.String)
                    {
                        if (!PEMS.Application.Expressions.ExpressionCompiler.TryCompile((string)vv!, out _, out var ex, PageExpressionRoots))
                            errors.Add(new(at + "/visible", ex!.Code, "Def_Err_Expression", ex.Message));
                    }
                    else errors.Add(new(at + "/visible", "wrong-type", "Def_Err_Type", "visible is an expression (text)."));
                }

                // SCRUM-600: bindings.record reads one row of a grid data source for a details part
                if (inst["bindings"] is { } bindings)
                {
                    if (bindings is not JsonObject bo) errors.Add(new(at + "/bindings", "wrong-type", "Def_Err_Type", "bindings is an object."));
                    else
                        foreach (var (bname, bnode) in bo)
                        {
                            if (bname == "matrix")
                            {
                                if (bnode is not JsonObject mx) { errors.Add(new(at + "/bindings/matrix", "wrong-type", "Def_Err_Type", "matrix is an object (source, rowField, columnField, valueField).")); continue; }
                                var msrc = mx["source"] is JsonValue ms && ms.GetValueKind() == JsonValueKind.String ? (string?)ms : null;
                                if (msrc is null || !System.Text.RegularExpressions.Regex.IsMatch(msrc, "^/d/[a-z0-9][a-z0-9._-]*$")) errors.Add(new(at + "/bindings/matrix/source", "invalid", "Def_Err_Binding", "source is /d/{key} of a grid data source."));
                                foreach (var need in new[] { "rowField", "columnField", "valueField" })
                                    if (!(mx[need] is JsonValue nv && nv.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)nv))) errors.Add(new(at + "/bindings/matrix/" + need, "required", "Def_Err_Required", need + " names a row property."));
                                continue;
                            }
                            if (bname == "filterOptions")
                            {
                                if (bnode is not JsonArray fltList || fltList.Count == 0 || fltList.Count > 10) { errors.Add(new(at + "/bindings/filterOptions", "wrong-type", "Def_Err_Type", "filterOptions is a list of 1 to 10 { field, source, valueField, labelField }.")); continue; }
                                for (var fltIndex = 0; fltIndex < fltList.Count; fltIndex++)
                                {
                                    var fltAt = at + "/bindings/filterOptions/" + fltIndex;
                                    if (fltList[fltIndex] is not JsonObject fltItem) { errors.Add(new(fltAt, "wrong-type", "Def_Err_Type", "A filter choice binding is an object.")); continue; }
                                    var fltSrc = fltItem["source"] is JsonValue fltSv && fltSv.GetValueKind() == JsonValueKind.String ? (string?)fltSv : null;
                                    if (fltSrc is null || !System.Text.RegularExpressions.Regex.IsMatch(fltSrc, "^/d/[a-z0-9][a-z0-9._-]*$")) errors.Add(new(fltAt + "/source", "invalid", "Def_Err_Binding", "source is /d/{key} of a grid data source."));
                                    foreach (var fltNeed in new[] { "field", "valueField" })
                                        if (!(fltItem[fltNeed] is JsonValue fltNv && fltNv.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)fltNv))) errors.Add(new(fltAt + "/" + fltNeed, "required", "Def_Err_Required", fltNeed + " is required."));
                                }
                                continue;
                            }
                            if (bname == "chart")
                            {
                                if (bnode is not JsonObject ch) { errors.Add(new(at + "/bindings/chart", "wrong-type", "Def_Err_Type", "chart is an object (source, labelField, series).")); continue; }
                                var csrc = ch["source"] is JsonValue cs && cs.GetValueKind() == JsonValueKind.String ? (string?)cs : null;
                                if (csrc is null || !System.Text.RegularExpressions.Regex.IsMatch(csrc, "^/d/[a-z0-9][a-z0-9._-]*([?].*)?$")) errors.Add(new(at + "/bindings/chart/source", "invalid", "Def_Err_Binding", "source is /d/{key} of a grid data source."));
                                if (!(ch["labelField"] is JsonValue lf && lf.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)lf))) errors.Add(new(at + "/bindings/chart/labelField", "required", "Def_Err_Required", "labelField names the row property of the labels."));
                                if (ch["series"] is not JsonArray sa || sa.Count == 0 || sa.Any(x => !(x is JsonObject xo && xo["field"] is JsonValue xf && xf.GetValueKind() == JsonValueKind.String))) errors.Add(new(at + "/bindings/chart/series", "required", "Def_Err_Required", "series is a list of { field, name, color }."));
                                continue;
                            }
                            if (bname != "record") { errors.Add(new(at + "/bindings/" + bname, "unknown-property", "Def_Err_UnknownProperty", $"'{bname}' is not a binding (record, chart)." )); continue; }
                            if (bnode is not JsonObject rec) { errors.Add(new(at + "/bindings/record", "wrong-type", "Def_Err_Type", "record is an object (source, idField, id).")); continue; }
                            var src = rec["source"] is JsonValue sv && sv.GetValueKind() == JsonValueKind.String ? (string?)sv : null;
                            if (src is null || !System.Text.RegularExpressions.Regex.IsMatch(src, "^/d/[a-z0-9][a-z0-9._-]*([?].*)?$")) errors.Add(new(at + "/bindings/record/source", "invalid", "Def_Err_Binding", "source is /d/{key} of a grid data source."));
                            var firstRow = rec["first"] is JsonValue fv0 && fv0.GetValueKind() == JsonValueKind.True;   // batch 5: the first row of the source needs no id
                            if (!firstRow && !(rec["idField"] is JsonValue iv && iv.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)iv))) errors.Add(new(at + "/bindings/record/idField", "required", "Def_Err_Required", "idField names the row property of the id."));
                            if (!firstRow && !(rec["id"] is JsonValue idv2 && idv2.GetValueKind() == JsonValueKind.String)) errors.Add(new(at + "/bindings/record/id", "required", "Def_Err_Required", "id is text, usually {{params.id}}."));
                            if (rec["map"] is { } map && !(map is JsonObject mo && mo.All(m => m.Value is JsonValue mv && mv.GetValueKind() == JsonValueKind.String))) errors.Add(new(at + "/bindings/record/map", "wrong-type", "Def_Err_Type", "map is an object: option name to row property."));
                            foreach (var extra in rec.Select(x => x.Key).Where(k => k is not ("source" or "idField" or "id" or "map" or "first"))) errors.Add(new(at + "/bindings/record/" + extra, "unknown-property", "Def_Err_UnknownProperty", $"'{extra}' is not a property of a record binding."));
                        }
                }

                var options = inst["options"] ?? new JsonObject();
                if (options is not JsonObject) { errors.Add(new(at + "/options", "wrong-type", "Def_Err_Type", "The options are an object.")); continue; }
                foreach (var e in _catalog.Validate(component, options.ToJsonString()).Errors)
                    errors.Add(new(at + "/options" + e.Pointer, e.Code, e.MessageKey, e.Message));
            }
        }
        return errors;
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.String => [(string)v!],
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        _ => []
    };

    public IReadOnlyList<DefinitionRef> References(string payloadJson) => ReferencesOf(payloadJson);

    /// <summary>SCRUM-451: the references of a page payload (its layout, its template, the data sources and actions its parts use); static so the harness can read them without the component registry.</summary>
    public static IReadOnlyList<DefinitionRef> ReferencesOf(string payloadJson)
    {
        var refs = new List<DefinitionRef>();
        var root = (JsonObject)JsonNode.Parse(payloadJson)!;
        if (root["layout"] is JsonValue layoutKey && layoutKey.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)layoutKey)) refs.Add(new DefinitionRef("layout", (string)layoutKey!, "frames"));
        if (root["template"] is JsonValue tpl && tpl.GetValueKind() == JsonValueKind.String) refs.Add(new DefinitionRef("component-definition", (string)tpl!, "templates"));
        // the /d/{key} urls in the arguments of a template are bindings too (the template puts them into a grid)
        if (root["args"] is JsonObject templateArgs)
            foreach (var text in Strings(templateArgs))
                if (System.Text.RegularExpressions.Regex.Match(text, "^/d/([a-z0-9][a-z0-9._-]*)(?:[?].*)?$") is { Success: true } am) refs.Add(new DefinitionRef("data-source", am.Groups[1].Value, "binds"));
        if (root["regions"] is JsonObject regions)
            foreach (var (_, list) in regions)
                if (list is JsonArray items)
                    foreach (var inst in items.OfType<JsonObject>())
                    {
                        if (inst["component"] is JsonValue cv && cv.GetValueKind() == JsonValueKind.String)
                            refs.Add(ComponentTemplate.IsDefinitionRef((string)cv!) ? new DefinitionRef("component-definition", ComponentTemplate.KeyOf((string)cv!), "renders") : new DefinitionRef("component", (string)cv!, "renders"));
                        // SCRUM-437: the bindings of a component to stored definitions: an url "/d/{key}" reads a data source, an action-button runs an action
                        if (inst["bindings"] is JsonObject mbnd && mbnd["matrix"] is JsonObject mbrec && mbrec["source"] is JsonValue mbsv && mbsv.GetValueKind() == JsonValueKind.String
                            && System.Text.RegularExpressions.Regex.Match((string)mbsv!, "^/d/([a-z0-9][a-z0-9._-]*)$") is { Success: true } mbm) refs.Add(new DefinitionRef("data-source", mbm.Groups[1].Value, "binds"));
                        if (inst["bindings"] is JsonObject cbnd && cbnd["chart"] is JsonObject cbrec && cbrec["source"] is JsonValue cbsv && cbsv.GetValueKind() == JsonValueKind.String
                            && System.Text.RegularExpressions.Regex.Match((string)cbsv!, "^/d/([a-z0-9][a-z0-9._-]*)$") is { Success: true } cbm) refs.Add(new DefinitionRef("data-source", cbm.Groups[1].Value, "binds"));
                        if (inst["bindings"] is JsonObject bnd && bnd["record"] is JsonObject brec && brec["source"] is JsonValue bsv && bsv.GetValueKind() == JsonValueKind.String
                            && System.Text.RegularExpressions.Regex.Match((string)bsv!, "^/d/([a-z0-9][a-z0-9._-]*)$") is { Success: true } bm) refs.Add(new DefinitionRef("data-source", bm.Groups[1].Value, "binds"));
                        if (inst["options"] is JsonObject options)
                        {
                            foreach (var text in Strings(options))
                                if (System.Text.RegularExpressions.Regex.Match(text, "^/d/([a-z0-9][a-z0-9._-]*)(?:[?].*)?$") is { Success: true } m) refs.Add(new DefinitionRef("data-source", m.Groups[1].Value, "reads"));
                            if ((string?)inst["component"] == "action-button" && options["action"] is JsonValue av && av.GetValueKind() == JsonValueKind.String) refs.Add(new DefinitionRef("action", (string)av!, "runs"));
                        }
                    }
        return refs;
    }
}
