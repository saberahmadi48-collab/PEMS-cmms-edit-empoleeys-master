namespace PEMS.Application.Interfaces;

/// <summary>One tile of the executive KPI dashboard as text (value, target, status).</summary>
public sealed record KpiTileRow(string Section, string Kpi, string Value, string Target, string Status, string Detail);

/// <summary>One KPI target (empty target = no target).</summary>
public sealed record KpiTargetRow(string Key, string Section, string Direction, string Unit, string Target, string Warn, string Source);

/// <summary>The answer of saving a target: the error is a resource key.</summary>
public sealed record KpiSaveResult(bool Success, string? ErrorKey);

/// <summary>The KPI pages as operations (SCRUM-713): the classic dashboard, the executive dashboard with its targets, and the target save (permission Reliability.Edit).</summary>
public interface IKpiOperations
{
    [PEMS.Application.Common.PemsOperation("Kpi", "Dashboard", "Reliability.View")]
    Task<List<ReportRow>> DashboardAsync(DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Kpi", "Tiles", "Reliability.View")]
    Task<List<KpiTileRow>> TilesAsync(DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Kpi", "Breakdown", "Reliability.View")]
    Task<List<ReportRow>> BreakdownAsync(DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Kpi", "Targets", "Reliability.View")]
    Task<List<KpiTargetRow>> TargetsAsync();

    [PEMS.Application.Common.PemsOperation("Kpi", "TargetChoices", "Reliability.View")]
    Task<List<ReportChoiceRow>> TargetChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Kpi", "SaveTarget", "Reliability.Edit")]
    Task<KpiSaveResult> SaveTargetAsync(string key, string? target = null, string? warn = null);
}
