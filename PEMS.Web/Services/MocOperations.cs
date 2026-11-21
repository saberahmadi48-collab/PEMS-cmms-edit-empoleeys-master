using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-720: the list, details and attachment upload of MOCController (it used the context inline), as operations.</summary>
public sealed class MocOperations(PEMSDbContext context, IFileStorageService storage) : IMocOperations
{
    private IQueryable<MOC> Filtered(string? riskLevel, string? status, int? projectId)
    {
        var query = context.MOCs.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(riskLevel)) query = query.Where(x => x.RiskLevel == riskLevel);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        return query;
    }

    public async Task<List<MocRow>> ListAsync(string? riskLevel = null, string? status = null, int? projectId = null)
        => await Filtered(riskLevel, status, projectId).OrderByDescending(x => x.RequestedDate).ThenByDescending(x => x.MOCId).Take(500)
            .Select(x => new MocRow(x.MOCId, x.MOCNumber, x.Title, x.Discipline != null ? x.Discipline.Name : "", x.ChangeType, x.RiskLevel, x.Status)).ToListAsync();

    public async Task<List<MocCountRow>> CountsAsync(string? riskLevel = null, string? status = null, int? projectId = null)
    {
        var q = Filtered(riskLevel, status, projectId);
        return [new MocCountRow(await q.CountAsync(), await q.CountAsync(x => x.Status != "Closed" && x.Status != "Rejected"), await q.CountAsync(x => x.RiskLevel == "High" && x.Status != "Closed" && x.Status != "Rejected"))];
    }

    public async Task<List<MocHeadRow>> HeadAsync(int id)
    {
        var x = await context.MOCs.AsNoTracking().Include(m => m.Discipline).Include(m => m.Project).Include(m => m.RelatedDocument).Include(m => m.RelatedEquipment).FirstOrDefaultAsync(m => m.MOCId == id);
        if (x is null) return [];
        var stage = await context.WorkflowInstances.AsNoTracking().Where(w => w.Module == "MOC" && w.BusinessObjectType == "MOC" && w.BusinessObjectId == id).OrderByDescending(w => w.StartDate)
            .Select(w => w.Status == "Running" && w.CurrentStage != null ? w.CurrentStage.Name : "").FirstOrDefaultAsync();
        return [new MocHeadRow(x.MOCId, x.MOCNumber, x.Title, x.Status, x.ChangeType, x.RiskLevel, x.Description, x.Reason ?? "", x.Project?.ProjectName ?? "", x.Discipline?.Name ?? "", x.RelatedDocument?.DocumentNo ?? "", x.RelatedEquipment?.TagNumber ?? "", x.RequestedDate,
            x.TemporaryExpiryDate, x.RiskAssessmentText ?? "", x.RiskAssessedDate, x.ApprovedDate, x.RejectionReason ?? "", x.ImplementationPlan ?? "", x.ImplementedDate, x.DocumentsUpdated, x.UpdatedDocumentsNote ?? "", x.TrainingRequired, x.TrainingCompleted, x.ClosedDate, stage ?? "")];
    }

    public async Task<List<MocAttachmentRow>> AttachmentsAsync(int id) => await context.DocumentAttachments.AsNoTracking().Where(a => a.MOCId == id && a.IsActive).OrderBy(a => a.UploadDate)
        .Select(a => new MocAttachmentRow(a.AttachmentId, a.FileName, a.FileSize, a.UploadDate)).ToListAsync();

    public async Task<List<ReportChoiceRow>> ProjectChoicesAsync() => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync()).Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();
    public async Task<List<ReportChoiceRow>> DisciplineChoicesAsync() => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<(bool Success, string? Error)> UploadAttachmentAsync(int? employeeId, int mocId, byte[] file, string? fileName = null)
    {
        if (file is null || file.Length == 0) return (false, "فایلی انتخاب نشده.");
        var name = string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : Path.GetFileName(fileName);
        string key;
        await using (var stream = new MemoryStream(file)) key = await storage.SaveAsync("moc", name, stream);
        await context.DocumentAttachments.AddAsync(new DocumentAttachment
        {
            MOCId = mocId, FileName = name, FileExtension = Path.GetExtension(name), FilePath = key, FileSize = file.Length, MimeType = "application/octet-stream", UploadedById = employeeId, UploadDate = DateTime.Now, IsActive = true, AIStatus = "NotApplicable"
        });
        await context.SaveChangesAsync();
        return (true, null);
    }
}
