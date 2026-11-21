using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-472: warehouse intake of work-order materials (SAP stock / non-stock component, IFS supply code).
/// The warehouse is picked by a person on every requisition; nothing here infers one. The store requisition it raises is a
/// normal Draft requisition, so submit / approve / reserve / issue stay in StoreRequisitionLifecycleService (not rebuilt).
/// </summary>
public class WarehouseIntakeService : IWarehouseIntakeService
{
    /// <summary>Work-order statuses whose materials are committed (the same set net demand counts).</summary>
    public static readonly IReadOnlyList<string> ReleasedWorkOrderStatuses = new[] { "Assigned", "InProgress", "OnHold" };
    private static readonly string[] DeadRequisitionStatuses = { StoreRequisitionStatuses.Rejected, StoreRequisitionStatuses.Cancelled };
    private const string RunningInstance = "Running";

    private readonly PEMSDbContext _context;
    private readonly IWorkflowRuntime _runtime;
    private readonly INumberingService _numbering;
    private readonly IPermissionService _permissions;

    public WarehouseIntakeService(PEMSDbContext context, IWorkflowRuntime runtime, INumberingService numbering, IPermissionService permissions)
    {
        _context = context;
        _runtime = runtime;
        _numbering = numbering;
        _permissions = permissions;
    }

    // ------------------------------------------------------------------ planner side

    public async Task<WarehouseIntakeResult> SetSupplyCodeAsync(int workOrderMaterialId, string supplyCode, WarehouseIntakeActor actor)
    {
        if (!WorkOrderMaterialSupplyCodes.IsValid(supplyCode)) return WarehouseIntakeResult.Fail("Whi_Err_SupplyCode");
        if (!await _permissions.HasPermissionAsync(actor.Roles, "WorkOrder", "Edit")) return WarehouseIntakeResult.Fail("Whi_Err_Forbidden");

        var line = await _context.WorkOrderMaterials.Include(m => m.WorkOrder).FirstOrDefaultAsync(m => m.WorkOrderMaterialId == workOrderMaterialId);
        if (line?.WorkOrder == null || !line.WorkOrder.IsActive) return WarehouseIntakeResult.Fail("Whi_Err_NotFound");
        if (line.SupplyCode == supplyCode) return new WarehouseIntakeResult(true, null);
        if (await LiveLinkedQuery(workOrderMaterialId).AnyAsync()) return WarehouseIntakeResult.Fail("Whi_Err_AlreadySourced");

        var from = line.SupplyCode;
        line.SupplyCode = supplyCode;
        Audit(nameof(WorkOrderMaterial), workOrderMaterialId, "SetSupplyCode",
            $"WorkOrder: {line.WorkOrder.WorkOrderNumber}\nSupplyCode: {from} -> {supplyCode}", actor);
        await _context.SaveChangesAsync();
        return new WarehouseIntakeResult(true, null);
    }

