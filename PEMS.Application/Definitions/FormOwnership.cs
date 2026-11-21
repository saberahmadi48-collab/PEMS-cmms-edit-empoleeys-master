using System.Text.Json.Nodes;

namespace PEMS.Application.Definitions;

// SCRUM-515 (owner decision 2026-11-06): a field of a form has one of two owners. A PLATFORM field (identity, status, audit stamps, the fields workflows and reports rely on) is ours: delivered and changed
// only by product updates, read-only for the customer. An APPLICATION field is the customer's: editable by them, never touched by an update. The customer's data and application fields always win; an update
// that would break that stops before it changes anything and says which form and field. The words are "platform" and "application" ("business" and "system" are not used).

/// <summary>One reason an update cannot be applied to a customised form.</summary>
public sealed record OwnershipConflict(string Field, string Code, string Message);

/// <summary>The customer's form with the new platform fields merged in, or the conflicts that stop the merge (then <see cref="MergedJson"/> is null).</summary>
public sealed record OwnershipMerge(string? MergedJson, IReadOnlyList<OwnershipConflict> Conflicts)
{
    public bool Ok => Conflicts.Count == 0;
}

public static class FormOwnership
{
    public const string PlatformOwner = "platform";

    private static readonly string[] TextLike = ["text", "textarea", "richtext"];

    private static string? Name(JsonNode? field) => (field as JsonObject)?["name"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool IsPlatform(JsonNode? field) => (field as JsonObject)?["owner"] is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s, PlatformOwner, StringComparison.OrdinalIgnoreCase);

    private static string KindOf(JsonNode? field) => ((field as JsonObject)?["kind"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "text").ToLowerInvariant();

    private static JsonArray Fields(string payloadJson) => (JsonNode.Parse(payloadJson) as JsonObject)?["fields"] as JsonArray ?? [];

    /// <summary>
    /// The rule for an edit by the customer: every platform field of the shipped form must still be there, unchanged, and the edit may not mark a field of its own as platform.
    /// Returns definition errors (JSON Pointers into the edited payload); empty = allowed.
    /// </summary>
    public static IReadOnlyList<DefinitionFieldError> Protect(string shippedPayloadJson, string editedPayloadJson)
    {
        var errors = new List<DefinitionFieldError>();
        var shipped = Fields(shippedPayloadJson);
        var edited = Fields(editedPayloadJson);
        var shippedPlatform = shipped.Where(IsPlatform).ToDictionary(f => Name(f) ?? "", f => f, StringComparer.Ordinal);

        foreach (var (name, original) in shippedPlatform)
        {
            var index = IndexOf(edited, name);
            if (index < 0) errors.Add(new("/fields", "platform-field-removed", "Def_Err_PlatformField", $"'{name}' is a platform field: it is delivered by product updates and cannot be removed."));
            else if (!JsonNode.DeepEquals(original, edited[index])) errors.Add(new($"/fields/{index}", "platform-field-changed", "Def_Err_PlatformField", $"'{name}' is a platform field: it is read-only and changed only by product updates."));
        }
        for (var i = 0; i < edited.Count; i++)
            if (IsPlatform(edited[i]) && !shippedPlatform.ContainsKey(Name(edited[i]) ?? ""))
                errors.Add(new($"/fields/{i}/owner", "platform-field-added", "Def_Err_PlatformField", "Only the product can declare a platform field."));
        return errors;
    }

    /// <summary>
    /// The update rule: platform fields of the new shipped form are merged into the customer's published form; application fields (and everything else of the form) stay byte for byte as the customer has them.
    /// Conflicts, all listed at once: a new platform field whose name is already an application field of the customer; a platform field whose kind changes to another family of values.
    /// </summary>
    public static OwnershipMerge Merge(string customerPayloadJson, string newShippedPayloadJson)
    {
        var customer = (JsonObject)JsonNode.Parse(customerPayloadJson)!;
        var customerFields = customer["fields"] as JsonArray ?? [];
        var shippedFields = Fields(newShippedPayloadJson);
        var conflicts = new List<OwnershipConflict>();

        foreach (var shipped in shippedFields.Where(IsPlatform))
        {
            var name = Name(shipped) ?? "";
            var at = IndexOf(customerFields, name);
            if (at < 0) continue;
            if (!IsPlatform(customerFields[at]))
                conflicts.Add(new(name, "name-taken", $"The shipped platform field '{name}' has the same name as an application field of the customer; rename the application field first."));
            else if (KindOf(customerFields[at]) != KindOf(shipped) && !(TextLike.Contains(KindOf(customerFields[at])) && TextLike.Contains(KindOf(shipped))))
                conflicts.Add(new(name, "kind-change", $"The platform field '{name}' changes from {KindOf(customerFields[at])} to {KindOf(shipped)}, which could lose stored values."));
        }
        if (conflicts.Count > 0) return new OwnershipMerge(null, conflicts);

        var merged = new JsonArray();
        var shippedPlatformByName = shippedFields.Where(IsPlatform).ToDictionary(f => Name(f) ?? "", f => f, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in customerFields)
        {
            var name = Name(field) ?? "";
            if (IsPlatform(field))
            {
                if (shippedPlatformByName.TryGetValue(name, out var updated)) { merged.Add(updated.DeepClone()); used.Add(name); }
                // a platform field the product no longer ships is removed: it was ours
            }
            else merged.Add(field!.DeepClone());
        }
        foreach (var (name, field) in shippedPlatformByName)
            if (!used.Contains(name)) merged.Add(field.DeepClone());      // a new platform field is appended; the customer's order of their own fields does not change

        customer["fields"] = merged;
        return new OwnershipMerge(customer.ToJsonString(), []);
    }

    private static int IndexOf(JsonArray fields, string name)
    {
        for (var i = 0; i < fields.Count; i++) if (Name(fields[i]) == name) return i;
        return -1;
    }
}
