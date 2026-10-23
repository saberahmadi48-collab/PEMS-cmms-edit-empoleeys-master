using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;

namespace PEMS.Web.Helpers;

/// <summary>نمایش مشترک Task Manager (برچسب دوزبانه، رنگ وضعیت/اولویت). فقط نمایش؛ مقادیر ذخیره‌شده تغییر نمی‌کنند.</summary>
public static class TaskManagerUi
{
    public static bool IsFa => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";

    /// <summary>نام فارسی پیکربندی‌شده در fa، در غیر این صورت نام اصلی.</summary>
    public static string Name(string? name, string? nameFa) => IsFa && !string.IsNullOrWhiteSpace(nameFa) ? nameFa! : name ?? "";

    public static string CategoryBadge(string? category) => category switch
    {
        WorkItemStatusCategories.Done => "bg-success",
        WorkItemStatusCategories.InProgress => "bg-info text-dark",
        _ => "bg-secondary"
    };

    public static string PriorityBadge(string? priority) => priority switch
    {
        "Urgent" => "bg-danger",
        "High" => "bg-warning text-dark",
        "Low" => "bg-light text-dark border",
        _ => "bg-secondary"
    };

    public static string PriorityIcon(string? priority) => priority switch
    {
        "Urgent" => "bi-chevron-double-up",
        "High" => "bi-chevron-up",
        "Low" => "bi-chevron-down",
        _ => "bi-dash"
    };

    /// <summary>برچسب وضعیت یک ردیف: نام وضعیت فضا یا رشته‌ی وضعیت قدیمی (کلید Tm_Status_*).</summary>
    public static string StatusLabel(IStringLocalizer l, WorkItemListItemDto i) =>
        i.StatusId != null ? Name(i.StatusName, i.StatusNameFa) : Localized(l, "Tm_Status_" + i.Status, i.Status);

    public static string LegacyStatus(IStringLocalizer l, string status) => Localized(l, "Tm_Status_" + status, status);
    public static string Priority(IStringLocalizer l, string? priority) => Localized(l, "Tm_Priority_" + priority, priority ?? "");
    public static string LinkType(IStringLocalizer l, string linkType, bool outward) =>
        Localized(l, $"Tm_Link_{linkType}_{(outward ? "Out" : "In")}", linkType);

    public static string Localized(IStringLocalizer l, string key, string fallback)
    {
        var s = l[key];
        return s.ResourceNotFound ? fallback : s.Value;
    }

    /// <summary>تاریخ میلادی (ذخیره و نمایش میلادی طبق قرارداد پروژه).</summary>
    public static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public static string DateInput(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
}
