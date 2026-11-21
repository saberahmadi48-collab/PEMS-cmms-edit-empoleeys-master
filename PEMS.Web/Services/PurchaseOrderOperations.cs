using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-741: what PurchaseOrderController showed and did on create, as operations over the purchase-order services.</summary>
public sealed class PurchaseOrderOperations(IPurchaseOrderService orders, IPurchaseOrderApprovalService approval, IPurchaseOrderReceivingService receiving, IAccessControlService access, PEMSDbContext context) : IPurchaseOrderOperations
{
    private static ReportChoiceRow Choice(int id, string label) => new(id.ToString(CultureInfo.InvariantCulture), label);

    public async Task<List<PurchaseOrderRow>> ListAsync()
        => (await orders.GetAllAsync()).Select(p => new PurchaseOrderRow(p.PoId, p.PoNumber, p.ProjectName, p.VendorName, p.ContractNo ?? "", p.Description ?? "", p.Value, p.Status, p.VDRItemsCount)).ToList();

    public async Task<List<PurchaseOrderDetailRow>> DetailAsync(int id)
    {
        var p = await orders.GetByIdAsync(id);
        if (p is null) return [];
        var a = await approval.GetAsync(id);
        return [new PurchaseOrderDetailRow(p.PoId, p.PoNumber, p.ProjectName, p.VendorName, p.ContractNo ?? "", p.Description ?? "", p.Value, p.Status, a?.Currency ?? "", a?.Total ?? 0m, a?.MaterialRequisitionNumber ?? "",
            a?.ApproverRole ?? "", a?.AuthorityRuleLevel, a?.SubmittedAt, a?.SubmittedBy ?? "", a?.ApprovedAt, a?.ApprovedBy ?? "", a?.RejectionReason ?? "", a?.CreatedBy ?? "", a?.RouteInvalid ?? false, a?.ResponsibleNotSet ?? false)];
    }

    public async Task<List<PurchaseOrderLineRow>> LinesAsync(int id)
        => (await approval.GetAsync(id))?.Lines.Select(l => new PurchaseOrderLineRow(l.PurchaseOrderLineId, l.LineNo, l.Description, l.PartNumber ?? "", l.IsCatalogued, l.Quantity, l.Unit ?? "", l.UnitPrice, l.LineAmount, l.RequiredDate,
            l.IsStockLine, l.CommodityGroup ?? "", l.WarehouseCode ?? "", l.FreeTextReason ?? "")).ToList() ?? [];

    public async Task<List<PurchaseOrderReceivingRow>> ReceivingAsync(int id)
        => (await receiving.GetPoLinesAsync(id)).Select(l => new PurchaseOrderReceivingRow(l.PurchaseOrderLineId, l.LineNo, l.Description, l.MaterialCode ?? "", l.WarehouseCode ?? "", l.Ordered, l.Received, l.InQc, l.Accepted, l.OnHold,
            l.Outstanding, l.OverReceived, l.InspectionRequired, l.InspectionReleasedAt, l.InspectionTestPlanNumber ?? "")).ToList();

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync()).Select(x => Choice(x.ProjectId, x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> VendorsAsync()
        => (await context.Vendors.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.VendorId, x.Name }).ToListAsync()).Select(x => Choice(x.VendorId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> ContractsAsync()
        => (await context.Contracts.AsNoTracking().Select(x => new { x.ContractId, x.ContractNo }).ToListAsync()).Select(x => Choice(x.ContractId, x.ContractNo)).ToList();

    public async Task<List<ReportChoiceRow>> RequisitionsAsync()
        => (await approval.GetRequisitionsForPoAsync()).Select(r => Choice(r.Id, $"{r.Number} - {r.Description}")).ToList();

    private Task RecordCreationAsync(int? userId, string? ip, int id)
        => userId.HasValue
            ? access.AuthorizeAsync(new AccessControlRequest { UserId = userId.Value, ResourceName = "PurchaseOrder", ActionName = "Create", ResourceId = id.ToString(CultureInfo.InvariantCulture), IpAddress = ip })
            : Task.CompletedTask;

    public async Task<(bool Success, string? Error)> CreateAsync(string poNumber, int projectId, int vendorId, int? contractId, string? description, decimal? value, int? userId, string? ip)
    {
        int id;
        try { id = await orders.CreateAsync(poNumber, projectId, vendorId, contractId, description, value); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }   // a duplicate or empty number, an invalid amount: a readable refusal
        await RecordCreationAsync(userId, ip, id);   // the creator row of the separation-of-duties policy (Create and Approve)
        return (true, null);
    }

    public async Task<PoResult> CreateFromRequisitionAsync(int mrId, int vendorId, string currency, string? poNumber, PoActor actor, int? userId, string? ip)
    {
        var r = await approval.CreateFromRequisitionAsync(mrId, vendorId, currency, poNumber, actor);
        if (r.Success && r.Id.HasValue) await RecordCreationAsync(userId, ip, r.Id.Value);
        return r;
    }
}
