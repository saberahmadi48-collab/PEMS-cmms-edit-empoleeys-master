using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق سند «PEMS — Global Data Grid Standard»: فیلتر و صفحه‌بندی همه در SQL انجام می‌شوند.
/// از RCM کلاس‌محور به بعد هر ردیف یک «الگو»ی استراتژی روی کلاس تجهیز است؛ تگ‌ها از
/// MaintenanceStrategyAssignment می‌آیند (ستون قدیمی EquipmentId فقط برای داده‌ی تک‌تگی پیشین).
/// </summary>
public class MaintenanceStrategyService : IMaintenanceStrategyService
{
    private readonly PEMSDbContext _context;

    public MaintenanceStrategyService(PEMSDbContext context)
    {
        _context = context;
    }

    public Task<PagedResult<MaintenanceStrategyListDto>> GetListAsync(int page = 1, int pageSize = 50, int? equipmentId = null)
        => GetListAsync(new StrategyListFilter { Page = page, PageSize = pageSize, EquipmentId = equipmentId });

    public async Task<PagedResult<MaintenanceStrategyListDto>> GetListAsync(StrategyListFilter filter)
    {
        var query = _context.MaintenanceStrategies.AsQueryable();

        if (!filter.IncludeInactive)
            query = query.Where(x => x.IsActive);

        if (filter.EquipmentId.HasValue)
        {
            var equipmentId = filter.EquipmentId.Value;
            query = query.Where(x => x.EquipmentId == equipmentId ||
                x.Assignments.Any(a => a.IsActive && a.EquipmentId == equipmentId));
        }

        if (filter.EquipmentClassId.HasValue)
            query = query.Where(x => x.EquipmentClassId == filter.EquipmentClassId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(x => x.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.StrategyType))
            query = query.Where(x => x.StrategyType == filter.StrategyType);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                (x.Title != null && x.Title.Contains(term)) ||
                (x.FailureModeCode != null && x.FailureModeCode.Contains(term)) ||
                (x.FailureMode != null && (x.FailureMode.Name.Contains(term) || (x.FailureMode.NameFa != null && x.FailureMode.NameFa.Contains(term)))) ||
                (x.EquipmentClass != null && (x.EquipmentClass.Name.Contains(term) || x.EquipmentClass.Code.Contains(term))) ||
                (x.Equipment != null && x.Equipment.TagNumber.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = DataGridPageSize.Normalize(filter.PageSize);
        var now = DateTime.Now;

        var items = await query
            .OrderBy(x => x.Equipment != null ? x.Equipment.TagNumber : (x.EquipmentClass != null ? x.EquipmentClass.Name : ""))
            .ThenBy(x => x.FailureModeCode)
            .ThenBy(x => x.MaintenanceStrategyId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaintenanceStrategyListDto
            {
                MaintenanceStrategyId = x.MaintenanceStrategyId,
                VersionNumber = x.VersionNumber,
                Status = x.Status,
                Title = x.Title,
                EquipmentId = x.EquipmentId,
                EquipmentTagNumber = x.Equipment != null ? x.Equipment.TagNumber : "",
                EquipmentClassId = x.EquipmentClassId,
                EquipmentClassName = x.EquipmentClass != null ? x.EquipmentClass.Name : null,
                EquipmentClassCode = x.EquipmentClass != null ? x.EquipmentClass.Code : null,
                AssignedTagCount = x.Assignments.Count(a => a.IsActive && a.Status == RcmCodes.AssignmentStatus.Active),
                StrategyType = x.StrategyType,
                FailureModeId = x.FailureModeId,
                FailureModeCode = x.FailureModeCode,
                FailureModeName = x.FailureMode != null ? x.FailureMode.Name : null,
                FailureModeNameFa = x.FailureMode != null ? x.FailureMode.NameFa : null,
                ConsequenceCategory = x.ConsequenceCategory,
                IsHiddenFailure = x.IsHiddenFailure,
                Frequency = x.Frequency,
                Justification = x.Justification,
                ReviewDate = x.ReviewDate,
                ReviewOverdue = x.ReviewDate != null && x.ReviewDate < now && x.Status == RcmCodes.Status.Active
            })
            .ToListAsync();

        return new PagedResult<MaintenanceStrategyListDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<StrategyDetailDto?> GetDetailAsync(int maintenanceStrategyId)
    {
        var s = await _context.MaintenanceStrategies.AsNoTracking()
            .Include(x => x.EquipmentClass)
            .Include(x => x.FailureMode)
            .Include(x => x.OwnerDiscipline)
            .Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (s == null) return null;

        var dto = new StrategyDetailDto
        {
            MaintenanceStrategyId = s.MaintenanceStrategyId,
            VersionNumber = s.VersionNumber,
            Status = s.Status,
            FamilyId = s.FamilyId,
            EquipmentClassName = s.EquipmentClass?.Name,
            FailureModeName = s.FailureMode?.Name,
            FailureModeNameFa = s.FailureMode?.NameFa,
            OwnerDisciplineName = s.OwnerDiscipline?.Name,
            LegacyEquipmentId = s.EquipmentId,
            LegacyEquipmentTag = s.Equipment?.TagNumber,
            ApprovedBy = s.ApprovedBy,
            ApprovedAt = s.ApprovedAt,
            AuthorEmployeeId = s.AuthorEmployeeId,
            IsActive = s.IsActive,
            Input = ToInput(s)
        };

        dto.Assignments = await _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.MaintenanceStrategyId == maintenanceStrategyId && a.IsActive)
            .OrderBy(a => a.Equipment!.TagNumber)
            .Select(a => new StrategyAssignmentRowDto
            {
                AssignmentId = a.MaintenanceStrategyAssignmentId,
                MaintenanceStrategyId = a.MaintenanceStrategyId,
                StrategyVersion = a.MaintenanceStrategy!.VersionNumber,
                Status = a.Status,
                OverrideStatus = a.OverrideStatus,
                OverrideIntervalDays = a.OverrideIntervalDays,
                OverrideTaskNote = a.OverrideTaskNote,
                OverrideJustification = a.OverrideJustification,
                AssignedDate = a.AssignedDate,
                Equipment = new EquipmentIdentityDto
                {
                    EquipmentId = a.EquipmentId,
                    Tag = a.Equipment!.TagNumber,
                    Name = a.Equipment.Name,
                    EquipmentClassId = a.Equipment.EquipmentClassId,
                    EquipmentClassName = a.Equipment.EquipmentClassRef != null ? a.Equipment.EquipmentClassRef.Name : null,
                    Area = a.Equipment.FunctionalLocationRef != null && a.Equipment.FunctionalLocationRef.Area != null
                        ? a.Equipment.FunctionalLocationRef.Area : a.Equipment.Area
                },
                PlanCount = _context.MaintenancePlans.Count(p => p.IsActive && p.MaintenanceStrategyAssignmentId == a.MaintenanceStrategyAssignmentId)
            })
            .Take(500)
            .ToListAsync();

        var tagIds = dto.Assignments.Select(a => a.Equipment.EquipmentId).ToList();
        var crit = await _context.EquipmentCriticalities.AsNoTracking()
            .Where(c => c.IsActive && tagIds.Contains(c.EquipmentId))
            .Select(c => new { c.EquipmentId, c.CriticalityClass }).ToListAsync();
        foreach (var a in dto.Assignments)
            a.Equipment.CriticalityClass = crit.FirstOrDefault(c => c.EquipmentId == a.Equipment.EquipmentId)?.CriticalityClass ?? RcmCodes.Criticality.Unrated;

        dto.History = await GetHistoryAsync(maintenanceStrategyId);

        dto.Versions = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.FamilyId == s.FamilyId)
            .OrderBy(x => x.VersionNumber)
            .Select(x => new StrategyVersionRowDto
            {
                MaintenanceStrategyId = x.MaintenanceStrategyId,
                VersionNumber = x.VersionNumber,
                Status = x.Status,
                ApprovedAt = x.ApprovedAt
            }).ToListAsync();

        // «کدام Plan از کدام نسخه استفاده می‌کند» - همه‌ی نسخه‌های این خانواده
        var familyIds = dto.Versions.Select(v => v.MaintenanceStrategyId).ToList();
        var plans = await _context.MaintenancePlans.AsNoTracking()
            .Where(p => p.IsActive && familyIds.Contains(p.MaintenanceStrategyId))
            .OrderBy(p => p.PlanNumber)
            .Select(p => new StrategyPlanUsageDto
            {
                MaintenancePlanId = p.MaintenancePlanId,
                PlanNumber = p.PlanNumber,
                Title = p.Title,
                Tag = p.Equipment != null ? p.Equipment.TagNumber : "",
                StrategyId = p.MaintenanceStrategyId,
                StrategyVersion = p.MaintenanceStrategy!.VersionNumber,
                // Plan روی نسخه‌ای مانده که دیگر نسخه‌ی جاری تخصیص نیست
                OutOfSync = p.MaintenanceStrategyAssignment != null
                    && p.MaintenanceStrategyAssignment.MaintenanceStrategyId != p.MaintenanceStrategyId
            })
            .Take(500)
            .ToListAsync();
        dto.Plans = plans;

        return dto;
    }

