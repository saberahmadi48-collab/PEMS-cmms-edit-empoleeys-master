using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 9» بند ۱۹. مستقیماً از PEMSDbContext هم برای خواندن‌های ساده استفاده
/// می‌کند (همان الگوی CmmsOperationsService فاز ۸) - جایی که یک سرویس موجود دقیقاً این شکل
/// داده را برنمی‌گرداند.
/// </summary>
public class EquipmentIntelligenceService : IEquipmentIntelligenceService
{
    // همان قرارداد CmmsOperationsService (فاز ۸) - وضعیت‌های غیرنهایی WorkOrder
    private static readonly string[] OpenWorkOrderStatuses = { "Open", "Assigned", "InProgress", "OnHold" };

    private readonly PEMSDbContext _context;
    private readonly IReliabilityService _reliabilityService;
    private readonly IReliabilityAnalyticsService _analyticsService;
    private readonly IRamWeibullService _ramWeibullService;
    private readonly IRcmFmeaReadinessService _rcmFmeaReadinessService;
    private readonly IPredictiveMaintenanceService _predictiveMaintenanceService;
    private readonly IConditionMonitoringService _conditionMonitoringService;

    public EquipmentIntelligenceService(PEMSDbContext context, IReliabilityService reliabilityService,
        IReliabilityAnalyticsService analyticsService, IRamWeibullService ramWeibullService,
        IRcmFmeaReadinessService rcmFmeaReadinessService, IPredictiveMaintenanceService predictiveMaintenanceService,
        IConditionMonitoringService conditionMonitoringService)
    {
        _context = context;
        _reliabilityService = reliabilityService;
        _analyticsService = analyticsService;
        _ramWeibullService = ramWeibullService;
        _rcmFmeaReadinessService = rcmFmeaReadinessService;
        _predictiveMaintenanceService = predictiveMaintenanceService;
        _conditionMonitoringService = conditionMonitoringService;
    }

    public async Task<EquipmentIntelligenceProfileDto?> GetProfileAsync(int equipmentId)
    {
        var equipment = await _context.Equipments.AsNoTracking()
            .Include(x => x.EquipmentClassRef)
            .Include(x => x.Discipline)
            .Include(x => x.ParentEquipment)
            .FirstOrDefaultAsync(x => x.EquipmentId == equipmentId);
        if (equipment == null) return null;

        var dto = new EquipmentIntelligenceProfileDto
        {
            EquipmentId = equipment.EquipmentId,
            TagNumber = equipment.TagNumber,
            Name = equipment.Name,
            EquipmentClassId = equipment.EquipmentClassId,
            EquipmentClassName = equipment.EquipmentClassRef?.Name,
            DisciplineName = equipment.Discipline?.Name,
            ParentEquipmentId = equipment.ParentEquipmentId,
            ParentEquipmentTagNumber = equipment.ParentEquipment?.TagNumber
        };

        // Criticality
        var activeCriticality = await _context.EquipmentCriticalities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EquipmentId == equipmentId && x.IsActive);
        dto.CurrentCriticalityClass = activeCriticality?.CriticalityClass;
        dto.CurrentRiskLevel = activeCriticality?.RiskLevel;

