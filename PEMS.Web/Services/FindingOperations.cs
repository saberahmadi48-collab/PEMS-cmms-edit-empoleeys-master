using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-699: what FindingController did (it used the context inline), as operations; every rule and message is kept.</summary>
public sealed class FindingOperations(PEMSDbContext context, IWorkRequestService workRequests) : IFindingOperations
{
    public async Task<List<FindingRow>> FindingsAsync(int? equipmentId = null, string? status = null)
    {
        var query = context.Findings.AsNoTracking().Include(x => x.Equipment).Include(x => x.FailureMode).Include(x => x.FailureCause).Where(x => x.IsActive);
        if (equipmentId.HasValue) query = query.Where(x => x.EquipmentId == equipmentId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var items = await query.OrderByDescending(x => x.ObservedAt).ToListAsync();
        return items.Select(x => new FindingRow(x.FindingId, x.EquipmentId, x.Equipment?.TagNumber ?? "", x.ObservedAt, x.Condition ?? "", x.Description ?? "", x.RecommendedAction ?? "", x.Status ?? "", x.FailureMode?.Name ?? "", x.FailureCause?.Name ?? "",
            x.ConditionReadingId.HasValue ? "Reading #" + x.ConditionReadingId : x.InspectionRecordId.HasValue ? "Inspection #" + x.InspectionRecordId : "", x.WorkRequestId)).ToList();
    }

    public async Task<List<ReportChoiceRow>> FailureModeChoicesAsync() => (await context.FailureModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.FailureModeId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.FailureModeId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> FailureCauseChoicesAsync() => (await context.FailureCauses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.FailureCauseId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.FailureCauseId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<(bool Success, string? Error)> CreateFromReadingAsync(int conditionReadingId, string description, string? recommendedAction = null, int? failureModeId = null, int? failureCauseId = null)
    {
        var reading = await context.ConditionReadings.Include(x => x.ConditionMonitoringPoint).FirstOrDefaultAsync(x => x.ConditionReadingId == conditionReadingId);
        if (reading?.ConditionMonitoringPoint is null) return (false, "NotFound");
        if (string.IsNullOrWhiteSpace(description)) return (false, "توضیح یافته الزامی است.");
        await context.Findings.AddAsync(new Finding
        {
            EquipmentId = reading.ConditionMonitoringPoint.EquipmentId, ConditionReadingId = conditionReadingId, ObservedAt = reading.ReadingDate, Description = description.Trim(), RecommendedAction = recommendedAction,
            Condition = reading.EvaluationStatus, FailureModeId = failureModeId, FailureCauseId = failureCauseId, Status = "Open", IsActive = true, CreatedDate = DateTime.Now
        });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateFromInspectionAsync(int inspectionRecordId, string description, string? recommendedAction = null, int? failureModeId = null, int? failureCauseId = null)
    {
        var inspection = await context.InspectionRecords.FirstOrDefaultAsync(x => x.InspectionRecordId == inspectionRecordId);
        if (inspection is null) return (false, "NotFound");
        if (string.IsNullOrWhiteSpace(description)) return (false, "توضیح یافته الزامی است.");
        await context.Findings.AddAsync(new Finding
        {
            EquipmentId = inspection.EquipmentId, InspectionRecordId = inspectionRecordId, ObservedAt = inspection.InspectionDate, Description = description.Trim(), RecommendedAction = recommendedAction,
            Condition = inspection.Condition ?? inspection.Result ?? "Unknown", FailureModeId = failureModeId, FailureCauseId = failureCauseId, Status = "Open", IsActive = true, CreatedDate = DateTime.Now
        });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CloseAsync(int id)
    {
        var finding = await context.Findings.FirstOrDefaultAsync(x => x.FindingId == id);
        if (finding is null) return (false, "NotFound");
        finding.Status = "Closed";
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RaiseWorkRequestAsync(int? employeeId, int findingId)
    {
        var finding = await context.Findings.FirstOrDefaultAsync(x => x.FindingId == findingId);
        if (finding is null) return (false, "NotFound");
        if (finding.WorkRequestId.HasValue) return (false, "این یافته از قبل به یک Work Request متصل است.");
        if (employeeId is not int emp) return (false, "کارمند مرتبط با این کاربر پیدا نشد.");
        // the priority is the configured condition mapping (no hard-coded Critical = urgent); Medium only when nothing is configured
        var mapping = await context.ConditionPriorityMappings.FirstOrDefaultAsync(x => x.Condition == finding.Condition && x.IsActive);
        var title = $"Finding - {finding.Condition} - Equipment #{finding.EquipmentId}";
        var reported = $"یافته ({finding.Condition}) در تاریخ {finding.ObservedAt:yyyy-MM-dd}: {finding.Description}";
        var (success, error, workRequestId) = await workRequests.CreateAsync(title, reported, emp, finding.EquipmentId, null, null, reported, mapping?.Priority ?? "Medium", null);
        if (!success || !workRequestId.HasValue) return (false, error);
        finding.WorkRequestId = workRequestId.Value;
        finding.Status = "Linked";
        var workRequest = await context.WorkRequests.FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId.Value);
        if (workRequest is not null) { workRequest.SourceType = "Finding"; workRequest.SourceIdentifier = $"Finding#{finding.FindingId}"; }
        await context.SaveChangesAsync();
        return (true, null);
    }
}
