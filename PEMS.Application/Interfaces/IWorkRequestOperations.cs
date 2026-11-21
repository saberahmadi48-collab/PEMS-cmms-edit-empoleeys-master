namespace PEMS.Application.Interfaces;

public sealed record WorkRequestRow(int WorkRequestId, string RequestNumber, string Title, string Status, string Priority, string RequestedBy, DateTime RequestedDate, string Equipment);

/// <summary>
/// Work requests (SCRUM-765) as operations: the list (status filter), the choices of the create form, a new request (the requester is the employee of the user), the legacy status step for a request without a workflow instance
/// (the permission depends on the next status like before), reject, cancel and the conversion to a work order. The details page (the workflow view with its available actions) stays on WorkRequestController; submit and the
/// workflow action were already actions.
/// </summary>
public interface IWorkRequestOperations
{
    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "List", "WorkRequest.View")]
    Task<List<WorkRequestRow>> RequestsAsync(string? status = null);

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "DisciplineChoices", "WorkRequest.Create")]
    Task<List<ReportChoiceRow>> DisciplineChoicesAsync();

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "LocationChoices", "WorkRequest.Create")]
    Task<List<ReportChoiceRow>> LocationChoicesAsync();

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "FailureModeChoices", "WorkRequest.Create")]
    Task<List<ReportChoiceRow>> FailureModeChoicesAsync();

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "Create", "WorkRequest.Create")]
    Task<(bool Success, string? Error)> CreateAsync(int? employeeId, string title, string priority, string? description = null, int? equipmentId = null, int? assetId = null, int? functionalLocationId = null, string? reportedCondition = null,
        int? suggestedDisciplineId = null, bool isHseRelated = false, bool isEmergency = false, int? riskLikelihood = null, int? riskSeverity = null, int? failureModeId = null, string? failureEffect = null, decimal? reportedDowntimeHours = null);

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "AdvanceStatus")]
    Task<(bool Success, string? Error)> AdvanceStatusAsync(List<string> userRoles, int? employeeId, int id, string? reason = null);

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "Reject", "WorkRequest.Approve")]
    Task<(bool Success, string? Error)> RejectAsync(int? employeeId, int id, string? reason = null);

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "Cancel", "WorkRequest.Create")]
    Task<(bool Success, string? Error)> CancelAsync(int? employeeId, int id, string? reason = null);

    [PEMS.Application.Common.PemsOperation("WorkRequestPage", "ConvertToWorkOrder", "WorkRequest.Approve")]
    Task<(bool Success, string? Error)> ConvertToWorkOrderAsync(List<string> userRoles, int? employeeId, string? userName, int id, string workOrderType, int? equipmentId = null);
}
