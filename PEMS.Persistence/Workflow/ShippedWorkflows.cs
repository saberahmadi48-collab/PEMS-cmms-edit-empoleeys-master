using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Workflow;

// SCRUM-517: shipped workflow content. A workflow the product ships is a document (stages with stable keys, transitions between those keys), not a C# seeder. The loader never touches what the customer runs:
// a workflow that does not exist is created and published; one that exists keeps its versions (the customer's published version stays active, running instances finish on the version they started with, versions are
// immutable); when the shipped document changed, the new structure is stored as a Draft version marked "upstream-changed" with a diff against the active version, for an administrator to review and publish.

public sealed class ShippedStage
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public int Order { get; set; }
    public string StageType { get; set; } = "Sequential";
    public string? ResponsibleRole { get; set; }
    public string ApprovalStrategy { get; set; } = "AllApprovalsRequired";
    public int? MinimumApprovalCount { get; set; }
    public bool IsMandatory { get; set; } = true;
    public bool IsSkippable { get; set; }
    public bool IsDelegatable { get; set; } = true;
    public bool IsEscalatable { get; set; }
    public int? TimeoutHours { get; set; }
    public string? EscalationRole { get; set; }
    public string? NotificationKey { get; set; }
    public string? Description { get; set; }
    public string? BusinessStatus { get; set; }
    public string? ResponsibilityKey { get; set; }
    public string? EntryCondition { get; set; }
}

public sealed class ShippedTransition
{
    /// <summary>Key of the stage it leaves; null = the start.</summary>
    public string? From { get; set; }
    /// <summary>Key of the stage it enters; null = the end.</summary>
    public string? To { get; set; }
    public string Action { get; set; } = "";
    public string? Condition { get; set; }
    public int Priority { get; set; }
    public string? Permission { get; set; }
    public bool ForbidInitiator { get; set; }
    public string? Label { get; set; }
    public string? LabelFa { get; set; }
}

public sealed class ShippedWorkflow
{
    public string Module { get; set; } = "";
    public string Context { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<ShippedStage> Stages { get; set; } = [];
    public List<ShippedTransition> Transitions { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Reads a shipped workflow document; the problems found are returned, an invalid document is never shipped.</summary>
    public static (ShippedWorkflow? Workflow, IReadOnlyList<string> Problems) Parse(string json)
    {
        ShippedWorkflow? w;
        try { w = JsonSerializer.Deserialize<ShippedWorkflow>(json, Options); }
        catch (JsonException e) { return (null, [$"The document is not valid JSON: {e.Message}"]); }
        if (w is null) return (null, ["The document is empty."]);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(w.Module)) problems.Add("module is required.");
        if (string.IsNullOrWhiteSpace(w.Context)) problems.Add("context is required.");
        if (string.IsNullOrWhiteSpace(w.Name)) problems.Add("name is required.");
        if (w.Stages.Count == 0) problems.Add("A workflow has at least one stage.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in w.Stages)
        {
            if (string.IsNullOrWhiteSpace(s.Key) || string.IsNullOrWhiteSpace(s.Name)) problems.Add("Every stage has a key and a name.");
            else if (!keys.Add(s.Key)) problems.Add($"The stage key '{s.Key}' is used twice.");
        }
        foreach (var t in w.Transitions)
        {
            if (string.IsNullOrWhiteSpace(t.Action)) problems.Add("Every transition has an action.");
            if (t.From is not null && !keys.Contains(t.From)) problems.Add($"A transition leaves the unknown stage '{t.From}'.");
            if (t.To is not null && !keys.Contains(t.To)) problems.Add($"A transition enters the unknown stage '{t.To}'.");
        }
        return (problems.Count == 0 ? w : null, problems);
    }

    /// <summary>A short fingerprint of the content: the same document always gives the same hash, any change gives another.</summary>
    public string Hash()
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Options))))[..16].ToLowerInvariant();

    [JsonIgnore]
    public string Marker => "shipped:" + Hash();
}

public enum ShipOutcome
{
    /// <summary>The workflow did not exist: created, validated and published as version 1.</summary>
    Created,
    /// <summary>The shipped content was already stored (published or as an upstream draft): nothing done.</summary>
    Unchanged,
    /// <summary>The shipped content changed: stored as a draft version for review, the active version untouched.</summary>
    UpstreamStored,
    /// <summary>The shipped content changed but an administrator has a draft open: nothing stored, ship again after it is published or discarded.</summary>
    DraftOpen,
}

public sealed record ShipResult(ShipOutcome Outcome, IReadOnlyList<string> Changes, int? VersionId = null);

public sealed class WorkflowShipper(PEMSDbContext db, IWorkflowDefinitionService defs)
{
    public const string UpstreamNote = " upstream-changed";

