using System.Text.Json;

namespace PEMS.Web.Models;

/// <summary>
/// توصیف یک نمودار Bklit برای صفحه‌های گزارش و KPI. صفحه فقط «چه چیزی» را می‌گوید (نوع، برچسب‌ها، سری‌ها)؛ رسم با کامپوننت‌های Bklit در
/// frontend/dashboards انجام می‌شود (Views/Shared/_BkChart.cshtml عنصر میزبان را می‌سازد). جایگزین canvas + Chart.js درون‌صفحه‌ای قبلی.
/// </summary>
public sealed class BkChartSpec
{
    public const string Primary = "var(--pems-chart-primary, #3b82f6)";
    public const string Secondary = "var(--pems-chart-secondary, #a855f7)";
    public const string Tertiary = "var(--pems-chart-tertiary, #14b8a6)";
    public const string Positive = "var(--pems-chart-positive, #22c55e)";
    public const string Negative = "var(--pems-chart-negative, #ef4444)";
    public const string Warning = "var(--pems-chart-warning, #f59e0b)";
    public const string Neutral = "var(--pems-chart-neutral, #94a3b8)";
    public const string Accent = "var(--pems-chart-accent, #d98c2b)";

    // bar, hbar, donut, gauge
    public string Kind { get; init; } = "bar";
    public string AriaLabel { get; init; } = string.Empty;
    public string EmptyText { get; init; } = "—";
    public List<string> Labels { get; init; } = new();
    public List<BkSeries> Series { get; init; } = new();
    public bool Monthly { get; init; }
    public bool Stacked { get; init; }
    public List<string>? Colors { get; init; }
    public string? CenterLabel { get; init; }
    public double? Value { get; init; }
    public string? Unit { get; init; }
    public int? Decimals { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>بزرگ‌ترین <paramref name="top"/> دسته؛ بقیه در یک ردیف «سایر» جمع می‌شوند تا نمودار افقی هرگز صدها ردیف نشود و داده‌ای هم پنهان نماند.</summary>
    public static List<(string Label, double Value)> TopWithOthers(IEnumerable<(string Label, int Value)> rows, int top, string otherLabel)
    {
        var ordered = rows.OrderByDescending(r => r.Value).ToList();
        var head = ordered.Take(top).Select(r => (r.Label, (double)r.Value)).ToList();
        var restSum = ordered.Skip(top).Sum(r => r.Value);
        if (restSum > 0) head.Add((otherLabel, restSum));
        return head;
    }

    /// <summary>ستون‌های ماهانه («yyyy-MM»)، یک یا چند سری.</summary>
    public static BkChartSpec MonthlyBars(string ariaLabel, string emptyText, IEnumerable<string> months, params BkSeries[] series)
        => new() { Kind = "bar", AriaLabel = ariaLabel, EmptyText = emptyText, Monthly = true, Labels = months.ToList(), Series = series.ToList() };

    /// <summary>میله‌ی افقی بر اساس دسته (مثلاً دیسیپلین)؛ یک سری یا چند سری (stacked اختیاری).</summary>
    public static BkChartSpec HBars(string ariaLabel, string emptyText, IEnumerable<string> labels, bool stacked, params BkSeries[] series)
        => new() { Kind = "hbar", AriaLabel = ariaLabel, EmptyText = emptyText, Labels = labels.ToList(), Stacked = stacked, Series = series.ToList() };

    public static BkChartSpec Donut(string ariaLabel, string emptyText, string centerLabel, IEnumerable<(string Label, double Value)> items, IEnumerable<string>? colors = null)
    {
        var list = items.ToList();
        return new BkChartSpec
        {
            Kind = "donut", AriaLabel = ariaLabel, EmptyText = emptyText, CenterLabel = centerLabel,
            Labels = list.Select(x => x.Label).ToList(), Series = { new BkSeries(centerLabel, list.Select(x => x.Value).ToList()) },
            Colors = colors?.ToList()
        };
    }

    public static BkChartSpec Gauge(string ariaLabel, string emptyText, string caption, double? value, string unit = "%", int decimals = 0)
        => new() { Kind = "gauge", AriaLabel = ariaLabel, EmptyText = emptyText, CenterLabel = caption, Value = value, Unit = unit, Decimals = decimals };
}

public sealed record BkSeries(string Name, List<double> Values, string? Color = null)
{
    public static BkSeries Of(string name, IEnumerable<int> values, string? color = null) => new(name, values.Select(v => (double)v).ToList(), color);
    public static BkSeries Of(string name, IEnumerable<decimal> values, string? color = null) => new(name, values.Select(v => (double)v).ToList(), color);
}
