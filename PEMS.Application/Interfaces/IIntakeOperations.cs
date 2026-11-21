namespace PEMS.Application.Interfaces;

/// <summary>One file of the AI intake queue.</summary>
public sealed record IntakeQueueRow(int AttachmentId, string FileName, string Project, string AiStatus, string Stage, string RoutedModule, DateTime UploadDate, string Confidence, string PredictedCategory, string SuggestedModule);

/// <summary>One file with what the AI suggests (one row), for the reviewer to confirm or correct.</summary>
public sealed record IntakeDetailRow(int AttachmentId, string FileName, string Project, string AiStatus, string AiError, string Stage, string RoutedModule, string Category, string DocumentType, string Discipline, string ProjectHint,
    string Area, string Unit, string System, string Subsystem, string Revision, string IssuePurpose, string Vendor, string MrNumber, string EquipmentTag, string Confidence, string ConfidenceLevel, string SuggestedModule, string ReviewStatus);

/// <summary>
/// The AI intake queue (SCRUM-709): reads over IIntakeService.GetQueueAsync and GetDetailAsync, the lookups of the review form, and the two decisions (route with the reviewer's corrections, reject) over
/// the same service calls as IntakeController.
/// </summary>
public interface IIntakeOperations
{
    [PEMS.Application.Common.PemsOperation("Intake", "Queue")]
    Task<List<IntakeQueueRow>> QueueAsync(string? stage = null);

    [PEMS.Application.Common.PemsOperation("Intake", "Detail")]
    Task<List<IntakeDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("Intake", "Projects")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("Intake", "Disciplines")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("Intake", "Route")]
    Task<(bool Success, string? Error)> RouteAsync(int attachmentId, string targetModule, string? category, string? documentType, string? disciplineCode, int? projectId, string? area, string? revisionNo,
        string? issuePurpose, string? vendorName, string? mrNumber, int? userId);

    [PEMS.Application.Common.PemsOperation("Intake", "Reject")]
    Task<(bool Success, string? Error)> RejectAsync(int attachmentId, int? userId);
}
