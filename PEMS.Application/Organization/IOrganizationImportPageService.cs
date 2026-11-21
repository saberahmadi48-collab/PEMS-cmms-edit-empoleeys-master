namespace PEMS.Application.Organization;

/// <summary>The template workbook (the engine downloads a result with Bytes) or a refusal.</summary>
public sealed record OrgImportTemplateResult(bool Success, string? Error, byte[]? Bytes = null, string? FileName = null, string? ContentType = null);

/// <summary>A step result: Success false = the Error key (with Args) is shown in both languages; Success true with an Error key shows it as the message.</summary>
public sealed record OrgImportStepResult(bool Success, string? Error, object?[]? Args = null);

/// <summary>The counts and warnings of the pending validated file of the user (the confirm form shows them read-only).</summary>
public class OrgImportPendingRowDto
{
    public string Id { get; set; } = "pending";
    public int PositionsCreated { get; set; }
    public int PositionsUpdated { get; set; }
    public int AssignmentsCreated { get; set; }
    public int AssignmentsUpdated { get; set; }
    public int Unchanged { get; set; }
    public string Warnings { get; set; } = string.Empty;
}

/// <summary>SCRUM-640: the Organization Import pages as operations: template, validate (nothing is saved; a clean file is kept in the server cache for the user for 30 minutes), confirm (all or nothing). The rules stay in IOrganizationImportService.</summary>
public interface IOrganizationImportPageService
{
    [PEMS.Application.Common.PemsOperation("OrganizationImport", "Template", Roles = "SystemAdmin")]
    Task<OrgImportTemplateResult> TemplateAsync();

    [PEMS.Application.Common.PemsOperation("OrganizationImport", "Validate", Roles = "SystemAdmin")]
    Task<OrgImportStepResult> ValidateAsync(byte[] file, string? userName);

    [PEMS.Application.Common.PemsOperation("OrganizationImport", "Pending", Roles = "SystemAdmin")]
    Task<List<OrgImportPendingRowDto>> PendingAsync(string? userName);

    [PEMS.Application.Common.PemsOperation("OrganizationImport", "Confirm", Roles = "SystemAdmin")]
    Task<OrgImportStepResult> ConfirmAsync(string? userName);
}
