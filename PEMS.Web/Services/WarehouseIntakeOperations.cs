using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-665: what WarehouseIntakeController showed and did, as operations over IWarehouseIntakeService (the service keeps the rules and the error keys).</summary>
public sealed class WarehouseIntakeOperations(IWarehouseIntakeService service, PEMSDbContext context) : IWarehouseIntakeOperations
{
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public async Task<List<WarehouseIntakeHeaderRow>> HeaderAsync(int workOrderId)
    {
        var intake = await service.GetAsync(workOrderId);
        if (intake is null) return [];
        var open = intake.Lines.Count(l => l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock && l.UnsourcedQuantity > 0);
        var state = intake.IsOpen ? "Open" : open == 0 ? "Sourced" : "NotRequested";
        return [new WarehouseIntakeHeaderRow(intake.WorkOrderId, intake.WorkOrderNumber, intake.WorkOrderStatus, state, intake.RequestedDate, open)];
    }

    public async Task<List<WarehouseIntakeLineRow>> LinesAsync(int workOrderId)
    {
        var intake = await service.GetAsync(workOrderId);
        if (intake is null) return [];
        return intake.Lines.Select(l => new WarehouseIntakeLineRow(l.WorkOrderMaterialId, $"{l.MaterialCode} - {l.MaterialDescription}", l.SupplyCode,
            $"{Number(l.RequiredQuantity)} {l.UnitName}".Trim(), l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock ? Number(l.SourcedQuantity) : "-",
            l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock ? l.UnsourcedQuantity : 0m,
            string.Join("; ", l.Requisitions.Select(r => $"{r.RequisitionNumber} · {r.WarehouseCode} · {r.Status} · {Number(r.Quantity)}")))).ToList();
    }

    public async Task<List<ReportChoiceRow>> WarehousesAsync()
        => (await context.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code).Select(w => new { w.WarehouseId, w.Code, w.Name }).ToListAsync())
            .Select(w => new ReportChoiceRow(w.WarehouseId.ToString(CultureInfo.InvariantCulture), $"{w.Code} - {w.Name}")).ToList();

    public async Task<WarehouseIntakeResult> SourceAllAsync(int workOrderId, int warehouseId, WarehouseIntakeActor actor)
    {
        var intake = await service.GetAsync(workOrderId);
        if (intake is null) return WarehouseIntakeResult.Fail("Whi_Err_NotFound");
        var quantities = intake.Lines.Where(l => l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock && l.UnsourcedQuantity > 0).ToDictionary(l => l.WorkOrderMaterialId, l => l.UnsourcedQuantity);
        return await service.SourceAsync(workOrderId, warehouseId, quantities, actor);
    }

    public Task<WarehouseIntakeResult> SourceLineAsync(int workOrderId, int workOrderMaterialId, int warehouseId, decimal quantity, WarehouseIntakeActor actor)
        => service.SourceAsync(workOrderId, warehouseId, new Dictionary<int, decimal> { [workOrderMaterialId] = quantity }, actor);
}
