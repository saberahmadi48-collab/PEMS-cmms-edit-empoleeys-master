namespace PEMS.Application.Interfaces;

/// <summary>
/// SCRUM-725: the lifecycle and assignment commands of MaintenanceStrategyController as operations (submit, review, activate, retire,
/// assign a tag, unassign, request and decide an override). The actor (employee, user name, roles) is the current user; the rule
/// errors are translated to the user's language. The list, details, create, edit, bulk assign and reports stay on the controller.
/// The permission of each operation is the floor the controller had (Edit or Approve).
/// </summary>
public interface IMaintenanceStrategyOperations
{
    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Submit", "MaintenanceStrategy.Edit")]
    Task<(bool Success, string? Error)> SubmitAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Review", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> ReviewAsync(int id, bool approve, string? comment = null);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Activate", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> ActivateAsync(int id, bool moveAssignments = false);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Retire", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> RetireAsync(int id, string? comment = null);

    /// <summary>Assigns one tag; success only when it was assigned, otherwise the skip reason is the message.</summary>
    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Assign", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> AssignAsync(int id, int equipmentId);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "Unassign", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> UnassignAsync(int assignmentId, string? comment = null);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "RequestOverride", "MaintenanceStrategy.Edit")]
    Task<(bool Success, string? Error)> RequestOverrideAsync(int assignmentId, int? overrideIntervalDays = null, string? overrideTaskNote = null, string? overrideJustification = null);

    [PEMS.Application.Common.PemsOperation("MaintenanceStrategy", "DecideOverride", "MaintenanceStrategy.Approve")]
    Task<(bool Success, string? Error)> DecideOverrideAsync(int assignmentId, bool approve, string? comment = null);
}
