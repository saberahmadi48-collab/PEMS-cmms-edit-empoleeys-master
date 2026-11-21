using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-696, 702, 710: the commands of the execution gate, gas test and isolation verification controllers, over the same services.</summary>
public sealed class PermitSafetyOperations(IExecutionGateService gate, IGasTestService gasTests, IIsolationService isolations, ISimopsService simops, IPermitAuthorityService authority) : IPermitSafetyOperations
{
    // the eligibility (role, competency, certification) is the server-side rule the controllers ran before the service; the security key itself is the stepUp of the action definition
    private async Task<string?> EligibilityErrorAsync(List<string> roles, int? userId, int? employeeId, int permitId, string module, string action)
    {
        var result = await authority.EvaluateApproverEligibilityAsync(userId ?? 0, employeeId, roles, permitId, module, action);
        if (string.Equals(result.Result, "Eligible", StringComparison.OrdinalIgnoreCase)) return null;
        var failed = result.Checks.Where(c => !c.Passed).Select(c => string.IsNullOrWhiteSpace(c.Detail) ? c.Name : c.Name + ": " + c.Detail).ToList();
        return failed.Count == 0 ? "The permit authority rules do not allow this action." : string.Join(" | ", failed);
    }

    public async Task<(bool Success, string? Error)> StartWorkAsync(List<string> userRoles, int? userId, int? employeeId, int permitId)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "ExecutionGate", "StartWork");
        return refused is null ? await gate.StartWorkAsync(permitId, employeeId) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> EmergencyOverrideAsync(List<string> userRoles, int? userId, int? employeeId, int permitId, string reason)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "ExecutionGate", "EmergencyOverride");
        return refused is null ? await gate.EmergencyOverrideAsync(permitId, reason, employeeId, userRoles) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> VerifyIsolationAsync(List<string> userRoles, int? userId, int? employeeId, int isolationPointId, int permitId, string? reason = null)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "LOTO", "Verify");
        return refused is null ? await isolations.AdvanceStatusAsync(isolationPointId, "Verified", reason, employeeId) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> ConfirmSimopsAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string? reason = null)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "Simops", "Confirm");
        return refused is null ? await simops.ConfirmAsync(conflictId, permitId, reason, employeeId) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> ResolveSimopsAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string resolutionNotes)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "Simops", "Resolve");
        return refused is null ? await simops.ResolveAsync(conflictId, permitId, resolutionNotes, employeeId) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> AcceptSimopsRiskAsync(List<string> userRoles, int? userId, int? employeeId, int conflictId, int permitId, string justification)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, permitId, "Simops", "AcceptRisk");
        return refused is null ? await simops.AcceptRiskAsync(conflictId, permitId, justification, employeeId) : (false, refused);
    }

    public async Task<(bool Success, string? Error)> CreateGasTestAsync(int? employeeId, int permitId, string location, DateTime testedAt, string? instrumentTag = null, DateTime? instrumentCalibrationDueDate = null,
        int? equipmentId = null, int? functionalLocationId = null, decimal? o2 = null, decimal? lel = null, decimal? h2s = null, decimal? co = null, decimal? otherValue = null, string? otherUnit = null)
    {
        if (employeeId is not int tester) return (false, "کارمند مرتبط با این کاربر پیدا نشد.");
        var list = new List<GasReadingInputDto>();
        void Add(string parameter, decimal? value, string unit) { if (value.HasValue) list.Add(new GasReadingInputDto { Parameter = parameter, Value = value.Value, Unit = unit }); }
        Add("O2", o2, "%VOL"); Add("LEL", lel, "%LEL"); Add("H2S", h2s, "PPM"); Add("CO", co, "PPM");
        if (otherValue.HasValue && !string.IsNullOrWhiteSpace(otherUnit)) list.Add(new GasReadingInputDto { Parameter = "Other", Value = otherValue.Value, Unit = otherUnit });
        var (success, error, _) = await gasTests.CreateAsync(permitId, location, testedAt, tester, instrumentTag, instrumentCalibrationDueDate, list, equipmentId, functionalLocationId);
        return (success, error);
    }
}
