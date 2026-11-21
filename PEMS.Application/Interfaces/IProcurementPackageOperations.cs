namespace PEMS.Application.Interfaces;

/// <summary>One procurement package of the list (and the head of one package).</summary>
public sealed record ProcurementPackageRow(int ProcurementPackageId, string PackageNumber, string Project, string Discipline, string Title, string Stage, string Status, int Mrs, int Mrqs, int OpenCommunications, DateTime CreatedDate);

/// <summary>One clarification or communication of a package.</summary>
public sealed record ProcurementCommunicationRow(int ProcurementCommunicationId, string Type, string Subject, string Body, string Vendor, string RaisedBy, DateTime RaisedDate, string ActionStatus, string Response, string RespondedBy, DateTime? ResponseDate);

/// <summary>One approval decision of a package.</summary>
public sealed record ProcurementApprovalRow(int ProcurementApprovalId, string Approver, string Decision, string Comment, DateTime ApprovalDate);

/// <summary>One tender of the package with its award and contract.</summary>
public sealed record ProcurementPackageTenderRow(int TenderId, string TenderNumber, string Title, string Status, string AwardedVendor, DateTime? AwardedDate, int? ContractId);

/// <summary>
/// The procurement package (SCRUM-736) as operations: the reads are what ProcurementPackageController showed (the package, its MRs, MRQs, communications, approvals and tenders), the create adds the access-control record
/// that makes the separation-of-duties check of the approval work (the creator may not approve), and the other commands are the operations marked on IProcurementPackageService (approve and close out run under the strict
/// access guard in their actions).
/// </summary>
public interface IProcurementPackageOperations
{
    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "List", "ProcurementPackage.View")]
    Task<List<ProcurementPackageRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Detail", "ProcurementPackage.View")]
    Task<List<ProcurementPackageRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Mrs", "ProcurementPackage.View")]
    Task<List<MaterialRequisitionRow>> MrsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Quotations", "ProcurementPackage.View")]
    Task<List<MaterialRequisitionQuotationRow>> QuotationsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Communications", "ProcurementPackage.View")]
    Task<List<ProcurementCommunicationRow>> CommunicationsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Approvals", "ProcurementPackage.View")]
    Task<List<ProcurementApprovalRow>> ApprovalsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Tenders", "ProcurementPackage.View")]
    Task<List<ProcurementPackageTenderRow>> TendersAsync(int id);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Projects", "ProcurementPackage.Create")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Disciplines", "ProcurementPackage.Create")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Vendors", "ProcurementPackage.Edit")]
    Task<List<ReportChoiceRow>> VendorsAsync();

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "UnlinkedMrs", "ProcurementPackage.Edit")]
    Task<List<ReportChoiceRow>> UnlinkedMrsAsync();

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Create", "ProcurementPackage.Create")]
    Task<(bool Success, string? Error)> CreateAsync(string packageNumber, int projectId, int? disciplineId, string title, string? description, int? userId, string? ip);
}
