namespace PEMS.Application.Interfaces;

public sealed record WorkImportBatchRow(int BatchId, string FileName, DateTime ImportDate, string Status, string ImportedBy, int TotalRows, int NewCount, int DuplicateCount, int UnresolvedCount, int ConflictCount, int ValidationErrorCount);
public sealed record ConditionReadingImportRow(int RowId, int RowNumber, string EquipmentTag, string PointTag, string VariableType, DateTime? ReadingDate, decimal? Value, string Source, string ValidationStatus, string ValidationMessage, bool IncludeInCommit, string Status);
public sealed record MaintenanceHistoryImportRow(int RowId, int RowNumber, string EquipmentTag, string EquipmentDescription, DateTime? ExecutionDate, string WorkOrderNumber, string ActionTaken, string Technician, string ValidationStatus, string ValidationMessage, bool IncludeInCommit, string Status);

/// <summary>
/// The two staged CMMS workbook imports (SCRUM-675 condition readings, SCRUM-722 maintenance history) as operations, the same shape as the MRS import: the recent batches, one batch, its rows (a status filter), the upload (staged into a
/// batch), the include toggle of a row, the commit (the highest-risk step: it inserts real records) and the discard. Permissions: ConditionReadingImport and MaintenanceHistoryImport View, Create, Approve and Delete like before.
/// </summary>
public interface IWorkImportOperations
{
    // ---- condition readings (SCRUM-675)
    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Batches", "ConditionReadingImport.View")]
    Task<List<WorkImportBatchRow>> ReadingBatchesAsync();

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Batch", "ConditionReadingImport.View")]
    Task<List<WorkImportBatchRow>> ReadingBatchAsync(int batchId);

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Rows", "ConditionReadingImport.View")]
    Task<List<ConditionReadingImportRow>> ReadingRowsAsync(int batchId, string? status = null);

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Upload", "ConditionReadingImport.Create")]
    Task<(bool Success, string? Error)> ReadingUploadAsync(int? employeeId, byte[] file, string? fileName = null);

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "ToggleRow", "ConditionReadingImport.Create")]
    Task<(bool Success, string? Error)> ReadingToggleRowAsync(int rowId, bool include);

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Commit", "ConditionReadingImport.Approve")]
    Task<(bool Success, string? Error)> ReadingCommitAsync(int batchId);

    [PEMS.Application.Common.PemsOperation("ConditionReadingImport", "Discard", "ConditionReadingImport.Delete")]
    Task<(bool Success, string? Error)> ReadingDiscardAsync(int batchId);

    // ---- maintenance history (SCRUM-722)
    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Batches", "MaintenanceHistoryImport.View")]
    Task<List<WorkImportBatchRow>> HistoryBatchesAsync();

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Batch", "MaintenanceHistoryImport.View")]
    Task<List<WorkImportBatchRow>> HistoryBatchAsync(int batchId);

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Rows", "MaintenanceHistoryImport.View")]
    Task<List<MaintenanceHistoryImportRow>> HistoryRowsAsync(int batchId, string? status = null);

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Upload", "MaintenanceHistoryImport.Create")]
    Task<(bool Success, string? Error)> HistoryUploadAsync(int? employeeId, byte[] file, string? fileName = null);

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "ToggleRow", "MaintenanceHistoryImport.Create")]
    Task<(bool Success, string? Error)> HistoryToggleRowAsync(int rowId, bool include);

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Commit", "MaintenanceHistoryImport.Approve")]
    Task<(bool Success, string? Error)> HistoryCommitAsync(int batchId);

    [PEMS.Application.Common.PemsOperation("MaintenanceHistoryImport", "Discard", "MaintenanceHistoryImport.Delete")]
    Task<(bool Success, string? Error)> HistoryDiscardAsync(int batchId);
}
