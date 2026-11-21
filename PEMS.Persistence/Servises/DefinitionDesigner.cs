using System.Text.Json;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>The designer operations (SCRUM-439, 604, 606, 607, 608); see <see cref="IDefinitionDesigner"/>. Edits are made on the open draft; nothing is published here.</summary>
public sealed class DefinitionDesigner(IDefinitionService definitions, IDefinitionWorkbench workbench) : IDefinitionDesigner
{
    public Task<(bool Success, string? Error)> SetValueAsync(string reference, string pointer, string valueJson, string actor)
        => EditAsync(reference, actor, "Designer: set " + pointer, root =>
        {
            var value = ParseJson(valueJson, out var error);
            if (error != null) return error;
            var (parent, last) = Locate(root, pointer, createMissing: true, out var locateError);
            if (locateError != null) return locateError;
            switch (parent)
            {
                case JsonObject o: o[last!] = value; return null;
                case JsonArray a when int.TryParse(last, out var i) && i >= 0 && i < a.Count: a[i] = value; return null;
                default: return $"The pointer '{pointer}' does not point at a place that can be set.";
            }
        });

    public Task<(bool Success, string? Error)> AddItemAsync(string reference, string arrayPointer, string itemJson, string actor)
        => EditAsync(reference, actor, "Designer: add to " + arrayPointer, root =>
        {
            var item = ParseJson(itemJson, out var error);
            if (error != null) return error;
            var array = EnsureArray(root, arrayPointer, out var arrayError);
            if (arrayError != null) return arrayError;
            array!.Add(item);
            return null;
        });

    public Task<(bool Success, string? Error)> RemoveItemAsync(string reference, string pointer, string actor)
        => EditAsync(reference, actor, "Designer: remove " + pointer, root =>
        {
            var (parent, last) = Locate(root, pointer, createMissing: false, out var error);
            if (error != null) return error;
            switch (parent)
            {
                case JsonObject o when o.ContainsKey(last!): o.Remove(last!); return null;
                case JsonArray a when int.TryParse(last, out var i) && i >= 0 && i < a.Count: a.RemoveAt(i); return null;
                default: return $"Nothing at '{pointer}' to remove.";
            }
        });

    public Task<(bool Success, string? Error)> MoveItemAsync(string reference, string arrayPointer, int from, int to, string actor)
        => EditAsync(reference, actor, $"Designer: move {from} to {to} in {arrayPointer}", root =>
        {
            var array = EnsureArray(root, arrayPointer, out var error, createMissing: false);
            if (error != null) return error;
            if (from < 0 || from >= array!.Count || to < 0 || to >= array.Count) return "The positions are outside the list.";
            var node = array[from];
            array.RemoveAt(from);
            array.Insert(to, node);
            return null;
        });

    public Task<(bool Success, string? Error)> RemoveRowAsync(string rowId, string actor)
    {
        var (reference, pointer, index, error) = SplitRow(rowId);
        return error != null ? Task.FromResult((false, (string?)error)) : RemoveItemAsync(reference, pointer + "/" + index, actor);
    }

    public Task<(bool Success, string? Error)> MoveRowAsync(string rowId, string direction, string actor)
    {
        var (reference, pointer, index, error) = SplitRow(rowId);
        if (error != null) return Task.FromResult((false, (string?)error));
        if (!int.TryParse(index, out var from)) return Task.FromResult((false, (string?)"Only rows of a list can be moved."));
        var to = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? from - 1 : from + 1;
        return MoveItemAsync(reference, pointer, from, to, actor);
    }

    private static (string Reference, string Pointer, string Index, string? Error) SplitRow(string rowId)
    {
        var parts = (rowId ?? "").Split('|', 3);
        return parts.Length == 3 && parts[1].StartsWith('/') ? (parts[0], parts[1], parts[2], null) : ("", "", "", "The row is not one of a designer list.");
    }

    public Task<(bool Success, string? Error)> AddFormFieldAsync(string reference, string name, string kind, string titleEn, string titleFa, string required, string actor)
    {
        var field = new JsonObject
        {
            ["name"] = name.Trim(), ["kind"] = string.IsNullOrWhiteSpace(kind) ? "text" : kind.Trim(),
            ["label"] = new JsonObject { ["en"] = titleEn.Trim(), ["fa"] = titleFa.Trim() }
        };
        if (bool.TryParse(required, out var r) && r) field["required"] = true;
        return AddItemAsync(reference, "/fields", field.ToJsonString(), actor);
    }

