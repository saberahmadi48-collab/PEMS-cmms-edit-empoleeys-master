namespace PEMS.Domain.Entities.Definitions;

public enum DefinitionStatus { Draft = 0, Validated = 1, Published = 2, Retired = 3 }

public enum DefinitionOrigin { System = 0, Customer = 1 }

/// <summary>
/// The one envelope every UI/metadata definition shares (ADR-0010, ADR-0011, SCRUM-434). Rows are only written through the definition store
/// service; <see cref="Revision"/> is the optimistic-concurrency token and the ETag source of the envelope.
/// </summary>
public class Definition
{
    public int DefinitionId { get; set; }
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string AppKey { get; set; } = "core";
    public DefinitionOrigin Origin { get; set; } = DefinitionOrigin.Customer;
    /// <summary>TranslatableText JSON (ADR-0008).</summary>
    public string TitleJson { get; set; } = "{}";
    public string? DescriptionJson { get; set; }
    public int? PublishedVersionId { get; set; }
    public DefinitionVersion? PublishedVersion { get; set; }
    public int? ShippedVersionId { get; set; }
    public bool IsCustomized { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public long Revision { get; set; } = 1;
    public ICollection<DefinitionVersion> Versions { get; set; } = new List<DefinitionVersion>();
}

/// <summary>One version of a definition; Published and Retired versions are immutable.</summary>
public class DefinitionVersion
{
    public int DefinitionVersionId { get; set; }
    public int DefinitionId { get; set; }
    public Definition? Definition { get; set; }
    public int VersionNumber { get; set; }
    public DefinitionStatus Status { get; set; }
    /// <summary>Canonical JSON (sorted property names, no whitespace), validated against the kind.</summary>
    public string PayloadJson { get; set; } = "{}";
    public int PayloadSchemaVersion { get; set; } = 1;
    /// <summary>SHA-256 (hex) of <see cref="PayloadJson"/>: ETag of reads, tamper check of packages, change detection.</summary>
    public string ContentHash { get; set; } = "";
    public string? ChangeSummary { get; set; }
    public int? ClonedFromVersionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
    public string? ValidatedBy { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public long Revision { get; set; } = 1;
    public ICollection<DefinitionReference> References { get; set; } = new List<DefinitionReference>();
}

/// <summary>Dependency edge written at validation time ("what uses this component", impact preview).</summary>
public class DefinitionReference
{
    public int DefinitionReferenceId { get; set; }
    public int FromVersionId { get; set; }
    public DefinitionVersion? FromVersion { get; set; }
    public string ToKind { get; set; } = "";
    public string ToKey { get; set; } = "";
    public string Role { get; set; } = "";
}

/// <summary>One monotonic counter per kind: publishing increments it, every instance compares it to refresh its cache.</summary>
public class DefinitionStamp
{
    public string Kind { get; set; } = "";
    public long Value { get; set; }
}
