using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-667: what AIExtractionController showed and did, as operations: the same queries over the completed attachments, and the same IDocumentAIIngestionService calls.</summary>
public sealed class AiExtractionOperations(PEMSDbContext context, IDocumentAIIngestionService ingestion) : IAiExtractionOperations
{
    private static string Percent(double? value) => value.HasValue ? value.Value.ToString("P0", CultureInfo.InvariantCulture) : "-";

    public async Task<List<AiExtractionFileRow>> FilesAsync()
    {
        var rows = await context.DocumentAttachments.AsNoTracking().Where(x => x.AIStatus == "Completed")
            .Select(x => new
            {
                x.AttachmentId, x.FileName, DocumentNo = x.Document != null ? x.Document.DocumentNo : "", x.AISummary, x.AIConfidence, x.AIProcessedDate,
                PendingTags = x.ExtractedTags.Count(t => t.ReviewStatus == "Draft"), PendingSpecs = x.ExtractedSpecifications.Count(s => s.ReviewStatus == "Draft")
            }).OrderByDescending(x => x.AIProcessedDate).ToListAsync();
        return rows.Select(x => new AiExtractionFileRow(x.AttachmentId, x.FileName, x.DocumentNo ?? "", x.AISummary ?? "", Percent(x.AIConfidence), x.PendingTags, x.PendingSpecs)).ToList();
    }

    public async Task<List<AiExtractionReviewRow>> ReviewAsync(int attachmentId)
        => (await context.DocumentAttachments.AsNoTracking().Where(x => x.AttachmentId == attachmentId).Select(x => new { x.FileName, x.AIConfidence, x.AISummary, x.DocumentId }).ToListAsync())
            .Select(x => new AiExtractionReviewRow(x.FileName, Percent(x.AIConfidence), x.AISummary ?? "", x.DocumentId ?? 0)).ToList();

    public async Task<List<AiExtractedTagRow>> TagsAsync(int attachmentId)
        => (await context.AIExtractedTags.AsNoTracking().Where(x => x.AttachmentId == attachmentId).OrderBy(x => x.ExtractedTagId).ToListAsync())
            .Select(x => new AiExtractedTagRow(x.ExtractedTagId, x.AttachmentId, x.TagNumber, x.TagType ?? "", x.Area ?? "", x.Service ?? "", x.Description ?? "", x.ReviewStatus)).ToList();

    public async Task<List<AiExtractedSpecRow>> SpecsAsync(int attachmentId)
        => (await context.AIExtractedSpecifications.AsNoTracking().Where(x => x.AttachmentId == attachmentId).OrderBy(x => x.ExtractedSpecificationId).ToListAsync())
            .Select(x => new AiExtractedSpecRow(x.ExtractedSpecificationId, x.AttachmentId, x.RelatedTagNumber ?? "", x.FieldName, x.FieldValue ?? "", x.Unit ?? "", x.ReviewStatus)).ToList();

    public Task ApproveTagAsync(int extractedTagId, int equipmentId) => ingestion.ApproveExtractedTagAsync(extractedTagId, equipmentId);
    public Task RejectTagAsync(int extractedTagId) => ingestion.RejectExtractedTagAsync(extractedTagId);
    public Task ApproveSpecAsync(int extractedSpecificationId) => ingestion.ApproveExtractedSpecificationAsync(extractedSpecificationId);
    public Task RejectSpecAsync(int extractedSpecificationId) => ingestion.RejectExtractedSpecificationAsync(extractedSpecificationId);
}
