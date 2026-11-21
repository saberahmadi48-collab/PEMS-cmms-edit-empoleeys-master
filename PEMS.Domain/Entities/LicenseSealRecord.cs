using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// The database copy of the install seal (licensing design, section 4.2): a single row holding the signed seal bundle.
/// Deliberately not a <c>BaseEntity</c>: one install has one seal, it is not tenant data and nothing edits it by hand.
/// The protected file holds the second, independent copy.
/// </summary>
public class LicenseSealRecord
{
    /// <summary>Always 1: there is one seal per installation.</summary>
    [Key]
    public int LicenseSealRecordId { get; set; } = 1;

    /// <summary>JSON bundle: signed seal envelope plus the active licence text. Verified by signature, never trusted as plain data.</summary>
    public string Bundle { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Append-only licensing event (activation, start, restore suspicion, clock jump...). Each row chains to the previous
/// one by hash (licensing design, section 4.4). Never updated or deleted; contains no raw serials or personal data.
/// </summary>
public class LicenseEventRecord
{
    [Key]
    public long Seq { get; set; }

    public DateTime UtcAt { get; set; }

    [MaxLength(60)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DataHash { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrevHash { get; set; } = string.Empty;

    [MaxLength(64)]
    public string EntryHash { get; set; } = string.Empty;
}
