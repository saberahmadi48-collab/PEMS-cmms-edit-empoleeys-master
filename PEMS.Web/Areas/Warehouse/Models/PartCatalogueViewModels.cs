using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;

namespace PEMS.Web.Areas.Warehouse.Models;

/// <summary>صفحه‌ی فهرست درخواست‌های کاتالوگ قطعه.</summary>
public sealed class PcIndexViewModel
{
    public PagedResult<PartCatalogueListItemDto> Page { get; set; } = new();
    public PartCatalogueListFilter Filter { get; set; } = new();
    public PartCatalogueSummaryDto Summary { get; set; } = new();
    public List<PcLookupItem> Sections { get; set; } = new();
    public bool CanCreate { get; set; }
}

/// <summary>صفحه‌ی ویرایش (فرم اصلی): جزئیات + لیست‌های کوچک ثابت. لیست‌های بزرگ فقط با AJAX جستجو می‌شوند.</summary>
public sealed class PcEditViewModel
{
    public PartCatalogueDetailsDto Details { get; set; } = new();
    public List<PcLookupItem> Sections { get; set; } = new();
    public List<PcLookupItem> Group1 { get; set; } = new();
    public List<PcLookupItem> Group2 { get; set; } = new();
    public List<PcLookupItem> ProductFamilies { get; set; } = new();
    public List<PcLookupItem> Units { get; set; } = new();
    public List<PcLookupItem> Currencies { get; set; } = new();
    public List<PcLookupItem> StockGrades { get; set; } = new();
    public List<PcLookupItem> PlanningMethods { get; set; } = new();
    public List<PcLookupItem> TypeDesignations { get; set; } = new();
    public List<PcMessage> Errors { get; set; } = new();
    public List<PcMessage> Warnings { get; set; } = new();

    public bool IsChange => string.Equals(Details.RequestType, "Change", StringComparison.OrdinalIgnoreCase);
}

public sealed class PcAdminViewModel
{
    public PcAdminOverview Overview { get; set; } = new();
    public List<PcLookupItem> Employees { get; set; } = new();
    public PcImportResult? LastImport { get; set; }

    /// <summary>تنظیمات سراسری (روش تولید شماره‌ی قطعه + قالب) برای پنل «Part No generation».</summary>
    public PartCatalogueSettingsDto Settings { get; set; } = new();
}

/// <summary>تبدیل پیام‌های سرویس (کلید Localization + نام فیلد) به متن قابل‌نمایش.</summary>
public static class PcMessages
{
    public static string Format(IStringLocalizer<SharedResource> localizer, PcMessage m)
    {
        var text = localizer[m.Key];
        var message = text.ResourceNotFound ? m.Key : text.Value;
        if (!string.IsNullOrEmpty(m.Arg)) message += " " + (m.Arg.StartsWith("Pc_", StringComparison.Ordinal) ? localizer[m.Arg].Value : m.Arg);

        var label = localizer["Pc_F_" + m.Field];
        return label.ResourceNotFound ? message : $"{label.Value}: {message}";
    }

    public static IEnumerable<string> FormatAll(IStringLocalizer<SharedResource> localizer, IEnumerable<PcMessage> messages) =>
        messages.Select(m => Format(localizer, m));
}