    public async Task<ShipResult> ShipAsync(ShippedWorkflow shipped, WorkflowActor actor, CancellationToken ct = default)
    {
        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Module == shipped.Module && d.Category == shipped.Context && d.Name == shipped.Name, ct);
        if (definition is null)
        {
            definition = await defs.CreateDefinitionAsync(shipped.Name, shipped.Context, shipped.Module, shipped.Description, actor, ct);
            var first = await defs.CreateDraftVersionAsync(definition.WorkflowDefinitionId, actor, shipped.Marker, ct);
            await FillAsync(first.WorkflowVersionId, shipped, actor, ct);
            var result = await defs.ValidateVersionAsync(first.WorkflowVersionId, actor, ct);
            if (!result.IsValid) throw new InvalidOperationException($"The shipped workflow '{shipped.Name}' is not valid: " + string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
            await defs.PublishVersionAsync(first.WorkflowVersionId, actor, null, ct);
            return new ShipResult(ShipOutcome.Created, [], first.WorkflowVersionId);
        }

        var versions = await db.WorkflowVersions.Where(v => v.WorkflowDefinitionId == definition.WorkflowDefinitionId).ToListAsync(ct);
        if (versions.Any(v => v.ChangeSummary != null && v.ChangeSummary.StartsWith(shipped.Marker, StringComparison.Ordinal))) return new ShipResult(ShipOutcome.Unchanged, []);
        if (versions.Any(v => v.Status is "Draft" or "Validated")) return new ShipResult(ShipOutcome.DraftOpen, []);

        var active = versions.Where(v => v.Status == "Published").OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        IReadOnlyList<string> changes = active is null ? [] : Diff(shipped, await StructureOfAsync(active.WorkflowVersionId, ct));
        var draft = await defs.CreateDraftVersionAsync(definition.WorkflowDefinitionId, actor, shipped.Marker + UpstreamNote, ct);
        await FillAsync(draft.WorkflowVersionId, shipped, actor, ct);
        return new ShipResult(ShipOutcome.UpstreamStored, changes, draft.WorkflowVersionId);
    }

    private async Task FillAsync(int versionId, ShippedWorkflow shipped, WorkflowActor actor, CancellationToken ct)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in shipped.Stages.OrderBy(s => s.Order))
        {
            var stage = await defs.AddStageAsync(versionId, new WorkflowStageInput
            {
                Name = s.Name, NameFa = s.NameFa, StageOrder = s.Order, StageType = s.StageType, ResponsibleRole = s.ResponsibleRole, ApprovalStrategy = s.ApprovalStrategy,
                MinimumApprovalCount = s.MinimumApprovalCount, IsMandatory = s.IsMandatory, IsSkippable = s.IsSkippable, IsDelegatable = s.IsDelegatable, IsEscalatable = s.IsEscalatable,
                TimeoutHours = s.TimeoutHours, EscalationRole = s.EscalationRole, NotificationKey = s.NotificationKey, Description = s.Description, BusinessStatus = s.BusinessStatus,
                ResponsibilityKey = s.ResponsibilityKey, EntryCondition = s.EntryCondition,
            }, actor, ct);
            ids[s.Key] = stage.WorkflowStageId;
        }
        foreach (var t in shipped.Transitions)
            await defs.AddTransitionAsync(versionId, new WorkflowTransitionInput
            {
                FromStageId = t.From is null ? null : ids[t.From], ToStageId = t.To is null ? null : ids[t.To], ActionType = t.Action, ConditionExpression = t.Condition, Priority = t.Priority,
                RequiredPermission = t.Permission, ForbidInitiator = t.ForbidInitiator, Label = t.Label, LabelFa = t.LabelFa,
            }, actor, ct);
    }

    private async Task<(List<(string Name, string Type, string? Role, int Order)> Stages, List<string> Transitions)> StructureOfAsync(int versionId, CancellationToken ct)
    {
        var stages = await db.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == versionId).ToListAsync(ct);
        var byId = stages.ToDictionary(s => s.WorkflowStageId, s => s.Name);
        var transitions = await db.WorkflowTransitions.AsNoTracking().Where(t => t.WorkflowVersionId == versionId).ToListAsync(ct);
        return (stages.Select(s => (s.Name, s.StageType, s.ResponsibleRole, s.StageOrder)).ToList(),
            transitions.Select(t => TransitionKey(t.FromStageId is { } f ? byId[f] : null, t.ToStageId is { } to ? byId[to] : null, t.ActionType)).ToList());
    }

    private static string TransitionKey(string? from, string? to, string action) => $"{from ?? "(start)"} -> {to ?? "(end)"} [{action}]";

    /// <summary>What the shipped content changes compared with the active version: stages and transitions added or removed, stages whose type, responsible role or order changed. Pure.</summary>
    public static IReadOnlyList<string> Diff(ShippedWorkflow shipped, (List<(string Name, string Type, string? Role, int Order)> Stages, List<string> Transitions) active)
    {
        var changes = new List<string>();
        var current = active.Stages.ToDictionary(s => s.Name, StringComparer.Ordinal);
        foreach (var s in shipped.Stages)
        {
            if (!current.TryGetValue(s.Name, out var c)) { changes.Add($"stage added: {s.Name}"); continue; }
            if (c.Type != s.StageType) changes.Add($"stage {s.Name}: type {c.Type} -> {s.StageType}");
            if (c.Role != s.ResponsibleRole) changes.Add($"stage {s.Name}: responsible {c.Role ?? "(none)"} -> {s.ResponsibleRole ?? "(none)"}");
            if (c.Order != s.Order) changes.Add($"stage {s.Name}: order {c.Order} -> {s.Order}");
        }
        foreach (var name in current.Keys.Where(n => shipped.Stages.All(s => s.Name != n))) changes.Add($"stage removed: {name}");

        var names = shipped.Stages.ToDictionary(s => s.Key, s => s.Name, StringComparer.Ordinal);
        var next = shipped.Transitions.Select(t => TransitionKey(t.From is null ? null : names[t.From], t.To is null ? null : names[t.To], t.Action)).ToList();
        foreach (var t in next.Except(active.Transitions)) changes.Add($"transition added: {t}");
        foreach (var t in active.Transitions.Except(next)) changes.Add($"transition removed: {t}");
        return changes;
    }
}
