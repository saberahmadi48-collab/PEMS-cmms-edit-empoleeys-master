using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 7 — Maintenance Intelligence & Reliability Operationalization»: پیاده‌سازی
/// IReliabilityIntelligenceService - همه‌ی متدها فقط‌خواندنی و روی داده‌ی واقعی موجود
/// (WorkOrder/FailureRecord/MaintenancePlan/EquipmentCriticality) هستند. هیچ‌جا مقدار جعل
/// نمی‌شود؛ هرجا داده کافی نیست، Null/DataStatus صریح برمی‌گردد (بند ۳۸).
/// </summary>
public class ReliabilityIntelligenceService : IReliabilityIntelligenceService
{
    private readonly PEMSDbContext _context;

    // طبق بند ۱۳: فقط این مقادیر از WorkOrderType شناخته‌شده‌اند (همان قرارداد قبلاً استفاده‌شده
    // در WorkOrderController.ValidWorkOrderTypes/WorkOrderLifecycleService) - هر مقدار دیگر
    // (خالی/ناشناخته) در سطر "Other" جمع می‌شود، نه این‌که به‌اشتباه به یک دسته نسبت داده شود.
    private static readonly string[] RecognizedTypes = { "Preventive", "Corrective", "Emergency", "Predictive", "Inspection" };
    private static readonly string[] CancelledStatuses = { "Cancelled", "Rejected" };
    private static readonly string[] CompletedStatuses = { "Completed", "Closed" };

    // طبق بند ۱۸: آستانه‌ی صریح و مستندشده - تغییر کمتر از این درصد، نوسان طبیعی در نظر گرفته
    // می‌شود، نه یک روند واقعی. یک ثابت پروژه‌ای، نه یک عدد پنهان داخل فرمول.
    private const double TrendStableThresholdPercent = 10.0;

    // طبق بند ۱۲: حداقل نمونه‌ی مستندشده برای این‌که یک درصد Compliance به‌عنوان READY (نه صرفاً PARTIAL) گزارش شود
    private const int MinimumReadySampleSize = 5;

    public ReliabilityIntelligenceService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<PmComplianceDto> GetPmComplianceAsync(DateTime? from, DateTime? to, int? equipmentClassId = null, int? disciplineId = null)
    {
        var query = _context.WorkOrders.AsNoTracking().Where(x => x.MaintenancePlanId != null);

        if (equipmentClassId.HasValue) query = query.Where(x => x.Equipment!.EquipmentClassId == equipmentClassId.Value);
        if (disciplineId.HasValue) query = query.Where(x => x.Equipment!.DisciplineId == disciplineId.Value);

        // طبق بند ۱۲: اگر بازه‌ی زمانی مشخص شده، فقط WorkOrder هایی که واقعاً DueDate دارند و در
        // آن بازه قرار می‌گیرند - رکورد بدون DueDate را نمی‌شود «در این بازه است» فرض کرد.
        if (from.HasValue || to.HasValue)
        {
            query = query.Where(x => x.DueDate != null);
            if (from.HasValue) query = query.Where(x => x.DueDate!.Value >= from.Value);
            if (to.HasValue) query = query.Where(x => x.DueDate!.Value <= to.Value);
        }

        var rows = await query
            .Select(x => new { x.Status, x.DueDate, x.CompletionDate })
            .ToListAsync();

        return BuildPmComplianceDto(rows.Select(x => (x.Status, x.DueDate, x.CompletionDate)));
    }

