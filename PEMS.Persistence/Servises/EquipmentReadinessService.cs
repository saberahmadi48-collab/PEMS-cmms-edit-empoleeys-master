using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 7.5 — CMMS Operational Data Readiness & Master Data Activation»: پیاده‌سازی
/// IEquipmentReadinessService. همه‌ی متدها فقط‌خواندنی، SQL-Aggregated (EXISTS/GROUP BY/COUNT
/// در دیتابیس) هستند و هرگز ۲۱٬۶۲۵ ردیف Equipment را کامل به حافظه بارگذاری نمی‌کنند.
/// پوشش سطح-کلاس (PMTemplate/JobPlan/FailureMode/Activity/Standard) از IEquipmentClassCoverageService
/// موجود (فاز ۳.۵) بازاستفاده می‌شود - هرگز دوباره محاسبه نمی‌شود.
/// </summary>
public class EquipmentReadinessService : IEquipmentReadinessService
{
    private readonly PEMSDbContext _context;
    private readonly IEquipmentClassCoverageService _coverageService;

    public EquipmentReadinessService(PEMSDbContext context, IEquipmentClassCoverageService coverageService)
    {
        _context = context;
        _coverageService = coverageService;
    }

    public async Task<CmmsOperationalReadinessDto> GetOperationalReadinessSummaryAsync()
    {
        var dto = new CmmsOperationalReadinessDto
        {
            TotalActiveEquipment = await _context.Equipments.CountAsync(x => x.IsActive),
            ClassifiedEquipment = await _context.Equipments.CountAsync(x => x.IsActive && x.EquipmentClassId != null),

            // طبق بند ۷: هر بُعد با EXISTS مستقل - ترکیب هیچ‌کدام شرط بُعد دیگر نیست
            CriticalityReadyEquipment = await _context.Equipments
                .CountAsync(x => x.IsActive && _context.EquipmentCriticalities.Any(c => c.IsActive && c.EquipmentId == x.EquipmentId)),

            StrategyReadyEquipment = await _context.Equipments
                .CountAsync(x => x.IsActive && (_context.MaintenanceStrategies.Any(s => s.IsActive && s.EquipmentId == x.EquipmentId) || _context.MaintenanceStrategyAssignments.Any(a => a.IsActive && a.Status == "Active" && a.EquipmentId == x.EquipmentId && a.MaintenanceStrategy!.IsActive))),

            PmPlanReadyEquipment = await _context.Equipments
                .CountAsync(x => x.IsActive && _context.MaintenancePlans.Any(p => p.IsActive && p.EquipmentId == x.EquipmentId)),

            // طبق بند ۱۴/۱۵: JobPlan/Safety آماده یعنی Plan فعال موجود دارد که واقعاً به یک
            // JobPlan واقعی وصل است - نه صرفاً وجود یک Plan خالی
            JobPlanReadyEquipment = await _context.Equipments
                .CountAsync(x => x.IsActive && _context.MaintenancePlans.Any(p => p.IsActive && p.EquipmentId == x.EquipmentId && p.JobPlanId != null))
        };

        // طبق بند ۱۵: در همین مرحله (پیش از وجود WorkOrder)، «ایمنی آماده» یعنی JobPlan واقعی
        // وصل شده (که فیلدهای PermitRequired/LOTORequired آن آگاهانه پیکربندی شده‌اند، نه
        // پیش‌فرض بی‌توجه) - دقیقاً همان مجموعه‌ی JobPlanReady. بررسی واقعی زمان اجرا همچنان
        // فقط توسط WorkOrderReleaseGateService (فاز ۶) انجام می‌شود.
        dto.SafetyReadyEquipment = dto.JobPlanReadyEquipment;

        dto.ExecutionReadyEquipment = await _context.Equipments
            .Where(x => x.IsActive && x.EquipmentClassId != null)
            .Where(x => _context.EquipmentCriticalities.Any(c => c.IsActive && c.EquipmentId == x.EquipmentId))
            .Where(x => (_context.MaintenanceStrategies.Any(s => s.IsActive && s.EquipmentId == x.EquipmentId) || _context.MaintenanceStrategyAssignments.Any(a => a.IsActive && a.Status == "Active" && a.EquipmentId == x.EquipmentId && a.MaintenanceStrategy!.IsActive)))
            .Where(x => _context.MaintenancePlans.Any(p => p.IsActive && p.EquipmentId == x.EquipmentId && p.JobPlanId != null))
            .CountAsync();

        return dto;
    }