    public async Task<WarehouseIntakeResult> RequestMaterialsAsync(int workOrderId, WarehouseIntakeActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "WorkOrder", "Edit")) return WarehouseIntakeResult.Fail("Whi_Err_Forbidden");

        var wo = await _context.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.WorkOrderId == workOrderId && w.IsActive);
        if (wo == null) return WarehouseIntakeResult.Fail("Whi_Err_NotFound");
        if (!ReleasedWorkOrderStatuses.Contains(wo.Status)) return WarehouseIntakeResult.Fail("Whi_Err_NotReleased");

        var intake = await GetAsync(workOrderId);
        if (intake!.IsOpen) return WarehouseIntakeResult.Fail("Whi_Err_AlreadyRequested");
        var open = intake.Lines.Where(l => l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock && l.UnsourcedQuantity > 0).ToList();
        if (open.Count == 0) return WarehouseIntakeResult.Fail("Whi_Err_NothingToRequest");

        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _runtime.StartInstanceAsync(WarehouseIntakeWorkflowSeeder.Module, WarehouseIntakeWorkflowSeeder.BusinessObjectType, workOrderId,
                WarehouseIntakeWorkflowSeeder.Category, null, actor.EmployeeId);
            Audit(nameof(WorkOrder), workOrderId, "RequestMaterials",
                $"WorkOrder: {wo.WorkOrderNumber}\nStock lines: {string.Join(", ", open.Select(l => $"{l.MaterialCode} x {Qty(l.UnsourcedQuantity)}"))}", actor);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        });
        return new WarehouseIntakeResult(true, null);
    }

    // ------------------------------------------------------------------ read

    public async Task<WarehouseIntakeDto?> GetAsync(int workOrderId)
    {
        var wo = await _context.WorkOrders.AsNoTracking()
            .Where(w => w.WorkOrderId == workOrderId && w.IsActive)
            .Select(w => new { w.WorkOrderId, w.WorkOrderNumber, w.Status })
            .FirstOrDefaultAsync();
        if (wo == null) return null;

        var materials = await _context.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.WorkOrderId == workOrderId)
            .OrderBy(m => m.WorkOrderMaterialId)
            .Select(m => new
            {
                m.WorkOrderMaterialId, m.MaterialItemId, m.MaterialItem!.MaterialCode, m.MaterialItem.Description,
                Unit = m.Uom != null ? m.Uom.Name : (m.MaterialItem.Uom != null ? m.MaterialItem.Uom.Name : null),
                m.SupplyCode, m.RequiredQuantity
            })
            .ToListAsync();

        var ids = materials.Select(m => m.WorkOrderMaterialId).ToList();
        var linked = await _context.StoreRequisitionLines.AsNoTracking()
            .Where(l => l.IsActive && l.WorkOrderMaterialId != null && ids.Contains(l.WorkOrderMaterialId.Value)
                        && l.StoreRequisition!.IsActive && !DeadRequisitionStatuses.Contains(l.StoreRequisition.Status))
            .Select(l => new
            {
                WoLineId = l.WorkOrderMaterialId!.Value, l.StoreRequisitionId, l.StoreRequisition!.RequisitionNumber, l.StoreRequisition.Status,
                WarehouseCode = l.StoreRequisition.Warehouse!.Code, Qty = l.ApprovedQuantity ?? l.RequestedQuantity
            })
            .ToListAsync();

        var lines = materials.Select(m =>
        {
            var refs = linked.Where(x => x.WoLineId == m.WorkOrderMaterialId)
                .Select(x => new WarehouseIntakeRequisitionRef(x.StoreRequisitionId, x.RequisitionNumber, x.Status, x.WarehouseCode, x.Qty))
                .ToList();
            var sourced = refs.Sum(r => r.Quantity);
            var unsourced = m.SupplyCode == WorkOrderMaterialSupplyCodes.Stock ? Math.Max(0m, m.RequiredQuantity - sourced) : 0m;
            return new WarehouseIntakeLineDto(m.WorkOrderMaterialId, m.MaterialItemId, m.MaterialCode, m.Description, m.Unit, m.SupplyCode,
                m.RequiredQuantity, sourced, unsourced, refs);
        }).ToList();

        var instance = await _runtime.GetInstanceAsync(WarehouseIntakeWorkflowSeeder.BusinessObjectType, workOrderId);
        return new WarehouseIntakeDto(wo.WorkOrderId, wo.WorkOrderNumber, wo.Status, instance?.Status, instance?.StartDate, lines);
    }

    // ------------------------------------------------------------------ storekeeper side

    public async Task<WarehouseIntakeResult> SourceAsync(int workOrderId, int warehouseId, IReadOnlyDictionary<int, decimal> quantityByWorkOrderMaterialId, WarehouseIntakeActor actor)
    {
        if (actor.EmployeeId == null) return WarehouseIntakeResult.Fail("Whi_Err_NoEmployee");
        if (!await _permissions.HasPermissionAsync(actor.Roles, StoreRequisitionActions.Module, "Create")) return WarehouseIntakeResult.Fail("Whi_Err_Forbidden");

        var warehouse = await _context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.WarehouseId == warehouseId);
        if (warehouse == null || !warehouse.IsActive) return WarehouseIntakeResult.Fail("Whi_Err_Warehouse");

        var intake = await GetAsync(workOrderId);
        if (intake == null) return WarehouseIntakeResult.Fail("Whi_Err_NotFound");
        if (!ReleasedWorkOrderStatuses.Contains(intake.WorkOrderStatus)) return WarehouseIntakeResult.Fail("Whi_Err_NotReleased");

        var requested = quantityByWorkOrderMaterialId.Where(kv => kv.Value != 0).ToList();
        if (requested.Count == 0) return WarehouseIntakeResult.Fail("Whi_Err_NothingSelected");
        foreach (var (lineId, qty) in requested)
        {
            var line = intake.Lines.FirstOrDefault(l => l.WorkOrderMaterialId == lineId);
            if (line == null) return WarehouseIntakeResult.Fail("Whi_Err_NotFound");
            if (line.SupplyCode != WorkOrderMaterialSupplyCodes.Stock) return WarehouseIntakeResult.Fail("Whi_Err_NotStockLine");
            if (qty < 0 || qty > line.UnsourcedQuantity) return WarehouseIntakeResult.Fail("Whi_Err_Quantity");
        }

        var units = await _context.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.WorkOrderId == workOrderId).ToDictionaryAsync(m => m.WorkOrderMaterialId, m => m.UomId);
        var leftAfter = intake.Lines.Where(l => l.SupplyCode == WorkOrderMaterialSupplyCodes.Stock)
            .Sum(l => l.UnsourcedQuantity - requested.Where(r => r.Key == l.WorkOrderMaterialId).Sum(r => r.Value));
        var completes = leftAfter <= 0 && intake.IsOpen;

        StoreRequisition? requisition = null;
        await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync();
            requisition = new StoreRequisition
            {
                RequisitionNumber = await _numbering.GetNextNumberAsync("StoreRequisition"),
                Status = StoreRequisitionStatuses.Draft,
                WarehouseId = warehouseId,
                WorkOrderId = workOrderId,
                RequestedById = actor.EmployeeId.Value,
                RequestedDate = DateTime.Now,
                Purpose = $"Warehouse intake of work order {intake.WorkOrderNumber}",
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            foreach (var (lineId, qty) in requested)
            {
                var line = intake.Lines.First(l => l.WorkOrderMaterialId == lineId);
                requisition.Lines.Add(new StoreRequisitionLine
                {
                    MaterialItemId = line.MaterialItemId,
                    RequestedQuantity = qty,
                    UomId = units.GetValueOrDefault(lineId),
                    WorkOrderMaterialId = lineId,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
            }
            _context.StoreRequisitions.Add(requisition);
            await _context.SaveChangesAsync();

            Audit(nameof(StoreRequisition), requisition.StoreRequisitionId, "WarehouseIntakeSource",
                $"WorkOrder: {intake.WorkOrderNumber}\nWarehouse: {warehouse.Code}\nLines: {string.Join(", ", requested.Select(r => $"{intake.Lines.First(l => l.WorkOrderMaterialId == r.Key).MaterialCode} x {Qty(r.Value)}"))}",
                actor);
            await _context.SaveChangesAsync();

            if (completes)
            {
                var instance = await _runtime.GetInstanceAsync(WarehouseIntakeWorkflowSeeder.BusinessObjectType, workOrderId);
                if (instance?.Status == RunningInstance)
                    await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, "Complete", actor.EmployeeId, actor.Roles,
                        $"All stock lines sourced ({requisition.RequisitionNumber})", null, null);
            }
            await tx.CommitAsync();
        });

        return new WarehouseIntakeResult(true, null, requisition!.StoreRequisitionId, completes);
    }

    public async Task<bool> CanRemoveMaterialAsync(int workOrderMaterialId) => !await LiveLinkedQuery(workOrderMaterialId).AnyAsync();

    // ------------------------------------------------------------------ procurement side (read only, for SCRUM-471)

    public async Task<IReadOnlyList<DirectPurchaseDemandDto>> GetDirectPurchaseDemandAsync() =>
        await _context.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.SupplyCode == WorkOrderMaterialSupplyCodes.DirectPurchase && m.WorkOrder!.IsActive
                        && ReleasedWorkOrderStatuses.Contains(m.WorkOrder.Status))
            .OrderBy(m => m.WorkOrderId).ThenBy(m => m.WorkOrderMaterialId)
            .Select(m => new DirectPurchaseDemandDto(m.WorkOrderMaterialId, m.WorkOrderId, m.WorkOrder!.WorkOrderNumber, m.MaterialItemId,
                m.MaterialItem!.MaterialCode, m.MaterialItem.Description, m.RequiredQuantity,
                m.Uom != null ? m.Uom.Name : (m.MaterialItem.Uom != null ? m.MaterialItem.Uom.Name : null)))
            .ToListAsync();

    // ------------------------------------------------------------------ helpers

    private IQueryable<StoreRequisitionLine> LiveLinkedQuery(int workOrderMaterialId) =>
        _context.StoreRequisitionLines.AsNoTracking().Where(l => l.IsActive && l.WorkOrderMaterialId == workOrderMaterialId
            && l.StoreRequisition!.IsActive && !DeadRequisitionStatuses.Contains(l.StoreRequisition.Status));

    private void Audit(string entity, int id, string action, string summary, WarehouseIntakeActor actor) =>
        _context.AuditLogs.Add(new AuditLog
        {
            EntityName = entity,
            EntityKeyValue = id.ToString(CultureInfo.InvariantCulture),
            Action = action,
            ChangedFieldsSummary = summary,
            UserName = actor.UserName,
            Timestamp = DateTime.Now
        });

    private static string Qty(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
