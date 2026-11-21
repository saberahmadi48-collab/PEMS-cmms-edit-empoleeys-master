using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-713: what KpiController did, as operations: the same services (KPI, reliability, executive, targets), the same period clamp (at most 366 days, swapped dates normalised), the same number parsing for targets.</summary>
public sealed class KpiOperations(IKpiService kpi, IReliabilityService reliability, IExecutiveKpiService executive, IKpiTargetService targets, IStringLocalizer<SharedResource> loc) : IKpiOperations
{
    private const int MaxPeriodDays = 366;

    private string T(string key) { var text = loc[key]; return text.ResourceNotFound ? key : text.Value; }
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<List<ReportRow>> DashboardAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var cost = await kpi.GetCostKpisAsync(fromDate, toDate);
        var maintenance = await kpi.GetMaintenanceKpisAsync(fromDate, toDate);
        var fleet = await reliability.GetFleetReliabilityAsync();
        var rows = new List<ReportRow>();
        rows.AddRange(ReportReads.Flatten(cost, Label).Select(r => r with { Section = T("Rpt_Cost") }));
        rows.AddRange(ReportReads.Flatten(maintenance, Label).Select(r => r with { Section = T("Rpt_Maintenance") }));
        var withAvailability = fleet.Where(x => x.AvailabilityPercent.HasValue).ToList();
        rows.Add(new(T("Rpt_Reliability"), T("Rpt_EquipmentWithFailures"), fleet.Count.ToString(Inv)));
        rows.Add(new(T("Rpt_Reliability"), T("Rpt_TotalFailureCount"), fleet.Sum(x => x.FailureCount).ToString(Inv)));
        rows.Add(new(T("Rpt_Reliability"), T("Rpt_AverageAvailabilityPercent"), withAvailability.Count > 0 ? withAvailability.Average(x => x.AvailabilityPercent!.Value).ToString("N1", Inv) : ""));
        rows.AddRange(cost.TopEquipmentByCost.Select(e => new ReportRow(T("Rpt_TopEquipmentByCost"), e.TagNumber, e.Cost.ToString("N0", Inv), "TopEquipmentByCost", (double)e.Cost)));
        rows.AddRange(cost.CostByFunctionalLocation.Select(e => new ReportRow(T("Rpt_CostByFunctionalLocation"), e.GroupName, e.Cost.ToString("N0", Inv), "CostByFunctionalLocation", (double)e.Cost)));
        rows.AddRange(cost.CostByMaintenanceType.Select(e => new ReportRow(T("Rpt_CostByMaintenanceType"), e.GroupName, e.Cost.ToString("N0", Inv), "CostByMaintenanceType", (double)e.Cost)));
        return rows;
    }

    public async Task<List<KpiTileRow>> TilesAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var (from, to) = Period(fromDate, toDate);
        var d = await executive.GetAsync(from, to);
        return d.Tiles.Select(t => new KpiTileRow(T("Kpi_Section_" + t.Section), T("Kpi_Name_" + t.Key), t.Value is null ? "" : Number(t.Value.Value, t.Decimals) + Suffix(t.Unit),
            t.Target is null ? "" : (t.Direction == KpiDirections.LowerIsBetter ? "<= " : ">= ") + Number(t.Target.Value, t.Decimals) + Suffix(t.Unit), T("Kpi_Status_" + t.Status), t.Detail ?? "")).ToList();
    }

    public async Task<List<ReportRow>> BreakdownAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var (from, to) = Period(fromDate, toDate);
        var d = await executive.GetAsync(from, to);
        var rows = d.EquipmentByStatus.Select(s => new ReportRow(T("Rpt_EquipmentByStatus"), s.Name, s.Count.ToString("N0", Inv), "EquipmentByStatus", s.Count)).ToList();
        rows.AddRange(d.TopEquipmentByCost.Select(e => new ReportRow(T("Rpt_TopEquipmentByCost"), e.TagNumber, e.Cost.ToString("N0", Inv), "TopEquipmentByCost", (double)e.Cost)));
        return rows;
    }

    public async Task<List<KpiTargetRow>> TargetsAsync()
        => (await targets.GetEffectiveAsync()).Select(x => new KpiTargetRow(T("Kpi_Name_" + x.Key), T("Kpi_Section_" + x.Section), x.Direction == KpiDirections.LowerIsBetter ? T("Rpt_LowerIsBetter") : T("Rpt_HigherIsBetter"), x.Unit, x.Target?.ToString("0.####", Inv) ?? "", x.Warn?.ToString("0.####", Inv) ?? "", x.IsCustom ? T("Rpt_Custom") : T("Rpt_Default"))).ToList();

    public async Task<List<ReportChoiceRow>> TargetChoicesAsync()
        => (await targets.GetEffectiveAsync()).Select(x => new ReportChoiceRow(x.Key, T("Kpi_Name_" + x.Key))).ToList();

    public async Task<KpiSaveResult> SaveTargetAsync(string key, string? target = null, string? warn = null)
    {
        if (!TryParse(target, out var targetValue) || !TryParse(warn, out var warnValue)) return new(false, "Kpi_Err_NotANumber");
        var result = await targets.SaveAsync(key, targetValue, warnValue);
        return result.Success ? new(true, null) : new(false, result.Errors[0].Key);
    }

    private string Label(string name) { var text = loc["Rpt_" + name]; return text.ResourceNotFound ? name : text.Value; }

    private static (DateTime From, DateTime To) Period(DateTime? fromDate, DateTime? toDate)
    {
        var to = (toDate ?? DateTime.Today).Date;
        var from = (fromDate ?? to.AddDays(-29)).Date;
        if (from > to) (from, to) = (to, from);
        if ((to - from).TotalDays > MaxPeriodDays) from = to.AddDays(-MaxPeriodDays);
        return (from, to);
    }

    private static string Number(decimal v, int decimals) => v.ToString("N" + decimals, Inv);
    private static string Suffix(string unit) => unit switch { "%" => "%", "h" => " h", "d" => " d", _ => "" };

    private static bool TryParse(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, Inv, out var parsed)) return false;
        value = parsed;
        return true;
    }
}
