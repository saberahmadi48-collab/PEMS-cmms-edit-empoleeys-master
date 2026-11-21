using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-734: the list and the commands of PermitController (it combined the workflow runtime and the context inline), as operations.</summary>
public sealed class PermitOperations(IPermitService permits, IFunctionalLocationService locations, IWorkflowRuntime runtime, IPermitAuthorityService authority, IRoleModuleActionService actions, PEMSDbContext context) : IPermitOperations
{
    private static readonly Dictionary<string, string> NextStatus = new() { ["Draft"] = "Submitted", ["Approved"] = "Issued", ["Issued"] = "Active", ["Suspended"] = "Active" };
    private static PermitRow Row(PermitListDto p) => new(p.PermitId, p.PermitNumber, p.PermitType, p.Title, p.Status, p.Priority, p.EquipmentTagNumber ?? "", p.WorkOrderNumber ?? "", p.IssuerName ?? "", p.ReceiverName ?? "", p.ValidFrom, p.ValidTo, p.CreatedDate);

    public async Task<List<PermitRow>> PermitsAsync(List<string> userRoles, int? employeeId, string? status = null, bool pendingMyApproval = false)
    {
        status = string.IsNullOrWhiteSpace(status) ? null : status;
        if (!pendingMyApproval) return (await permits.GetPagedListAsync(status, 1, 500)).Items.Select(Row).ToList();
        // the waiting permits come from the workflow runtime (already limited to this user's queue), longest waiting first; only then the rows are read
        if (employeeId is not int emp) return [];
        var pending = (await runtime.GetPendingTasksAsync(emp, userRoles)).Where(t => t.BusinessObjectType == "Permit").GroupBy(t => t.BusinessObjectId).ToDictionary(g => g.Key, g => g.First());
        if (pending.Count == 0) return [];
        var matching = (await permits.GetActiveIdsMatchingAsync(pending.Keys.ToList(), status)).ToHashSet();
        var ordered = pending.Where(kv => matching.Contains(kv.Key)).OrderBy(kv => kv.Value.AssignedDate).ThenBy(kv => kv.Key).Select(kv => kv.Key).Take(500).ToList();
        var byId = (await permits.GetByIdsAsync(ordered)).ToDictionary(x => x.PermitId);
        return ordered.Where(byId.ContainsKey).Select(id => Row(byId[id])).ToList();
    }

    public Task<List<ReportChoiceRow>> TypeChoicesAsync() => Task.FromResult(PermitService.ValidPermitTypes.Select(x => new ReportChoiceRow(x, x)).ToList());
    public async Task<List<ReportChoiceRow>> LocationChoicesAsync() => (await locations.GetPickerListAsync()).Select(x => new ReportChoiceRow(x.FunctionalLocationId.ToString(CultureInfo.InvariantCulture), x.Code + " " + x.PathBreadcrumb)).ToList();
    public async Task<List<ReportChoiceRow>> ReceiverChoicesAsync() => (await context.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FirstName).Select(x => new { x.EmployeeId, x.FirstName, x.LastName, x.PersonnelType }).ToListAsync())
        .Select(x => new ReportChoiceRow(x.EmployeeId.ToString(CultureInfo.InvariantCulture), x.FirstName + " " + x.LastName + " (" + x.PersonnelType + ")")).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(int? employeeId, string permitType, string title, string priority, string? description = null, int? equipmentId = null, int? functionalLocationId = null, int? workOrderId = null,
        int? permitReceiverEmployeeId = null, DateTime? validFrom = null, DateTime? validTo = null)
    {
        if (employeeId is not int emp) return (false, "کارمند مرتبط با این کاربر پیدا نشد.");
        var (success, error, _) = await permits.CreateAsync(permitType, title, description, emp, equipmentId, functionalLocationId, workOrderId, permitReceiverEmployeeId, priority, validFrom, validTo);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> AdvanceStatusAsync(List<string> userRoles, int? employeeId, int id, string? reason = null)
    {
        var permit = await permits.GetDetailsAsync(id);
        if (permit is null) return (false, "NotFound");
        if (!NextStatus.TryGetValue(permit.Status, out var next)) return (false, "این Permit در وضعیت نهایی است.");
        // Issued or Suspended to Active is refused here: it must go through the execution gate (the server never relies on a hidden button)
        if (next == "Active") return (false, "گذار به Active باید از طریق Execution Gate (Start Work) انجام شود.");
        var required = next == "Issued" ? "Issue" : "Submit";
        if (!await actions.HasActionAsync(userRoles, "Permit", required)) throw new UnauthorizedAccessException();
        return await permits.AdvanceStatusAsync(id, next, reason, employeeId);
    }

    private async Task<string?> EligibilityErrorAsync(List<string> roles, int? userId, int? employeeId, int permitId, string action)
    {
        var result = await authority.EvaluateApproverEligibilityAsync(userId ?? 0, employeeId, roles, permitId, "Permit", action);
        if (string.Equals(result.Result, "Eligible", StringComparison.OrdinalIgnoreCase)) return null;
        var failed = result.Checks.Where(c => !c.Passed).Select(c => string.IsNullOrWhiteSpace(c.Detail) ? c.Name : c.Name + ": " + c.Detail).ToList();
        return failed.Count == 0 ? "The permit authority rules do not allow this action." : string.Join(" | ", failed);
    }

    // two layers: the module action (may this role approve or reject a permit at all) and the workflow (is it exactly this role's stage), plus the permit authority eligibility; the security key is the stepUp of the action
    public async Task<(bool Success, string? Error)> RespondApprovalAsync(List<string> userRoles, int? userId, int? employeeId, string? ip, string? userAgent, int id, string decision, string? comment = null)
    {
        var required = decision == "Approve" ? "Approve" : "Reject";
        if (!await actions.HasActionAsync(userRoles, "Permit", required)) throw new UnauthorizedAccessException();
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, id, required);
        if (refused is not null) return (false, refused);
        return await permits.RespondApprovalAsync(id, decision, comment, employeeId, userRoles, ip, userAgent);
    }

    public async Task<(bool Success, string? Error)> CloseAsync(List<string> userRoles, int? userId, int? employeeId, int id, string? reason = null)
    {
        var refused = await EligibilityErrorAsync(userRoles, userId, employeeId, id, "Close");
        return refused is not null ? (false, refused) : await permits.AdvanceStatusAsync(id, "Closed", reason, employeeId);
    }
}
