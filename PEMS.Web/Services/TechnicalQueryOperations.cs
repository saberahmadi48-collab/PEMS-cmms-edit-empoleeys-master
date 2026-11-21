using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-758: the list, details and attachment upload of TechnicalQueryController (it used the context inline), as operations.</summary>
public sealed class TechnicalQueryOperations(PEMSDbContext context, IFileStorageService storage) : ITechnicalQueryOperations
{
    private IQueryable<PEMS.Domain.Entities.TechnicalQuery> Filtered(string? queryType, string? status, int? projectId)
    {
        var query = context.TechnicalQueries.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(queryType)) query = query.Where(x => x.QueryType == queryType);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        return query;
    }

    public async Task<List<TechnicalQueryRow>> ListAsync(string? queryType = null, string? status = null, int? projectId = null)
    {
        var now = DateTime.Now;
        return await Filtered(queryType, status, projectId).OrderByDescending(x => x.RaisedDate).ThenByDescending(x => x.TechnicalQueryId).Take(500)
            .Select(x => new TechnicalQueryRow(x.TechnicalQueryId, x.QueryNumber, x.QueryType, x.Subject, x.Discipline != null ? x.Discipline.Name : "", x.Priority, x.RequiredResponseDate, x.Status,
                (x.Status == "Open" || x.Status == "Reopened") && x.RequiredResponseDate.HasValue && x.RequiredResponseDate.Value < now)).ToListAsync();
    }

    public async Task<List<TechnicalQueryCountRow>> CountsAsync(string? queryType = null, string? status = null, int? projectId = null)
    {
        var q = Filtered(queryType, status, projectId);
        var now = DateTime.Now;
        return [new TechnicalQueryCountRow(await q.CountAsync(), await q.CountAsync(x => x.Status == "Open" || x.Status == "Reopened" || x.Status == "PendingConfirmation"),
            await q.CountAsync(x => (x.Status == "Open" || x.Status == "Reopened") && x.RequiredResponseDate.HasValue && x.RequiredResponseDate.Value < now))];
    }

    public async Task<List<TechnicalQueryHeadRow>> HeadAsync(int id)
    {
        var x = await context.TechnicalQueries.AsNoTracking().Include(t => t.Discipline).Include(t => t.Project).Include(t => t.RelatedDocument).Include(t => t.RelatedEquipment).FirstOrDefaultAsync(t => t.TechnicalQueryId == id);
        if (x is null) return [];
        var stage = await context.WorkflowInstances.AsNoTracking().Where(w => w.Module == "TechnicalQuery" && w.BusinessObjectType == "TechnicalQuery" && w.BusinessObjectId == id).OrderByDescending(w => w.StartDate)
            .Select(w => w.Status == "Running" && w.CurrentStage != null ? w.CurrentStage.Name : "").FirstOrDefaultAsync();
        return [new TechnicalQueryHeadRow(x.TechnicalQueryId, x.QueryNumber, x.QueryType, x.Status, x.Subject, x.Description ?? "", x.Project?.ProjectName ?? "", x.Discipline?.Name ?? "", x.RelatedDocument?.DocumentNo ?? "", x.RelatedDocumentId,
            x.RelatedEquipment?.TagNumber ?? "", x.RelatedEquipmentId, x.Priority, x.RaisedDate, x.RequiredResponseDate, x.IsOverdue, x.ResponseText ?? "", x.RespondedDate, stage ?? "")];
    }

    public async Task<List<TechnicalQueryAttachmentRow>> AttachmentsAsync(int id) => await context.DocumentAttachments.AsNoTracking().Where(a => a.TechnicalQueryId == id && a.IsActive).OrderBy(a => a.UploadDate)
        .Select(a => new TechnicalQueryAttachmentRow(a.AttachmentId, a.FileName, a.FileSize, a.UploadDate)).ToListAsync();

    public async Task<List<ReportChoiceRow>> ProjectChoicesAsync() => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync()).Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();
    public async Task<List<ReportChoiceRow>> DisciplineChoicesAsync() => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<(bool Success, string? Error)> UploadAttachmentAsync(int? employeeId, int technicalQueryId, byte[] file, string? fileName = null)
    {
        if (file is null || file.Length == 0) return (false, "فایلی انتخاب نشده.");
        var name = string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : Path.GetFileName(fileName);
        string key;
        await using (var stream = new MemoryStream(file)) key = await storage.SaveAsync("technical-queries", name, stream);
        await context.DocumentAttachments.AddAsync(new PEMS.Domain.Entities.DocumentAttachment
        {
            TechnicalQueryId = technicalQueryId, FileName = name, FileExtension = Path.GetExtension(name), FilePath = key, FileSize = file.Length, MimeType = "application/octet-stream", UploadedById = employeeId, UploadDate = DateTime.Now, IsActive = true, AIStatus = "NotApplicable"
        });
        await context.SaveChangesAsync();
        return (true, null);
    }
}