    private static PmComplianceDto BuildPmComplianceDto(IEnumerable<(string Status, DateTime? DueDate, DateTime? CompletionDate)> rows)
    {
        var dto = new PmComplianceDto();
        var now = DateTime.Now;
        var list = rows.ToList();
        dto.TotalQualifyingWorkOrders = list.Count;

        foreach (var row in list)
        {
            if (row.DueDate == null) { dto.InsufficientDataCount++; continue; }
            if (CancelledStatuses.Contains(row.Status)) { dto.CancelledCount++; continue; }

            if (CompletedStatuses.Contains(row.Status))
            {
                if (row.CompletionDate == null) { dto.InsufficientDataCount++; continue; }
                if (row.CompletionDate.Value.Date <= row.DueDate.Value.Date) dto.CompliantCount++;
                else dto.CompletedLateCount++;
                continue;
            }

            // هنوز باز است (Open/Assigned/InProgress/OnHold)
            if (row.DueDate.Value.Date < now.Date) dto.OverdueCount++;
            else dto.NotYetDueCount++;
        }

        var denominator = dto.CompliantCount + dto.CompletedLateCount + dto.OverdueCount;
        dto.CompliancePercent = denominator == 0 ? null : Math.Round(dto.CompliantCount * 100.0 / denominator, 1);

        if (dto.TotalQualifyingWorkOrders == 0)
        {
            dto.DataStatus = "NOT_READY";
            dto.DataStatusReason = "No PM-generated Work Orders exist in scope (no Maintenance Plan has produced a Work Order matching this filter).";
        }
        else if (denominator == 0)
        {
            dto.DataStatus = "INSUFFICIENT_DATA";
            dto.DataStatusReason = "All qualifying Work Orders are Not Yet Due, Cancelled, or missing a recorded due/completion date.";
        }
        else if (denominator < MinimumReadySampleSize)
        {
            dto.DataStatus = "PARTIAL";
            dto.DataStatusReason = $"Only {denominator} Work Order(s) have reached a determinable compliance state - treat the percentage as indicative only.";
        }
        else
        {
            dto.DataStatus = "READY";
        }

        return dto;
    }

