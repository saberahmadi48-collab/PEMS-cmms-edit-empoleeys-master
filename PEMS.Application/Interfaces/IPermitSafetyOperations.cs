namespace PEMS.Application.Interfaces;

/// <summary>
/// The permit safety commands of ExecutionGateController, GasTestController and IsolationController.Verify (SCRUM-696, 702, 710, 750) as operations. The three sensitive ones (start work, emergency override, verify an isolation)
/// are run by an action definition that declares <c>stepUp</c> (the security-key confirmation, SCRUM-595); the operation then checks the permit authority eligibility (role, competency, certification) like the old controllers did
/// before the service call. The four required gas readings (O2 in %VOL, LEL in %LEL, H2S and CO in PPM) and one optional other reading are separate arguments.
/// </summary>
public interface IPermitSafetyOperations
{
    [PEMS.Application.Common.PemsOperation("ExecutionGate", "StartWork", "ExecutionGate.StartWork")]
    Task<(bool Success, string? Error)> StartWorkAsync(List<string> userRoles, int? userId, int? employeeId, int permitId);

    [PEMS.Application.Common.PemsOperation("ExecutionGate", "EmergencyOverride", "ExecutionGate.EmergencyOverride")]
    Task<(bool Success, string? Error)> EmergencyOverrideAsync(List<string> userRoles, int? userId, int? employeeId, int permitId, string reason);

    [PEMS.Application.Common.PemsOperation("Isolation", "Verify", "LOTO.Verify")]
    Task<(bool Success, string? Error)> VerifyIsolationAsync(List<string> userRoles, int? userId, int? employeeId, int isolationPointId, int permitId, string? reason = null);

    [PEMS.Application.Common.PemsOperation("Simops", "Confirm", "Simops.Confirm")]
    Task<(bool Success, string? Error)> ConfirmSimopsAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string? reason = null);

    [PEMS.Application.Common.PemsOperation("Simops", "Resolve", "Simops.Resolve")]
    Task<(bool Success, string? Error)> ResolveSimopsAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string resolutionNotes);

    [PEMS.Application.Common.PemsOperation("Simops", "AcceptRisk", "Simops.AcceptRisk")]
    Task<(bool Success, string? Error)> AcceptSimopsRiskAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string justification);

    [PEMS.Application.Common.PemsOperation("GasTest", "Create", "GasTest.Create")]
    Task<(bool Success, string? Error)> CreateGasTestAsync(int? employeeId, int permitId, string location, DateTime testedAt, string? instrumentTag = null, DateTime? instrumentCalibrationDueDate = null,
        int? equipmentId = null, int? functionalLocationId = null, decimal? o2 = null, decimal? lel = null, decimal? h2s = null, decimal? co = null, decimal? otherValue = null, string? otherUnit = null);
}
