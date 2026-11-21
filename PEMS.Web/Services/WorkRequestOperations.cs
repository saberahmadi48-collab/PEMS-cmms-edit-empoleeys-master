using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-765: the list and the commands of WorkRequestController over IWorkRequestService (the same calls, rules and messages).</summary>
public sealed class WorkRequestOperations(IWorkRequestService service, IFunctionalLocationService locations, IPermissionService permissions, PEMSDbContext context) : IWorkRequestOperations
{
    private static string S(int id) => id.ToString(CultureInfo.InvariantCulture);
    private static string L(string fa, string en) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? fa : en;

    public async Task<List<WorkRequestRow>> RequestsAsync(string? status = null)
        => (await service.GetListAsync(1, 500, string.IsNullOrWhiteSpace(status) ? null : status)).Items.Select(r => new WorkRequestRow(r.WorkRequestId, r.RequestNumber, r.Title, r.Status, r.Priority, r.RequestedByName, r.RequestedDate, r.EquipmentTagNumber ?? "")).ToList();

    public async Task<List<ReportChoiceRow>> DisciplineChoicesAsync() => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.DisciplineId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> LocationChoicesAsync() => (await locations.GetPickerListAsync()).Select(x => new ReportChoiceRow(S(x.FunctionalLocationId), x.Code + " " + x.PathBreadcrumb)).ToList();
    public async Task<List<ReportChoiceRow>> FailureModeChoicesAsync() => (await context.FailureModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => new { x.FailureModeId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.FailureModeId), x.Code + " - " + x.Name)).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(int? employeeId, string title, string priority, string? description = null, int? equipmentId = null, int? assetId = null, int? functionalLocationId = null, string? reportedCondition = null,
        int? suggestedDisciplineId = null, bool isHseRelated = false, bool isEmergency = false, int? riskLikelihood = null, int? riskSeverity = null, int? failureModeId = null, string? failureEffect = null, decimal? reportedDowntimeHours = null)
    {
        if (employeeId is not int emp) return (false, L("کارمند مرتبط با این کاربر پیدا نشد.", "No employee is linked to this user."));
        var (success, error, _) = await service.CreateAsync(new WorkRequestCreateRequest
        {
            Title = title, Description = description, RequestedById = emp, EquipmentId = equipmentId, AssetId = assetId, FunctionalLocationId = functionalLocationId, ReportedCondition = reportedCondition, Priority = priority,
            SuggestedDisciplineId = suggestedDisciplineId, IsHseRelated = isHseRelated, IsEmergency = isEmergency, RiskLikelihood = riskLikelihood, RiskSeverity = riskSeverity, FailureModeId = failureModeId, FailureEffect = failureEffect, ReportedDowntimeHours = reportedDowntimeHours
        });
        return (success, error);
    }

    // only for a request without a workflow instance (the service refuses the others); Submitted to UnderReview needs Create, the supervisory steps need Approve
    public async Task<(bool Success, string? Error)> AdvanceStatusAsync(List<string> userRoles, int? employeeId, int id, string? reason = null)
    {
        var wr = await service.GetDetailsAsync(id);
        if (wr is null) return (false, "NotFound");
        var next = service.GetLegacyNextStatus(wr.Status);
        if (next is null) return (false, L("این درخواست در وضعیت نهایی است.", "This request is in a final status."));
        var required = next is "Approved" or "Planned" ? "Approve" : "Create";
        if (!await permissions.HasPermissionAsync(userRoles, "WorkRequest", required)) throw new UnauthorizedAccessException();
        return await service.AdvanceStatusAsync(id, next, reason, employeeId);
    }

    public Task<(bool Success, string? Error)> RejectAsync(int? employeeId, int id, string? reason = null) => service.AdvanceStatusAsync(id, "Rejected", reason, employeeId);
    public Task<(bool Success, string? Error)> CancelAsync(int? employeeId, int id, string? reason = null) => service.AdvanceStatusAsync(id, "Cancelled", reason, employeeId);

    public async Task<(bool Success, string? Error)> ConvertToWorkOrderAsync(List<string> userRoles, int? employeeId, string? userName, int id, string workOrderType, int? equipmentId = null)
    {
        var (success, error, _) = await service.ConvertToWorkOrderAsync(id, workOrderType, userName, new WorkRequestActor(employeeId, userRoles), equipmentId);
        return (success, error);
    }
}
