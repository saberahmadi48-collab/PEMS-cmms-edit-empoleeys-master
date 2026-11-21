using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-736: what ProcurementPackageController showed and did on create, as operations over IProcurementPackageService.</summary>
public sealed class ProcurementPackageOperations(IProcurementPackageService service, IAccessControlService access, PEMSDbContext context) : IProcurementPackageOperations
{
    private const int ListLimit = 2000;

    private static ProcurementPackageRow Row(ProcurementPackageDto p) => new(p.ProcurementPackageId, p.PackageNumber, p.ProjectName, p.DisciplineName ?? "", p.Title, p.Stage, p.Status, p.MRCount, p.MRQCount, p.OpenCommunicationsCount, p.CreatedDate);

    public async Task<List<ProcurementPackageRow>> ListAsync() => (await service.GetPagedAsync(1, ListLimit)).Items.Select(Row).ToList();

    public async Task<List<ProcurementPackageRow>> DetailAsync(int id) => (await service.GetDetailAsync(id)) is { } d ? [Row(d.Summary)] : [];

    public async Task<List<MaterialRequisitionRow>> MrsAsync(int id)
        => (await service.GetDetailAsync(id))?.MaterialRequisitions.Select(m => new MaterialRequisitionRow(m.MRId, m.MRNumber, m.ProjectName, m.DisciplineName, m.Description, m.RequiredDate, m.Status, m.CreatedDate, m.QuotationsCount)).ToList() ?? [];

    public async Task<List<MaterialRequisitionQuotationRow>> QuotationsAsync(int id)
        => (await service.GetDetailAsync(id))?.Quotations.Select(q => new MaterialRequisitionQuotationRow(q.MRQId, q.MRQNumber, q.MRId, q.MRNumber, q.VendorName, q.InquiryDate, q.QuotedAmount, q.Currency ?? "", q.QuotationStatus, q.TechnicalEvaluation, q.CommercialEvaluation, q.ApprovalStatus, q.Notes ?? "")).ToList() ?? [];

    public async Task<List<ProcurementCommunicationRow>> CommunicationsAsync(int id)
        => (await service.GetDetailAsync(id))?.Communications.Select(c => new ProcurementCommunicationRow(c.ProcurementCommunicationId, c.Type, c.Subject, c.Body, c.VendorName ?? "", c.RaisedByName ?? "", c.RaisedDate, c.ActionStatus, c.ResponseText ?? "", c.RespondedByName ?? "", c.ResponseDate)).ToList() ?? [];

    public async Task<List<ProcurementApprovalRow>> ApprovalsAsync(int id)
        => (await service.GetDetailAsync(id))?.Approvals.Select(a => new ProcurementApprovalRow(a.ProcurementApprovalId, a.ApproverName, a.Decision, a.Comment ?? "", a.ApprovalDate)).ToList() ?? [];

    public async Task<List<ProcurementPackageTenderRow>> TendersAsync(int id)
        => (await context.Tenders.AsNoTracking().Include(x => x.AwardedBid).ThenInclude(x => x!.Vendor).Where(x => x.ProcurementPackageId == id).OrderByDescending(x => x.TenderId).ToListAsync())
            .Select(t => new ProcurementPackageTenderRow(t.TenderId, t.TenderNumber, t.Title, t.Status, t.AwardedBid?.Vendor?.Name ?? "", t.AwardedDate, t.ContractId)).ToList();

    private static ReportChoiceRow Choice(int id, string label) => new(id.ToString(CultureInfo.InvariantCulture), label);

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync()).Select(x => Choice(x.ProjectId, x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => Choice(x.DisciplineId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> VendorsAsync()
        => (await context.Vendors.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.VendorId, x.Name }).ToListAsync()).Select(x => Choice(x.VendorId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> UnlinkedMrsAsync()
        => (await context.MaterialRequisitions.AsNoTracking().Where(x => x.ProcurementPackageId == null).Select(x => new { x.MRId, x.MRNumber }).ToListAsync()).Select(x => Choice(x.MRId, x.MRNumber)).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(string packageNumber, int projectId, int? disciplineId, string title, string? description, int? userId, string? ip)
    {
        int id;
        try { id = await service.CreateAsync(packageNumber, projectId, disciplineId, title, description); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }   // an empty or duplicate number: a readable refusal
        // the Allow record of the creation: if the same user later tries to approve this package, the separation-of-duties check of the approval refuses (the creator may not approve)
        if (userId.HasValue)
            await access.AuthorizeAsync(new AccessControlRequest { UserId = userId.Value, ResourceName = "ProcurementPackage", ActionName = "Create", ResourceId = id.ToString(CultureInfo.InvariantCulture), IpAddress = ip });
        return (true, null);
    }
}
