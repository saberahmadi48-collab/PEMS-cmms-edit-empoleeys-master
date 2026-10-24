using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 6» بند ۱۳/۵۳-۵۴: پیاده‌سازی پل RCM→عملیاتی.
///
/// قاعده‌ی بازاستفاده (بند ۵۴): کلید تشخیص «آیا استراتژی معادل از قبل موجود است» دقیقاً همان
/// چیزی است که خودِ MaintenanceStrategy از ابتدا طراحی شده بود تا نشان دهد - Equipment +
/// FailureModeCode (طبق کامنت خودِ Entity: «هر استراتژی به یک حالت خرابی مشخص وصل می‌شه»؛
/// نه یک کلید مصنوعی جدید). اگر موجود بود، بازاستفاده/به‌روزرسانی می‌شود؛ هرگز رکورد تکراری
/// ساخته نمی‌شود.
///
/// اگر RCMDecision یک PMTemplate واقعی توصیه کرده باشد (RecommendedPMTemplateId)، طبق طراحی
/// فاز ۲ (PMTemplate.TriggerType/IntervalDays/MeterType/MeterInterval دقیقاً همنام
/// MaintenancePlan) یک MaintenancePlan هم با کپی مستقیم همان مقادیر واقعی ساخته/به‌روز می‌شود -
/// هرگز یک تناوب جعلی اختراع نمی‌شود.
/// </summary>
public class RcmOperationalBridgeService : IRcmOperationalBridgeService
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numberingService;

    public RcmOperationalBridgeService(PEMSDbContext context, INumberingService numberingService)
    {
        _context = context;
        _numberingService = numberingService;
    }

    public async Task<ApplyRcmDecisionResultDto> ApplyApprovedDecisionAsync(int rcmDecisionId, int equipmentId, string appliedBy)
    {
        var decision = await _context.RCMDecisions
            .Include(x => x.RCMAnalysis)
            .Include(x => x.MaintenanceStrategyType)
            .Include(x => x.RecommendedPMTemplate)
            .Include(x => x.FMEAItem!).ThenInclude(x => x.FailureMode)
            .FirstOrDefaultAsync(x => x.RCMDecisionId == rcmDecisionId);

        if (decision == null)
            return new ApplyRcmDecisionResultDto { Success = false, Error = "RCM Decision یافت نشد." };

        // طبق بند ۲۰/۵۳: فقط توصیه‌ی تاییدشده قابل اعمال است - هرگز یک Draft.
        if (decision.Status != "APPROVED")
            return new ApplyRcmDecisionResultDto { Success = false, Error = $"فقط تصمیمات RCM با وضعیت APPROVED قابل اعمال هستند (وضعیت فعلی: {decision.Status})." };

        var equipment = await _context.Equipments.FirstOrDefaultAsync(x => x.EquipmentId == equipmentId && x.IsActive);
        if (equipment == null)
            return new ApplyRcmDecisionResultDto { Success = false, Error = "تجهیز یافت نشد یا فعال نیست." };

        // طبق بند ۵۳: این توصیه فقط برای همان EquipmentClass (یا کلاسی که تحلیل RCM برایش انجام
        // شده) معتبر است - هرگز به تجهیزی از کلاس نامرتبط اعمال نمی‌شود.
        if (equipment.EquipmentClassId != decision.RCMAnalysis?.EquipmentClassId)
            return new ApplyRcmDecisionResultDto { Success = false, Error = "کلاس تجهیز با کلاس تحلیل RCM این تصمیم مطابقت ندارد." };

        var failureMode = decision.FMEAItem?.FailureMode;
        var failureModeCode = failureMode?.Code;
        var result = new ApplyRcmDecisionResultDto();

        // ==================== MaintenanceStrategy (الگوی کلاس‌محور) + Assignment ====================
        // RCM کلاس‌محور: تصمیم تاییدشده‌ی RCM یک «الگو» روی کلاس تجهیز می‌سازد (نه یک استراتژی مستقل برای هر تگ)
        // و تگ از طریق Assignment به آن وصل می‌شود. همان قوانین JA1011 اینجا هم اعمال می‌شود.
        var strategyType = LegacyMaps.StrategyType(decision.MaintenanceStrategyType?.Code) ?? LegacyMaps.StrategyType(decision.DecisionType);
        var consequence = LegacyMaps.Consequence(decision.ConsequenceCategory) ?? RcmCodes.Consequence.Operational;
        if (strategyType == null)
            return new ApplyRcmDecisionResultDto { Success = false, Error = $"نوع تصمیم «{decision.MaintenanceStrategyType?.Code ?? decision.DecisionType}» به نوع استراتژی نگهداری قابل نگاشت نیست." };
        if (failureMode == null)
            return new ApplyRcmDecisionResultDto { Success = false, Error = "حالت خرابی کاتالوگ برای این تصمیم مشخص نیست؛ استراتژی بدون حالت خرابی ساخته نمی‌شود." };
        if (StrategyRules.RequiresProactiveTask(consequence, false) && StrategyRules.ReactiveTypes.Contains(strategyType))
            return new ApplyRcmDecisionResultDto { Success = false, Error = $"طبق منطق تصمیم RCM (SAE JA1011) برای پیامد {consequence} استراتژی {strategyType} مجاز نیست؛ کار پیشگیرانه یا بازطراحی لازم است." };

        // طبق بند ۵۱: appliedBy جدا از CreatedBy/ModifiedBy خودکار BaseEntity نگه داشته می‌شود
        var justificationNote = $"Applied by {appliedBy} from RCM Decision #{decision.RCMDecisionId} (approved by {decision.ApprovedBy} on {decision.ApprovedAt:yyyy-MM-dd}). {decision.Rationale}".Trim();

        var existingStrategy = await _context.MaintenanceStrategies
            .FirstOrDefaultAsync(x => x.IsActive && x.EquipmentClassId == equipment.EquipmentClassId &&
                x.FailureModeId == failureMode.FailureModeId && x.StrategyType == strategyType &&
                (x.Status == RcmCodes.Status.Active || x.Status == RcmCodes.Status.Approved));

        MaintenanceStrategy strategy;
        if (existingStrategy != null)
        {
            strategy = existingStrategy;
            result.MaintenanceStrategyWasReused = true;
        }
        else
        {
            strategy = new MaintenanceStrategy
            {
                EquipmentClassId = equipment.EquipmentClassId,
                StrategyType = strategyType,
                MaintenanceStrategyTypeId = decision.MaintenanceStrategyTypeId,
                FailureModeId = failureMode.FailureModeId,
                FailureModeCode = failureModeCode,
                ConsequenceCategory = consequence,
                IsHiddenFailure = consequence == RcmCodes.Consequence.Hidden,
                Justification = justificationNote,
                Source = RcmCodes.Source.RcmStudy,
                RecommendedTriggerType = RcmCodes.Trigger.Calendar,
                // تصمیم RCM پیش‌تر در گردش‌کار خودش تایید شده است (بند ۲۰)؛ الگو مستقیم Active ثبت و در تاریخچه اثبات می‌شود
                Status = RcmCodes.Status.Active,
                ApprovedBy = decision.ApprovedBy,
                ApprovedAt = decision.ApprovedAt,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            _context.MaintenanceStrategies.Add(strategy);
            await _context.SaveChangesAsync();

            _context.MaintenanceStrategyHistories.Add(new MaintenanceStrategyHistory
            {
                MaintenanceStrategyId = strategy.MaintenanceStrategyId,
                VersionNumber = strategy.VersionNumber,
                Action = RcmCodes.HistoryAction.Created,
                ToStatus = strategy.Status,
                Comment = $"From approved RCM Decision #{decision.RCMDecisionId}",
                ActorName = appliedBy,
                Timestamp = DateTime.Now
            });
            result.MaintenanceStrategyWasReused = false;
        }

        result.MaintenanceStrategyId = strategy.MaintenanceStrategyId;

        var assignment = await _context.MaintenanceStrategyAssignments
            .FirstOrDefaultAsync(a => a.MaintenanceStrategyId == strategy.MaintenanceStrategyId && a.EquipmentId == equipmentId && a.IsActive);
        if (assignment == null)
        {
            assignment = new MaintenanceStrategyAssignment
            {
                MaintenanceStrategyId = strategy.MaintenanceStrategyId,
                EquipmentId = equipmentId,
                Status = RcmCodes.AssignmentStatus.Active,
                AssignedDate = DateTime.Now,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            _context.MaintenanceStrategyAssignments.Add(assignment);
        }

        await _context.SaveChangesAsync();

        // ==================== MaintenancePlan: فقط اگر یک PMTemplate واقعی توصیه شده باشد ====================
        if (decision.RecommendedPMTemplateId.HasValue && decision.RecommendedPMTemplate != null)
        {
            var template = decision.RecommendedPMTemplate;

            var existingPlan = await _context.MaintenancePlans
                .FirstOrDefaultAsync(x => x.EquipmentId == equipmentId && x.IsActive &&
                    (x.MaintenanceStrategyAssignmentId == assignment.MaintenanceStrategyAssignmentId ||
                     x.MaintenanceStrategyId == result.MaintenanceStrategyId));

            if (existingPlan != null)
            {
                existingPlan.MaintenanceStrategyAssignmentId ??= assignment.MaintenanceStrategyAssignmentId;
                existingPlan.JobPlanId = template.JobPlanId;
                existingPlan.TriggerType = template.TriggerType;
                existingPlan.IntervalDays = template.IntervalDays;
                existingPlan.MeterType = template.MeterType;
                existingPlan.MeterInterval = template.MeterInterval;

                result.MaintenancePlanId = existingPlan.MaintenancePlanId;
                result.MaintenancePlanWasReused = true;
            }
            else
            {
                var planNumber = await _numberingService.GetNextNumberAsync("MaintenancePlan", equipment.TagNumber);

                var newPlan = new MaintenancePlan
                {
                    EquipmentId = equipmentId,
                    MaintenanceStrategyId = result.MaintenanceStrategyId!.Value,
                    MaintenanceStrategyAssignmentId = assignment.MaintenanceStrategyAssignmentId,
                    PlanNumber = planNumber,
                    Title = $"{template.Name} (from RCM Decision #{decision.RCMDecisionId})",
                    JobPlanId = template.JobPlanId,
                    TriggerType = template.TriggerType,
                    IntervalDays = template.IntervalDays,
                    MeterType = template.MeterType,
                    MeterInterval = template.MeterInterval,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.MaintenancePlans.Add(newPlan);
                await _context.SaveChangesAsync();

                result.MaintenancePlanId = newPlan.MaintenancePlanId;
                result.MaintenancePlanWasReused = false;
            }

            await _context.SaveChangesAsync();
        }

        result.Success = true;
        return result;
    }
}
