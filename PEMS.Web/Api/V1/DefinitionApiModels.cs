using System.Text.Json;
using PEMS.Application.Definitions;

namespace PEMS.Web.Api.V1;

public sealed record DefinitionCreateInput(string? Kind, string? Key, string? AppKey, JsonElement Title, JsonElement? Description, JsonElement Payload, string? ChangeSummary);

public sealed record DefinitionEnvelopeInput(JsonElement Title, JsonElement? Description);

public sealed record DefinitionDraftInput(JsonElement Payload, string? ChangeSummary);

public sealed record CreateDraftInput(int? FromVersion);

public sealed record RollbackInput(int? ToVersion);

/// <summary>One finding of an impact check: severity safe, warning or blocked, the pointer (into the payload or the missing target) and the message.</summary>
public sealed record ImpactItemResource(string Severity, string Path, string Message);

/// <summary>The verdict of a precheck (SCRUM-785, 789): allowed, warnings or blocked, with the findings.</summary>
public sealed record ImpactReportResource(string Verdict, IReadOnlyList<ImpactItemResource> Items)
{
    public static ImpactReportResource From(ImpactReport r) => new(r.Verdict, r.Items.Select(i => new ImpactItemResource(i.Severity, i.Path, i.Message)).ToList());
}

/// <summary>A published definition that points at this one.</summary>
public sealed record DefinitionUsageResource(string Kind, string Key, string Role);

/// <summary>What this definition points at and whether the target exists and is published.</summary>
public sealed record DefinitionTargetResource(string Kind, string Key, string Role, bool Exists);

public sealed record DefinitionUsedByResource(IReadOnlyList<DefinitionUsageResource> Items);

public sealed record DefinitionReferencesResource(IReadOnlyList<DefinitionTargetResource> Items);

public sealed record DefinitionVersionResource(int Number, string Status, string ContentHash, string? ChangeSummary, int? ClonedFromVersion, DateTime CreatedAt,
    DateTime? ValidatedAt, DateTime? PublishedAt, string? PublishedBy, DateTime? RetiredAt)
{
    public static DefinitionVersionResource From(DefinitionVersionInfo v) =>
        new(v.Number, v.Status.ToString(), v.ContentHash, v.ChangeSummary, v.ClonedFromNumber, Utc(v.CreatedAtUtc), Utc(v.ValidatedAtUtc), Utc(v.PublishedAtUtc), v.PublishedBy, Utc(v.RetiredAtUtc));

    internal static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    internal static DateTime? Utc(DateTime? d) => d is { } x ? Utc(x) : null;
}

public sealed record DefinitionResource(int Id, string Kind, string Key, string AppKey, string Origin, JsonElement Title, JsonElement? Description,
    int? PublishedVersion, bool HasDraft, bool IsCustomized, DateTime CreatedAt, IReadOnlyList<DefinitionVersionResource>? Versions, IReadOnlyList<string> AllowedActions)
{
    public static readonly IReadOnlySet<string> FieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "id", "kind", "key", "appKey", "origin", "title", "description", "publishedVersion", "hasDraft", "isCustomized", "createdAt", "versions", "allowedActions" };

    public static DefinitionResource From(DefinitionSummary s, IReadOnlyList<DefinitionVersionInfo>? versions, IReadOnlyList<string> allowed) =>
        new(s.Id, s.Kind, s.Key, s.AppKey, s.Origin.ToString(), Parse(s.TitleJson)!.Value, s.DescriptionJson == null ? null : Parse(s.DescriptionJson), s.PublishedVersion,
            s.HasDraft, s.IsCustomized, DefinitionVersionResource.Utc(s.CreatedAtUtc), versions?.Select(DefinitionVersionResource.From).ToList(), allowed);

    internal static JsonElement? Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

/// <summary>A version with its payload: the draft or a historical version.</summary>
public sealed record DefinitionVersionPayloadResource(DefinitionVersionResource Version, JsonElement Payload)
{
    public static DefinitionVersionPayloadResource From(DefinitionVersionPayload p) =>
        new(DefinitionVersionResource.From(p.Info), JsonDocument.Parse(p.PayloadJson).RootElement.Clone());
}

/// <summary>One difference between the customer's published payload and the shipped one (JSON Pointer, what happened, both values as JSON text).</summary>
public sealed record DefinitionDiffEntryResource(string Pointer, string Change, string? Published, string? Shipped);

/// <summary>What a product update did to a customised definition (SCRUM-448).</summary>
public sealed record DefinitionUpstreamResource(bool Customized, bool UpstreamChanged, int? ShippedVersion, int? BasedOnShippedVersion, int? PublishedVersion, IReadOnlyList<DefinitionDiffEntryResource> Diff, bool Truncated)
{
    public static DefinitionUpstreamResource From(DefinitionUpstream u) =>
        new(u.Customized, u.UpstreamChanged, u.ShippedVersion, u.BasedOnShippedVersion, u.PublishedVersion, u.Diff.Select(d => new DefinitionDiffEntryResource(d.Pointer, d.Change, d.Published, d.Shipped)).ToList(), u.Truncated);
}

public sealed record DefinitionPackageResource(string Format, DateTime ExportedAt, IReadOnlyList<DefinitionPackageItemResource> Items)
{
    /// <summary>The package of the application layer (the same mapping the REST import and the import action use); missing parts become empty text.</summary>
    public PEMS.Application.Definitions.DefinitionPackage ToPackage()
    {
        var items = Items?.Select(i => new PEMS.Application.Definitions.DefinitionPackageItem(i.Kind ?? "", i.Key ?? "", i.AppKey ?? "core", i.Title.ValueKind == JsonValueKind.Undefined ? "" : i.Title.GetRawText(),
            i.Description is { ValueKind: not JsonValueKind.Null } d ? d.GetRawText() : null, i.Payload.ValueKind == JsonValueKind.Undefined ? "" : i.Payload.GetRawText(), i.ContentHash ?? "", i.VersionNumber)).ToList()
            ?? new List<PEMS.Application.Definitions.DefinitionPackageItem>();
        return new PEMS.Application.Definitions.DefinitionPackage(Format ?? "", ExportedAt, items);
    }
}

public sealed record DefinitionPackageItemResource(string Kind, string Key, string AppKey, JsonElement Title, JsonElement? Description, JsonElement Payload, string ContentHash, int VersionNumber);

public sealed record DefinitionImportResource(int Created, int Updated, IReadOnlyList<int> Ids);
