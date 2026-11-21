namespace PEMS.Application.Interfaces;

public sealed record FindingRow(int FindingId, int EquipmentId, string Equipment, DateTime ObservedAt, string Condition, string Description, string RecommendedAction, string Status, string FailureMode, string FailureCause, string Source, int? WorkRequestId);

/// <summary>
/// The findings of equipment condition (SCRUM-699) as operations: the list (equipment and status filters), the failure mode and cause choices, a finding from a condition reading or from an inspection (the condition is
/// copied from the frozen evaluation of the reading, never recomputed), closing a finding, and raising one work request from a finding through the real work request service (the priority comes from the configured condition
/// to priority mapping, default Medium; the finding is linked and the work request records its source).
/// </summary>
public interface IFindingOperations
{
    [PEMS.Application.Common.PemsOperation("Finding", "List", "CMFinding.View")]
    Task<List<FindingRow>> FindingsAsync(int? equipmentId = null, string? status = null);

    [PEMS.Application.Common.PemsOperation("Finding", "FailureModeChoices", "CMFinding.View")]
    Task<List<ReportChoiceRow>> FailureModeChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Finding", "FailureCauseChoices", "CMFinding.View")]
    Task<List<ReportChoiceRow>> FailureCauseChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Finding", "CreateFromReading", "CMFinding.Create")]
    Task<(bool Success, string? Error)> CreateFromReadingAsync(int conditionReadingId, string description, string? recommendedAction = null, int? failureModeId = null, int? failureCauseId = null);

    [PEMS.Application.Common.PemsOperation("Finding", "CreateFromInspection", "CMFinding.Create")]
    Task<(bool Success, string? Error)> CreateFromInspectionAsync(int inspectionRecordId, string description, string? recommendedAction = null, int? failureModeId = null, int? failureCauseId = null);

    [PEMS.Application.Common.PemsOperation("Finding", "Close", "CMFinding.Create")]
    Task<(bool Success, string? Error)> CloseAsync(int id);

    [PEMS.Application.Common.PemsOperation("Finding", "RaiseWorkRequest", "WorkRequest.Create")]
    Task<(bool Success, string? Error)> RaiseWorkRequestAsync(int? employeeId, int findingId);
}
