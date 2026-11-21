namespace PEMS.Application.Interfaces;

public sealed record PermitRow(int PermitId, string PermitNumber, string PermitType, string Title, string Status, string Priority, string Equipment, string WorkOrder, string Issuer, string Receiver, DateTime? ValidFrom, DateTime? ValidTo, DateTime CreatedDate);

/// <summary>
/// The permit to work (SCRUM-734) as operations: the list (a status filter, or only the permits waiting for the user's approval, longest waiting first), the choices of the create form, a new permit, the plain status steps
/// (submit, issue), the response to the approval step and closing a permit. The approval response and the close are run by an action definition with <c>stepUp</c> (the security key); the operations then check the permit
/// authority eligibility like the controller did. The details page (it combines the isolation points, the gas tests, SIMOPS, the execution gate and the approval chain) stays on PermitController.
/// </summary>
public interface IPermitOperations
{
    [PEMS.Application.Common.PemsOperation("PermitPage", "List", "Permit.View")]
    Task<List<PermitRow>> PermitsAsync(List<string> userRoles, int? employeeId, string? status = null, bool pendingMyApproval = false);

    [PEMS.Application.Common.PemsOperation("PermitPage", "TypeChoices", "Permit.Create")]
    Task<List<ReportChoiceRow>> TypeChoicesAsync();

    [PEMS.Application.Common.PemsOperation("PermitPage", "LocationChoices", "Permit.Create")]
    Task<List<ReportChoiceRow>> LocationChoicesAsync();

    [PEMS.Application.Common.PemsOperation("PermitPage", "ReceiverChoices", "Permit.Create")]
    Task<List<ReportChoiceRow>> ReceiverChoicesAsync();

    [PEMS.Application.Common.PemsOperation("PermitPage", "Create", "Permit.Create")]
    Task<(bool Success, string? Error)> CreateAsync(int? employeeId, string permitType, string title, string priority, string? description = null, int? equipmentId = null, int? functionalLocationId = null, int? workOrderId = null,
        int? permitReceiverEmployeeId = null, DateTime? validFrom = null, DateTime? validTo = null);

    /// <summary>Draft to Submitted and Approved to Issued (the step to Active only goes through the execution gate).</summary>
    [PEMS.Application.Common.PemsOperation("PermitPage", "AdvanceStatus")]
    Task<(bool Success, string? Error)> AdvanceStatusAsync(List<string> userRoles, int? employeeId, int id, string? reason = null);

    [PEMS.Application.Common.PemsOperation("PermitPage", "RespondApproval")]
    Task<(bool Success, string? Error)> RespondApprovalAsync(List<string> userRoles, int? userId, int? employeeId, string? ip, string? userAgent, int id, string decision, string? comment = null);

    [PEMS.Application.Common.PemsOperation("PermitPage", "Close", "Permit.Close")]
    Task<(bool Success, string? Error)> CloseAsync(List<string> userRoles, int? userId, int? employeeId, int id, string? reason = null);
}
