using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق «PEMS Phase 9» بند ۱۵. VariableType به یک فهرست ثابت محدود شده - از مقدار دلخواه
/// جلوگیری می‌کند بدون این‌که یک جدول Lookup جدید اضافه کند (سازگار با بند ۲۵: فقط وقتی
/// واقعاً لازم است Migration اضافه کن).
/// </summary>
public class ConditionMonitoringService : IConditionMonitoringService
{
    public static readonly string[] ValidVariableTypes =
    {
        "Vibration", "Temperature", "Pressure", "Flow", "Current", "Voltage",
        "Lubrication", "OilAnalysis", "Thermography", "Acoustic", "ProcessDeviation"
    };

    private readonly PEMSDbContext _context;

    public ConditionMonitoringService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<ConditionMonitoringFoundationStatusDto> GetFoundationStatusAsync()
    {
        var pointCount = await _context.ConditionMonitoringPoints.CountAsync(x => x.IsActive);
        var readingCount = await _context.ConditionReadings.CountAsync();

        return new ConditionMonitoringFoundationStatusDto
        {
            FoundationStatus = "READY",
            LiveTelemetryStatus = readingCount > 0 ? "PARTIAL" : "NOT_READY",
            MonitoringPointCount = pointCount,
            ReadingCount = readingCount,
            SupportedVariableTypes = ValidVariableTypes.ToList()
        };
    }

    public async Task<List<ConditionMonitoringPointDto>> GetPointsForEquipmentAsync(int equipmentId)
    {
        var points = await _context.ConditionMonitoringPoints
            .Where(x => x.EquipmentId == equipmentId && x.IsActive)
            .Include(x => x.Equipment)
            .Include(x => x.EvaluationProfile)
            .ToListAsync();

        var pointIds = points.Select(x => x.ConditionMonitoringPointId).ToList();
        var lastReadings = await _context.ConditionReadings
            .Where(x => pointIds.Contains(x.ConditionMonitoringPointId))
            .GroupBy(x => x.ConditionMonitoringPointId)
            .Select(g => new
            {
                PointId = g.Key,
                Count = g.Count(),
                Last = g.OrderByDescending(x => x.ReadingDate).First()
            })
            .ToListAsync();

        return points.Select(p =>
        {
            var last = lastReadings.FirstOrDefault(x => x.PointId == p.ConditionMonitoringPointId);
            return new ConditionMonitoringPointDto
            {
                ConditionMonitoringPointId = p.ConditionMonitoringPointId,
                EquipmentId = p.EquipmentId,
                EquipmentTagNumber = p.Equipment?.TagNumber,
                VariableType = p.VariableType,
                Tag = p.Tag,
                Unit = p.Unit,
                AlarmThresholdLow = p.AlarmThresholdLow,
                AlarmThresholdHigh = p.AlarmThresholdHigh,
                ReadingCount = last?.Count ?? 0,
                LastReadingDate = last?.Last.ReadingDate,
                LastValue = last?.Last.Value,
                LastReadingIsAlarm = last?.Last.IsAlarm ?? false,
                LastReadingId = last?.Last.ConditionReadingId,
                EvaluationProfileId = p.EvaluationProfileId,
                EvaluationProfileCode = p.EvaluationProfile?.Code,
                LastReadingEvaluationStatus = last?.Last.EvaluationStatus ?? "NotEvaluated"
            };
        }).ToList();
    }

    public async Task<(bool Success, string? Error, int? PointId)> CreatePointAsync(
        int equipmentId, string variableType, string tag, string? unit,
        decimal? alarmLow, decimal? alarmHigh, string? description, string? createdBy)
    {
        if (!ValidVariableTypes.Contains(variableType))
            return (false, $"نوع متغیر نامعتبر است. مقادیر مجاز: {string.Join(", ", ValidVariableTypes)}", null);

        if (string.IsNullOrWhiteSpace(tag))
            return (false, "Tag نقطه‌ی پایش الزامی است.", null);

        var equipmentExists = await _context.Equipments.AnyAsync(x => x.EquipmentId == equipmentId && x.IsActive);
        if (!equipmentExists) return (false, "تجهیز یافت نشد.", null);

        var point = new ConditionMonitoringPoint
        {
            EquipmentId = equipmentId,
            VariableType = variableType,
            Tag = tag,
            Unit = unit,
            AlarmThresholdLow = alarmLow,
            AlarmThresholdHigh = alarmHigh,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now,
            CreatedBy = createdBy
        };

        await _context.ConditionMonitoringPoints.AddAsync(point);
        await _context.SaveChangesAsync();

        return (true, null, point.ConditionMonitoringPointId);
    }

    // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۵/بند ۲۴:
    // ترتیب شدت برای انتخاب بدترین سطحی که مقدار در آن قرار می‌گیرد - اگر محدوده‌های تعریف‌شده
    // (اشتباهاً) هم‌پوشانی داشته باشند، ایمن‌ترین انتخاب (بدترین حالت) در نظر گرفته می‌شود.
    // internal (نه private): طبق فاز ۹، ConditionReadingImportService هم باید همین منطق دقیق
    // ارزیابی چندسطحی را برای قرائت‌های وارداتی استفاده کند - نه یک پیاده‌سازی موازی/احتمالاً
    // ناهماهنگ.
    internal static readonly string[] SeverityOrder = { "Critical", "Alarm", "Warning", "Normal" };

    internal static bool ValueInLimit(decimal value, EvaluationLimit limit)
    {
        if (limit.LowValue.HasValue)
        {
            if (limit.LowInclusive ? value < limit.LowValue.Value : value <= limit.LowValue.Value) return false;
        }
        if (limit.HighValue.HasValue)
        {
            if (limit.HighInclusive ? value > limit.HighValue.Value : value >= limit.HighValue.Value) return false;
        }
        return true;
    }

    // طبق بند ۲۵: فقط سطرهای Limit واقعاً پیکربندی‌شده ارزیابی می‌شوند - نبود پیکربندی یعنی
    // NotEvaluated، هرگز حدس زده نمی‌شود که Normal است.
    internal static string EvaluateAgainstProfile(EvaluationProfile profile, decimal value)
    {
        var activeLimits = profile.Limits.Where(l => l.IsActive).ToList();
        if (activeLimits.Count == 0) return "NotEvaluated";

        var matching = activeLimits.Where(l => ValueInLimit(value, l)).Select(l => l.Status).ToHashSet();
        foreach (var s in SeverityOrder)
        {
            if (matching.Contains(s)) return s;
        }
        return "NotEvaluated";
    }

    public async Task<(bool Success, string? Error)> RecordReadingAsync(
        int pointId, DateTime readingDate, decimal value, string source, string? notes, string? recordedBy,
        int? instrumentId = null)
    {
        var point = await _context.ConditionMonitoringPoints
            .Include(x => x.EvaluationProfile).ThenInclude(p => p!.Limits)
            .FirstOrDefaultAsync(x => x.ConditionMonitoringPointId == pointId && x.IsActive);
        if (point == null) return (false, "نقطه‌ی پایش یافت نشد.");

        // طبق «PEMS — Equipment Passport Audit & CBM» فاز ۱ ممیزی §3.3: ابزار انتخاب‌شده باید
        // واقعاً روی همین تجهیز نصب باشد - هرگز به ابزار تجهیز دیگری پیوند نمی‌خورد.
        if (instrumentId.HasValue)
        {
            var instrumentBelongsToEquipment = await _context.Instruments
                .AnyAsync(x => x.InstrumentId == instrumentId.Value && x.EquipmentId == point.EquipmentId);
            if (!instrumentBelongsToEquipment) return (false, "ابزار انتخاب‌شده متعلق به این تجهیز نیست.");
        }

        var isAlarm = (point.AlarmThresholdLow.HasValue && value < point.AlarmThresholdLow.Value) ||
                      (point.AlarmThresholdHigh.HasValue && value > point.AlarmThresholdHigh.Value);

        var reading = new ConditionReading
        {
            ConditionMonitoringPointId = pointId,
            ReadingDate = readingDate,
            Value = value,
            Source = string.IsNullOrWhiteSpace(source) ? "Manual" : source,
            Provenance = "OBSERVED",
            IsAlarm = isAlarm,
            Notes = notes,
            RecordedBy = recordedBy,
            InstrumentId = instrumentId,
            CreatedDate = DateTime.Now
        };

        // طبق بند ۲۶ (Versioning): نسخه‌ی دقیق پروفایل و نتیجه‌ی ارزیابی در همین لحظه منجمد
        // می‌شوند - تغییر بعدی پروفایل هرگز تفسیر این قرائت خاص را عوض نمی‌کند.
        if (point.EvaluationProfile != null)
        {
            reading.EvaluationProfileId = point.EvaluationProfile.EvaluationProfileId;
            reading.EvaluationProfileVersion = point.EvaluationProfile.Version;
            reading.EvaluationStatus = EvaluateAgainstProfile(point.EvaluationProfile, value);
        }

        await _context.ConditionReadings.AddAsync(reading);
        await _context.SaveChangesAsync();

        return (true, null);
    }

