namespace PEMS.Application.Interfaces;

public sealed record MaintenancePlanRow(int MaintenancePlanId, string PlanNumber, int EquipmentId, string EquipmentTag, string EquipmentName, string EquipmentClass, string CriticalityClass, string Title, string StrategyType, int? StrategyId, int? StrategyVersion,
    string FailureMode, string ScheduleMode, bool HasAssignment, string TriggerType, int? IntervalDays, string MeterType, decimal? MeterInterval, decimal? LastExecutionMeterReading, decimal? NextDueMeterReading, DateTime? LastExecutionDate, DateTime? NextDueDate, bool IsOverdue);

/// <summary>
/// The maintenance plans (SCRUM-724) as operations: the list and the four commands of MaintenancePlanController other than the plan form (record a meter reading, generate one work order, generate every due work order, deactivate).
/// A generated work order is brought under the work order lifecycle governance like before. The create form (it needs the strategy assignment context) stays on the controller.
/// </summary>
public interface IMaintenancePlanOperations
{
    [PEMS.Application.Common.PemsOperation("MaintenancePlanPage", "List", "MaintenancePlan.View")]
    Task<List<MaintenancePlanRow>> PlansAsync();

    [PEMS.Application.Common.PemsOperation("MaintenancePlanPage", "RecordMeterReading", "MaintenancePlan.Edit")]
    Task<(bool Success, string? Error)> RecordMeterReadingAsync(int planId, decimal currentReading);

    [PEMS.Application.Common.PemsOperation("MaintenancePlanPage", "GenerateWorkOrder", "MaintenancePlan.Edit")]
    Task<(bool Success, string? Error)> GenerateWorkOrderAsync(List<string> userRoles, int? employeeId, int planId);

    [PEMS.Application.Common.PemsOperation("MaintenancePlanPage", "GenerateDueWorkOrders", "MaintenancePlan.Edit")]
    Task<(bool Success, string? Error)> GenerateDueWorkOrdersAsync(List<string> userRoles, int? employeeId);

    [PEMS.Application.Common.PemsOperation("MaintenancePlanPage", "Deactivate", "MaintenancePlan.Edit")]
    Task<(bool Success, string? Error)> DeactivateAsync(int planId);
}
