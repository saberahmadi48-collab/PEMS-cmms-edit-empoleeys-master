namespace PEMS.Application.Interfaces;

/// <summary>
/// The child-row edits of a Work Order (spare parts, required skills and materials) as service operations (SCRUM-437). The logic used to sit in the controller; it lives
/// here so an action definition can run it (operation.invoke, with the section guard as a setting of the definition) and every rule is kept: the row must belong to the work order,
/// stock is checked and issued in one save, a second add of the same skill or material is refused.
/// The section x phase x permission decision is NOT here: it is the sectionGuard of the action definition. The floor permission of each operation is "WorkOrder.Edit".
/// </summary>
public interface IWorkOrderChildService
{
    [PEMS.Application.Common.PemsOperation("WorkOrderSpare", "AddUsage", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> AddSpareUsageAsync(int workOrderId, int materialItemId, decimal quantity, decimal? unitCost, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("WorkOrderSpare", "ReturnUsage", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> ReturnSpareUsageAsync(int maintenanceSpareUsageId, int workOrderId, decimal returnQuantity, string? reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("WorkOrderSkill", "Add", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> AddRequiredSkillAsync(int workOrderId, int skillId, string? minimumProficiencyLevel, decimal? plannedHours);

    [PEMS.Application.Common.PemsOperation("WorkOrderSkill", "Remove", "WorkOrder.Edit")]
    /// <summary>The third item is the text of the removed row (for the change log: what it was).</summary>
    Task<(bool Success, string? Error, string? Before)> RemoveRequiredSkillAsync(int requiredSkillId, int workOrderId);

    [PEMS.Application.Common.PemsOperation("WorkOrderMaterial", "Add", "WorkOrder.Edit")]
    Task<(bool Success, string? Error)> AddRequiredMaterialAsync(int workOrderId, int materialItemId, decimal requiredQuantity);

    [PEMS.Application.Common.PemsOperation("WorkOrderMaterial", "Remove", "WorkOrder.Edit")]
    /// <summary>The third item is the text of the removed row (for the change log: what it was).</summary>
    Task<(bool Success, string? Error, string? Before)> RemoveRequiredMaterialAsync(int workOrderMaterialId, int workOrderId);
}
