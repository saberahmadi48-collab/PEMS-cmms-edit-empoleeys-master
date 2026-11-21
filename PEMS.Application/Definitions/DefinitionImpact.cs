using System.Text.Json;
using System.Text.Json.Nodes;

namespace PEMS.Application.Definitions;

/// <summary>One finding of an impact check: <c>Severity</c> is <c>safe</c>, <c>warning</c> or <c>blocked</c>; <c>Path</c> points into the payload or names the missing target.</summary>
public sealed record ImpactItem(string Severity, string Path, string Message);

/// <summary>The verdict of a precheck (SCRUM-450, 457, 459): <c>allowed</c>, <c>warnings</c> (listed, the caller may go on) or <c>blocked</c> (reasons listed, nothing changes).</summary>
public sealed record ImpactReport(string Verdict, IReadOnlyList<ImpactItem> Items)
{
    public bool Blocked => Verdict == "blocked";

    public static ImpactReport From(IReadOnlyList<ImpactItem> items)
        => new(items.Any(i => i.Severity == "blocked") ? "blocked" : items.Any(i => i.Severity == "warning") ? "warnings" : "allowed", items);

    /// <summary>The report as one line per finding, for a message to a person.</summary>
    public string Text() => string.Join(" ", Items.Where(i => i.Severity != "safe").Select(i => $"[{i.Severity}] {i.Path}: {i.Message}"));
}

/// <summary>What a definition points at: the <c>Kind</c> and <c>Key</c> of the target, the <c>Role</c> of the reference and whether the target <c>Exists</c> as a published definition (components of the interface registry and links to code routes are always reported as existing).</summary>
public sealed record DefinitionTarget(string Kind, string Key, string Role, bool Exists);

/// <summary>A published definition that points at another one: <c>Kind</c> and <c>Key</c> of the user, and the <c>Role</c> of the reference.</summary>
public sealed record DefinitionUsage(string Kind, string Key, string Role);

/// <summary>
/// The impact of a change to a definition (SCRUM-450, 457, 459): references must exist and be published, a newer payload must keep what stored data needs, a retired version may be published again only if it passes the same check
/// against today's definitions. Read-only: nothing is changed by asking.
/// </summary>
public interface IDefinitionImpact
{
    /// <summary>Who uses this definition: the published definitions whose published version points at <paramref name="kind"/>/<paramref name="key"/>.</summary>
    Task<IReadOnlyList<DefinitionUsage>> UsedByAsync(string kind, string key, CancellationToken ct = default);

    /// <summary>What the published version of the definition points at, with whether each target exists and is published (SCRUM-785).</summary>
    Task<IReadOnlyList<DefinitionTarget>> ReferencesAsync(int id, CancellationToken ct = default);

    /// <summary>The check before publishing the open draft: its references and its changes against the published version.</summary>
    Task<ImpactReport> PrecheckPublishAsync(int id, CancellationToken ct = default);

    /// <summary>The check before publishing a retired version again: its references against today's definitions and its changes against the published version.</summary>
    Task<ImpactReport> PrecheckRollbackAsync(int id, int toVersion, CancellationToken ct = default);
}

/// <summary>
/// The compatibility rules between two payloads of one kind (SCRUM-457): a form field that disappears is a data loss (stored values have no place), a field that changes its type or becomes required is breaking, a property of another kind
/// that disappears is breaking, everything else is safe. Pure: JSON in, findings out, so every kind gets the rule without its own code.
/// </summary>
public static class DefinitionCompatibility
{
    public static IReadOnlyList<ImpactItem> Analyze(string kind, string fromJson, string toJson)
    {
        JsonNode? from, to;
        try { from = JsonNode.Parse(fromJson); to = JsonNode.Parse(toJson); }
        catch (JsonException) { return [new ImpactItem("blocked", "/", "A payload is not valid JSON.")]; }
        if (from is not JsonObject f || to is not JsonObject t) return [];
        var items = new List<ImpactItem>();
        if (kind == "form" && f["fields"] is JsonArray ff && t["fields"] is JsonArray tf)
        {
            var before = ByName(ff);
            var after = ByName(tf);
            foreach (var (name, old) in before)
            {
                if (!after.TryGetValue(name, out var now)) { items.Add(new ImpactItem("blocked", $"/fields/{name}", "The field was removed; values stored under it would be lost.")); continue; }
                var oldKind = old["kind"]?.ToString();
                var newKind = now["kind"]?.ToString();
                if (oldKind != newKind) items.Add(new ImpactItem("warning", $"/fields/{name}/kind", $"The type changed from '{oldKind}' to '{newKind}'; stored values may not fit."));
                if (IsTrue(now["required"]) && !IsTrue(old["required"]))
                    items.Add(new ImpactItem("warning", $"/fields/{name}/required", "The field became required; older records may be empty."));
            }
            foreach (var (name, added) in after.Where(a => !before.ContainsKey(a.Key)))
                if (IsTrue(added["required"])) items.Add(new ImpactItem("warning", $"/fields/{name}", "A new required field; existing records have no value."));
        }
        else
        {
            foreach (var (name, _) in f.Where(p => !t.ContainsKey(p.Key)))
                items.Add(new ImpactItem("warning", "/" + name, "The property was removed."));
        }
        return items;
    }

    private static bool IsTrue(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static Dictionary<string, JsonObject> ByName(JsonArray fields)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var node in fields)
            if (node is JsonObject o && o["name"]?.ToString() is { Length: > 0 } name) map[name] = o;
        return map;
    }
}

/// <summary>
/// The two-person publish rule (SCRUM-808, guideline 04 policy table): a protected definition is published by a person other than the one who made the draft, so a critical page never changes by one person alone.
/// Protected definitions are the setting <c>Definitions:ProtectedDefinitions</c>: a comma separated list of <c>kind:key</c> or <c>kind:*</c> (every definition of a kind). The rule is enforced by the REST publish and by the
/// workbench alike; the permission to publish (Definitions.Approve) is still needed. Pure.
/// </summary>
public static class TwoPersonPublish
{
    public static bool IsProtected(string? setting, string kind, string key)
        => (setting ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, kind + ":" + key, StringComparison.OrdinalIgnoreCase) || string.Equals(e, kind + ":*", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the publish must wait for another person: the definition is protected and the publisher made the draft.</summary>
    public static bool NeedsSecondApprover(string? setting, string kind, string key, string? draftCreatedBy, string publisher)
        => IsProtected(setting, kind, key) && !string.IsNullOrWhiteSpace(draftCreatedBy) && string.Equals(draftCreatedBy, publisher, StringComparison.OrdinalIgnoreCase);

    public const string Message = "This definition is protected: another person with the permission to publish must publish it, not the person who made the draft.";
}
