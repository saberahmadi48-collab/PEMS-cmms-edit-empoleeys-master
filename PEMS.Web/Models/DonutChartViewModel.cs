namespace PEMS.Web.Models;

/// <summary>داده‌ی نمودار حلقه‌ای (Shared/_DonutChart): هر آیتم یک برچسب و مقدار؛ رنگ‌ها از توکن‌های تم در مرورگر می‌آیند.</summary>
public class DonutChartViewModel
{
    public string Id { get; set; } = "donut";

    // برچسب زیر مجموع در مرکز حلقه (وقتی نشانگر روی هیچ بخشی نیست)
    public string DefaultLabel { get; set; } = string.Empty;

    public string AriaLabel { get; set; } = string.Empty;

    public string EmptyText { get; set; } = string.Empty;

    public List<DonutChartItem> Items { get; set; } = new();
}

public record DonutChartItem(string Label, int Value);