    public async Task<List<ClassActivationBacklogItemDto>> GetClassActivationBacklogAsync()
    {
        var coverage = await _coverageService.GetCoverageMatrixAsync();

        // طبق بند ۹: چهار Query گروه‌بندی‌شده‌ی مستقل - نه یک Query جدا به‌ازای هر کلاس (~۷۰ کلاس)
        var criticalityByClass = await CountEquipmentByClassAsync(
            _context.EquipmentCriticalities.Where(x => x.IsActive).Select(x => x.EquipmentId));

        var strategyByClass = await CountEquipmentByClassAsync(
            _context.MaintenanceStrategies.Where(x => x.IsActive && x.EquipmentId != null).Select(x => x.EquipmentId!.Value)
                .Union(_context.MaintenanceStrategyAssignments.Where(a => a.IsActive && a.Status == "Active" && a.MaintenanceStrategy!.IsActive).Select(a => a.EquipmentId)));

        var planByClass = await CountEquipmentByClassAsync(
            _context.MaintenancePlans.Where(x => x.IsActive).Select(x => x.EquipmentId));

        var executionReadyByClass = await _context.Equipments.AsNoTracking()
            .Where(x => x.IsActive && x.EquipmentClassId != null)
            .Where(x => _context.EquipmentCriticalities.Any(c => c.IsActive && c.EquipmentId == x.EquipmentId))
            .Where(x => (_context.MaintenanceStrategies.Any(s => s.IsActive && s.EquipmentId == x.EquipmentId) || _context.MaintenanceStrategyAssignments.Any(a => a.IsActive && a.Status == "Active" && a.EquipmentId == x.EquipmentId && a.MaintenanceStrategy!.IsActive)))
            .Where(x => _context.MaintenancePlans.Any(p => p.IsActive && p.EquipmentId == x.EquipmentId && p.JobPlanId != null))
            .GroupBy(x => x.EquipmentClassId!.Value)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClassId, x => x.Count);

        var result = new List<ClassActivationBacklogItemDto>();

        foreach (var cls in coverage.Classes.Where(x => x.EquipmentCount > 0))
        {
            var item = new ClassActivationBacklogItemDto
            {
                EquipmentClassId = cls.EquipmentClassId,
                Code = cls.Code,
                Name = cls.Name,
                EquipmentCount = cls.EquipmentCount,
                ClassifiedIntoThisClassCount = cls.EquipmentCount,
                CriticalityReadyCount = criticalityByClass.GetValueOrDefault(cls.EquipmentClassId, 0),
                StrategyReadyCount = strategyByClass.GetValueOrDefault(cls.EquipmentClassId, 0),
                PmPlanReadyCount = planByClass.GetValueOrDefault(cls.EquipmentClassId, 0),
                ExecutionReadyCount = executionReadyByClass.GetValueOrDefault(cls.EquipmentClassId, 0),
                HasPMTemplateCoverage = cls.TotalPMTemplateCoverage > 0,
                HasJobPlanCoverage = (cls.DirectJobPlanCount + cls.InheritedJobPlanCount) > 0,
                HasFailureModeCoverage = cls.TotalFailureModeCoverage > 0,
                HasMaintenanceActivityCoverage = cls.TotalActivityCoverage > 0,
                HasStandardReferenceCoverage = (cls.DirectStandardReferenceCount + cls.InheritedStandardReferenceCount) > 0
            };

            // طبق بند ۱۹/۲۶: عوامل شفاف - نه یک امتیاز پنهان
            if (item.HasPMTemplateCoverage) item.PriorityFactors.Add("PM Template already defined for this class");
            if (item.HasFailureModeCoverage) item.PriorityFactors.Add("Failure taxonomy already defined for this class");
            if (item.HasJobPlanCoverage) item.PriorityFactors.Add("Job Plan already available for this class");
            if (item.EquipmentCount >= 10) item.PriorityFactors.Add($"Significant equipment population ({item.EquipmentCount})");

            if (item.CriticalityReadyCount == 0) item.MissingRequirements.Add("CRITICALITY ASSESSMENT REQUIRED");
            if (!item.HasDefensibleBasis) item.MissingRequirements.Add("No defensible basis yet (no PM Template or Failure Mode defined for this class)");
            if (item.StrategyReadyCount == 0 && item.HasDefensibleBasis) item.MissingRequirements.Add("STRATEGY_REQUIRED - defensible basis exists but no MaintenanceStrategy created yet");
            if (!item.HasJobPlanCoverage) item.MissingRequirements.Add("JOB_PLAN_REQUIRED");

            result.Add(item);
        }

