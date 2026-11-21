using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-709: what IntakeController showed and did, as operations over IIntakeService.</summary>
public sealed class IntakeOperations(IIntakeService intake, PEMSDbContext context) : IIntakeOperations
{
    public async Task<List<IntakeQueueRow>> QueueAsync(string? stage = null)
        => (await intake.GetQueueAsync(string.IsNullOrWhiteSpace(stage) ? null : stage, 1, 2000)).Items
            .Select(x => new IntakeQueueRow(x.AttachmentId, x.FileName, x.ProjectName ?? "", x.AIStatus, x.IntakeStage ?? "", x.RoutedModule ?? "", x.UploadDate,
                x.AIConfidence.HasValue ? x.AIConfidence.Value.ToString("P0", CultureInfo.InvariantCulture) : "", x.PredictedCategory ?? "", x.SuggestedTargetModule ?? "")).ToList();

    public async Task<List<IntakeDetailRow>> DetailAsync(int id)
    {
        var d = await intake.GetDetailAsync(id);
        if (d is null) return [];
        return [new IntakeDetailRow(d.AttachmentId, d.FileName, d.ProjectName ?? "", d.AIStatus, d.AIError ?? "", d.IntakeStage ?? "", d.RoutedModule ?? "", d.PredictedCategory ?? "", d.PredictedDocumentType ?? "", d.PredictedDisciplineCode ?? "",
            d.PredictedProjectHint ?? "", d.PredictedArea ?? "", d.PredictedUnit ?? "", d.PredictedSystem ?? "", d.PredictedSubsystem ?? "", d.PredictedRevisionNo ?? "", d.PredictedIssuePurpose ?? "", d.PredictedVendorName ?? "",
            d.PredictedMRNumber ?? "", d.PredictedEquipmentTagNumber ?? "", d.ConfidenceScore.ToString("P0", CultureInfo.InvariantCulture), d.ConfidenceLevel, d.SuggestedTargetModule ?? "", d.ReviewStatus)];
    }

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.Code, x.Name)).ToList();

    public async Task<(bool Success, string? Error)> RouteAsync(int attachmentId, string targetModule, string? category, string? documentType, string? disciplineCode, int? projectId, string? area, string? revisionNo,
        string? issuePurpose, string? vendorName, string? mrNumber, int? userId)
    {
        try
        {
            await intake.RouteAsync(attachmentId, targetModule, category, documentType, disciplineCode, projectId, area, revisionNo, issuePurpose, vendorName, mrNumber, userId);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string? Error)> RejectAsync(int attachmentId, int? userId)
    {
        await intake.RejectAsync(attachmentId, userId);
        return (true, null);
    }
}
