namespace PEMS.Application.DTOs;

public static class KpiStatuses
{
    /// <summary>مقدار به هدف رسیده.</summary>
    public const string Met = "Met";

    /// <summary>مقدار هدف را برآورده نکرده ولی در آستانه‌ی «نزدیک به هدف» است.</summary>
    public const string Near = "Near";

    public const string Missed = "Missed";

    /// <summary>مقدار محاسبه شده ولی هدفی تعریف نشده.</summary>
    public const string NoTarget = "NoTarget";

    /// <summary>داده‌ی کافی برای محاسبه نیست (مثلاً هیچ دستورکاری در بازه نیست) - هرگز صفر جعلی نشان داده نمی‌شود.</summary>
    public const string NoData = "NoData";
}

/// <summary>یک کاشی شاخص: Value=null یعنی داده‌ی کافی نیست.</summary>
public sealed class KpiTileDto
{
    public string Key { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int Decimals { get; set; }

    public decimal? Value { get; set; }
    public decimal? Target { get; set; }
    public decimal? Warn { get; set; }
    public string Status { get; set; } = KpiStatuses.NoData;

    /// <summary>توضیح محاسبه (مثلاً «n=45 تجهیز دارای داده») - بدون متن‌ساخته‌ی جعلی.</summary>
    public string? Detail { get; set; }
}

public sealed class KpiNamedCountDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class ExecutiveKpiDashboardDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public List<KpiTileDto> Tiles { get; set; } = new();

    public List<KpiNamedCountDto> EquipmentByStatus { get; set; } = new();
    public List<CostByEquipmentItem> TopEquipmentByCost { get; set; } = new();
}

/// <summary>هدف مؤثر یک شاخص (پیش‌فرض کاتالوگ یا مقدار ذخیره‌شده).</summary>
public sealed class KpiTargetDto
{
    public string Key { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal? Target { get; set; }
    public decimal? Warn { get; set; }

    /// <summary>true وقتی مقدار از جدول KpiTargets آمده؛ false = پیش‌فرض کاتالوگ.</summary>
    public bool IsCustom { get; set; }
}
