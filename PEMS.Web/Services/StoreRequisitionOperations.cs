using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-664: what StoreRequisitionController read and did, as operations; the lifecycle service keeps every rule.</summary>
public sealed class StoreRequisitionOperations(PEMSDbContext context, IStoreRequisitionLifecycleService lifecycle, INumberingService numbering) : IStoreRequisitionOperations
{
    private const int ListLimit = 2000;

    private static readonly Dictionary<string, string> NextStatusMap = new()
    {
        ["Draft"] = "Submitted", ["Submitted"] = "Approved", ["Approved"] = "Reserved", ["Reserved"] = "Issued", ["PartiallyIssued"] = "Issued", ["Issued"] = "Closed",
    };

    public async Task<List<StoreRequisitionRow>> ListAsync(string? status = null)
    {
        var query = context.StoreRequisitions.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var rows = await query.OrderByDescending(x => x.RequestedDate).ThenByDescending(x => x.StoreRequisitionId).Take(ListLimit)
            .Select(x => new { x.StoreRequisitionId, x.RequisitionNumber, x.Status, Warehouse = x.Warehouse!.Code, By = x.RequestedBy!.FirstName + " " + x.RequestedBy!.LastName, x.RequestedDate, x.Purpose }).ToListAsync();
        return rows.Select(x => new StoreRequisitionRow(x.StoreRequisitionId, x.RequisitionNumber, x.Status, x.Warehouse, x.By, x.RequestedDate, x.Purpose ?? "")).ToList();
    }

    public async Task<List<StoreRequisitionDetailRow>> DetailAsync(int id)
    {
        var r = await context.StoreRequisitions.AsNoTracking().Include(x => x.Warehouse).Include(x => x.RequestedBy).Include(x => x.ApprovedBy).Include(x => x.WorkOrder).FirstOrDefaultAsync(x => x.StoreRequisitionId == id);
        if (r is null) return [];
        return [new StoreRequisitionDetailRow(r.StoreRequisitionId, r.RequisitionNumber, r.Status, NextStatusMap.GetValueOrDefault(r.Status, ""), r.Warehouse?.Code ?? "", $"{r.RequestedBy?.FirstName} {r.RequestedBy?.LastName}".Trim(),
            r.ApprovedBy is null ? "" : $"{r.ApprovedBy.FirstName} {r.ApprovedBy.LastName}".Trim(), r.WorkOrder?.WorkOrderNumber ?? "", r.RequestedDate, r.Purpose ?? "")];
    }

    public async Task<List<StoreRequisitionLineRow>> LinesAsync(int id)
    {
        var lines = await context.StoreRequisitionLines.AsNoTracking().Include(x => x.MaterialItem).Where(x => x.StoreRequisitionId == id && x.IsActive).OrderBy(x => x.StoreRequisitionLineId).ToListAsync();
        return lines.Select(l =>
        {
            var approved = l.ApprovedQuantity ?? l.RequestedQuantity;
            return new StoreRequisitionLineRow(l.StoreRequisitionLineId, $"{l.MaterialItem?.MaterialCode} - {l.MaterialItem?.Description}", l.RequestedQuantity, l.ApprovedQuantity, l.IssuedQuantity, Math.Max(0m, approved - l.IssuedQuantity));
        }).ToList();
    }

    public async Task<List<StoreRequisitionHistoryRow>> HistoryAsync(int id)
        => (await context.StoreRequisitionStatusHistories.AsNoTracking().Include(x => x.ChangedBy).Where(x => x.StoreRequisitionId == id).OrderByDescending(x => x.ChangedDate).ToListAsync())
            .Select(h => new StoreRequisitionHistoryRow(h.StoreRequisitionStatusHistoryId, h.ChangedDate, h.FromStatus, h.ToStatus, h.ChangedBy is null ? "" : $"{h.ChangedBy.FirstName} {h.ChangedBy.LastName}".Trim(), h.Reason ?? "")).ToList();

