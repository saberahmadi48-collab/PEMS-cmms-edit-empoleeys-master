using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// ==============================================================================
/// ساخت Maintenance Plan از یک «تخصیص» (نسخه‌ی استراتژی + تگ). تجهیز هرگز از کلاینت نمی‌آید؛
/// از تخصیص مشتق می‌شود، پس ناهم‌خوانی equipmentId/strategyId (و خطای FK 500) غیرممکن است.
/// همه‌ی قواعد در PlanRules (سمت سرور): RTF/Corrective مسدود، فقط استراتژی Approved/Active،
/// تریگر/تناوب/سررسید/Job Plan متناسب با نوع، عنوان معتبر و یکتا برای هر تجهیز.
/// ==============================================================================
/// </summary>
public class MaintenancePlanCommandService : IMaintenancePlanCommandService
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numbering;

    public MaintenancePlanCommandService(PEMSDbContext context, INumberingService numbering)
    {
        _context = context;
        _numbering = numbering;
    }

    public async Task<PlanFormContextDto?> GetFormContextAsync(int assignmentId)
    {
        var a = await LoadAssignmentAsync(assignmentId);
        if (a == null) return null;

        var identity = await EquipmentIdentityProjection.Project(_context,
            _context.Equipments.AsNoTracking().Where(e => e.EquipmentId == a.EquipmentId)).FirstAsync();

        var s = a.MaintenanceStrategy!;
        var baseInterval = a.OverrideStatus == RcmCodes.OverrideStatus.Approved && a.OverrideIntervalDays.HasValue
            ? a.OverrideIntervalDays
            : (RcmCodes.StrategyType.ConditionBased.Contains(s.StrategyType) ? s.InspectionIntervalDays : null) ?? s.DefaultIntervalDays;

        // مقدار تأییدشده‌ی Override دقیقاً همان است (ضریب بحرانی فقط روی پیش‌فرض الگو اعمال می‌شود)
        var suggested = a.OverrideStatus == RcmCodes.OverrideStatus.Approved && a.OverrideIntervalDays.HasValue
            ? a.OverrideIntervalDays
            : StrategyRules.SuggestedIntervalDays(baseInterval, identity.CriticalityClass);

        return new PlanFormContextDto
        {
            AssignmentId = a.MaintenanceStrategyAssignmentId,
            StrategyId = s.MaintenanceStrategyId,
            StrategyVersion = s.VersionNumber,
            StrategyType = s.StrategyType,
            ConsequenceCategory = s.ConsequenceCategory,
            FailureModeName = s.FailureMode?.Name,
            FailureModeNameFa = s.FailureMode?.NameFa,
            RecommendedTriggerType = RcmCodes.StrategyType.ConditionBased.Contains(s.StrategyType)
                ? RcmCodes.Trigger.Calendar : s.RecommendedTriggerType,
            SuggestedIntervalDays = suggested,
            InspectionIntervalDays = s.InspectionIntervalDays,
            SuggestedTitle = PlanRules.SuggestTitle(identity.Tag, s.FailureMode?.Name, s.StrategyType),
            Equipment = identity
        };
    }

    public async Task<OpResult> CreateAsync(PlanInputDto input, StrategyActor actor, DateTime? now = null)
    {
        var clock = now ?? DateTime.Now;

        var a = await LoadAssignmentAsync(input.MaintenanceStrategyAssignmentId);
        if (a == null) return OpResult.Fail(new RuleError("Rcm_Err_AssignmentNotFound", "assignmentId"));

        var s = a.MaintenanceStrategy!;
        var trigger = string.IsNullOrWhiteSpace(input.TriggerType)
            ? (RcmCodes.StrategyType.ConditionBased.Contains(s.StrategyType) ? RcmCodes.Trigger.Calendar : s.RecommendedTriggerType)
            : input.TriggerType.Trim();

        var (interval, nextDue, meterType, meterInterval) =
            PlanRules.Normalize(trigger, input.IntervalDays, input.NextDueDate, input.MeterType, input.MeterInterval);

        var errors = PlanRules.Validate(new PlanRuleInput
        {
            StrategyType = s.StrategyType,
            StrategyStatus = s.IsActive ? s.Status : RcmCodes.Status.Retired,
            AssignmentActive = a.IsActive && a.Status == RcmCodes.AssignmentStatus.Active,
            Title = input.Title,
            TriggerType = trigger,
            IntervalDays = interval,
            MeterType = meterType,
            MeterInterval = meterInterval,
            NextDueDate = nextDue,
            JobPlanId = input.JobPlanId,
            ScheduleMode = input.ScheduleMode,
            StrategyInspectionIntervalDays = s.InspectionIntervalDays,
            AllowPastDueDate = input.AllowPastDueDate
        }, clock);

        if (input.JobPlanId.HasValue && !await _context.JobPlans.AnyAsync(j => j.JobPlanId == input.JobPlanId && j.IsActive))
            errors.Add(new RuleError("Rcm_Err_JobPlanNotFound", "jobPlanId"));

        var title = (input.Title ?? "").Trim();
        if (errors.All(e => e.Code != "Rcm_Err_PlanTitleInvalid"))
        {
            var lowered = title.ToLower();
            var titleTaken = await _context.MaintenancePlans.AnyAsync(p =>
                p.IsActive && p.EquipmentId == a.EquipmentId && p.Title.ToLower() == lowered);
            if (titleTaken) errors.Add(new RuleError("Rcm_Err_PlanTitleDuplicate", "title"));
        }

        if (errors.Count > 0) return OpResult.Fail(errors);

        // تاریخ سررسید برای تریگر تقویمی در صورت خالی‌بودن: امروز + تناوب
        if (interval.HasValue && !nextDue.HasValue) nextDue = clock.AddDays(interval.Value);

        var planNumber = await _numbering.GetNextNumberAsync("MaintenancePlan", a.Equipment?.TagNumber ?? a.EquipmentId.ToString());

        var plan = new MaintenancePlan
        {
            EquipmentId = a.EquipmentId,                       // مشتق از تخصیص، نه ورودی کلاینت
            MaintenanceStrategyId = a.MaintenanceStrategyId,
            MaintenanceStrategyAssignmentId = a.MaintenanceStrategyAssignmentId,
            PlanNumber = planNumber,
            Title = title,
            TriggerType = trigger,
            IntervalDays = interval,
            MeterType = meterType,
            MeterInterval = meterInterval,
            NextDueDate = nextDue,
            Description = input.Description,
            JobPlanId = input.JobPlanId,
            ScheduleMode = string.IsNullOrWhiteSpace(input.ScheduleMode) ? RcmCodes.ScheduleMode.Fixed : input.ScheduleMode,
            IsActive = true,
            CreatedDate = clock
        };

        _context.MaintenancePlans.Add(plan);
        await _context.SaveChangesAsync();
        return OpResult.Ok(plan.MaintenancePlanId);
    }

    public async Task<OpResult> DeactivateAsync(int maintenancePlanId, StrategyActor actor)
    {
        var plan = await _context.MaintenancePlans.FirstOrDefaultAsync(x => x.MaintenancePlanId == maintenancePlanId);
        if (plan == null) return OpResult.Fail(new RuleError("Rcm_Err_PlanNotFound"));
        plan.IsActive = false;
        await _context.SaveChangesAsync();
        return OpResult.Ok(plan.MaintenancePlanId);
    }

    private Task<MaintenanceStrategyAssignment?> LoadAssignmentAsync(int assignmentId)
        => _context.MaintenanceStrategyAssignments
            .Include(x => x.Equipment)
            .Include(x => x.MaintenanceStrategy!).ThenInclude(s => s.FailureMode)
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyAssignmentId == assignmentId);
}