    // طبق «PEMS — Equipment Passport Audit & CBM» فاز ۱ ممیزی §3.6: RequiresEngineeringReview
    // ششمین مقدار EvaluationStatus - فقط با یک اقدام دستی صریح انسانی تنظیم می‌شود، هرگز توسط
    // EvaluateAgainstProfile محاسبه نمی‌شود. طبقه‌بندی خودکار پیشین در Notes حفظ می‌شود (هرگز
    // دور ریخته نمی‌شود) - این یک Override است، نه یک بازمحاسبه‌ی خاموش.
    public async Task<(bool Success, string? Error)> MarkRequiresEngineeringReviewAsync(int readingId, string? markedBy)
    {
        var reading = await _context.ConditionReadings.FirstOrDefaultAsync(x => x.ConditionReadingId == readingId);
        if (reading == null) return (false, "قرائت یافت نشد.");

        if (reading.EvaluationStatus == "RequiresEngineeringReview")
            return (false, "این قرائت از قبل برای بازبینی مهندسی علامت‌گذاری شده است.");

        var note = $"[بازبینی مهندسی درخواست شد در {DateTime.Now:yyyy-MM-dd HH:mm} توسط {markedBy ?? "نامشخص"}؛ ارزیابی پیشین: {reading.EvaluationStatus}]";
        reading.Notes = string.IsNullOrWhiteSpace(reading.Notes) ? note : $"{reading.Notes}\n{note}";
        reading.EvaluationStatus = "RequiresEngineeringReview";

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // طبق فاز ۵: پیوند/تغییر پروفایل ارزیابی یک نقطه‌ی پایش - AlarmThresholdLow/High دست‌نخورده
    // می‌مانند (مصرف‌کننده‌های موجود نمی‌شکنند)؛ این فقط لایه‌ی ارزیابی چندسطحی را فعال می‌کند.
    public async Task<(bool Success, string? Error)> AssignEvaluationProfileAsync(int pointId, int? evaluationProfileId)
    {
        var point = await _context.ConditionMonitoringPoints.FirstOrDefaultAsync(x => x.ConditionMonitoringPointId == pointId && x.IsActive);
        if (point == null) return (false, "نقطه‌ی پایش یافت نشد.");

        if (evaluationProfileId.HasValue)
        {
            var profileExists = await _context.EvaluationProfiles.AnyAsync(x => x.EvaluationProfileId == evaluationProfileId.Value && x.IsActive);
            if (!profileExists) return (false, "Evaluation Profile معتبر نیست.");
        }

        point.EvaluationProfileId = evaluationProfileId;
        await _context.SaveChangesAsync();
        return (true, null);
    }

    // طبق فاز ۸/بند ۳۱: سقف سخت روی تعداد نقاط بازگردانده‌شده - حتی اگر بازه‌ی تاریخ توسط
    // کاربر خیلی بزرگ انتخاب شود، کوئری هرگز بدون محدودیت به SQL نمی‌رود.
    private const int MaxTrendPoints = 2000;

    public async Task<ConditionMonitoringTrendDto?> GetTrendAsync(int pointId, DateTime fromDate, DateTime toDate)
    {
        var point = await _context.ConditionMonitoringPoints.FirstOrDefaultAsync(x => x.ConditionMonitoringPointId == pointId);
        if (point == null) return null;

        var query = _context.ConditionReadings
            .Where(x => x.ConditionMonitoringPointId == pointId && x.ReadingDate >= fromDate && x.ReadingDate <= toDate);

        var totalInRange = await query.CountAsync();

        var points = await query
            .OrderBy(x => x.ReadingDate)
            .Take(MaxTrendPoints)
            .Select(x => new ConditionReadingTrendPointDto
            {
                ReadingDate = x.ReadingDate,
                Value = x.Value,
                EvaluationStatus = x.EvaluationStatus,
                IsAlarm = x.IsAlarm
            })
            .ToListAsync();

        return new ConditionMonitoringTrendDto
        {
            ConditionMonitoringPointId = pointId,
            VariableType = point.VariableType,
            Tag = point.Tag,
            Unit = point.Unit,
            FromDate = fromDate,
            ToDate = toDate,
            TotalInRange = totalInRange,
            Truncated = totalInRange > MaxTrendPoints,
            Points = points
        };
    }

    // طبق «PEMS — Equipment Passport Audit & CBM» فاز ۱ ممیزی §3.8/بند ۳۱: لیست‌های داشبورد
    // همیشه به MaxDashboardRows محدود می‌شوند - هرگز کل جدول بدون سقف بارگذاری نمی‌شود.
    private const int MaxDashboardRows = 50;

    public async Task<CbmDashboardDto> GetCbmDashboardAsync()
    {
        var activePoints = await _context.ConditionMonitoringPoints
            .Where(x => x.IsActive)
            .Select(x => new { x.ConditionMonitoringPointId, x.EquipmentId, x.VariableType, x.Tag, x.EvaluationProfileId })
            .ToListAsync();

        var pointIds = activePoints.Select(x => x.ConditionMonitoringPointId).ToList();

        // طبق همان الگوی EquipmentService.GetPassportAsync: فقط آخرین قرائت هر نقطه، نه کل تاریخچه.
        var lastReadings = await _context.ConditionReadings
            .Where(x => pointIds.Contains(x.ConditionMonitoringPointId))
            .GroupBy(x => x.ConditionMonitoringPointId)
            .Select(g => new { PointId = g.Key, Last = g.OrderByDescending(x => x.ReadingDate).First() })
            .ToListAsync();
        var lastReadingByPoint = lastReadings.ToDictionary(x => x.PointId, x => x.Last);

        var equipmentIds = activePoints.Select(x => x.EquipmentId).Distinct().ToList();
        var equipmentTagsById = await _context.Equipments
            .Where(x => equipmentIds.Contains(x.EquipmentId))
            .Select(x => new { x.EquipmentId, x.TagNumber })
            .ToDictionaryAsync(x => x.EquipmentId, x => x.TagNumber);

        var cbmStrategyPointIds = await _context.MaintenanceStrategies
            .Where(x => x.IsActive && x.ConditionMonitoringPointId != null && pointIds.Contains(x.ConditionMonitoringPointId!.Value))
            .Select(x => x.ConditionMonitoringPointId!.Value)
            .ToListAsync();

        var dashboard = new CbmDashboardDto
        {
            TotalActivePoints = activePoints.Count,
            PointsWithEvaluationProfile = activePoints.Count(x => x.EvaluationProfileId.HasValue),
            PointsWithoutEvaluationProfile = activePoints.Count(x => !x.EvaluationProfileId.HasValue),
            PointsWithCbmStrategy = cbmStrategyPointIds.Distinct().Count()
        };

        foreach (var status in lastReadingByPoint.Values.Select(x => x.EvaluationStatus))
        {
            switch (status)
            {
                case "Critical": dashboard.CriticalCount++; break;
                case "Alarm": dashboard.AlarmCount++; break;
                case "Warning": dashboard.WarningCount++; break;
                case "RequiresEngineeringReview": dashboard.RequiresEngineeringReviewCount++; break;
            }
        }

        dashboard.OpenFindingCount = await _context.Findings.CountAsync(x => x.IsActive && x.Status == "Open");
        dashboard.LinkedFindingCount = await _context.Findings.CountAsync(x => x.IsActive && x.Status == "Linked");

        var attentionSeverity = new[] { "Critical", "Alarm", "RequiresEngineeringReview", "Warning" };
        var attentionCandidates = activePoints
            .Where(p => lastReadingByPoint.ContainsKey(p.ConditionMonitoringPointId) &&
                        attentionSeverity.Contains(lastReadingByPoint[p.ConditionMonitoringPointId].EvaluationStatus))
            .Select(p => new CbmDashboardPointRowDto
            {
                ConditionMonitoringPointId = p.ConditionMonitoringPointId,
                EquipmentId = p.EquipmentId,
                EquipmentTagNumber = equipmentTagsById.GetValueOrDefault(p.EquipmentId, string.Empty),
                VariableType = p.VariableType,
                Tag = p.Tag,
                EvaluationStatus = lastReadingByPoint[p.ConditionMonitoringPointId].EvaluationStatus,
                LastReadingDate = lastReadingByPoint[p.ConditionMonitoringPointId].ReadingDate
            })
            .OrderBy(x => Array.IndexOf(attentionSeverity, x.EvaluationStatus))
            .ThenByDescending(x => x.LastReadingDate)
            .ToList();

        dashboard.AttentionPointsTruncated = attentionCandidates.Count > MaxDashboardRows;
        dashboard.AttentionPoints = attentionCandidates.Take(MaxDashboardRows).ToList();

        // طبق بند ۳۶: Overdue فقط از MaintenancePlan واقعی موجود خوانده می‌شود - هرگز اختراع نمی‌شود.
        var now = DateTime.Now;
        var overdueCandidates = await _context.MaintenanceStrategies
            .Where(x => x.IsActive && x.ConditionMonitoringPointId != null)
            .Join(_context.MaintenancePlans.Where(p => p.IsActive && p.NextDueDate.HasValue),
                s => s.MaintenanceStrategyId, p => p.MaintenanceStrategyId,
                (s, p) => new { s.ConditionMonitoringPointId, p.EquipmentId, p.NextDueDate })
            .Where(x => x.NextDueDate!.Value < now)
            .OrderBy(x => x.NextDueDate)
            .ToListAsync();

        dashboard.OverdueCbmPlanCount = overdueCandidates.Count;
        dashboard.DueSoonCbmPlanCount = await _context.MaintenanceStrategies
            .Where(x => x.IsActive && x.ConditionMonitoringPointId != null)
            .Join(_context.MaintenancePlans.Where(p => p.IsActive && p.NextDueDate.HasValue),
                s => s.MaintenanceStrategyId, p => p.MaintenanceStrategyId,
                (s, p) => p.NextDueDate!.Value)
            .CountAsync(d => d >= now && d <= now.AddDays(7));

        dashboard.OverdueCbmPlansTruncated = overdueCandidates.Count > MaxDashboardRows;
        var pointLookupById = activePoints.ToDictionary(x => x.ConditionMonitoringPointId);
        dashboard.OverdueCbmPlans = overdueCandidates.Take(MaxDashboardRows)
            .Where(x => pointLookupById.ContainsKey(x.ConditionMonitoringPointId!.Value))
            .Select(x =>
            {
                var p = pointLookupById[x.ConditionMonitoringPointId!.Value];
                return new CbmDashboardOverdueRowDto
                {
                    EquipmentId = x.EquipmentId,
                    EquipmentTagNumber = equipmentTagsById.GetValueOrDefault(x.EquipmentId, string.Empty),
                    VariableType = p.VariableType,
                    Tag = p.Tag,
                    NextDueDate = x.NextDueDate!.Value,
                    DaysOverdue = (int)(now - x.NextDueDate.Value).TotalDays
                };
            })
            .ToList();

        return dashboard;
    }
}