    public Task<(bool Success, string? Error)> AddPageInstanceAsync(string reference, string region, string id, string component, string? optionsJson, string actor)
    {
        var options = string.IsNullOrWhiteSpace(optionsJson) ? "{}" : optionsJson;
        var instance = new JsonObject { ["id"] = id.Trim(), ["component"] = component.Trim() };
        var parsed = ParseJson(options, out var error);
        if (error != null) return Task.FromResult((false, (string?)error));
        instance["options"] = parsed;
        return AddItemAsync(reference, "/regions/" + Escape(region.Trim()), instance.ToJsonString(), actor);
    }

    public Task<(bool Success, string? Error)> AddMenuItemAsync(string reference, string itemKey, string? parent, string titleEn, string titleFa, string page, int order, string actor)
    {
        var item = new JsonObject
        {
            ["key"] = itemKey.Trim(), ["kind"] = "link", ["order"] = order, ["page"] = page.Trim(),
            ["title"] = new JsonObject { ["en"] = titleEn.Trim(), ["fa"] = titleFa.Trim() }
        };
        if (!string.IsNullOrWhiteSpace(parent)) item["parent"] = parent.Trim();
        return AddItemAsync(reference, "/items", item.ToJsonString(), actor);
    }

    public async Task<(bool Success, string? Error)> CreateActionAsync(string key, string? appKey, string model, string action, string? argsJson, string successEn, string successFa, string? redirect, string actor)
    {
        var args = ParseJson(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson, out var error);
        if (error != null || args is not JsonObject) return (false, error ?? "The arguments are a JSON object of field:name, literal:text or context:name sources.");
        var payload = new JsonObject
        {
            ["kind"] = "handler", ["handler"] = "operation.invoke",
            ["parameters"] = new JsonObject { ["model"] = model.Trim(), ["action"] = action.Trim(), ["args"] = args },
            ["success"] = new JsonObject { ["en"] = successEn.Trim(), ["fa"] = successFa.Trim() }
        };
        if (!string.IsNullOrWhiteSpace(redirect)) payload["redirect"] = redirect.Trim();
        return await workbench.CreateAsync("action", key.Trim(), appKey, $"{model} {action}", $"{model} {action}", payload.ToJsonString(), "Designer: action", actor);
    }

    // ------------------------------------------------------------------ SCRUM-810, 811, 812, 813: typed editors

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Sets a property of an object from text: null leaves it, <c>-</c> removes it, otherwise the text is stored as the type asks (text, whole number, true or false).</summary>
    private static string? SetProperty(JsonObject target, string name, string? text, string type = "string")
    {
        var value = Clean(text);
        if (value is null) return null;
        if (value == "-") { target.Remove(name); return null; }
        switch (type)
        {
            case "int":
                if (!int.TryParse(value, out var n) || n < 0) return $"{name} is a whole number.";
                target[name] = n;
                return null;
            case "bool":
                if (!bool.TryParse(value, out var b)) return $"{name} is true or false.";
                target[name] = b;
                return null;
            default:
                target[name] = value;
                return null;
        }
    }

    private static void SetText(JsonObject target, string name, string? en, string? fa)
    {
        var e = Clean(en); var f = Clean(fa);
        if (e is null && f is null) return;
        var map = target[name] as JsonObject ?? new JsonObject();
        if (e is not null) map["en"] = e;
        if (f is not null) map["fa"] = f;
        target[name] = map;
    }

    public Task<(bool Success, string? Error)> EditFormFieldAsync(string reference, string name, string? titleEn, string? titleFa, string? kind, string? required, string? visible, string? requiredWhen, string? readOnlyWhen,
        string? minLength, string? maxLength, string? pattern, string? span, string? optionsFrom, string? helpEn, string? helpFa, string actor)
        => EditAsync(reference, actor, "Designer: edit field " + name, root =>
        {
            if (root["fields"] is not JsonArray fields) return "The form has no fields.";
            var field = fields.OfType<JsonObject>().FirstOrDefault(f => string.Equals((string?)f["name"], name?.Trim(), StringComparison.Ordinal));
            if (field is null) return $"The form has no field '{name}'.";
            SetText(field, "label", titleEn, titleFa);
            SetText(field, "helpText", helpEn, helpFa);
            foreach (var problem in new[]
            {
                SetProperty(field, "kind", kind), SetProperty(field, "required", required, "bool"), SetProperty(field, "visible", visible), SetProperty(field, "requiredWhen", requiredWhen),
                SetProperty(field, "readOnlyWhen", readOnlyWhen), SetProperty(field, "minLength", minLength, "int"), SetProperty(field, "maxLength", maxLength, "int"), SetProperty(field, "pattern", pattern),
                SetProperty(field, "span", span, "int"), SetProperty(field, "optionsFrom", optionsFrom),
            })
                if (problem != null) return problem;
            return null;
        });

