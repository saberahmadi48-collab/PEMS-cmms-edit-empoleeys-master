using PEMS.Application.Numbering;

namespace PEMS.Application.Interfaces;

/// <summary>A scheme as the admin page and the create forms see it.</summary>
public sealed record NumberingSchemeView(
    string DocumentType, string Pattern, int PadWidth, string Calendar, string ResetPeriod, bool IsActive, bool AllowManualEntry,
    int Version, string? RowVersion, string Preview, string? ModifiedBy, DateTime? ModifiedDate);

public sealed record NumberingSchemeInput(
    string DocumentType, string Pattern, int PadWidth, string Calendar, string ResetPeriod, bool IsActive, bool AllowManualEntry, string? RowVersion);

/// <summary>How a create form shows the number field: assigned automatically (with the next number as a hint), or typed.</summary>
public sealed record DocumentNumberField(bool Automatic, bool AllowManualEntry, string? NextNumberHint);

/// <summary>SCRUM-40: administration of numbering schemes (Admin > Numbering).</summary>
public interface INumberingSchemeService
{
    /// <summary>The document types that can be configured (the procurement documents).</summary>
    IReadOnlyList<string> DocumentTypes { get; }

    Task<IReadOnlyList<NumberingSchemeView>> GetAllAsync();

    /// <summary>Resource key of the error, or null when saved. Each save raises Version by one (who and when: BaseEntity stamping and AuditLog).</summary>
    Task<string?> SaveAsync(NumberingSchemeInput input);

    /// <summary>Formats an unsaved pattern for the live preview; null text plus an error key when invalid.</summary>
    (string? Number, string? Error) Preview(NumberingSchemeInput input, NumberingContext context);

    Task<DocumentNumberField> GetFieldAsync(string documentType);
}
