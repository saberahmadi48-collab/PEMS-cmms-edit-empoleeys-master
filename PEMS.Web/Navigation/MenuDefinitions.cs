using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.DTOs;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.UI.Abstractions;
using PEMS.Web.Definitions;

namespace PEMS.Web.Navigation;

// SCRUM-590: the menu is a definition. A new page appears in the sidebar by a shipped (migrator feed) <c>menu</c> definition only: no code, no controller. The kind validates the feed; MenuDefinitions turns it into the
// records NavigationMenuService.SyncDefaultsAsync already knows, so the ownership rule stays the one of the sidebar (what the product ships is updated, what an administrator changed - order, parent, icon, title, visibility
// switch - is never touched, a custom item with the same key is never taken over) and visibility is the rule language of the sidebar (Perm:Module.Action, RoleAction, Role, Flag, Always, with & and |).

/// <summary>
/// The <c>menu</c> definition: <c>{ "items": [ { "key": "stock.list", "parent": "warehouse", "kind": "link", "order": 120, "title": { "en": "Stock", "fa": "موجودی" }, "icon": "bi-box",
/// "rule": "Perm:Stock.View", "page": "stock.list" } ] }</c>. A link points at exactly one of <c>page</c> (/p/key), <c>form</c> (/f/key) or <c>url</c> (a path of this site); a section or subheading has no target.
/// </summary>
public sealed class MenuDefinitionKind : IDefinitionKind
{
    public const int MaxItems = 200;
    private static readonly Regex KeyShape = new("^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly HashSet<string> ItemProperties = new(StringComparer.Ordinal) { "key", "parent", "kind", "order", "titleKey", "title", "icon", "rule", "page", "form", "url", "newTab", "badge" };
    private static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["link"] = NavigationMenuItemKinds.Link, ["section"] = NavigationMenuItemKinds.Section, ["subheading"] = NavigationMenuItemKinds.Subheading,
    };

    public string Key => "menu";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "wrong-type", "Def_Err_Type", "A menu is an object.")];
        foreach (var p in root) if (p.Key != "items") errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a menu."));
        if (root["items"] is not JsonArray items || items.Count == 0) { errors.Add(new("/items", "required", "Def_Err_Required", "A menu has at least one item.")); return errors; }
        if (items.Count > MaxItems) errors.Add(new("/items", "too-many", "Def_Err_Range", $"A menu definition has at most {MaxItems} items."));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var at = $"/items/{i}";
            if (items[i] is not JsonObject item) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "An item is an object.")); continue; }
            foreach (var p in item) if (!ItemProperties.Contains(p.Key)) errors.Add(new($"{at}/{p.Key}", "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a menu item."));

            var key = DataSourceDefinitionKind.Str(item["key"]);
            if (key is null || key.Length > 80 || !KeyShape.IsMatch(key)) errors.Add(new($"{at}/key", "invalid-key", "Def_Err_Key", "key is lower-case letters, digits and . _ - (at most 80)."));
            else if (!keys.Add(key)) errors.Add(new($"{at}/key", "duplicate", "Def_Err_Duplicate", $"The key '{key}' is used twice."));
            if (item["parent"] is { } parent && (DataSourceDefinitionKind.Str(parent) is not { } pk || !KeyShape.IsMatch(pk) || pk == key)) errors.Add(new($"{at}/parent", "invalid-key", "Def_Err_Key", "parent is the key of another item."));

            var kind = DataSourceDefinitionKind.Str(item["kind"]);
            if (kind is null || !Kinds.ContainsKey(kind)) errors.Add(new($"{at}/kind", "unknown-kind", "Def_Err_Kind", "kind is link, section or subheading."));
            if (!(item["order"] is JsonValue ov && ov.TryGetValue<int>(out var order) && order is >= 0 and <= 100000)) errors.Add(new($"{at}/order", "range", "Def_Err_Range", "order is a whole number from 0 to 100000."));

            var hasTitle = item["title"] is not null;
            if (hasTitle) DataSourceDefinitionKind.CheckLabel(item["title"], $"{at}/title", errors);
            if (!hasTitle && string.IsNullOrWhiteSpace(DataSourceDefinitionKind.Str(item["titleKey"]))) errors.Add(new($"{at}/title", "required", "Def_Err_Required", "An item has a title (both languages) or a titleKey."));

            if (item["icon"] is { } icon && !NavigationMenuService.IsValidIcon(DataSourceDefinitionKind.Str(icon))) errors.Add(new($"{at}/icon", "invalid-icon", "Def_Err_Type", "icon is a Bootstrap icon name such as bi-box."));
            foreach (var problem in NavigationRuleEvaluator.Validate(DataSourceDefinitionKind.Str(item["rule"]))) errors.Add(new($"{at}/rule", "invalid-rule", "Def_Err_Type", $"rule: {problem}. Visibility follows the permission rules of the sidebar (for example Perm:Module.Action)."));

            var targets = new[] { "page", "form", "url" }.Where(t => item[t] is not null).ToList();
            if (kind == "link")
            {
                if (targets.Count != 1) errors.Add(new($"{at}/page", "target", "Def_Err_Required", "A link points at exactly one of page, form or url."));
                else if (DataSourceDefinitionKind.Str(item[targets[0]]) is not { } target) errors.Add(new($"{at}/{targets[0]}", "wrong-type", "Def_Err_Type", $"{targets[0]} is text."));
                else if (targets[0] == "url" ? !target.StartsWith('/') || target.StartsWith("//") || target.Contains('\\') || target.Length > 300 : !KeyShape.IsMatch(target))
                    errors.Add(new($"{at}/{targets[0]}", "invalid-target", "Def_Err_Redirect", targets[0] == "url" ? "url is a path of this site (starts with one /)." : $"{targets[0]} is the key of a definition."));
            }
            else if (targets.Count > 0) errors.Add(new($"{at}/{targets[0]}", "target", "Def_Err_Overlap", "A section or subheading has no target."));
            if (item["newTab"] is { } nt && !(nt is JsonValue nv && nv.TryGetValue<bool>(out _))) errors.Add(new($"{at}/newTab", "wrong-type", "Def_Err_Type", "newTab is true or false."));
        }
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson)
    {
        try
        {
            return (JsonNode.Parse(payloadJson) as JsonObject)?["items"] is JsonArray items
                ? items.OfType<JsonObject>().SelectMany(i => new[] { ("page", "page"), ("form", "form") }
                    .Where(t => DataSourceDefinitionKind.Str(i[t.Item1]) is not null).Select(t => new DefinitionRef(t.Item2, DataSourceDefinitionKind.Str(i[t.Item1])!, "menu-link"))).ToList()
                : [];
        }
        catch (JsonException) { return []; }
    }
}