    public Task<(bool Success, string? Error)> SetFormLayoutAsync(string reference, string? columns, string? warnOnLeave, string? submitEn, string? submitFa, string actor)
        => EditAsync(reference, actor, "Designer: form layout", root =>
        {
            if (root is not JsonObject form) return "The form is not an object.";
            if (Clean(columns) is { } c && c != "-" && c is not ("1" or "2")) return "columns is 1 or 2.";
            var problem = SetProperty(form, "columns", columns, "int") ?? SetProperty(form, "warnOnLeave", warnOnLeave, "bool");
            if (problem != null) return problem;
            SetText(form, "submitLabel", submitEn, submitFa);
            return null;
        });

    public Task<(bool Success, string? Error)> AddFormRuleAsync(string reference, string expression, string? field, string messageEn, string messageFa, string actor)
        => EditAsync(reference, actor, "Designer: form rule", root =>
        {
            if (root is not JsonObject form) return "The form is not an object.";
            if (string.IsNullOrWhiteSpace(expression)) return "A rule needs an expression.";
            if (string.IsNullOrWhiteSpace(messageEn) || string.IsNullOrWhiteSpace(messageFa)) return "A rule needs its message in both languages.";
            var rule = new JsonObject { ["expression"] = expression.Trim(), ["message"] = new JsonObject { ["en"] = messageEn.Trim(), ["fa"] = messageFa.Trim() } };
            if (Clean(field) is { } f) rule["field"] = f;
            var rules = form["rules"] as JsonArray ?? new JsonArray();
            rules.Add(rule);
            form["rules"] = rules;
            return null;
        });

    private static JsonObject? ActionParameters(JsonNode root, out string? error)
    {
        error = null;
        if (root is JsonObject o && o["parameters"] is JsonObject p) return p;
        error = "The action has no parameters (only an operation action has a guard).";
        return null;
    }