        // طبق بند ۸: اول کلاس‌هایی که مبنای مدافعه‌پذیر دارند (PMTemplate/FailureMode موجود)،
        // بعد بر اساس جمعیت تجهیز (بیشترین اثر بالقوه) - هر دو عامل صریح، نه یک فرمول ترکیبی مبهم.
        return result
            .OrderByDescending(x => x.HasDefensibleBasis)
            .ThenByDescending(x => x.EquipmentCount)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private async Task<Dictionary<int, int>> CountEquipmentByClassAsync(IQueryable<int> equipmentIdsQuery)
    {
        return await _context.Equipments.AsNoTracking()
            .Where(x => x.IsActive && x.EquipmentClassId != null && equipmentIdsQuery.Contains(x.EquipmentId))
            .GroupBy(x => x.EquipmentClassId!.Value)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClassId, x => x.Count);
    }

    public async Task<PagedResult<EquipmentReadinessRowDto>> GetEquipmentReadinessAsync(int? equipmentClassId, int page = 1, int pageSize = 50)
    {
        var query = _context.Equipments.AsNoTracking().Where(x => x.IsActive);
        if (equipmentClassId.HasValue) query = query.Where(x => x.EquipmentClassId == equipmentClassId.Value);

        var totalCount = await query.CountAsync();
        page = page < 1 ? 1 : page;
        pageSize = DataGridPageSize.Normalize(pageSize);

        var pageRows = await query
            .OrderBy(x => x.TagNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.EquipmentId,
                x.TagNumber,
                x.EquipmentClassId,
                EquipmentClassName = x.EquipmentClassRef != null ? x.EquipmentClassRef.Name : null
            })
            .ToListAsync();

        var pageEquipmentIds = pageRows.Select(x => x.EquipmentId).ToList();

        // طبق بند ۳۹: چهار Query کوچک محدود به همین صفحه (نه کل ۲۱٬۶۲۵ ردیف) - بدون N+1
        var criticalIds = await _context.EquipmentCriticalities.AsNoTracking()
            .Where(x => x.IsActive && pageEquipmentIds.Contains(x.EquipmentId))
            .Select(x => x.EquipmentId).Distinct().ToListAsync();

        var strategyIds = (await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.IsActive && x.EquipmentId != null && pageEquipmentIds.Contains(x.EquipmentId.Value))
            .Select(x => x.EquipmentId!.Value).Distinct().ToListAsync())
            .Union(await _context.MaintenanceStrategyAssignments.AsNoTracking()
                .Where(a => a.IsActive && a.Status == "Active" && a.MaintenanceStrategy!.IsActive && pageEquipmentIds.Contains(a.EquipmentId))
                .Select(a => a.EquipmentId).Distinct().ToListAsync())
            .ToList();

        var plans = await _context.MaintenancePlans.AsNoTracking()
            .Where(x => x.IsActive && pageEquipmentIds.Contains(x.EquipmentId))
            .Select(x => new { x.EquipmentId, x.JobPlanId })
            .ToListAsync();
        var plannedIds = plans.Select(x => x.EquipmentId).Distinct().ToHashSet();
        var jobPlanLinkedIds = plans.Where(x => x.JobPlanId != null).Select(x => x.EquipmentId).Distinct().ToHashSet();

        var criticalSet = criticalIds.ToHashSet();
        var strategySet = strategyIds.ToHashSet();

        var items = pageRows.Select(x =>
        {
            var classified = x.EquipmentClassId != null;
            var hasCriticality = criticalSet.Contains(x.EquipmentId);
            var hasStrategy = strategySet.Contains(x.EquipmentId);
            var hasPlan = plannedIds.Contains(x.EquipmentId);
            var hasJobPlanLink = jobPlanLinkedIds.Contains(x.EquipmentId);

            var row = new EquipmentReadinessRowDto
            {
                EquipmentId = x.EquipmentId,
                TagNumber = x.TagNumber,
                EquipmentClassName = x.EquipmentClassName,
                Classification = classified ? ReadinessState.Ready : ReadinessState.NotReady,
                Criticality = hasCriticality ? ReadinessState.Ready : ReadinessState.NotReady,
                Strategy = hasStrategy ? ReadinessState.Ready : ReadinessState.NotReady,
                PmPlan = hasPlan ? ReadinessState.Ready : ReadinessState.NotReady,
                // طبق بند ۱۴: بدون Plan، هنوز چیزی برای ارزیابی JobPlan نیست
                JobPlan = !hasPlan ? ReadinessState.NotApplicable
                    : hasJobPlanLink ? ReadinessState.Ready : ReadinessState.MissingMasterData,
                Safety = !hasPlan ? ReadinessState.NotApplicable
                    : hasJobPlanLink ? ReadinessState.Ready : ReadinessState.BlockedBySafety
            };

            row.ExecutionReady = classified && hasCriticality && hasStrategy && hasPlan && hasJobPlanLink;
            return row;
        }).ToList();

        return new PagedResult<EquipmentReadinessRowDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }
}
