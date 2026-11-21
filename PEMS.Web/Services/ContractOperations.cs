using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-676: the list, details and the document, NTP and kick-off commands of ContractController (it used the context inline), as operations.</summary>
public sealed class ContractOperations(IContractService contracts, IContractWorkflowService workflow, PEMSDbContext context, ICurrentUserService currentUser) : IContractOperations
{
    public async Task<List<ContractRow>> ContractsAsync() => (await contracts.GetPagedAsync(1, 500)).Items.Select(c => new ContractRow(c.ContractId, c.ContractNo, c.ProjectName, c.VendorName, c.ContractValue, c.Currency ?? "", c.StartDate, c.EndDate, c.Status, c.PurchaseOrdersCount, c.RequiresInspection)).ToList();

    public async Task<List<ContractHeadRow>> HeadAsync(int id)
    {
        var c = await contracts.GetByIdAsync(id);
        if (c is null) return [];
        // Tender.ContractId is the only source of truth of the relation; it is only read in the other direction here
        var tender = await context.Tenders.AsNoTracking().Include(x => x.ProcurementPackage).FirstOrDefaultAsync(x => x.ContractId == id);
        return [new ContractHeadRow(c.ContractId, c.ContractNo, c.ProjectName, c.VendorName, c.ScopeOfWork ?? "", c.ContractValue, c.Currency ?? "", c.StartDate, c.EndDate, c.Status, c.PurchaseOrdersCount, c.RequiresInspection,
            tender?.TenderId, tender?.TenderNumber ?? "", tender?.ProcurementPackageId, tender?.ProcurementPackage?.PackageNumber ?? "")];
    }

    public async Task<List<ContractReviewRow>> ReviewsAsync(int id) => (await context.ContractDisciplineReviews.AsNoTracking().Include(x => x.Reviewer).Where(x => x.ContractId == id).ToListAsync())
        .Select(r => new ContractReviewRow(r.ContractDisciplineReviewId, r.DisciplineRole, r.IsRequired, (r.Reviewer?.FirstName + " " + r.Reviewer?.LastName).Trim(), r.Decision, r.Comment ?? "", r.ReviewDate)).ToList();

    public async Task<List<ContractRevisionRow>> RevisionsAsync(int id) => (await context.ContractRevisions.AsNoTracking().Include(x => x.RequestedBy).Include(x => x.ApprovedBy).Where(x => x.ContractId == id).OrderByDescending(x => x.RevisionNo).ToListAsync())
        .Select(r => new ContractRevisionRow(r.ContractRevisionId, r.RevisionNo, r.ChangeType, r.PreviousValue, r.NewValue, r.Reason ?? "", (r.RequestedBy?.FirstName + " " + r.RequestedBy?.LastName).Trim(), r.RequestedDate,
            (r.ApprovedBy?.FirstName + " " + r.ApprovedBy?.LastName).Trim(), r.ApprovedDate, r.Status)).ToList();

    public async Task<List<ContractDocumentRow>> DocumentsAsync(int id) => (await context.ContractDocuments.AsNoTracking().Where(x => x.ContractId == id).OrderByDescending(x => x.CreatedDate).ToListAsync())
        .Select(d => new ContractDocumentRow(d.DocumentId, d.DocumentNo, d.Title, d.DocumentType, d.ClauseReference ?? "", d.Status, d.CreatedDate)).ToList();

    public async Task<List<ContractNtpRow>> NtpsAsync(int id) => (await context.ContractNTPs.AsNoTracking().Where(x => x.ContractId == id).OrderByDescending(x => x.IssuedDate).ToListAsync())
        .Select(n => new ContractNtpRow(n.ContractNTPId, n.NTPNumber, n.IssuedDate, n.EffectiveStartDate, n.Status)).ToList();

    public async Task<List<ContractMeetingRow>> KickoffsAsync(int id) => (await context.Meetings.AsNoTracking().Where(x => x.ContractId == id && x.MeetingType == "KickOff").OrderByDescending(x => x.MeetingDate).ToListAsync())
        .Select(m => new ContractMeetingRow(m.MeetingId, m.Title, m.MeetingDate, m.Status)).ToList();

    public async Task<List<ContractItpRow>> ItpsAsync(int id) => (await context.InspectionTestPlans.AsNoTracking().Where(x => x.ContractId == id).ToListAsync()).Select(i => new ContractItpRow(i.InspectionTestPlanId, i.ITPNumber, i.Status)).ToList();

    // the contract document was a skeleton nobody created rows for; adding one edits the contract content, so the editor event is saved with it
    public async Task<(bool Success, string? Error)> AddDocumentAsync(int? userId, string? ip, int contractId, string documentNo, string title, string documentType, string? clauseReference = null)
    {
        var contract = await context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract is null) return (false, "NotFound");
        context.ContractDocuments.Add(new ContractDocument { DocumentKind = "Contract", DocumentNo = documentNo, Title = title, DocumentType = documentType, ProjectId = contract.ProjectId, ContractId = contractId, ClauseReference = clauseReference, Status = "Draft" });
        ContractEditorEvents.Add(context, contractId, userId, ip, "Added a contract document");
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> IssueNtpAsync(int? employeeId, int contractId, string? reference = null, DateTime? effectiveStartDate = null)
    {
        var r = await workflow.IssueNtpAsync(contractId, reference, effectiveStartDate, employeeId);
        return (r.Success, r.Error);
    }

    public async Task<(bool Success, string? Error)> ScheduleKickoffAsync(int contractId, DateTime meetingDate, string? location = null, string? agenda = null)
    {
        var r = await workflow.ScheduleKickoffMeetingAsync(contractId, meetingDate, location, currentUser.FullName, agenda);
        return (r.Success, r.Error);
    }
}
