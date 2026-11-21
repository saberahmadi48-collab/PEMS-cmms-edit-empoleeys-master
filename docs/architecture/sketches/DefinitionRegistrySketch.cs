// Compile-only sketch for docs/architecture/METADATA_REGISTRY_DESIGN.md (plan rows #338 and #430).
// Not part of any project: it is compiled in a scratch project to prove the shapes are consistent. Do not copy it into src as is.
namespace PEMS.Registry.Sketch;

public enum DefinitionStatus { Draft = 0, Validated = 1, Published = 2, Retired = 3 }

public enum DefinitionOrigin { System = 0, Customer = 1 }

/// <summary>The one envelope every definition kind shares (entity, field set, page, form, view, menu, theme, data source, action ...).</summary>
public sealed class Definition
{
    public int DefinitionId { get; set; }
    public string Kind { get; set; } = "";            // registered kind key, e.g. "page", "form", "entity"
    public string Key { get; set; } = "";             // unique per (AppKey, Kind), e.g. "work-orders.list"
    public string AppKey { get; set; } = "";          // owning app/module key, "core" for the platform
    public DefinitionOrigin Origin { get; set; }      // System = shipped by migration, Customer = created in this installation
    public string TitleJson { get; set; } = "{}";     // TranslatableText JSON map (ADR-0008)
    public string? DescriptionJson { get; set; }
    public int? PublishedVersionId { get; set; }      // the version consumers read; null = no published version yet
    public int? ShippedVersionId { get; set; }        // the System version this installation received last (for reset and upstream merge)
    public bool IsCustomized { get; set; }            // a System definition whose published version differs from the shipped one
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedById { get; set; }
    public byte[]? RowVersion { get; set; }           // ETag source for the envelope
    public ICollection<DefinitionVersion> Versions { get; set; } = new List<DefinitionVersion>();
}

/// <summary>One immutable-once-published version of a definition. Same lifecycle as WorkflowVersion.</summary>
public sealed class DefinitionVersion
{
    public int DefinitionVersionId { get; set; }
    public int DefinitionId { get; set; }
    public Definition? Definition { get; set; }
    public int VersionNumber { get; set; }
    public DefinitionStatus Status { get; set; }
    public string PayloadJson { get; set; } = "{}";   // kind-specific body, validated against the kind's JSON Schema
    public int PayloadSchemaVersion { get; set; } = 1;
    public string ContentHash { get; set; } = "";     // SHA-256 of the canonical payload: ETag, package signature, change detection
    public string? ChangeSummary { get; set; }
    public int? ClonedFromVersionId { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
    public int? ValidatedById { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int? PublishedById { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }           // optimistic concurrency of the draft
}

/// <summary>Dependency edge written at validation time: lets the planner show "what breaks if this changes".</summary>
public sealed class DefinitionReference
{
    public int DefinitionReferenceId { get; set; }
    public int FromVersionId { get; set; }
    public string ToKind { get; set; } = "";
    public string ToKey { get; set; } = "";
    public string Role { get; set; } = "";            // e.g. "renders", "reads", "calls", "menu-target"
}

/// <summary>One monotonic counter per kind; publish increments it, every instance compares it to refresh its cache.</summary>
public sealed class DefinitionStamp
{
    public string Kind { get; set; } = "";
    public long Value { get; set; }
}
