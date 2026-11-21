namespace PEMS.Application.Interfaces;

public sealed record ContractRow(int ContractId, string ContractNo, string Project, string Vendor, decimal? ContractValue, string Currency, DateTime? StartDate, DateTime? EndDate, string Status, int PurchaseOrdersCount, bool RequiresInspection);
public sealed record ContractHeadRow(int ContractId, string ContractNo, string Project, string Vendor, string ScopeOfWork, decimal? ContractValue, string Currency, DateTime? StartDate, DateTime? EndDate, string Status, int PurchaseOrdersCount, bool RequiresInspection,
    int? TenderId, string TenderNumber, int? PackageId, string PackageNumber);
public sealed record ContractReviewRow(int Id, string DisciplineRole, bool IsRequired, string Reviewer, string Decision, string Comment, DateTime? ReviewDate);
public sealed record ContractRevisionRow(int Id, int RevisionNo, string ChangeType, decimal? PreviousValue, decimal? NewValue, string Reason, string RequestedBy, DateTime RequestedDate, string ApprovedBy, DateTime? ApprovedDate, string Status);
public sealed record ContractDocumentRow(int Id, string DocumentNo, string Title, string DocumentType, string ClauseReference, string Status, DateTime CreatedDate);
public sealed record ContractNtpRow(int Id, string NtpNumber, DateTime IssuedDate, DateTime? EffectiveStartDate, string Status);
public sealed record ContractMeetingRow(int Id, string Title, DateTime MeetingDate, string Status);
public sealed record ContractItpRow(int Id, string ItpNumber, string Status);

/// <summary>
/// The contracts (SCRUM-676) as operations: the list, the details (head with the originating tender, discipline reviews, revisions, documents, NTPs, kick-off meetings, linked ITPs), adding a contract document (with the
/// editor event for the separation of duties), issuing an NTP and scheduling the kick-off. The workflow steps were already actions. The create form (the creator event, the uncertain commit recovery) stays on ContractController.
/// </summary>
public interface IContractOperations
{
    [PEMS.Application.Common.PemsOperation("ContractPage", "List", "Contracts.View")]
    Task<List<ContractRow>> ContractsAsync();

    [PEMS.Application.Common.PemsOperation("ContractPage", "Head", "Contracts.View")]
    Task<List<ContractHeadRow>> HeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Reviews", "Contracts.View")]
    Task<List<ContractReviewRow>> ReviewsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Revisions", "Contracts.View")]
    Task<List<ContractRevisionRow>> RevisionsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Documents", "Contracts.View")]
    Task<List<ContractDocumentRow>> DocumentsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Ntps", "Contracts.View")]
    Task<List<ContractNtpRow>> NtpsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Kickoffs", "Contracts.View")]
    Task<List<ContractMeetingRow>> KickoffsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "Itps", "Contracts.View")]
    Task<List<ContractItpRow>> ItpsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ContractPage", "AddDocument", "Contracts.Edit")]
    Task<(bool Success, string? Error)> AddDocumentAsync(int? userId, string? ip, int contractId, string documentNo, string title, string documentType, string? clauseReference = null);

    [PEMS.Application.Common.PemsOperation("ContractPage", "IssueNtp", "Contracts.Approve")]
    Task<(bool Success, string? Error)> IssueNtpAsync(int? employeeId, int contractId, string? reference = null, DateTime? effectiveStartDate = null);

    [PEMS.Application.Common.PemsOperation("ContractPage", "ScheduleKickoff", "Contracts.Edit")]
    Task<(bool Success, string? Error)> ScheduleKickoffAsync(int contractId, DateTime meetingDate, string? location = null, string? agenda = null);
}
