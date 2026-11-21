namespace PEMS.Application.Interfaces;

/// <summary>
/// SCRUM-764: the task check-off, the labor entry and the warehouse and supply commands of WorkOrderController as operations.
/// The work order section guard (section x phase x permission, with the denial record and the change log) is the <c>sectionGuard</c> of each action definition;
/// the module permission floor (WorkOrder.Edit) is the one of the operation. The details page, the status steps, create and the discipline review stay on the controller.
/// </summary>
public interface IWorkOrderTaskOperations
{
    /// <summary>Marks a task of the work order done (both ids must belong together: a task of another work order is never touched).</summary>
    [PEMS.Application.Common.PemsOperation("WorkOrderTask", "Complete", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> CompleteTaskAsync(int workOrderTaskId, int workOrderId, string? userName);

    [PEMS.Application.Common.PemsOperation("WorkOrderTask", "Reopen", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> ReopenTaskAsync(int workOrderTaskId, int workOrderId);

    [PEMS.Application.Common.PemsOperation("WorkOrderLabor", "AddEntry", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> AddLaborEntryAsync(int workOrderId, int employeeId, int? teamId, DateTime workDate, decimal regularHours, decimal overtimeHours, string? notes, int? actorEmployeeId, List<string> userRoles);

    /// <summary>The supply code of a required material (Stock through the warehouse intake, or DirectPurchase to procurement).</summary>
    [PEMS.Application.Common.PemsOperation("WorkOrderWarehouse", "SetSupplyCode", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> SetMaterialSupplyCodeAsync(int workOrderMaterialId, int workOrderId, string supplyCode, int? actorEmployeeId, List<string> userRoles, string? userName);

    /// <summary>Sends the unsourced stock lines to the warehouse queue and the direct-purchase lines to the procurement queue.</summary>
    [PEMS.Application.Common.PemsOperation("WorkOrderWarehouse", "RequestMaterials", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> RequestWarehouseMaterialsAsync(int workOrderId, int? actorEmployeeId, List<string> userRoles, string? userName);
}
