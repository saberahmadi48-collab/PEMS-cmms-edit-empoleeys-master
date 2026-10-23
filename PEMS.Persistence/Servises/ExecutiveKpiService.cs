using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// داشبورد مدیریتی KPI. فقط شاخص‌های KpiCatalog را محاسبه می‌کند که داده‌ی واقعی در PEMS دارند؛ تا جای ممکن از سرویس‌های
/// موجود (IKpiService, IReliabilityService, IReliabilityIntelligenceService) بازاستفاده می‌شود و فقط برای موجودی/تولید/مدارک/
/// وضعیت تجهیز کوئری مستقیم دارد (سرویس مشابهی نبود). اگر داده‌ی کافی برای یک شاخص نیست، Value=null (NoData) می‌ماند -
/// هرگز صفر یا عدد نمونه‌ی جعلی نمایش داده نمی‌شود.
/// </summary>
public class ExecutiveKpiService : IExecutiveKpiService
{
    // همان وضعیت‌های Backlog که KpiService.GetMaintenanceKpisAsync استفاده می‌کند (عدد یکسان در یک صفحه)
    private static readonly string[] BacklogStatuses = { "Open", "Assigned", "InProgress", "OnHold" };

    // ماشین وضعیت ProductionOrderService: سفارش‌های «باز» = برنامه‌ریزی‌شده تا در جریان/متوقف
    private static readonly string[] OpenProductionStatuses = { "Planned", "Released", "InProgress", "OnHold" };
    private static readonly string[] CompletedProductionStatuses = { "Completed", "Closed" };

    private static readonly string[] DocumentReviewStatuses = { "Submitted", "Under Review" };

    private readonly PEMSDbContext _ctx;
    private readonly IKpiService _kpi;
    private readonly IReliabilityService _reliability;
    private readonly IReliabilityIntelligenceService _intelligence;
    private readonly IKpiTargetService _targets;

    public ExecutiveKpiService(PEMSDbContext ctx, IKpiService kpi, IReliabilityService reliability,
        IReliabilityIntelligenceService intelligence, IKpiTargetService targets)
    {
        _ctx = ctx;
        _kpi = kpi;
        _reliability = reliability;
        _intelligence = intelligence;
        _targets = targets;
    }

    public async Task<ExecutiveKpiDashboardDto> GetAsync(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1).AddTicks(-1);
        if (end < start) (start, end) = (end.Date, start.AddDays(1).AddTicks(-1));

        var dto = new ExecutiveKpiDashboardDto { From = start, To = end.Date, GeneratedAt = DateTime.Now };
        var targets = (await _targets.GetEffectiveAsync()).ToDictionary(t => t.Key, StringComparer.Ordinal);

        void Add(string key, decimal? value, string? detail = null)
        {
            var def = KpiCatalog.Find(key) ?? throw new InvalidOperationException("Unknown KPI key: " + key);
            targets.TryGetValue(key, out var t);
            dto.Tiles.Add(new KpiTileDto
            {
                Key = key, Section = def.Section, Unit = def.Unit, Direction = def.Direction, Decimals = def.Decimals,
                Value = value, Target = t?.Target, Warn = t?.Warn,
                Status = KpiStatusCalculator.Evaluate(def.Direction, value, t?.Target, t?.Warn),
                Detail = detail
            });
        }

        await AddCostAsync(dto, Add, start, end);
        await AddMaintenanceAsync(Add, start, end);
        await AddReliabilityAsync(Add, start, end);
        await AddEquipmentAsync(dto, Add);
        await AddInventoryAsync(Add);
        await AddProductionAsync(Add, start, end);
        await AddDocumentsAsync(Add);

