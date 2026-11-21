namespace PEMS.Application.Interfaces;

public sealed record TechnicalQueryRow(int TechnicalQueryId, string QueryNumber, string QueryType, string Subject, string Discipline, string Priority, DateTime? RequiredResponseDate, string Status, bool IsOverdue);
public sealed record TechnicalQueryCountRow(int Total, int Open, int Overdue);
public sealed record TechnicalQueryHeadRow(int TechnicalQueryId, string QueryNumber, string QueryType, string Status, string Subject, string Description, string Project, string Discipline, string RelatedDocument, int? RelatedDocumentId, string RelatedEquipment,
    int? RelatedEquipmentId, string Priority, DateTime RaisedDate, DateTime? RequiredResponseDate, bool IsOverdue, string ResponseText, DateTime? RespondedDate, string WorkflowStage);
public sealed record TechnicalQueryAttachmentRow(int AttachmentId, string FileName, long FileSize, DateTime UploadDate);

/// <summary>
/// TQ / TSR / RFI (SCRUM-758) as operations: the list with its filters and counts, the details with the attachments and the running workflow stage, the choices, a new query (service attribute) and an attachment upload
/// (the file is saved in the technical-queries store and linked like before; the original file name comes from a field because the upload does not carry it). Respond, confirm and reopen were already actions.
/// </summary>
public interface ITechnicalQueryOperations
{
    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "List")]
    Task<List<TechnicalQueryRow>> ListAsync(string? queryType = null, string? status = null, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "Counts")]
    Task<List<TechnicalQueryCountRow>> CountsAsync(string? queryType = null, string? status = null, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "Head")]
    Task<List<TechnicalQueryHeadRow>> HeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "Attachments")]
    Task<List<TechnicalQueryAttachmentRow>> AttachmentsAsync(int id);

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "ProjectChoices")]
    Task<List<ReportChoiceRow>> ProjectChoicesAsync();

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "DisciplineChoices")]
    Task<List<ReportChoiceRow>> DisciplineChoicesAsync();

    [PEMS.Application.Common.PemsOperation("TechnicalQueryPage", "UploadAttachment")]
    Task<(bool Success, string? Error)> UploadAttachmentAsync(int? employeeId, int technicalQueryId, byte[] file, string? fileName = null);
}
