namespace PEMS.Application.Interfaces;

public sealed record TenderRow(int TenderId, string TenderNumber, string Title, string Package, int? PackageId, string Status, DateTime? IssueDate, DateTime? ResponseDeadline, int BidCount, int? ContractId);

/// <summary>
/// Tenders (SCRUM-759) as operations: the list (package filter), a new tender (service attribute), the queued email invitations (all invited vendors, or one) and raising or answering a clarification. The details page and the
/// approval of a negotiation, the choice of the winner and the contract from the winning bid stay on TenderController: they carry creator-separation and separation-of-duties guards (and the tests of those guards call the controller)
/// that are not a generic guard yet; the details page posts those three and now posts the converted commands to the action host.
/// </summary>
public interface ITenderOperations
{
    [PEMS.Application.Common.PemsOperation("TenderPage", "List", "Tender.View")]
    Task<List<TenderRow>> TendersAsync(int? procurementPackageId = null);

    [PEMS.Application.Common.PemsOperation("TenderPage", "SendInvitations", "Tender.Edit")]
    Task<(bool Success, string? Error)> SendInvitationsAsync(string? userName, int tenderId, int? vendorId = null);

    [PEMS.Application.Common.PemsOperation("TenderPage", "RaiseClarification", "Tender.Edit")]
    Task<(bool Success, string? Error)> RaiseClarificationAsync(int tenderId, int procurementPackageId, string type, string subject, string body, int? vendorId = null);

    [PEMS.Application.Common.PemsOperation("TenderPage", "RespondClarification", "Tender.Edit")]
    Task<(bool Success, string? Error)> RespondClarificationAsync(int procurementPackageId, int communicationId, string responseText);
}
