namespace PEMS.Application.Interfaces;

/// <summary>One barcode identity (recent list, verification head).</summary>
public sealed record BarcodeRow(int BarcodeIdentityId, string Code, string Symbology, string TargetType, int TargetId, string Label, string Status, DateTime CreatedDate, int PrintCount);

/// <summary>The outcome of resolving (or verifying) a scanned code; Mismatch is true when an expected target was given and the code belongs to another.</summary>
public sealed record BarcodeResolutionRow(bool Success, string ErrorCode, string ErrorMessage, string TargetType, int? TargetId, string Label, string Status, bool Mismatch, string TargetUrl);

/// <summary>One equipment a bulk generation may cover, with whether it already has an active barcode.</summary>
public sealed record BarcodeCandidateRow(int EquipmentId, string TagNumber, string Name, string EquipmentType, bool AlreadyBarcoded);

/// <summary>A file an operation produced (an image, a label sheet): served by the file host or downloaded by an action; Success false is a refusal with the Error.</summary>
public sealed record BarcodeFileResult(bool Success, string? Error, byte[] Bytes, string FileName, string ContentType);

/// <summary>
/// The enterprise barcode (SCRUM-671) as operations: the reads and the machine answers of BarcodeController (the SVG, PNG and PDF label of a barcode, the label sheet of several, the resolution as JSON for the scan fields of
/// other pages) are read operations served by the file host (GET /x/{key}); generating, printing (it records the print and downloads the label), bulk generation (it creates the barcodes and downloads the label sheet),
/// disabling, revoking and replacing are commands. The permissions are the old ones (Barcode.View, Create, Approve, Delete).
/// </summary>
public interface IBarcodeOperations
{
    [PEMS.Application.Common.PemsOperation("Barcode", "Recent", "Barcode.View")]
    Task<List<BarcodeRow>> RecentAsync();

    [PEMS.Application.Common.PemsOperation("Barcode", "Resolve", "Barcode.View")]
    Task<List<BarcodeResolutionRow>> ResolveAsync(string code, string? expectedTargetType = null, int? expectedTargetId = null);

    /// <summary>The same resolution as one object (success, targetType, targetId, label) for the scan fields of other pages.</summary>
    [PEMS.Application.Common.PemsOperation("Barcode", "ResolveJson", "Barcode.View")]
    Task<object> ResolveJsonAsync(string code);

    [PEMS.Application.Common.PemsOperation("Barcode", "Svg", "Barcode.View")]
    Task<BarcodeFileResult> SvgAsync(int id);

    [PEMS.Application.Common.PemsOperation("Barcode", "Png", "Barcode.View")]
    Task<BarcodeFileResult> PngAsync(int id);

    [PEMS.Application.Common.PemsOperation("Barcode", "LabelPdf", "Barcode.View")]
    Task<BarcodeFileResult> LabelPdfAsync(int id);

    [PEMS.Application.Common.PemsOperation("Barcode", "LabelSheet", "Barcode.View")]
    Task<BarcodeFileResult> LabelSheetAsync(string targetType, string ids);

    [PEMS.Application.Common.PemsOperation("Barcode", "Candidates", "Barcode.Approve")]
    Task<List<BarcodeCandidateRow>> CandidatesAsync(int? disciplineId = null, string? equipmentType = null, string? tagSearch = null);

    [PEMS.Application.Common.PemsOperation("Barcode", "Disciplines", "Barcode.Approve")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("Barcode", "EquipmentTypes", "Barcode.Approve")]
    Task<List<ReportChoiceRow>> EquipmentTypesAsync();

    [PEMS.Application.Common.PemsOperation("Barcode", "Generate", "Barcode.Create")]
    Task<(bool Success, string? Error)> GenerateAsync(string targetType, int targetId, string symbology, int? employeeId);

    /// <summary>Records the print and downloads the label of one barcode.</summary>
    [PEMS.Application.Common.PemsOperation("Barcode", "Print", "Barcode.Create")]
    Task<BarcodeFileResult> PrintAsync(int id);

    /// <summary>Creates the barcodes of the chosen equipment (ids separated by commas) and downloads their label sheet.</summary>
    [PEMS.Application.Common.PemsOperation("Barcode", "BulkGenerate", "Barcode.Approve")]
    Task<BarcodeFileResult> BulkGenerateAsync(string equipmentIds, string symbology, int? employeeId);

    [PEMS.Application.Common.PemsOperation("Barcode", "Disable", "Barcode.Delete")]
    Task<(bool Success, string? Error)> DisableAsync(int id, string? reason, int? employeeId);

    [PEMS.Application.Common.PemsOperation("Barcode", "Revoke", "Barcode.Delete")]
    Task<(bool Success, string? Error)> RevokeAsync(int id, string? reason, int? employeeId);

    [PEMS.Application.Common.PemsOperation("Barcode", "Replace", "Barcode.Delete")]
    Task<(bool Success, string? Error)> ReplaceAsync(int id, string symbology, int? employeeId);
}