    public Task<(bool Success, string? Error)> SetActionGuardAsync(string reference, string resource, string action, string? resourceId, string? mode, string actor)
        => EditAsync(reference, actor, "Designer: action guard", root =>
        {
            var parameters = ActionParameters(root, out var error);
            if (parameters is null) return error;
            if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(action)) return "A guard names the resource and the action of the access decision.";
            if (Clean(mode) is { } m && m is not ("sod" or "strict")) return "mode is sod or strict.";
            var guard = new JsonObject { ["resource"] = resource.Trim(), ["action"] = action.Trim() };
            if (Clean(resourceId) is { } r) guard["resourceId"] = r;
            if (Clean(mode) is { } md) guard["mode"] = md;
            parameters["guard"] = guard;
            return null;
        });

    public Task<(bool Success, string? Error)> SetSectionGuardAsync(string reference, string section, string action, string? workOrderId, string? reason, string? recordRef, string actor)
        => EditAsync(reference, actor, "Designer: section guard", root =>
        {
            var parameters = ActionParameters(root, out var error);
            if (parameters is null) return error;
            if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(action)) return "A section guard names the section and the action.";
            var guard = new JsonObject { ["section"] = section.Trim(), ["action"] = action.Trim() };
            if (Clean(workOrderId) is { } w) guard["workOrderId"] = w;
            if (Clean(reason) is { } rs) guard["reason"] = rs;
            if (Clean(recordRef) is { } rr) guard["recordRef"] = rr;
            parameters["sectionGuard"] = guard;
            return null;
        });

    public Task<(bool Success, string? Error)> SetEntityGridAsync(string reference, string entity, string? fields, string? searchFields, string? sort, string? whereJson, string? pageSize, string actor)
        => EditAsync(reference, actor, "Designer: entity grid", root =>
        {
            if (root is not JsonObject source || source["parameters"] is not JsonObject parameters) return "The data source has no parameters.";
            if (string.IsNullOrWhiteSpace(entity)) return "Say which entity the list reads.";
            parameters["entity"] = entity.Trim();
            static JsonArray Names(string text) => new(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
            if (Clean(fields) is { } f) parameters["fields"] = Names(f);
            if (Clean(searchFields) is { } sf) parameters["search"] = Names(sf);
            if (Clean(sort) is { } so) parameters["sort"] = Names(so);
            if (Clean(whereJson) is { } wj)
            {
                var where = ParseJson(wj, out var error);
                if (error != null || where is not JsonObject) return "The fixed row scope is a JSON object of property to value, for example { \"IsActive\": true }.";
                parameters["where"] = where;
            }
            if (Clean(pageSize) is { } ps)
            {
                if (!int.TryParse(ps, out var n) || n is < 1 or > 500) return "The page size is a number from 1 to 500.";
                parameters["maxRows"] = n;
            }
            return null;
        });

    public Task<(bool Success, string? Error)> MoveMenuItemAsync(string reference, string itemKey, string? parent, int order, string actor)
        => EditAsync(reference, actor, "Designer: move menu item " + itemKey, root =>
        {
            if (root["items"] is not JsonArray items) return "The menu has no items.";
            var item = items.OfType<JsonObject>().FirstOrDefault(i => string.Equals((string?)i["key"], itemKey?.Trim(), StringComparison.Ordinal));
            if (item is null) return $"The menu has no item '{itemKey}'.";
            var newParent = Clean(parent);
            if (newParent is not null)
            {
                if (string.Equals(newParent, itemKey.Trim(), StringComparison.Ordinal)) return "An item cannot be its own parent.";
                var target = items.OfType<JsonObject>().FirstOrDefault(i => string.Equals((string?)i["key"], newParent, StringComparison.Ordinal));
                if (target is null) return $"The menu has no item '{newParent}' to move under.";
                // the new parent may not be a descendant of the item (a loop)
                for (var cursor = target; cursor is not null;)
                {
                    var up = (string?)cursor["parent"];
                    if (string.Equals(up, itemKey.Trim(), StringComparison.Ordinal)) return "An item cannot be moved under its own descendant.";
                    cursor = up is null ? null : items.OfType<JsonObject>().FirstOrDefault(i => string.Equals((string?)i["key"], up, StringComparison.Ordinal));
                }
                item["parent"] = newParent;
            }
            else item.Remove("parent");
            item["order"] = order;
            return null;
        });

    // ------------------------------------------------------------------ SCRUM-809: component instances by id

    private static (JsonArray? List, JsonObject? Instance) FindInstance(JsonNode root, string instanceId)
    {
        if (root["regions"] is not JsonObject regions) return (null, null);
        foreach (var (_, node) in regions)
            if (node is JsonArray list)
                foreach (var instance in list.OfType<JsonObject>())
                    if (string.Equals((string?)instance["id"], instanceId?.Trim(), StringComparison.Ordinal)) return (list, instance);
        return (null, null);
    }

    public Task<(bool Success, string? Error)> SetInstanceOptionAsync(string reference, string instanceId, string option, string? valueJson, string actor)
        => EditAsync(reference, actor, $"Designer: option {option} of {instanceId}", root =>
        {
            var (_, instance) = FindInstance(root, instanceId);
            if (instance is null) return $"The page has no component with the id '{instanceId}'.";
            if (string.IsNullOrWhiteSpace(option) || option.Contains('/')) return "The option is the name of one option of the component.";
            var options = instance["options"] as JsonObject ?? new JsonObject();
            if (string.IsNullOrWhiteSpace(valueJson)) options.Remove(option.Trim());
            else
            {
                var value = ParseJson(valueJson, out var error);
                if (error != null) return "The value is JSON: text in quotes, a number, true or false, or an object.";
                options[option.Trim()] = value;
            }
            instance["options"] = options;
            return null;
        });

    public Task<(bool Success, string? Error)> MoveInstanceAsync(string reference, string instanceId, string region, int position, string actor)
        => EditAsync(reference, actor, $"Designer: move {instanceId} to {region}", root =>
        {
            var (list, instance) = FindInstance(root, instanceId);
            if (list is null || instance is null) return $"The page has no component with the id '{instanceId}'.";
            if (string.IsNullOrWhiteSpace(region)) return "Say which region to move the component to.";
            if (root["regions"] is not JsonObject regions) return "The page has no regions.";
            var target = regions[region.Trim()] as JsonArray;
            if (target is null) { target = new JsonArray(); regions[region.Trim()] = target; }
            list.Remove(instance);
            target.Insert(Math.Clamp(position, 0, target.Count), instance);
            return null;
        });

    // ------------------------------------------------------------------ the draft edit

    private async Task<(bool Success, string? Error)> EditAsync(string reference, string actor, string summary, Func<JsonNode, string?> change)
    {
        var parts = (reference ?? "").Split(':', 3);
        if (parts.Length != 3) return (false, "The reference is appKey:kind:key.");
        var page = await definitions.ListAsync(new DefinitionListQuery(parts[1], parts[0], parts[2], null, [], 1, 20));
        var id = page.Items.FirstOrDefault(d => d.Kind == parts[1] && d.Key == parts[2] && d.AppKey == parts[0])?.Id;
        if (id is null) return (false, $"The definition {parts[1]}/{parts[2]} does not exist.");

        var draft = await definitions.GetDraftAsync(id.Value);
        if (!draft.Success)
        {
            var detail = await definitions.GetAsync(id.Value);
            if (!detail.Success) return Answer(detail);
            var opened = await definitions.CreateDraftAsync(id.Value, null, detail.Value!.Summary.Revision, actor);
            if (!opened.Success) return Answer(opened);
            draft = opened;
        }

        JsonNode? root;
        try { root = JsonNode.Parse(draft.Value!.PayloadJson); }
        catch (JsonException) { return (false, "The draft is not valid JSON."); }
        if (root is null) return (false, "The draft is empty.");
        var problem = change(root);
        if (problem != null) return (false, problem);
        return Answer(await definitions.SaveDraftAsync(id.Value, root.ToJsonString(), summary, draft.Value.Info.Revision, actor));
    }

    // ------------------------------------------------------------------ JSON Pointer helpers

    private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");
    private static string Unescape(string segment) => segment.Replace("~1", "/").Replace("~0", "~");

    private static JsonNode? ParseJson(string json, out string? error)
    {
        error = null;
        try { return JsonNode.Parse(json); }
        catch (JsonException) { error = "The value is not valid JSON."; return null; }
    }

    /// <summary>The container that holds the last segment of a pointer, and that segment. With <paramref name="createMissing"/> missing objects on the way are created.</summary>
    private static (JsonNode? Parent, string? Last) Locate(JsonNode root, string pointer, bool createMissing, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/') { error = "A pointer starts with '/' (for example /fields/0/label)."; return (null, null); }
        var segments = pointer[1..].Split('/').Select(Unescape).ToArray();
        var node = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var next = node switch
            {
                JsonObject o => o[segments[i]],
                JsonArray a when int.TryParse(segments[i], out var idx) && idx >= 0 && idx < a.Count => a[idx],
                _ => null
            };
            if (next is null)
            {
                if (!createMissing || node is not JsonObject obj) { error = $"Nothing at '/{string.Join('/', segments.Take(i + 1).Select(Escape))}'."; return (null, null); }
                next = new JsonObject();
                obj[segments[i]] = next;
            }
            node = next;
        }
        return (node, segments[^1]);
    }

    private static JsonArray? EnsureArray(JsonNode root, string pointer, out string? error, bool createMissing = true)
    {
        var (parent, last) = Locate(root, pointer, createMissing, out error);
        if (error != null) return null;
        JsonNode? target = parent switch
        {
            JsonObject o => o[last!],
            JsonArray a when int.TryParse(last, out var i) && i >= 0 && i < a.Count => a[i],
            _ => null
        };
        if (target is JsonArray existing) return existing;
        if (target is null && createMissing && parent is JsonObject po)
        {
            var created = new JsonArray();
            po[last!] = created;
            return created;
        }
        error = $"'{pointer}' is not a list.";
        return null;
    }

    private static (bool Success, string? Error) Answer<T>(DefinitionOutcome<T> outcome)
    {
        if (outcome.Success) return (true, null);
        var first = outcome.Errors?.FirstOrDefault();
        return (false, first is null ? outcome.Code ?? "The definition could not be saved." : $"{first.Pointer} {first.Message}".Trim());
    }
}
