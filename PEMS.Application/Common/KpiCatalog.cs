namespace PEMS.Application.Common;

public static class KpiDirections
{
    /// <summary>مقدار بیشتر بهتر است (مثلاً PM Compliance): محقق = مقدار ≥ هدف.</summary>
    public const string HigherIsBetter = "Higher";

    /// <summary>مقدار کمتر بهتر است (مثلاً MTTR): محقق = مقدار ≤ هدف.</summary>
    public const string LowerIsBetter = "Lower";
}

public static class KpiSections
{
    public const string Cost = "Cost";
    public const string Maintenance = "Maintenance";
    public const string Reliability = "Reliability";
    public const string Equipment = "Equipment";
    public const string Inventory = "Inventory";
    public const string Production = "Production";
    public const string Documents = "Documents";

    public static readonly string[] Ordered = { Cost, Maintenance, Reliability, Equipment, Inventory, Production, Documents };
}

/// <summary>
/// تعریف یک شاخص. Unit: "%", "h", "d", "count", "currency". Decimals تعداد رقم اعشار نمایش.
/// DefaultTarget/DefaultWarn فقط وقتی مقدار دارند که در سند KPI صراحتاً هدف داشته‌اند؛ بدون هدف، شاخص فقط مقدار نشان می‌دهد.
/// </summary>
public sealed record KpiDefinition(
    string Key, string Section, string Unit, string Direction, int Decimals,
    decimal? DefaultTarget = null, decimal? DefaultWarn = null);

/// <summary>
/// کاتالوگ شاخص‌های داشبورد مدیریتی: فقط شاخص‌هایی که داده‌ی واقعی در PEMS دارند. شاخص‌های بدون منبع داده
/// (ایمنی، انرژی، منابع انسانی، بودجه، OEE، EVM، ...) عمداً این‌جا نیستند - در NotTracked فهرست می‌شوند، نه با عدد نمونه.
/// </summary>
public static class KpiCatalog
{
    public static readonly IReadOnlyList<KpiDefinition> All = new KpiDefinition[]
    {
        // ---- Cost
        new("cost_total", KpiSections.Cost, "currency", KpiDirections.LowerIsBetter, 0),
        new("cost_labor_share", KpiSections.Cost, "%", KpiDirections.LowerIsBetter, 1),
        new("cost_material_share", KpiSections.Cost, "%", KpiDirections.LowerIsBetter, 1),
        new("cost_contractor_share", KpiSections.Cost, "%", KpiDirections.LowerIsBetter, 1),
        new("cost_corrective_share", KpiSections.Cost, "%", KpiDirections.LowerIsBetter, 1),

        // ---- Maintenance
        new("pm_compliance", KpiSections.Maintenance, "%", KpiDirections.HigherIsBetter, 1, 95m, 90m),
        new("pm_overdue", KpiSections.Maintenance, "count", KpiDirections.LowerIsBetter, 0),
        new("wo_completion_rate", KpiSections.Maintenance, "%", KpiDirections.HigherIsBetter, 1, 90m, 80m),
        new("backlog_count", KpiSections.Maintenance, "count", KpiDirections.LowerIsBetter, 0),
        new("backlog_hours", KpiSections.Maintenance, "h", KpiDirections.LowerIsBetter, 0),
        new("backlog_overdue_pct", KpiSections.Maintenance, "%", KpiDirections.LowerIsBetter, 1, 2m, 5m),
        new("wo_aging_days", KpiSections.Maintenance, "d", KpiDirections.LowerIsBetter, 1),
        new("emergency_share", KpiSections.Maintenance, "%", KpiDirections.LowerIsBetter, 1),

        // ---- Reliability (all-time: derived from the failure history, not from the selected period)
        new("fleet_mtbf", KpiSections.Reliability, "h", KpiDirections.HigherIsBetter, 0, 2400m, 2000m),
        new("fleet_mttr", KpiSections.Reliability, "h", KpiDirections.LowerIsBetter, 1, 5m, 8m),
        new("fleet_availability", KpiSections.Reliability, "%", KpiDirections.HigherIsBetter, 1, 95m, 90m),
        new("failure_count", KpiSections.Reliability, "count", KpiDirections.LowerIsBetter, 0),
        new("repeat_failure_groups", KpiSections.Reliability, "count", KpiDirections.LowerIsBetter, 0),
        new("uncovered_critical_equipment", KpiSections.Reliability, "count", KpiDirections.LowerIsBetter, 0),

        // ---- Equipment
        new("equipment_total", KpiSections.Equipment, "count", KpiDirections.HigherIsBetter, 0),

        // ---- Inventory
        new("stock_in_rate", KpiSections.Inventory, "%", KpiDirections.HigherIsBetter, 1, 95m, 90m),
        new("low_stock_items", KpiSections.Inventory, "count", KpiDirections.LowerIsBetter, 0),
        new("out_of_stock_items", KpiSections.Inventory, "count", KpiDirections.LowerIsBetter, 0),

        // ---- Production
        new("po_on_time_rate", KpiSections.Production, "%", KpiDirections.HigherIsBetter, 1),
        new("po_overdue_open", KpiSections.Production, "count", KpiDirections.LowerIsBetter, 0),
        new("po_completed", KpiSections.Production, "count", KpiDirections.HigherIsBetter, 0),
        new("production_downtime_hours", KpiSections.Production, "h", KpiDirections.LowerIsBetter, 1),

        // ---- Documents
        new("docs_total", KpiSections.Documents, "count", KpiDirections.HigherIsBetter, 0),
        new("docs_under_review", KpiSections.Documents, "count", KpiDirections.LowerIsBetter, 0),
        new("docs_rejected", KpiSections.Documents, "count", KpiDirections.LowerIsBetter, 0),
    };

    private static readonly Dictionary<string, KpiDefinition> ByKey = All.ToDictionary(k => k.Key, StringComparer.Ordinal);

    public static KpiDefinition? Find(string? key) => key != null && ByKey.TryGetValue(key, out var d) ? d : null;

    /// <summary>
    /// دسته‌هایی از سند KPI که در PEMS هیچ منبع داده‌ای ندارند (کلید Localization به‌همراه توضیح نیازمندی). عمداً مقدار ندارند.
    /// </summary>
    public static readonly IReadOnlyList<string> NotTracked = new[]
    {
        "budget", "safety", "energy", "workforce", "customer", "risk_register", "earned_value", "oee", "pm_roi"
    };
}
