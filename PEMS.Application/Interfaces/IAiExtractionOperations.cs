namespace PEMS.Application.Interfaces;

/// <summary>One analysed file of the AI extraction list.</summary>
public sealed record AiExtractionFileRow(int AttachmentId, string FileName, string DocumentNo, string Summary, string Confidence, int PendingTags, int PendingSpecs);

/// <summary>The header of the review page (one row).</summary>
public sealed record AiExtractionReviewRow(string FileName, string Confidence, string Summary, int DocumentId);

public sealed record AiExtractedTagRow(int ExtractedTagId, int AttachmentId, string TagNumber, string TagType, string Area, string Service, string Description, string Status);

public sealed record AiExtractedSpecRow(int ExtractedSpecificationId, int AttachmentId, string RelatedTag, string Field, string Value, string Unit, string Status);

/// <summary>The AI extraction pages as operations (SCRUM-667): the list of analysed files, the review of one file, and approve (permission AIExtraction.Approve) or reject (AIExtraction.Create) of an extracted tag or specification, as the controller checked.</summary>
public interface IAiExtractionOperations
{
    [PEMS.Application.Common.PemsOperation("AiExtraction", "Files", "AIExtraction.View")]
    Task<List<AiExtractionFileRow>> FilesAsync();

    [PEMS.Application.Common.PemsOperation("AiExtraction", "Review", "AIExtraction.View")]
    Task<List<AiExtractionReviewRow>> ReviewAsync(int attachmentId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "Tags", "AIExtraction.View")]
    Task<List<AiExtractedTagRow>> TagsAsync(int attachmentId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "Specs", "AIExtraction.View")]
    Task<List<AiExtractedSpecRow>> SpecsAsync(int attachmentId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "ApproveTag", "AIExtraction.Approve")]
    Task ApproveTagAsync(int extractedTagId, int equipmentId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "RejectTag", "AIExtraction.Create")]
    Task RejectTagAsync(int extractedTagId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "ApproveSpec", "AIExtraction.Approve")]
    Task ApproveSpecAsync(int extractedSpecificationId);

    [PEMS.Application.Common.PemsOperation("AiExtraction", "RejectSpec", "AIExtraction.Create")]
    Task RejectSpecAsync(int extractedSpecificationId);
}