        return dto;
    }

    private static decimal? Round(double? v, int decimals) => v.HasValue ? Math.Round((decimal)v.Value, decimals) : null;

    // ------------------------------------------------------------------ Cost

    private async Task AddCostAsync(ExecutiveKpiDashboardDto dto, Action<string, decimal?, string?> add, DateTime start, DateTime end)
    {
        var cost = await _kpi.GetCostKpisAsync(start, end);
        dto.TopEquipmentByCost = cost.TopEquipmentByCost.Take(5).ToList();

        // هزینه‌ی صفر = هیچ هزینه‌ای در MaintenanceHistory این بازه ثبت نشده، نه «هزینه‌ی صفر»
        var hasCost = cost.TotalCost > 0;
        add("cost_total", hasCost ? cost.TotalCost : null, null);

        decimal? Share(decimal part) => hasCost ? Math.Round(part * 100m / cost.TotalCost, 1) : null;
        add("cost_labor_share", Share(cost.LaborCost), null);
        add("cost_material_share", Share(cost.MaterialCost), null);
        add("cost_contractor_share", Share(cost.ContractorCost), null);

        var correctiveCost = cost.CostByMaintenanceType
            .Where(x => x.GroupName is "Corrective" or "Emergency").Sum(x => x.Cost);
        add("cost_corrective_share", Share(correctiveCost), null);
    }

    // ------------------------------------------------------------------ Maintenance

    private async Task AddMaintenanceAsync(Action<string, decimal?, string?> add, DateTime start, DateTime end)
    {
        var m = await _kpi.GetMaintenanceKpisAsync(start, end);

        add("pm_compliance", Round(m.PmCompliancePercent, 1), m.PmActivePlanCount > 0 ? $"{m.PmActivePlanCount} active plans" : null);
        add("pm_overdue", m.PmActivePlanCount > 0 ? m.PmOverdueCount : null, null);
        add("wo_completion_rate", Round(m.WorkOrderCompletionRatePercent, 1), m.TotalWorkOrdersInPeriod > 0 ? $"{m.TotalWorkOrdersInPeriod} work orders created in period" : null);
        add("backlog_count", m.BacklogCount, null);
        add("backlog_hours", m.BacklogCount > 0 ? m.BacklogHours : null, null);
        add("wo_aging_days", Round(m.WorkOrderAgingDays, 1), null);
        add("emergency_share", m.TotalWorkOrdersInPeriod > 0 ? Math.Round((decimal)m.EmergencyPercent, 1) : null, null);

        // سهم عقب‌افتاده‌ها از کل Backlog (DueDate گذشته) - همان تعریف Backlog که KpiService استفاده می‌کند
        var today = DateTime.Today;
        var backlogDueDates = await _ctx.WorkOrders.AsNoTracking()
            .Where(x => BacklogStatuses.Contains(x.Status))
            .Select(x => x.DueDate)
            .ToListAsync();
        var overdue = backlogDueDates.Count(d => d.HasValue && d.Value.Date < today);
        add("backlog_overdue_pct", backlogDueDates.Count > 0 ? Math.Round(overdue * 100m / backlogDueDates.Count, 1) : null,
            backlogDueDates.Count > 0 ? $"{overdue} of {backlogDueDates.Count} open work orders past due date" : null);
    }

    // ------------------------------------------------------------------ Reliability

    private async Task AddReliabilityAsync(Action<string, decimal?, string?> add, DateTime start, DateTime end)
    {
        var fleet = await _reliability.GetFleetReliabilityAsync();

        // میانگین ساده‌ی تجهیزهایی که داده‌ی کافی دارند؛ n در توضیح می‌آید تا «میانگین کل ناوگان» برداشت نشود
        (decimal? value, string? detail) Mean(Func<EquipmentReliabilityDto, double?> pick, int decimals)
        {
            var values = fleet.Select(pick).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? (null, null)
                : (Math.Round((decimal)values.Average(), decimals), $"n={values.Count} of {fleet.Count} equipment with enough data (all-time)");
        }

        var (mtbf, mtbfDetail) = Mean(e => e.MTBFHours, 0);
        var (mttr, mttrDetail) = Mean(e => e.MTTRHours, 1);
        var (availability, availabilityDetail) = Mean(e => e.AvailabilityPercent, 1);
        add("fleet_mtbf", mtbf, mtbfDetail);
        add("fleet_mttr", mttr, mttrDetail);
        add("fleet_availability", availability, availabilityDetail);
        add("failure_count", fleet.Count == 0 ? null : fleet.Sum(e => e.FailureCount), fleet.Count == 0 ? null : "all-time, active failure records");

        var repeats = await _intelligence.GetRepeatFailuresAsync(start, end, 90);
        add("repeat_failure_groups", repeats.Count, "90-day window");

        var matrix = await _intelligence.GetCriticalityFailureMaintenanceMatrixAsync();
        var classA = matrix.FirstOrDefault(r => r.CriticalityClass == "A");
        add("uncovered_critical_equipment", classA == null ? null : classA.EquipmentCount - classA.StrategyCoveredEquipmentCount,
            classA == null ? null : $"class A equipment without an active maintenance strategy ({classA.EquipmentCount} class A in total)");
    }

    // ------------------------------------------------------------------ Equipment

    private async Task AddEquipmentAsync(ExecutiveKpiDashboardDto dto, Action<string, decimal?, string?> add)
    {
        // وضعیت تجهیز واژگان کنترل‌شده ندارد؛ دقیقاً مقادیر موجود در داده شمرده می‌شود (هیچ دسته‌بندی حدسی نیست)
        var byStatus = await _ctx.Equipments.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.Status)
            .Select(g => new KpiNamedCountDto { Name = g.Key, Count = g.Count() })
            .ToListAsync();
        dto.EquipmentByStatus = byStatus.OrderByDescending(x => x.Count).ThenBy(x => x.Name).ToList();

        var total = byStatus.Sum(x => x.Count);
        add("equipment_total", total > 0 ? total : null, null);
    }

    // ------------------------------------------------------------------ Inventory

    private async Task AddInventoryAsync(Action<string, decimal?, string?> add)
    {
        var rows = await _ctx.MaterialStocks.AsNoTracking()
            .Where(s => s.IsActive && s.MaterialItem!.IsActive)
            .Select(s => new { s.MaterialItemId, s.OnHandQuantity, s.ReservedQuantity, s.ReorderPoint })
            .ToListAsync();

        var items = rows.GroupBy(r => r.MaterialItemId).Select(g => new
        {
            OnHand = g.Sum(x => x.OnHandQuantity),
            Low = g.Any(x => x.ReorderPoint.HasValue && x.OnHandQuantity - x.ReservedQuantity <= x.ReorderPoint.Value)
        }).ToList();

        if (items.Count == 0)
        {
            add("stock_in_rate", null, null);
            add("low_stock_items", null, null);
            add("out_of_stock_items", null, null);
            return;
        }

        var detail = $"{items.Count} stocked items";
        var inStock = items.Count(i => i.OnHand > 0);
        add("stock_in_rate", Math.Round(inStock * 100m / items.Count, 1), detail);
        add("low_stock_items", items.Count(i => i.OnHand > 0 && i.Low), "at or below reorder point, still in stock");
        add("out_of_stock_items", items.Count(i => i.OnHand <= 0), detail);
    }

    // ------------------------------------------------------------------ Production

    private async Task AddProductionAsync(Action<string, decimal?, string?> add, DateTime start, DateTime end)
    {
        var orders = await _ctx.ProductionOrders.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Status, x.PlannedFinishDate, x.ActualFinishDate })
            .ToListAsync();

        if (orders.Count == 0)
        {
            add("po_on_time_rate", null, null);
            add("po_overdue_open", null, null);
            add("po_completed", null, null);
            add("production_downtime_hours", null, null);
            return;
        }

        var today = DateTime.Today;
        add("po_overdue_open", orders.Count(o => OpenProductionStatuses.Contains(o.Status) && o.PlannedFinishDate.HasValue && o.PlannedFinishDate.Value.Date < today), null);

        var completedInPeriod = orders.Where(o => CompletedProductionStatuses.Contains(o.Status)
            && o.ActualFinishDate.HasValue && o.ActualFinishDate.Value >= start && o.ActualFinishDate.Value <= end).ToList();
        add("po_completed", completedInPeriod.Count, null);

        // درصد به‌موقع فقط روی سفارش‌هایی که هم تاریخ برنامه و هم تاریخ واقعی دارند؛ مقادیر تولید جمع زده نمی‌شوند (واحدها متفاوت‌اند)
        var measurable = completedInPeriod.Where(o => o.PlannedFinishDate.HasValue).ToList();
        add("po_on_time_rate",
            measurable.Count == 0 ? null : Math.Round(measurable.Count(o => o.ActualFinishDate!.Value.Date <= o.PlannedFinishDate!.Value.Date) * 100m / measurable.Count, 1),
            measurable.Count == 0 ? null : $"{measurable.Count} completed orders with a planned date");

        var events = await _ctx.ProductionDowntimeEvents.AsNoTracking()
            .Where(e => e.StartTime >= start && e.StartTime <= end)
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync();
        var cap = DateTime.Now < end ? DateTime.Now : end;   // رویداد باز تا همین لحظه (حداکثر انتهای بازه) حساب می‌شود
        var hours = events.Sum(e => Math.Max(0, ((e.EndTime ?? cap) - e.StartTime).TotalHours));
        add("production_downtime_hours", Math.Round((decimal)hours, 1), $"{events.Count} downtime events started in period");
    }

    // ------------------------------------------------------------------ Documents

    private async Task AddDocumentsAsync(Action<string, decimal?, string?> add)
    {
        var byStatus = await _ctx.EngineeringDocuments.AsNoTracking()
            .Where(x => x.IsActive)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = byStatus.Sum(x => x.Count);
        if (total == 0)
        {
            add("docs_total", null, null);
            add("docs_under_review", null, null);
            add("docs_rejected", null, null);
            return;
        }

        add("docs_total", total, "active engineering documents");
        add("docs_under_review", byStatus.Where(x => DocumentReviewStatuses.Contains(x.Status)).Sum(x => x.Count), null);
        add("docs_rejected", byStatus.Where(x => x.Status == "Rejected").Sum(x => x.Count), null);
    }
}
