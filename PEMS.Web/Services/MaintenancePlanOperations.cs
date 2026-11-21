using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-724: the list and the plan commands of MaintenancePlanController (it used the context inline), as operations.</summary>
public sealed class MaintenancePlanOperations(IMaintenancePlanService plans, IPMGenerationService generation, IWorkOrderLifecycleService lifecycle, PEMSDbContext context) : IMaintenancePlanOperations
{
    public async Task<List<MaintenancePlanRow>> PlansAsync() => (await plans.GetListAsync(1, 500)).Items.Select(p => new MaintenancePlanRow(p.MaintenancePlanId, p.PlanNumber, p.EquipmentId, p.EquipmentTagNumber, p.EquipmentName, p.EquipmentClassName ?? "", p.CriticalityClass ?? "",
        p.Title, p.StrategyType ?? "", p.StrategyId, p.StrategyVersion, p.FailureModeName ?? "", p.ScheduleMode, p.HasAssignment, p.TriggerType, p.IntervalDays, p.MeterType ?? "", p.MeterInterval, p.LastExecutionMeterReading, p.NextDueMeterReading,
        p.LastExecutionDate, p.NextDueDate, p.IsOverdue)).ToList();

    // a meter-triggered plan is executed by recording the current reading, not only by a date
    public async Task<(bool Success, string? Error)> RecordMeterReadingAsync(int planId, decimal currentReading)
    {
        var plan = await context.MaintenancePlans.FirstOrDefaultAsync(x => x.MaintenancePlanId == planId);
        if (plan is null) return (false, "NotFound");
        if (plan.TriggerType is "Meter" or "Both" && plan.MeterInterval.HasValue)
        {
            plan.LastExecutionMeterReading = currentReading;
            plan.NextDueMeterReading = currentReading + plan.MeterInterval.Value;
        }
        await context.SaveChangesAsync();
        return (true, null);
    }

    // the generated work order is governed (origin snapshot of the user and the start of the lifecycle)
    private async Task<string?> GovernAsync(List<string> roles, int? employeeId)
    {
        var (ok, error, _) = await lifecycle.GovernPendingNewAsync(new WorkOrderActor(employeeId, roles));
        return ok ? null : error;
    }

    public async Task<(bool Success, string? Error)> GenerateWorkOrderAsync(List<string> userRoles, int? employeeId, int planId)
    {
        var number = await generation.GenerateForPlanAsync(planId);
        if (number is null) return (false, "این Plan یا وجود ندارد یا از قبل یک Work Order فعال (تکمیل‌نشده) دارد - برای جلوگیری از تکرار، Work Order جدید ساخته نشد.");
        var governError = await GovernAsync(userRoles, employeeId);
        return governError is null ? (true, null) : (false, governError);
    }

    public async Task<(bool Success, string? Error)> GenerateDueWorkOrdersAsync(List<string> userRoles, int? employeeId)
    {
        await generation.GenerateDueWorkOrdersAsync();
        var governError = await GovernAsync(userRoles, employeeId);
        return governError is null ? (true, null) : (false, governError);
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(int planId)
    {
        var plan = await context.MaintenancePlans.FirstOrDefaultAsync(x => x.MaintenancePlanId == planId);
        if (plan is null) return (false, "NotFound");
        plan.IsActive = false;
        await context.SaveChangesAsync();
        return (true, null);
    }
}
