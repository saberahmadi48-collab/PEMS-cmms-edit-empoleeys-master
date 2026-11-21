namespace PEMS.Application.Interfaces;

/// <summary>One staged MRS import batch (also the head of one batch on the review page).</summary>
public sealed record MrsImportBatchRow(int MrsImportBatchId, string SourceFile, string Sheet, string Status, int TotalRows, int NewCount, int MatchedCount, int DuplicateCount, int InvalidCount, int ApprovedCount, DateTime ImportDate, string SourceFileUrl);

/// <summary>One row of a batch with what the matching found.</summary>
public sealed record MrsImportRowItem(int MrsImportRowId, string MrsNo, string ItemCode, string Supplier, string PoNo, string Description, string Uom, decimal? QtyReceived, decimal? QtyIssued, decimal? Inventory, DateTime? DateReceived,
    string Warehouse, string MatchStatus, string MatchedMaterial, string ReviewStatus, string Error);

/// <summary>The outcome of staging a file: the new batch id, so the page can open it.</summary>
public sealed record MrsImportUploadResult(bool Success, string? Error, int? BatchId);

/// <summary>
/// The MRS (material receipt sheet) import (SCRUM-657) as operations: upload an Excel file into a staged batch, review its rows (matched, new, duplicate, invalid), then commit or reject rows by id or a whole match
/// status of the batch. The same IMrsImportService calls as MrsImportController; the source file is stored as a document attachment like before (its original name is not carried by the form, so it is saved as
/// mrs-import.xlsx). The permissions are the old ones: View, Create (upload, reject) and Approve (commit, which creates materials, stock and transactions).
/// </summary>
public interface IMrsImportOperations
{
    [PEMS.Application.Common.PemsOperation("MrsImport", "Batches", "MrsImport.View")]
    Task<List<MrsImportBatchRow>> BatchesAsync();

    [PEMS.Application.Common.PemsOperation("MrsImport", "Batch", "MrsImport.View")]
    Task<List<MrsImportBatchRow>> BatchAsync(int batchId);

    [PEMS.Application.Common.PemsOperation("MrsImport", "Rows", "MrsImport.View")]
    Task<List<MrsImportRowItem>> RowsAsync(int batchId, string? matchStatus = null);

    [PEMS.Application.Common.PemsOperation("MrsImport", "Upload", "MrsImport.Create")]
    Task<MrsImportUploadResult> UploadAsync(byte[] file, string? sheetName, int? employeeId);

    [PEMS.Application.Common.PemsOperation("MrsImport", "Commit", "MrsImport.Approve")]
    Task<(bool Success, string? Error)> CommitAsync(int batchId, string rowIds, int? employeeId);

    [PEMS.Application.Common.PemsOperation("MrsImport", "Reject", "MrsImport.Create")]
    Task<(bool Success, string? Error)> RejectAsync(int batchId, string rowIds, int? employeeId);

    [PEMS.Application.Common.PemsOperation("MrsImport", "CommitByStatus", "MrsImport.Approve")]
    Task<(bool Success, string? Error)> CommitByStatusAsync(int batchId, string matchStatus, int? employeeId);

    [PEMS.Application.Common.PemsOperation("MrsImport", "RejectByStatus", "MrsImport.Create")]
    Task<(bool Success, string? Error)> RejectByStatusAsync(int batchId, string matchStatus, int? employeeId);
}
