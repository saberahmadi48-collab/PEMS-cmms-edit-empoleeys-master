namespace PEMS.Application.Interfaces;

public class NumberingSchemeFormRowDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public int PadWidth { get; set; }
    public string Calendar { get; set; } = string.Empty;
    public string ResetPeriod { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool AllowManualEntry { get; set; }
}

public class NumberingPreviewRowDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public int PadWidth { get; set; }
    public string Calendar { get; set; } = string.Empty;
    public string ResetPeriod { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? Error { get; set; }
}

public class VdrNumberSearchRowDto
{
    public int VDRItemId { get; set; }
    public string Search { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string? DocumentDescription { get; set; }
    public string? VendorName { get; set; }
    public string? VendorFormatDocumentNumber { get; set; }
}

public sealed record NumberingActionResult(bool Success, string? Error);

/// <summary>SCRUM-638: the Admin document numbering page as operations: the schemes, one save, the live preview, and the VDR number forms (the two numbers of a VDR item). The rules stay in INumberingSchemeService and IVdrDocumentNumberingService.</summary>
public interface INumberingPageService
{
    [PEMS.Application.Common.PemsOperation("Numbering", "Schemes", Roles = "SystemAdmin")]
    Task<List<NumberingSchemeView>> SchemesAsync();

    [PEMS.Application.Common.PemsOperation("Numbering", "SchemeOf", Roles = "SystemAdmin")]
    Task<List<NumberingSchemeFormRowDto>> SchemeOfAsync(string documentType);

    /// <summary>Success carries the key Num_Saved (the message); a refusal carries the key of the service.</summary>
    [PEMS.Application.Common.PemsOperation("Numbering", "Save", Roles = "SystemAdmin")]
    Task<NumberingActionResult> SaveAsync(NumberingSchemeInput input);

    [PEMS.Application.Common.PemsOperation("Numbering", "Preview", Roles = "SystemAdmin")]
    Task<List<NumberingPreviewRowDto>> PreviewAsync(string documentType, string pattern, int padWidth, string calendar, string resetPeriod);

    [PEMS.Application.Common.PemsOperation("Numbering", "VdrSearch", Roles = "SystemAdmin")]
    Task<List<VdrNumberSearchRowDto>> VdrSearchAsync(string search);

    [PEMS.Application.Common.PemsOperation("Numbering", "GenerateContractorNumber", "VDR.Edit")]
    Task<NumberingActionResult> GenerateContractorNumberAsync(int vdrItemId, string? vendorCode, string? plantCode, string? unitCode, string? materialCode);

    [PEMS.Application.Common.PemsOperation("Numbering", "GenerateVendorNumber", "VDR.Edit")]
    Task<NumberingActionResult> GenerateVendorNumberAsync(int vdrItemId, string? jobNumber, string? unitCode, string? typologyCode);
}
