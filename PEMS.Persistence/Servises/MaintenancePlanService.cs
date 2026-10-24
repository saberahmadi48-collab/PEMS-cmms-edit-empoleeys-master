using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق سند «PEMS — Global Data Grid Standard»: قبلاً MaintenancePlanController.Index کل جدول
/// MaintenancePlans را با Include(Equipment)+Include(MaintenanceStrategy)+ToListAsync (بدون
/// Skip/Take) می‌خواند. اکنون فقط فیلدهای لازم Grid با Select پروجکشن می‌شوند و صفحه‌بندی در
/// SQL انجام می‌شود. این Grid فیلتری نداشت؛ این مهاجرت فیلتر جدیدی اضافه نمی‌کند.
/// </summary>
public class MaintenancePlanService : IMaintenancePlanService
{
    private readonly PEMSDbContext _context;

    public MaintenancePlanService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<MaintenancePlanListDto>> GetListAsync(int page = 1, int pageSize = 50)
    {
        var now = DateTime.Now;

        var query = _context.MaintenancePlans.Where(x => x.IsActive).AsQueryable();

        var totalCount = await query.CountAsync();

        page = page < 1 ? 1 : page;
        pageSize = DataGridPageSize.Normalize(pageSize);

        // همون مرتب‌سازی قبلی (OrderBy NextDueDate - نزدیک‌ترین سررسید اول) + ThenBy PK برای
        // تعیین‌پذیری کامل بین رکوردهایی با NextDueDate یکسان (یا هر دو Null).
        var items = await query
            .OrderBy(x => x.NextDueDate)
            .ThenBy(x => x.MaintenancePlanId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaintenancePlanListDto
            {
                MaintenancePlanId = x.MaintenancePlanId,
                PlanNumber = x.PlanNumber,
                EquipmentId = x.EquipmentId,
                EquipmentTagNumber = x.Equipment != null ? x.Equipment.TagNumber : "",
                Title = x.Title,
                StrategyType = x.MaintenanceStrategy != null ? x.MaintenanceStrategy.StrategyType : null,
                EquipmentName = x.Equipment != null ? x.Equipment.Name : "",
                EquipmentClassName = x.Equipment != null && x.Equipment.EquipmentClassRef != null ? x.Equipment.EquipmentClassRef.Name : null,
                CriticalityClass = _context.EquipmentCriticalities
                    .Where(c => c.IsActive && c.EquipmentId == x.EquipmentId).Select(c => c.CriticalityClass).FirstOrDefault(),
                StrategyId = x.MaintenanceStrategy != null ? x.MaintenanceStrategy.MaintenanceStrategyId : (int?)null,
                StrategyVersion = x.MaintenanceStrategy != null ? x.MaintenanceStrategy.VersionNumber : (int?)null,
                FailureModeName = x.MaintenanceStrategy != null && x.MaintenanceStrategy.FailureMode != null ? x.MaintenanceStrategy.FailureMode.Name : null,
                FailureModeNameFa = x.MaintenanceStrategy != null && x.MaintenanceStrategy.FailureMode != null ? x.MaintenanceStrategy.FailureMode.NameFa : null,
                ScheduleMode = x.ScheduleMode,
                HasAssignment = x.MaintenanceStrategyAssignmentId != null,
                TriggerType = x.TriggerType,
                IntervalDays = x.IntervalDays,
                MeterType = x.MeterType,
                MeterInterval = x.MeterInterval,
                LastExecutionMeterReading = x.LastExecutionMeterReading,
                NextDueMeterReading = x.NextDueMeterReading,
                LastExecutionDate = x.LastExecutionDate,
                NextDueDate = x.NextDueDate,
                IsOverdue =
                    (x.NextDueDate != null && x.NextDueDate < now) ||
                    (x.NextDueMeterReading != null && x.LastExecutionMeterReading != null && x.LastExecutionMeterReading >= x.NextDueMeterReading)
            })
            .ToListAsync();

        return new PagedResult<MaintenancePlanListDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }
}