    public async Task<MaintenanceTypeBreakdownDto> GetMaintenanceTypeBreakdownAsync(DateTime? from, DateTime? to)
    {
        var query = _context.WorkOrders.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(x => x.CreatedDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedDate <= to.Value);

        var grouped = await query
            .GroupBy(x => x.WorkOrderType)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count(),
                HasDowntime = g.Any(x => x.DowntimeHours != null),
                Downtime = g.Sum(x => x.DowntimeHours ?? 0)
            })
            .ToListAsync();

        var total = grouped.Sum(x => x.Count);

        var laborByType = await _context.WorkOrderLabors.AsNoTracking()
            .Where(x => x.IsActive)
            .Join(query, l => l.WorkOrderId, w => w.WorkOrderId, (l, w) => new { w.WorkOrderType, l.RegularHours, l.OvertimeHours, l.LaborCost })
            .GroupBy(x => x.WorkOrderType)
            .Select(g => new { Type = g.Key, Hours = g.Sum(x => x.RegularHours + x.OvertimeHours), Cost = g.Sum(x => x.LaborCost) })
            .ToDictionaryAsync(x => x.Type, x => x);

        var buckets = new Dictionary<string, MaintenanceTypeItemDto>();
        foreach (var canonical in RecognizedTypes)
            buckets[canonical] = new MaintenanceTypeItemDto { TypeName = canonical };
        buckets["Other"] = new MaintenanceTypeItemDto { TypeName = "Other" };

        foreach (var g in grouped)
        {
            var key = RecognizedTypes.FirstOrDefault(t => string.Equals(t, g.Type, StringComparison.OrdinalIgnoreCase)) ?? "Other";
            var item = buckets[key];
            item.Count += g.Count;
            if (g.HasDowntime)
                item.TotalDowntimeHours = (item.TotalDowntimeHours ?? 0) + g.Downtime;

            if (laborByType.TryGetValue(g.Type, out var labor))
            {
                item.TotalHours = (item.TotalHours ?? 0) + labor.Hours;
                item.TotalCost = (item.TotalCost ?? 0) + labor.Cost;
            }
        }

        foreach (var item in buckets.Values)
            item.Percent = total == 0 ? null : Math.Round(item.Count * 100.0 / total, 1);

        var emergency = buckets["Emergency"];

        return new MaintenanceTypeBreakdownDto
        {
            TotalWorkOrdersInPeriod = total,
            Items = buckets.Values.Where(x => x.Count > 0).OrderByDescending(x => x.Count).ToList(),
            EmergencyWorkOrderCount = emergency.Count,
            EmergencyWorkRatioPercent = emergency.Percent,
            DataStatus = total == 0 ? "NOT_READY" : "READY"
        };
    }

    public async Task<List<RepeatFailureGroupDto>> GetRepeatFailuresAsync(DateTime? from, DateTime? to, int windowDays = 90)
    {
        var query = _context.FailureRecords.AsNoTracking().Where(x => x.IsActive);
        if (from.HasValue) query = query.Where(x => x.FailureDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.FailureDate <= to.Value);

        var rows = await query
            .Select(x => new
            {
                x.EquipmentId,
                TagNumber = x.Equipment!.TagNumber,
                FailureModeKey = x.FailureModeRef != null ? x.FailureModeRef.Name : x.FailureMode,
                x.FailureDate
            })
            .ToListAsync();

        var result = new List<RepeatFailureGroupDto>();

        foreach (var group in rows.GroupBy(x => new { x.EquipmentId, x.TagNumber, x.FailureModeKey }))
        {
            var dates = group.Select(x => x.FailureDate).OrderBy(d => d).ToList();
            if (dates.Count < 2) continue;

            // طبق بند ۱۵: قاعده‌ی صریح - حداقل یک زوج خرابی متوالی با فاصله‌ی ≤ windowDays روز
            var hasRepeatPair = false;
            for (var i = 1; i < dates.Count; i++)
            {
                if ((dates[i] - dates[i - 1]).TotalDays <= windowDays) { hasRepeatPair = true; break; }
            }
            if (!hasRepeatPair) continue;

            result.Add(new RepeatFailureGroupDto
            {
                EquipmentId = group.Key.EquipmentId,
                TagNumber = group.Key.TagNumber,
                FailureModeKey = group.Key.FailureModeKey ?? "Unspecified",
                OccurrenceCount = dates.Count,
                FirstOccurrence = dates[0],
                LastOccurrence = dates[^1],
                WindowDays = windowDays,
                OccurrenceDates = dates
            });
        }

        return result.OrderByDescending(x => x.OccurrenceCount).ThenByDescending(x => x.LastOccurrence).ToList();
    }

    public async Task<MaintenanceEffectivenessDto> GetMaintenanceEffectivenessAsync(int windowDays = 90)
    {
        var now = DateTime.Now;
        var currentStart = now.AddDays(-windowDays);
        var priorStart = now.AddDays(-2 * windowDays);

        var pmCompliance = await GetPmComplianceAsync(currentStart, now);
        var repeatFailures = await GetRepeatFailuresAsync(currentStart, now, windowDays);
        var breakdown = await GetMaintenanceTypeBreakdownAsync(currentStart, now);

        var currentFailureCount = await _context.FailureRecords.CountAsync(x => x.IsActive && x.FailureDate >= currentStart && x.FailureDate <= now);
        var priorFailureCount = await _context.FailureRecords.CountAsync(x => x.IsActive && x.FailureDate >= priorStart && x.FailureDate < currentStart);

        var currentDowntime = await _context.FailureRecords
            .Where(x => x.IsActive && x.FailureDate >= currentStart && x.FailureDate <= now)
            .SumAsync(x => (decimal?)x.DowntimeHours) ?? 0;
        var priorDowntime = await _context.FailureRecords
            .Where(x => x.IsActive && x.FailureDate >= priorStart && x.FailureDate < currentStart)
            .SumAsync(x => (decimal?)x.DowntimeHours) ?? 0;

        var currentMttr = await AverageRepairHoursAsync(currentStart, now);
        var priorMttr = await AverageRepairHoursAsync(priorStart, currentStart);

        return new MaintenanceEffectivenessDto
        {
            WindowDays = windowDays,
            CurrentPeriodStart = currentStart,
            PriorPeriodStart = priorStart,

            PmCompliancePercent = pmCompliance.CompliancePercent,
            PmComplianceDataStatus = pmCompliance.DataStatus,

            RepeatFailureGroupCount = repeatFailures.Count,

            CurrentPeriodFailureCount = currentFailureCount,
            PriorPeriodFailureCount = priorFailureCount,
            FailureFrequencyTrend = ComputeTrend(currentFailureCount, priorFailureCount, higherIsWorse: true),

            CurrentPeriodAverageMTTRHours = currentMttr,
            PriorPeriodAverageMTTRHours = priorMttr,
            MttrTrend = ComputeTrend(currentMttr, priorMttr, higherIsWorse: true),

            CurrentPeriodDowntimeHours = currentDowntime,
            PriorPeriodDowntimeHours = priorDowntime,
            DowntimeTrend = ComputeTrend((double)currentDowntime, (double)priorDowntime, higherIsWorse: true),

            CorrectiveWorkloadPercent = breakdown.Items.FirstOrDefault(x => x.TypeName == "Corrective")?.Percent,
            EmergencyWorkloadPercent = breakdown.Items.FirstOrDefault(x => x.TypeName == "Emergency")?.Percent
        };
    }

    private async Task<double?> AverageRepairHoursAsync(DateTime start, DateTime end)
    {
        var durations = await _context.FailureRecords.AsNoTracking()
            .Where(x => x.IsActive && x.FailureDate >= start && x.FailureDate < end &&
                x.WorkOrder != null && x.WorkOrder.StartDate != null && x.WorkOrder.CompletionDate != null)
            .Select(x => new { Start = x.WorkOrder!.StartDate!.Value, End = x.WorkOrder.CompletionDate!.Value })
            .ToListAsync();

        return durations.Count > 0 ? durations.Average(x => (x.End - x.Start).TotalHours) : (double?)null;
    }

    // طبق بند ۱۸: قاعده‌ی روند صریح - بدون خط‌مبنا (دوره‌ی قبلی) معنادار (Null یا صفر)، هیچ
    // روندی محاسبه نمی‌شود (نه یک روند جعلی از یک مقایسه‌ی بی‌معنا با صفر).
    private static string ComputeTrend(double? current, double? prior, bool higherIsWorse)
    {
        if (!prior.HasValue || prior.Value == 0 || !current.HasValue) return "INSUFFICIENT_DATA";

        var changePercent = (current.Value - prior.Value) / prior.Value * 100.0;
        if (Math.Abs(changePercent) < TrendStableThresholdPercent) return "STABLE";

        var worsened = higherIsWorse ? changePercent > 0 : changePercent < 0;
        return worsened ? "WORSENING" : "IMPROVING";
    }

    public async Task<List<CriticalityMatrixRowDto>> GetCriticalityFailureMatrixAsync()
    {
        var criticalityByEquipment = await _context.EquipmentCriticalities.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.EquipmentId, x.CriticalityClass })
            .ToDictionaryAsync(x => x.EquipmentId, x => x.CriticalityClass);

        var equipmentIds = await _context.Equipments.AsNoTracking().Where(x => x.IsActive).Select(x => x.EquipmentId).ToListAsync();
        if (equipmentIds.Count == 0) return new List<CriticalityMatrixRowDto>();

        var groupedByClass = equipmentIds
            .GroupBy(id => criticalityByEquipment.GetValueOrDefault(id, "Unclassified"));

        var allRepeatFailures = await GetRepeatFailuresAsync(null, null, 90);
        var repeatFailureEquipmentIds = allRepeatFailures.Select(x => x.EquipmentId).ToHashSet();

        var failureAgg = await _context.FailureRecords.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.EquipmentId)
            .Select(g => new { EquipmentId = g.Key, Count = g.Count(), Downtime = g.Sum(x => x.DowntimeHours ?? 0) })
            .ToDictionaryAsync(x => x.EquipmentId);

        var fleetAvgFailurePerEquipment = equipmentIds.Count == 0 ? 0
            : failureAgg.Values.Sum(x => x.Count) / (double)equipmentIds.Count;
        var fleetAvgDowntimePerEquipment = equipmentIds.Count == 0 ? 0m
            : failureAgg.Values.Sum(x => x.Downtime) / equipmentIds.Count;

        var result = new List<CriticalityMatrixRowDto>();

        foreach (var group in groupedByClass)
        {
            var idsInGroup = group.ToList();
            var failureCount = idsInGroup.Sum(id => failureAgg.GetValueOrDefault(id)?.Count ?? 0);
            var downtime = idsInGroup.Sum(id => failureAgg.GetValueOrDefault(id)?.Downtime ?? 0);
            var withHistory = idsInGroup.Count(id => failureAgg.ContainsKey(id));
            var repeatCount = idsInGroup.Count(id => repeatFailureEquipmentIds.Contains(id));

            var pmRows = await _context.WorkOrders.AsNoTracking()
                .Where(x => x.MaintenancePlanId != null && idsInGroup.Contains(x.EquipmentId))
                .Select(x => new { x.Status, x.DueDate, x.CompletionDate })
                .ToListAsync();
            var pmCompliance = BuildPmComplianceDto(pmRows.Select(x => (x.Status, x.DueDate, x.CompletionDate)));

            var row = new CriticalityMatrixRowDto
            {
                CriticalityClass = group.Key,
                EquipmentCount = idsInGroup.Count,
                EquipmentWithFailureHistoryCount = withHistory,
                FailureCount = failureCount,
                TotalDowntimeHours = downtime,
                RepeatFailureGroupCount = repeatCount,
                PmCompliancePercent = pmCompliance.CompliancePercent
            };

            // طبق بند ۱۹: یافته‌ها فقط مقایسه‌ی صریح و مستندشده‌اند - نه یک تصمیم خودکار.
            var isHighCriticality = group.Key is "A" or "B";
            var avgFailurePerEquipmentInGroup = idsInGroup.Count == 0 ? 0 : failureCount / (double)idsInGroup.Count;
            var avgDowntimePerEquipmentInGroup = idsInGroup.Count == 0 ? 0m : downtime / idsInGroup.Count;

            if (isHighCriticality && failureCount > 0 && avgFailurePerEquipmentInGroup > fleetAvgFailurePerEquipment)
                row.Findings.Add("High Criticality + High Failure Frequency (above fleet average per equipment)");
            if (isHighCriticality && downtime > 0 && avgDowntimePerEquipmentInGroup > fleetAvgDowntimePerEquipment)
                row.Findings.Add("High Criticality + High Downtime (above fleet average per equipment)");
            if (isHighCriticality && repeatCount > 0)
                row.Findings.Add("High Criticality + Repeat Failure");
            if (isHighCriticality && pmCompliance.CompliancePercent.HasValue && pmCompliance.CompliancePercent.Value < 80.0)
                row.Findings.Add("High Criticality + Poor PM Compliance (below 80%)");
            if (group.Key == "C" && avgFailurePerEquipmentInGroup > fleetAvgFailurePerEquipment * 1.5)
                row.Findings.Add("Low Criticality + Excessive Maintenance Burden (failure rate well above fleet average)");

            result.Add(row);
        }

        return result.OrderBy(x => x.CriticalityClass).ToList();
    }

    // طبق «PEMS Phase 9» بند ۹: بُعد جدید MaintenanceStrategy/RCM روی همان گروه‌بندی بحرانیت -
    // از GetCriticalityFailureMatrixAsync/GetRepeatFailuresAsync موجود بازاستفاده می‌کند، فقط
    // MaintenanceStrategy/RCMAnalysis واقعی را اضافه می‌کند.
    public async Task<List<CriticalityFailureMaintenanceRowDto>> GetCriticalityFailureMaintenanceMatrixAsync()
    {
        var criticalityByEquipment = await _context.EquipmentCriticalities.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.EquipmentId, x.CriticalityClass })
            .ToDictionaryAsync(x => x.EquipmentId, x => x.CriticalityClass);

        var equipment = await _context.Equipments.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.EquipmentId, x.EquipmentClassId })
            .ToListAsync();

        if (equipment.Count == 0) return new List<CriticalityFailureMaintenanceRowDto>();

        var groupedByClass = equipment.GroupBy(e => criticalityByEquipment.GetValueOrDefault(e.EquipmentId, "Unclassified"));

        var allRepeatFailures = await GetRepeatFailuresAsync(null, null, 90);
        var repeatFailureEquipmentIds = allRepeatFailures.Select(x => x.EquipmentId).ToHashSet();

        var failureCounts = await _context.FailureRecords.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.EquipmentId)
            .Select(g => new { EquipmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EquipmentId, x => x.Count);

        // طبق بازطراحی RCM کلاس‌محور: استراتژی‌های جدید «الگو» هستند و EquipmentId آن‌ها Null است - پوشش واقعی هر تگ از
        // MaintenanceStrategyAssignment فعال می‌آید. استراتژی‌های قدیمی تک‌تگی (EquipmentId پر) هم همچنان پوشش حساب می‌شوند.
        // کلید Dictionary هرگز Null نیست (قبلاً ToDictionary روی EquipmentId؟ می‌ترکید) و جفت (تجهیز، استراتژی) یک‌بار شمرده
        // می‌شود، چون مهاجرت RCM برای هر استراتژی قدیمی یک Assignment هم می‌سازد.
        var activeStrategies = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.MaintenanceStrategyId, x.EquipmentId, x.StrategyType })
            .ToListAsync();
        var strategyTypeById = activeStrategies.ToDictionary(x => x.MaintenanceStrategyId, x => x.StrategyType);

        var activeAssignments = await _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.Status == PEMS.Domain.Common.RcmCodes.AssignmentStatus.Active)
            .Select(a => new { a.EquipmentId, a.MaintenanceStrategyId })
            .ToListAsync();

        var coveredPairs = new HashSet<(int EquipmentId, int StrategyId)>();
        foreach (var s in activeStrategies)
            if (s.EquipmentId.HasValue) coveredPairs.Add((s.EquipmentId.Value, s.MaintenanceStrategyId));
        foreach (var a in activeAssignments)
            if (strategyTypeById.ContainsKey(a.MaintenanceStrategyId)) coveredPairs.Add((a.EquipmentId, a.MaintenanceStrategyId));

        var strategiesByEquipmentId = coveredPairs
            .GroupBy(p => p.EquipmentId)
            .ToDictionary(g => g.Key, g => g.Select(p => strategyTypeById[p.StrategyId]).ToList());

        var approvedRcmClassIds = await _context.RCMAnalyses.AsNoTracking()
            .Where(x => x.IsActive && x.Status == "APPROVED")
            .Select(x => x.EquipmentClassId)
            .Distinct()
            .ToListAsync();
        var approvedRcmClassIdSet = approvedRcmClassIds.ToHashSet();

        var result = new List<CriticalityFailureMaintenanceRowDto>();

        foreach (var group in groupedByClass)
        {
            var idsInGroup = group.Select(g => g.EquipmentId).ToList();
            var classIdsInGroup = group.Select(g => g.EquipmentClassId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

            var failureCount = idsInGroup.Sum(id => failureCounts.GetValueOrDefault(id));
            var repeatCount = idsInGroup.Count(id => repeatFailureEquipmentIds.Contains(id));

            var pmRows = await _context.WorkOrders.AsNoTracking()
                .Where(x => x.MaintenancePlanId != null && idsInGroup.Contains(x.EquipmentId))
                .Select(x => new { x.Status, x.DueDate, x.CompletionDate })
                .ToListAsync();
            var pmCompliance = BuildPmComplianceDto(pmRows.Select(x => (x.Status, x.DueDate, x.CompletionDate)));

            var strategyCoveredCount = idsInGroup.Count(id => strategiesByEquipmentId.ContainsKey(id));
            var strategyTypeMix = idsInGroup
                .SelectMany(id => strategiesByEquipmentId.GetValueOrDefault(id) ?? new List<string>())
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count());

            var classesWithApprovedRcm = classIdsInGroup.Count(cid => approvedRcmClassIdSet.Contains(cid));

            var row = new CriticalityFailureMaintenanceRowDto
            {
                CriticalityClass = group.Key,
                EquipmentCount = idsInGroup.Count,
                FailureCount = failureCount,
                RepeatFailureGroupCount = repeatCount,
                PmCompliancePercent = pmCompliance.CompliancePercent,
                StrategyCoveredEquipmentCount = strategyCoveredCount,
                StrategyCoveragePercent = idsInGroup.Count == 0 ? 0 : 100.0 * strategyCoveredCount / idsInGroup.Count,
                StrategyTypeMix = strategyTypeMix,
                DistinctEquipmentClassCount = classIdsInGroup.Count,
                EquipmentClassesWithApprovedRcmCount = classesWithApprovedRcm,
                RcmClassCoveragePercent = classIdsInGroup.Count == 0 ? null : 100.0 * classesWithApprovedRcm / classIdsInGroup.Count
            };

            var isHighCriticality = group.Key is "A" or "B";
            if (isHighCriticality && row.StrategyCoveragePercent < 50.0 && idsInGroup.Count > 0)
                row.Findings.Add("High Criticality + Low Maintenance Strategy Coverage (below 50% of equipment)");
            if (isHighCriticality && row.RcmClassCoveragePercent.HasValue && row.RcmClassCoveragePercent.Value == 0)
                row.Findings.Add("High Criticality + No Approved RCM Analysis for its represented Equipment Classes");
            if (isHighCriticality && repeatCount > 0 && strategyCoveredCount > 0)
                row.Findings.Add("Repeated Failure Despite Existing Maintenance Strategy — investigate strategy effectiveness");

            result.Add(row);
        }

        return result.OrderBy(x => x.CriticalityClass).ToList();
    }
}