    public async Task<List<StrategyHistoryRowDto>> GetHistoryAsync(int maintenanceStrategyId)
    {
        var family = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.MaintenanceStrategyId == maintenanceStrategyId)
            .Select(x => x.FamilyId).FirstOrDefaultAsync();

        var ids = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.FamilyId == family || x.MaintenanceStrategyId == maintenanceStrategyId)
            .Select(x => x.MaintenanceStrategyId).ToListAsync();

        return await _context.MaintenanceStrategyHistories.AsNoTracking()
            .Where(h => ids.Contains(h.MaintenanceStrategyId))
            .OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.MaintenanceStrategyHistoryId)
            .Select(h => new StrategyHistoryRowDto
            {
                VersionNumber = h.VersionNumber,
                Action = h.Action,
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                ChangesSummary = h.ChangesSummary,
                Comment = h.Comment,
                ActorName = h.ActorName,
                Timestamp = h.Timestamp
            })
            .Take(300)
            .ToListAsync();
    }

    internal static StrategyInputDto ToInput(Domain.Entities.MaintenanceStrategy s) => new()
    {
        StrategyType = s.StrategyType,
        ConsequenceCategory = s.ConsequenceCategory,
        IsHiddenFailure = s.IsHiddenFailure,
        EquipmentClassId = s.EquipmentClassId,
        FailureModeId = s.FailureModeId,
        FailureCauseId = s.FailureCauseId,
        Title = s.Title,
        FunctionalFailure = s.FunctionalFailure,
        FailureEffect = s.FailureEffect,
        Justification = s.Justification,
        RecommendedTriggerType = s.RecommendedTriggerType,
        Frequency = s.Frequency,
        DefaultIntervalDays = s.DefaultIntervalDays,
        Description = s.Description,
        StartDate = s.StartDate,
        TaskType = s.TaskType,
        OwnerDisciplineId = s.OwnerDisciplineId,
        RequiredSkills = s.RequiredSkills,
        EstimatedDurationHours = s.EstimatedDurationHours,
        CbmParameter = s.CbmParameter,
        ThresholdUnit = s.ThresholdUnit,
        AlarmThreshold = s.AlarmThreshold,
        TripThreshold = s.TripThreshold,
        PfIntervalDays = s.PfIntervalDays,
        InspectionIntervalDays = s.InspectionIntervalDays,
        Source = s.Source,
        ReviewDate = s.ReviewDate,
        ConditionMonitoringPointId = s.ConditionMonitoringPointId,
        RowVersion = s.RowVersion
    };
}
