using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Servises;

namespace PEMS.Web.Services;

/// <summary>SCRUM-737, 738, 739: the read side of ProductionController, ProductionOrderController and ProductionRunController over the order and run services.</summary>
public sealed class ProductionOperations(IProductionOrderService orders, IProductionRunService runs, PEMSDbContext context) : IProductionOperations
{
    private static ProductionOrderRow Row(ProductionOrderListDto o) => new(o.ProductionOrderId, o.OrderNumber, o.MaterialCode, o.MaterialDescription, o.PlannedQuantity, o.UomCode ?? "", o.Status, o.WorkCenterCode, o.PlannedStartDate, o.PlannedFinishDate, o.RunCount);
    private static ProductionLossRow Row(ProductionLossDto l) => new(l.ProductionLossId, l.Quantity, l.UomCode ?? "", l.LossCategory, l.EmployeeName ?? "", l.TransactionDate, l.Notes ?? "");
    private static ProductionBatchRow Row(ProductionBatchDto b) => new(b.ProductionBatchId, b.BatchNumber, b.ProductionOrderId, b.OrderNumber, b.ProductionRunId, b.RunNumber, b.MaterialCode + " " + b.MaterialDescription, b.Quantity, b.UomCode ?? "", b.ProducedDate, b.QualityStatus,
        b.QualityStatusReason ?? "", b.QualityStatusChangedDate, b.QualityStatusChangedByName ?? "", b.Notes ?? "");

    public async Task<List<ProductionSummaryRow>> SummaryAsync()
    {
        var d = await orders.GetDashboardAsync();
        return [new ProductionSummaryRow(d.ActiveOrderCount, d.ReleasedOrderCount, d.ActiveRunCount, d.HeldRunCount, d.TodaysOutputQuantity, d.TodaysLossQuantity, d.UnresolvedReconciliationIssueCount)];
    }

    public async Task<List<ProductionOrderRow>> ActiveOrdersAsync() => (await orders.GetDashboardAsync()).ActiveOrders.Select(Row).ToList();
    public async Task<List<ProductionBatchRow>> RecentBatchesAsync() => (await orders.GetDashboardAsync()).RecentBatches.Select(Row).ToList();
    public async Task<List<ProductionLossRow>> RecentLossesAsync() => (await orders.GetDashboardAsync()).RecentLosses.Select(Row).ToList();
    public async Task<List<ProductionOrderRow>> OrdersAsync(string? status = null, string? search = null) => (await orders.GetListAsync(1, 500, status, search)).Items.Select(Row).ToList();

    public async Task<List<ProductionOrderHeadRow>> OrderHeadAsync(int id)
    {
        var o = await orders.GetByIdAsync(id);
        return o is null ? [] : [new ProductionOrderHeadRow(o.ProductionOrderId, o.OrderNumber, o.MaterialCode + " " + o.MaterialDescription, o.PlannedQuantity, o.UomCode ?? "", o.Status, o.WorkCenterCode + " " + o.WorkCenterName, o.EquipmentTagNumber ?? "",
            o.ResponsibleEmployeeName ?? "", o.PlannedStartDate, o.PlannedFinishDate, o.ActualStartDate, o.ActualFinishDate, o.Notes ?? "", o.HoldReason ?? "", o.CancellationReason ?? "", o.CreatedBy ?? "", o.CreatedDate)];
    }

    public async Task<List<ProductionRunRow>> OrderRunsAsync(int id) => (await orders.GetByIdAsync(id))?.Runs.Select(r => new ProductionRunRow(r.ProductionRunId, r.OrderNumber, r.RunNumber, r.Status, r.StartDate, r.EndDate, r.TotalOutputQuantity, r.TotalLossQuantity)).ToList() ?? [];

    public async Task<List<ProductionRunHeadRow>> RunHeadAsync(int id)
    {
        var r = await runs.GetByIdAsync(id);
        if (r is null) return [];
        var c = r.Reconciliation;
        return [new ProductionRunHeadRow(r.ProductionRunId, r.ProductionOrderId, r.OrderNumber, r.MaterialCode + " " + r.MaterialDescription, r.RunNumber, r.Status, r.StartDate, r.EndDate, r.WorkCenterCode ?? "", r.EquipmentTagNumber ?? "", r.OperatorEmployeeName ?? "",
            r.ShiftName ?? "", r.Notes ?? "", r.HoldReason ?? "", c.IsComparable, c.Note ?? "", c.TotalInputQuantity, c.TotalOutputQuantity, c.TotalLossQuantity, c.Difference)];
    }

    public async Task<List<ProductionInputRow>> RunInputsAsync(int id) => (await runs.GetByIdAsync(id))?.Inputs.Select(i => new ProductionInputRow(i.ProductionInputId, i.MaterialCode, i.Quantity, i.UomCode ?? "", i.SourceBatchNumber ?? "", i.PostedToInventory, i.EmployeeName ?? "", i.TransactionDate, i.Notes ?? "")).ToList() ?? [];
    public async Task<List<ProductionOutputRow>> RunOutputsAsync(int id) => (await runs.GetByIdAsync(id))?.Outputs.Select(o => new ProductionOutputRow(o.ProductionOutputId, o.MaterialCode, o.ProductionBatchId, o.BatchNumber, o.Quantity, o.UomCode ?? "", o.OutputClassification, o.PostedToInventory, o.EmployeeName ?? "", o.TransactionDate, o.Notes ?? "")).ToList() ?? [];
    public async Task<List<ProductionLossRow>> RunLossesAsync(int id) => (await runs.GetByIdAsync(id))?.Losses.Select(Row).ToList() ?? [];
    public async Task<List<ProductionEventRow>> RunEventsAsync(int id) => (await runs.GetByIdAsync(id))?.Events.Select(e => new ProductionEventRow(e.ProductionEventId, e.EventType, e.EventDate, e.EmployeeName ?? "", e.Details ?? "")).ToList() ?? [];
    public async Task<List<ProductionBatchRow>> BatchHeadAsync(int id) => await runs.GetBatchByIdAsync(id) is { } b ? [Row(b)] : [];

    private static string S(int id) => id.ToString(CultureInfo.InvariantCulture);
    public async Task<List<ReportChoiceRow>> MaterialChoicesAsync() => (await context.MaterialItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaterialItemId), x.MaterialCode + " " + x.Description)).ToList();
    public async Task<List<ReportChoiceRow>> WorkCenterChoicesAsync() => (await context.ProductionWorkCenters.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.ProductionWorkCenterId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.ProductionWorkCenterId), x.Code + " " + x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> UomChoicesAsync() => (await context.MaterialUOMs.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.MaterialUOMId, x.Code }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaterialUOMId), x.Code)).ToList();
    public async Task<List<ReportChoiceRow>> EquipmentChoicesAsync() => (await context.Equipments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.TagNumber).Select(x => new { x.EquipmentId, x.TagNumber, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EquipmentId), x.TagNumber + " " + x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> EmployeeChoicesAsync() => (await context.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FirstName).Select(x => new { x.EmployeeId, x.FirstName, x.LastName }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EmployeeId), x.FirstName + " " + x.LastName)).ToList();
    public async Task<List<ReportChoiceRow>> ShiftChoicesAsync() => (await context.ShiftDefinitions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.ShiftDefinitionId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.ShiftDefinitionId), x.Code + " " + x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> WarehouseChoicesAsync() => (await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.WarehouseId), x.Code + " " + x.Name)).ToList();
    public Task<List<ReportChoiceRow>> LossCategoryChoicesAsync() => Task.FromResult(ProductionRunService.LossCategories.Select(c => new ReportChoiceRow(c, c)).ToList());
}