    public async Task<List<ReportChoiceRow>> WarehousesAsync()
        => (await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.WarehouseId.ToString(CultureInfo.InvariantCulture), x.Code)).ToList();

    public async Task<List<ReportChoiceRow>> StockMaterialsAsync()
        => (await context.MaterialItems.AsNoTracking().Where(x => x.IsActive && x.IsStockItem).OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).Take(5000).ToListAsync())
            .Select(x => new ReportChoiceRow(x.MaterialItemId.ToString(CultureInfo.InvariantCulture), $"{x.MaterialCode} - {x.Description}")).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(int warehouseId, string? purpose, int? workOrderId, StoreRequisitionActor actor)
    {
        if (!actor.EmployeeId.HasValue) return (false, "The employee linked to this user was not found.");
        var requisition = new StoreRequisition
        {
            RequisitionNumber = await numbering.GetNextNumberAsync("StoreRequisition"), Status = "Draft", WarehouseId = warehouseId, WorkOrderId = workOrderId, RequestedById = actor.EmployeeId.Value,
            RequestedDate = DateTime.Now, Purpose = purpose, IsActive = true, CreatedDate = DateTime.Now
        };
        await context.StoreRequisitions.AddAsync(requisition);
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddLineAsync(int storeRequisitionId, int materialItemId, decimal requestedQuantity)
    {
        var requisition = await context.StoreRequisitions.FirstOrDefaultAsync(x => x.StoreRequisitionId == storeRequisitionId);
        if (requisition is null) return (false, "Store requisition not found.");
        if (requisition.Status != "Draft") return (false, "A line can only be added while the requisition is a Draft.");
        if (requestedQuantity <= 0) return (false, "The requested quantity must be greater than zero.");
        await context.StoreRequisitionLines.AddAsync(new StoreRequisitionLine { StoreRequisitionId = storeRequisitionId, MaterialItemId = materialItemId, RequestedQuantity = requestedQuantity, IsActive = true, CreatedDate = DateTime.Now });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AdvanceAsync(int id, string? reason, string? requestKey, StoreRequisitionActor actor)
    {
        var status = await context.StoreRequisitions.AsNoTracking().Where(x => x.StoreRequisitionId == id).Select(x => x.Status).FirstOrDefaultAsync();
        if (status is null) return (false, "Store requisition not found.");
        if (!NextStatusMap.TryGetValue(status, out var next)) return (false, "This requisition is in its final status.");
        return await lifecycle.AdvanceStatusAsync(id, next, reason, actor, requestKey);
    }

    public Task<(bool Success, string? Error)> RejectAsync(int id, string? reason, StoreRequisitionActor actor) => lifecycle.AdvanceStatusAsync(id, "Rejected", reason, actor);
    public Task<(bool Success, string? Error)> CancelAsync(int id, string? reason, StoreRequisitionActor actor) => lifecycle.AdvanceStatusAsync(id, "Cancelled", reason, actor);
    public Task<(bool Success, string? Error)> CloseShortAsync(int id, string? reason, StoreRequisitionActor actor) => lifecycle.AdvanceStatusAsync(id, "Closed", reason, actor);

    public Task<(bool Success, string? Error)> SetApprovedQuantityAsync(int id, int lineId, decimal approved, string? requestKey, StoreRequisitionActor actor)
        => lifecycle.SetApprovedQuantitiesAsync(id, new Dictionary<int, decimal> { [lineId] = approved }, actor, requestKey);

    public Task<(bool Success, string? Error)> IssueAllAsync(int id, string? reason, string? requestKey, StoreRequisitionActor actor)
        => lifecycle.IssueAsync(id, null, reason, actor, requestKey);

    public Task<(bool Success, string? Error)> IssueLineAsync(int id, int lineId, decimal quantity, string? reason, string? requestKey, StoreRequisitionActor actor)
        => lifecycle.IssueAsync(id, new Dictionary<int, decimal> { [lineId] = quantity }, reason, actor, requestKey);
}
