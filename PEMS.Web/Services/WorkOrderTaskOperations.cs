using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-764: the task check-off, labor entry, supply code and warehouse request of WorkOrderController (same rules and messages); the section guard is the sectionGuard of the action definition.</summary>
public sealed class WorkOrderTaskOperations(PEMSDbContext context, IWorkOrderLaborService labor, IWarehouseIntakeService warehouseIntake, IStringLocalizer<SharedResource> localizer, IPreProcurementService? preProcurement = null) : IWorkOrderTaskOperations
{
    // both ids are queried together, so a task of another work order is never even read
    private Task<WorkOrderTask?> OwnedTaskAsync(int workOrderTaskId, int workOrderId)
        => context.WorkOrderTasks.FirstOrDefaultAsync(x => x.WorkOrderTaskId == workOrderTaskId && x.WorkOrderId == workOrderId);

    public async Task<(bool Success, string? Error)> CompleteTaskAsync(int workOrderTaskId, int workOrderId, string? userName)
    {
        var task = await OwnedTaskAsync(workOrderTaskId, workOrderId);
        if (task == null) return (false, "NotFound");
        task.IsCompleted = true;
        task.CompletedBy = userName;
        task.CompletedDate = DateTime.Now;
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReopenTaskAsync(int workOrderTaskId, int workOrderId)
    {
        var task = await OwnedTaskAsync(workOrderTaskId, workOrderId);
        if (task == null) return (false, "NotFound");
        task.IsCompleted = false;
        task.CompletedBy = null;
        task.CompletedDate = null;
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddLaborEntryAsync(int workOrderId, int employeeId, int? teamId, DateTime workDate, decimal regularHours, decimal overtimeHours, string? notes, int? actorEmployeeId, List<string> userRoles)
    {
        try
        {
            await labor.AddLaborEntryAsync(workOrderId, employeeId, teamId, workDate, regularHours, overtimeHours, notes, new WorkOrderActor(actorEmployeeId, userRoles), isCorrection: true);
            return (true, null);
        }
        catch (LaborRateMissingException ex) { return (false, ex.Message); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private string Text(string key) => localizer[key].Value;

    public async Task<(bool Success, string? Error)> SetMaterialSupplyCodeAsync(int workOrderMaterialId, int workOrderId, string supplyCode, int? actorEmployeeId, List<string> userRoles, string? userName)
    {
        if (!await context.WorkOrderMaterials.AnyAsync(x => x.WorkOrderMaterialId == workOrderMaterialId && x.WorkOrderId == workOrderId)) return (false, "NotFound");
        var r = await warehouseIntake.SetSupplyCodeAsync(workOrderMaterialId, supplyCode, new WarehouseIntakeActor(actorEmployeeId, userRoles, userName));
        return r.Success ? (true, null) : (false, Text(r.Error!));
    }

    public async Task<(bool Success, string? Error)> RequestWarehouseMaterialsAsync(int workOrderId, int? actorEmployeeId, List<string> userRoles, string? userName)
    {
        var actor = new WarehouseIntakeActor(actorEmployeeId, userRoles, userName);
        // direct-purchase lines go to the procurement queue; stock lines go to the warehouse queue
        var purchase = preProcurement == null ? null : await preProcurement.RaiseDirectPurchaseLinesAsync(workOrderId, new PreProcurementActor(actor.EmployeeId, actor.Roles, actor.UserName));
        var stock = await warehouseIntake.RequestMaterialsAsync(workOrderId, actor);
        if (stock.Success || purchase is { Success: true, Count: > 0 }) return (true, null);
        return (false, Text(purchase is { Success: false } ? purchase.Error! : stock.Error!));
    }
}
