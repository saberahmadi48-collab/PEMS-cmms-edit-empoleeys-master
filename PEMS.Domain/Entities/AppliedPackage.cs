using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-807, 814: a signed package (feed, install or update) that was run on this installation, with its hash. A feed whose hash is already recorded as succeeded is not applied again (a refeed changes nothing); the rows are
/// also the history of the migrator center. Every change is in the audit log through the context's SaveChanges.
/// </summary>
public class AppliedPackage
{
    [Key]
    public int AppliedPackageId { get; set; }

    /// <summary>SHA-256 (hex) of the package.</summary>
    [MaxLength(64)]
    public string PackageHash { get; set; } = string.Empty;

    /// <summary>feed, install or update.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The product version the package delivers (install, update); null for a feed.</summary>
    [MaxLength(40)]
    public string? Version { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>What the run did, or why it failed (the first reasons).</summary>
    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>The backup taken before the run (name or path), when there was one; a rollback needs it.</summary>
    [MaxLength(260)]
    public string? BackupPoint { get; set; }

    [MaxLength(200)]
    public string? AppliedBy { get; set; }

    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
