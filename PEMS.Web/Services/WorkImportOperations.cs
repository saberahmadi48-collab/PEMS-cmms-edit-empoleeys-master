using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-675, 722: the condition reading and maintenance history import controllers over their services (the same calls; the file name comes from a field because the upload carries bytes only).</summary>
public sealed class WorkImportOperations(IConditionReadingImportService readings, IMaintenanceHistoryImportService history) : IWorkImportOperations
{
    private const int RowLimit = 5000;
    private const string Fallback = "import.xlsx";

    private static WorkImportBatchRow Row(ConditionReadingImportBatchSummaryDto b) => new(b.ConditionReadingImportBatchId, b.FileName, b.ImportDate, b.Status, b.ImportedByName ?? "", b.TotalRows, b.NewCount, b.DuplicateCount, b.UnresolvedCount, b.ConflictCount, b.ValidationErrorCount);
    private static WorkImportBatchRow Row(MaintenanceHistoryImportBatchSummaryDto b) => new(b.MaintenanceHistoryImportBatchId, b.FileName, b.ImportDate, b.Status, b.ImportedByName ?? "", b.TotalRows, b.NewCount, b.DuplicateCount, b.UnresolvedCount, b.ConflictCount, b.ValidationErrorCount);
    private static string Name(string? fileName) => string.IsNullOrWhiteSpace(fileName) ? Fallback : Path.GetFileName(fileName);

    // ---- condition readings
    public async Task<List<WorkImportBatchRow>> ReadingBatchesAsync() => (await readings.GetRecentBatchesAsync()).Select(Row).ToList();
    public async Task<List<WorkImportBatchRow>> ReadingBatchAsync(int batchId) => await readings.GetBatchSummaryAsync(batchId) is { } b ? [Row(b)] : [];

    public async Task<List<ConditionReadingImportRow>> ReadingRowsAsync(int batchId, string? status = null)
        => (await readings.GetBatchAsync(batchId, 1, RowLimit, string.IsNullOrWhiteSpace(status) ? null : status))?.Rows.Items
            .Select(r => new ConditionReadingImportRow(r.ConditionReadingImportRowId, r.RowNumber, r.EquipmentTag, r.PointTag, r.VariableType ?? "", r.ReadingDate, r.Value, r.Source ?? "", r.ValidationStatus, r.ValidationMessage ?? "", r.IncludeInCommit, r.Status)).ToList() ?? [];

    public async Task<(bool Success, string? Error)> ReadingUploadAsync(int? employeeId, byte[] file, string? fileName = null)
    {
        if (file is null || file.Length == 0) return (false, "Please select a Condition Reading Excel file.");
        await using var stream = new MemoryStream(file);
        await readings.StageFromExcelAsync(stream, Name(fileName), employeeId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReadingToggleRowAsync(int rowId, bool include) { await readings.SetRowIncludeAsync(rowId, include); return (true, null); }
    public async Task<(bool Success, string? Error)> ReadingCommitAsync(int batchId) { await readings.CommitBatchAsync(batchId); return (true, null); }
    public async Task<(bool Success, string? Error)> ReadingDiscardAsync(int batchId) { await readings.DiscardBatchAsync(batchId); return (true, null); }

    // ---- maintenance history
    public async Task<List<WorkImportBatchRow>> HistoryBatchesAsync() => (await history.GetRecentBatchesAsync()).Select(Row).ToList();
    public async Task<List<WorkImportBatchRow>> HistoryBatchAsync(int batchId) => await history.GetBatchSummaryAsync(batchId) is { } b ? [Row(b)] : [];

    public async Task<List<MaintenanceHistoryImportRow>> HistoryRowsAsync(int batchId, string? status = null)
        => (await history.GetBatchAsync(batchId, 1, RowLimit, string.IsNullOrWhiteSpace(status) ? null : status))?.Rows.Items
            .Select(r => new MaintenanceHistoryImportRow(r.MaintenanceHistoryImportRowId, r.RowNumber, r.EquipmentTag, r.EquipmentDescription ?? "", r.ExecutionDate, r.WorkOrderNumber ?? "", r.ActionTaken ?? "", r.Technician ?? "", r.ValidationStatus, r.ValidationMessage ?? "", r.IncludeInCommit, r.Status)).ToList() ?? [];

    public async Task<(bool Success, string? Error)> HistoryUploadAsync(int? employeeId, byte[] file, string? fileName = null)
    {
        if (file is null || file.Length == 0) return (false, "Please select a Maintenance History Excel file.");
        await using var stream = new MemoryStream(file);
        await history.StageFromExcelAsync(stream, Name(fileName), employeeId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> HistoryToggleRowAsync(int rowId, bool include) { await history.SetRowIncludeAsync(rowId, include); return (true, null); }
    public async Task<(bool Success, string? Error)> HistoryCommitAsync(int batchId) { await history.CommitBatchAsync(batchId); return (true, null); }
    public async Task<(bool Success, string? Error)> HistoryDiscardAsync(int batchId) { await history.DiscardBatchAsync(batchId); return (true, null); }
}
