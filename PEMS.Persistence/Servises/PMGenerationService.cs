using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 6 — CMMS Operational Excellence» بند ۱۵/۵۵-۵۶: منطق تولید WorkOrder از
/// MaintenancePlanController.GenerateWorkOrder موجود این‌جا منتقل شد (Controller نازک‌تر شود،
/// طبق CLAUDE.md) و یک محافظ Idempotency واقعی اضافه شد که قبلاً وجود نداشت - قبلاً هر کلیک
/// (حتی تکراری/Double-Submit) بدون قید‌وشرط یک WorkOrder جدید می‌ساخت. Snapshot کردن
/// Task/Skill/Material از JobPlan دقیقاً همون منطق قبلی است - فقط منتقل شده، بازنویسی نشده.
/// </summary>
public class PMGenerationService : IPMGenerationService
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numberingService;

    // طبق بند ۵۵: وضعیت‌هایی که یعنی «این WorkOrder هنوز اشغال‌کننده‌ی همین سررسید است» -
    // Closed/Cancelled/Rejected یعنی چرخه‌ی قبلی تمام شده و سررسید بعدی آزاد است.
    private static readonly string[] OccupyingStatuses = { "Open", "Assigned", "InProgress", "OnHold", "Completed" };

    public PMGenerationService(PEMSDbContext context, INumberingService numberingService)
    {
        _context = context;
        _numberingService = numberingService;
    }

    public async Task<string?> GenerateForPlanAsync(int planId)
    {
        var plan = await _context.MaintenancePlans
            .Include(x => x.Equipment)
            .Include(x => x.MaintenanceStrategy)
            .Include(x => x.MaintenanceStrategyAssignment)
            .FirstOrDefaultAsync(x => x.MaintenancePlanId == planId && x.IsActive);

        if (plan == null) return null;

        // RCM: تخصیص بسته‌شده (Retired/Suspended) یا استراتژی «بدون کار زمان‌بندی‌شده» (RTF/Corrective)
        // هرگز Work Order تولید نمی‌کند - Plan های قدیمی نامعتبر هم بی‌صدا WO نمی‌سازند.
        if (plan.MaintenanceStrategyAssignment != null &&
            (!plan.MaintenanceStrategyAssignment.IsActive || plan.MaintenanceStrategyAssignment.Status != RcmCodes.AssignmentStatus.Active))
            return null;
        if (plan.MaintenanceStrategy != null && StrategyRules.ReactiveTypes.Contains(plan.MaintenanceStrategy.StrategyType))
            return null;

        // طبق بند ۱۵/۵۵: محافظ Idempotency - اگر همین Plan از قبل یک WorkOrder اشغال‌کننده دارد،
        // یکی دیگر ساخته نمی‌شود (نه حتی با کلیک تکراری کاربر، نه با اجرای دوباره‌ی Batch).
        var hasOpenWorkOrder = await _context.WorkOrders
            .AnyAsync(x => x.MaintenancePlanId == planId && OccupyingStatuses.Contains(x.Status));

        if (hasOpenWorkOrder) return null;

        // اولویت از ماتریس پیامد × بحرانی‌بودن (نه Medium ثابت) و رشته‌ی مسئول از استراتژی
        var strategy = plan.MaintenanceStrategy;
        var criticality = await _context.EquipmentCriticalities.AsNoTracking()
            .Where(c => c.IsActive && c.EquipmentId == plan.EquipmentId)
            .Select(c => c.CriticalityClass).FirstOrDefaultAsync();
        var consequence = LegacyMaps.Consequence(strategy?.ConsequenceCategory) ?? strategy?.ConsequenceCategory;
        var priority = strategy == null
            ? RcmCodes.Priority.Medium
            : StrategyRules.DerivePriority(consequence, criticality, strategy.IsHiddenFailure);

        int? responsibleDisciplineId = null;
        if (strategy?.OwnerDisciplineId is int ownerId &&
            await _context.Disciplines.AnyAsync(d => d.DisciplineId == ownerId && d.IsActive))
            responsibleDisciplineId = ownerId;

        var assignment = plan.MaintenanceStrategyAssignment;
        var taskNote = assignment is { OverrideStatus: RcmCodes.OverrideStatus.Approved } ? assignment.OverrideTaskNote : null;

        var workOrderNumber = await _numberingService.GetNextNumberAsync("WorkOrder");

        var workOrder = new WorkOrder
        {
            EquipmentId = plan.EquipmentId,
            MaintenancePlanId = plan.MaintenancePlanId,
            WorkOrderNumber = workOrderNumber,
            WorkOrderType = "Preventive",
            Priority = priority,
            ResponsibleDisciplineId = responsibleDisciplineId,
            ResponsibleOrganization = responsibleDisciplineId.HasValue ? OrganizationDomains.Maintenance : null,
            Status = "Open",
            // طبق «PEMS Phase 7» بند ۱۲: سررسید فعلیِ Plan پیش از جلو رفتن (چند خط پایین‌تر)
            // این‌جا کپی می‌شود - تنها فرصت برای ثبت این مقدار، همین لحظه‌ی تولید است.
            DueDate = plan.NextDueDate,
            Description = plan.Title + (string.IsNullOrEmpty(plan.Description) ? "" : " - " + plan.Description)
                          + (string.IsNullOrWhiteSpace(taskNote) ? "" : " | " + taskNote)
                          + (string.IsNullOrWhiteSpace(strategy?.RequiredSkills) ? "" : " | Skills: " + strategy!.RequiredSkills),
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.WorkOrders.AddAsync(workOrder);
        await _context.SaveChangesAsync();

        // طبق CMMS Production Baseline v1 بند ۱۰.۴: Snapshot زمان ساخت از JobPlan - دقیقاً همون
        // منطقی که قبلاً در Controller بود، فقط منتقل‌شده (نه بازطراحی‌شده).
        if (plan.JobPlanId.HasValue)
        {
            var tasks = await _context.MaintenanceTasks
                .Where(x => x.JobPlanId == plan.JobPlanId.Value && x.IsActive)
                .OrderBy(x => x.TaskNumber)
                .ToListAsync();

            foreach (var task in tasks)
            {
                await _context.WorkOrderTasks.AddAsync(new WorkOrderTask
                {
                    WorkOrderId = workOrder.WorkOrderId,
                    TaskNumber = task.TaskNumber,
                    Title = task.Title,
                    Description = task.Description,
                    IsCompleted = false,
                    CreatedDate = DateTime.Now
                });
            }

            var requiredSkills = await _context.JobPlanRequiredSkills
                .Where(x => x.JobPlanId == plan.JobPlanId.Value)
                .ToListAsync();

            foreach (var rs in requiredSkills)
            {
                await _context.RequiredSkills.AddAsync(new RequiredSkill
                {
                    WorkOrderId = workOrder.WorkOrderId,
                    SkillId = rs.SkillId,
                    MinimumProficiencyLevel = rs.MinimumProficiencyLevel,
                    PlannedHours = rs.PlannedHours,
                    CreatedDate = DateTime.Now
                });
            }

            var requiredMaterials = await _context.JobPlanRequiredMaterials
                .Where(x => x.JobPlanId == plan.JobPlanId.Value)
                .ToListAsync();

            foreach (var rm in requiredMaterials)
            {
                await _context.WorkOrderMaterials.AddAsync(new WorkOrderMaterial
                {
                    WorkOrderId = workOrder.WorkOrderId,
                    MaterialItemId = rm.MaterialItemId,
                    RequiredQuantity = rm.RequiredQuantity,
                    UomId = rm.UomId,
                    CreatedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }

        AdvanceSchedule(plan, DateTime.Now);

        await _context.SaveChangesAsync();

        return workOrderNumber;
    }

    /// <summary>
    /// سیاست زمان‌بندی (ScheduleMode):
    /// Fixed    — NextDueDate روی سررسید قبلی لنگر می‌شود و بدون «طوفان جبران» به اولین سررسید آینده می‌پرد.
    /// Floating — NextDueDate عمداً جلو نمی‌رود؛ پس از تکمیل WO از تاریخ تکمیل واقعی محاسبه می‌شود
    ///            (ReconcileFloatingSchedulesAsync). Guard ضد-تکرار تا آن زمان WO جدید را مسدود می‌کند.
    /// تناوب مؤثر: Override تاییدشده‌ی تخصیص، وگرنه IntervalDays خود Plan.
    /// </summary>
    private static void AdvanceSchedule(MaintenancePlan plan, DateTime now)
    {
        plan.LastExecutionDate = now;

        var interval = EffectiveInterval(plan);
        if (!interval.HasValue || interval <= 0) return;

        if (plan.ScheduleMode == RcmCodes.ScheduleMode.Floating) return;

        plan.NextDueDate = ScheduleCalculator.NextFixedDue(plan.NextDueDate, interval.Value, now);
    }

    private static int? EffectiveInterval(MaintenancePlan plan)
        => plan.MaintenanceStrategyAssignment is { OverrideStatus: RcmCodes.OverrideStatus.Approved, OverrideIntervalDays: > 0 } a
            ? a.OverrideIntervalDays
            : plan.IntervalDays;

    public async Task<int> ReconcileFloatingSchedulesAsync()
    {
        var plans = await _context.MaintenancePlans
            .Include(p => p.MaintenanceStrategyAssignment)
            .Where(p => p.IsActive && p.ScheduleMode == RcmCodes.ScheduleMode.Floating)
            .ToListAsync();

        var updated = 0;
        foreach (var plan in plans)
        {
            var interval = EffectiveInterval(plan);
            if (!interval.HasValue || interval <= 0) continue;

            var lastCompletion = await _context.WorkOrders
                .Where(w => w.MaintenancePlanId == plan.MaintenancePlanId && w.CompletionDate != null
                            && (w.Status == "Completed" || w.Status == "Closed"))
                .OrderByDescending(w => w.CompletionDate)
                .Select(w => w.CompletionDate)
                .FirstOrDefaultAsync();

            if (lastCompletion == null) continue;
            if (plan.LastCompletionDate.HasValue && lastCompletion <= plan.LastCompletionDate) continue;

            plan.LastCompletionDate = lastCompletion;
            plan.NextDueDate = ScheduleCalculator.NextFloatingDue(lastCompletion.Value, interval.Value);
            updated++;
        }

        if (updated > 0) await _context.SaveChangesAsync();
        return updated;
    }

    public async Task<PMGenerationResultDto> GenerateDueWorkOrdersAsync()
    {
        var now = DateTime.Now;

        // Floating: سررسید از تکمیل‌های واقعی به‌روز می‌شود، پیش از انتخاب Plan های سررسیدرسیده
        await ReconcileFloatingSchedulesAsync();

        // طبق بند ۵۶: Overdue/Due قطعی است - فقط NextDueDate واقعی <= اکنون، نه ساعت سمت کلاینت.
        var duePlanIds = await _context.MaintenancePlans
            .Where(x => x.IsActive && x.NextDueDate.HasValue && x.NextDueDate.Value <= now)
            .Select(x => x.MaintenancePlanId)
            .ToListAsync();

        var result = new PMGenerationResultDto();

        foreach (var planId in duePlanIds)
        {
            var workOrderNumber = await GenerateForPlanAsync(planId);
            if (workOrderNumber != null)
            {
                result.WorkOrdersGenerated++;
                result.GeneratedWorkOrderNumbers.Add(workOrderNumber);
            }
            else
            {
                result.PlansSkippedAlreadyHasOpenWorkOrder++;
            }
        }

        return result;
    }
}