        var latestAssessment = await _context.CriticalityAssessments.AsNoTracking()
            .Where(x => x.EquipmentId == equipmentId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.Status, x.FrameworkVersion, EvidenceCount = x.Evidence.Count })
            .FirstOrDefaultAsync();
        if (latestAssessment != null)
        {
            dto.LatestAssessmentStatus = latestAssessment.Status;
            dto.LatestAssessmentFrameworkVersion = latestAssessment.FrameworkVersion;
            dto.LatestAssessmentEvidenceCount = latestAssessment.EvidenceCount;
        }
        else
        {
            dto.UnavailableMetricReasons.Add("Criticality: هیچ ارزیابی (Assessment) ای هنوز برای این تجهیز ثبت نشده.");
        }

        // Maintenance
        dto.ActiveStrategyCount = await _context.MaintenanceStrategies.CountAsync(x => x.IsActive && (x.EquipmentId == equipmentId || x.Assignments.Any(a => a.IsActive && a.Status == "Active" && a.EquipmentId == equipmentId)));
        dto.ActiveMaintenancePlanCount = await _context.MaintenancePlans.CountAsync(x => x.EquipmentId == equipmentId && x.IsActive);

        var workOrderTypeCounts = await _context.WorkOrders.AsNoTracking()
            .Where(x => x.EquipmentId == equipmentId && x.IsActive)
            .GroupBy(x => x.WorkOrderType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        dto.PreventiveWorkOrderCount = workOrderTypeCounts.FirstOrDefault(x => x.Type == "Preventive")?.Count ?? 0;
        dto.CorrectiveWorkOrderCount = workOrderTypeCounts.FirstOrDefault(x => x.Type == "Corrective")?.Count ?? 0;
        dto.EmergencyWorkOrderCount = workOrderTypeCounts.FirstOrDefault(x => x.Type == "Emergency")?.Count ?? 0;
        dto.OpenBacklogWorkOrderCount = await _context.WorkOrders.CountAsync(x =>
            x.EquipmentId == equipmentId && x.IsActive && OpenWorkOrderStatuses.Contains(x.Status));

        // Reliability
        var reliability = await _reliabilityService.GetEquipmentReliabilityAsync(equipmentId);
        if (reliability != null)
        {
            dto.FailureCount = reliability.FailureCount;
            dto.TotalDowntimeHours = reliability.TotalDowntimeHours;
            dto.MTBFHours = reliability.MTBFHours;
            dto.MTTRHours = reliability.MTTRHours;
            dto.AvailabilityPercent = reliability.AvailabilityPercent;
            dto.ReliabilityDataStatus = reliability.MTBFHours.HasValue ? "READY"
                : reliability.FailureCount > 0 ? "INSUFFICIENT_DATA" : "NOT_READY";

            if (!reliability.MTBFHours.HasValue)
                dto.UnavailableMetricReasons.Add($"MTBF: حداقل ۲ رکورد خرابی لازم است (فعلاً {reliability.FailureCount}).");
        }
        else
        {
            dto.ReliabilityDataStatus = "NOT_READY";
            dto.UnavailableMetricReasons.Add("Reliability: هیچ رکورد خرابی‌ای برای این تجهیز ثبت نشده.");
        }

        // Failure Intelligence - top 3 failure modes
        dto.TopFailureModes = await _context.FailureRecords.AsNoTracking()
            .Where(x => x.EquipmentId == equipmentId && x.IsActive)
            .Select(x => x.FailureModeRef != null ? x.FailureModeRef.Name : x.FailureMode)
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()})")
            .ToListAsync();

        // RCM/FMEA (class-level)
        if (equipment.EquipmentClassId.HasValue)
        {
            var rcmFmea = await _rcmFmeaReadinessService.GetForEquipmentClassAsync(equipment.EquipmentClassId.Value);
            if (rcmFmea != null)
            {
                dto.FMEAReadiness = rcmFmea.FMEAReadiness;
                dto.RCMReadiness = rcmFmea.RCMReadiness;
            }
        }
        else
        {
            dto.UnavailableMetricReasons.Add("FMEA/RCM: تجهیز هنوز به یک EquipmentClass طبقه‌بندی نشده (FMEA/RCM در سطح کلاس تعریف می‌شوند).");
        }

        // Safety - از JobPlan متصل به MaintenancePlan های فعال این تجهیز
        var linkedJobPlans = await _context.MaintenancePlans.AsNoTracking()
            .Where(x => x.EquipmentId == equipmentId && x.IsActive && x.JobPlanId != null)
            .Select(x => x.JobPlan)
            .Where(jp => jp != null)
            .ToListAsync();
        dto.AnyLinkedJobPlanRequiresPermit = linkedJobPlans.Any(jp => jp!.PermitRequired);
        dto.AnyLinkedJobPlanRequiresLOTO = linkedJobPlans.Any(jp => jp!.LOTORequired);
        if (linkedJobPlans.Count == 0)
            dto.UnavailableMetricReasons.Add("Safety: هیچ JobPlan فعالی به این تجهیز متصل نیست.");

        // Intelligence
        var badActors = await _analyticsService.GetBadActorsAsync(null, null, top: 1000);
        dto.IsBadActor = badActors.Any(x => x.EquipmentId == equipmentId);

        var weibull = await _ramWeibullService.GetWeibullAnalysisAsync(equipmentId);
        dto.WeibullDataStatus = weibull.DataStatus;

        var predictiveReadiness = await _predictiveMaintenanceService.GetReadinessAsync();
        dto.PredictiveStatus = predictiveReadiness.ProductionModelStatus;

        var conditionPoints = await _conditionMonitoringService.GetPointsForEquipmentAsync(equipmentId);
        dto.ConditionMonitoringStatus = conditionPoints.Count == 0 ? "NOT_READY"
            : conditionPoints.Any(p => p.ReadingCount > 0) ? "PARTIAL" : "NOT_READY";

        return dto;
    }
}
