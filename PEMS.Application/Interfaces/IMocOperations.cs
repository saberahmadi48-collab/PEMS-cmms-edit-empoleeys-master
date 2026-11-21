namespace PEMS.Application.Interfaces;

public sealed record MocRow(int MocId, string MocNumber, string Title, string Discipline, string ChangeType, string RiskLevel, string Status);
public sealed record MocCountRow(int Total, int Open, int HighRiskOpen);
public sealed record MocHeadRow(int MocId, string MocNumber, string Title, string Status, string ChangeType, string RiskLevel, string Description, string Reason, string Project, string Discipline, string RelatedDocument, string RelatedEquipment, DateTime RequestedDate,
    DateTime? TemporaryExpiryDate, string RiskAssessmentText, DateTime? RiskAssessedDate, DateTime? ApprovedDate, string RejectionReason, string ImplementationPlan, DateTime? ImplementedDate, bool DocumentsUpdated, string UpdatedDocumentsNote, bool TrainingRequired,
    bool TrainingCompleted, DateTime? ClosedDate, string WorkflowStage);
public sealed record MocAttachmentRow(int AttachmentId, string FileName, long FileSize, DateTime UploadDate);

/// <summary>
/// Management of change (SCRUM-720) as operations: the list with its filters and counts, the details with the attachments and the running workflow stage, the choices, a new MOC (service attribute) and an attachment upload.
/// The workflow steps (risk assessment, approve, reject, implementation, documents, training, close) were already actions.
/// </summary>
public interface IMocOperations
{
    [PEMS.Application.Common.PemsOperation("MocPage", "List")]
    Task<List<MocRow>> ListAsync(string? riskLevel = null, string? status = null, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("MocPage", "Counts")]
    Task<List<MocCountRow>> CountsAsync(string? riskLevel = null, string? status = null, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("MocPage", "Head")]
    Task<List<MocHeadRow>> HeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("MocPage", "Attachments")]
    Task<List<MocAttachmentRow>> AttachmentsAsync(int id);

    [PEMS.Application.Common.PemsOperation("MocPage", "ProjectChoices")]
    Task<List<ReportChoiceRow>> ProjectChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MocPage", "DisciplineChoices")]
    Task<List<ReportChoiceRow>> DisciplineChoicesAsync();

    [PEMS.Application.Common.PemsOperation("MocPage", "UploadAttachment")]
    Task<(bool Success, string? Error)> UploadAttachmentAsync(int? employeeId, int mocId, byte[] file, string? fileName = null);
}