public static class MenuDefinitions
{
    /// <summary>The menu items of the published <c>menu</c> definitions, as the records the sidebar sync takes (an invalid definition is skipped: the store only holds valid ones).</summary>
    public static IReadOnlyList<NavigationMenuItemDefinition> From(IEnumerable<PublishedDefinition> published)
    {
        var result = new List<NavigationMenuItemDefinition>();
        foreach (var definition in published.OrderBy(d => d.AppKey, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal))
        {
            if (JsonNode.Parse(definition.PayloadJson) is not JsonObject root || root["items"] is not JsonArray items) continue;
            foreach (var item in items.OfType<JsonObject>())
            {
                var kind = Normalize(DataSourceDefinitionKind.Str(item["kind"]));
                var title = item["title"]?.Deserialize<TranslatableText>(PemsJson.Options);
                var en = title?.Texts.GetValueOrDefault("en");
                var target = DataSourceDefinitionKind.Str(item["page"]) is { } page ? "/p/" + page : DataSourceDefinitionKind.Str(item["form"]) is { } form ? "/f/" + form : DataSourceDefinitionKind.Str(item["url"]);
                result.Add(new NavigationMenuItemDefinition(
                    Key: DataSourceDefinitionKind.Str(item["key"])!, ParentKey: DataSourceDefinitionKind.Str(item["parent"]), Kind: kind,
                    SortOrder: item["order"] is JsonValue ov && ov.TryGetValue<int>(out var order) ? order : 0,
                    TitleKey: DataSourceDefinitionKind.Str(item["titleKey"]) ?? en ?? DataSourceDefinitionKind.Str(item["key"])!,
                    Icon: DataSourceDefinitionKind.Str(item["icon"]), VisibilityRule: DataSourceDefinitionKind.Str(item["rule"]) ?? NavigationVisibilityRules.Always,
                    ExternalPath: kind == NavigationMenuItemKinds.Link ? target : null,
                    OpenInNewTab: item["newTab"] is JsonValue nv && nv.TryGetValue<bool>(out var tab) && tab,
                    BadgeKey: DataSourceDefinitionKind.Str(item["badge"]),
                    TitleEn: en, TitleFa: title?.Texts.GetValueOrDefault("fa")));
            }
        }
        return result;
    }

    private static string Normalize(string? kind) => kind switch
    {
        "section" => NavigationMenuItemKinds.Section, "subheading" => NavigationMenuItemKinds.Subheading, _ => NavigationMenuItemKinds.Link,
    };
}
