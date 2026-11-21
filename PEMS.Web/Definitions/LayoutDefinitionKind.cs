using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

/// <summary>
/// A layout is the frame of a page, defined as data (ADR-0011 point 3, SCRUM-572): a tree of containers with named regions in it. A page names its layout and fills
/// the regions with component instances; the page host writes the containers around them. Only plain containers are allowed (no script, no inline style, no event handlers).
/// <code>{ "root": { "tag": "div", "css": "lp-page", "children": [ { "region": "header" }, { "tag": "section", "children": [ { "region": "content", "required": true } ] } ] } }</code>
/// </summary>
public sealed class LayoutDefinitionKind : IDefinitionKind
{
    private const int MaxDepth = 8;
    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal) { "div", "section", "header", "main", "aside", "footer", "nav", "article" };
    private static readonly HashSet<string> NodeProperties = new(StringComparer.Ordinal) { "tag", "css", "attrs", "children" };
    private static readonly HashSet<string> RegionProperties = new(StringComparer.Ordinal) { "region", "required" };
    private static readonly Regex RegionName = new("^[a-z][a-z0-9-]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex AttributeName = new("^(role|id|aria-[a-z-]+|data-[a-z0-9-]+)$", RegexOptions.Compiled);
    private static readonly Regex CssClasses = new(@"^[A-Za-z0-9_\- ]{0,200}$", RegexOptions.Compiled);

    public string Key => "layout";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        var root = JsonNode.Parse(payloadJson) as JsonObject;
        if (root is null) return [new("", "invalid-layout", "Def_Err_Layout", "A layout is an object.")];
        foreach (var p in root) if (p.Key != "root") errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"\"{p.Key}\" is not a property of a layout."));
        if (root["root"] is not JsonObject tree) { errors.Add(new("/root", "required", "Def_Err_Required", "A layout needs a root container.")); return errors; }
        var regions = new HashSet<string>(StringComparer.Ordinal);
        Walk(tree, "/root", 1, regions, errors);
        if (regions.Count == 0) errors.Add(new("/root", "no-region", "Def_Err_LayoutRegion", "A layout needs at least one region."));
        return errors;
    }

    private static void Walk(JsonObject node, string at, int depth, HashSet<string> regions, List<DefinitionFieldError> errors)
    {
        if (depth > MaxDepth) { errors.Add(new(at, "too-deep", "Def_Err_Depth", $"A layout is at most {MaxDepth} containers deep.")); return; }

        if (node.ContainsKey("region"))
        {
            foreach (var p in node) if (!RegionProperties.Contains(p.Key)) errors.Add(new(at + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"\"{p.Key}\" is not a property of a region."));
            var name = node["region"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;
            if (name is null || !RegionName.IsMatch(name)) errors.Add(new(at + "/region", "invalid-name", "Def_Err_RegionName", "A region name is lower-case letters, digits and hyphens."));
            else if (!regions.Add(name)) errors.Add(new(at + "/region", "duplicate", "Def_Err_Duplicate", $"The region \"{name}\" is used twice."));
            if (node["required"] is { } req && !(req is JsonValue rv && rv.GetValueKind() is JsonValueKind.True or JsonValueKind.False)) errors.Add(new(at + "/required", "wrong-type", "Def_Err_Type", "required is true or false."));
            return;
        }

        foreach (var p in node) if (!NodeProperties.Contains(p.Key)) errors.Add(new(at + "/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"\"{p.Key}\" is not a property of a container."));
        var tag = node["tag"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String ? (string?)tv : "div";
        if (!Tags.Contains(tag!)) errors.Add(new(at + "/tag", "invalid-tag", "Def_Err_LayoutTag", $"\"{tag}\" is not an allowed container (div, section, header, main, aside, footer, nav, article)."));
        if (node["css"] is { } css && !(css is JsonValue cv && cv.GetValueKind() == JsonValueKind.String && CssClasses.IsMatch((string)cv!))) errors.Add(new(at + "/css", "invalid-css", "Def_Err_LayoutCss", "css is a list of class names."));
        if (node["attrs"] is { } attrs)
        {
            if (attrs is not JsonObject ao) errors.Add(new(at + "/attrs", "wrong-type", "Def_Err_Type", "attrs is an object."));
            else foreach (var a in ao)
                {
                    if (!AttributeName.IsMatch(a.Key)) errors.Add(new(at + "/attrs/" + a.Key, "invalid-attribute", "Def_Err_LayoutAttr", $"\"{a.Key}\" is not allowed (role, id, aria-*, data-*)."));
                    else if (!(a.Value is JsonValue av && av.GetValueKind() == JsonValueKind.String)) errors.Add(new(at + "/attrs/" + a.Key, "wrong-type", "Def_Err_Type", "An attribute value is text."));
                }
        }
        if (node["children"] is not JsonArray children) { errors.Add(new(at + "/children", "required", "Def_Err_Required", "A container needs children.")); return; }
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is JsonObject child) Walk(child, $"{at}/children/{i}", depth + 1, regions, errors);
            else errors.Add(new($"{at}/children/{i}", "wrong-type", "Def_Err_Type", "A child is a container or a region."));
        }
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson) => [];
}
