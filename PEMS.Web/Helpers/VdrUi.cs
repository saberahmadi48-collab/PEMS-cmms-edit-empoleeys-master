using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Helpers;

/// <summary>
/// طبق بازطراحی Stitch بخش Vendor/VDR: فقط نگاشت وضعیت واقعی → کلاس ظاهری (Pill). هیچ منطق
/// کسب‌وکاری و هیچ وضعیت جدیدی این‌جا ساخته نمی‌شود؛ همان نگاشت‌های قبلی Views (Closed=سبز،
/// PendingResubmission=هشدار، UnderReview=آبی، ...) فقط یک‌جا جمع شده‌اند.
/// </summary>
public static class VdrUi
{
    public static string WorkflowTone(string? status) => status switch
    {
        "Closed" => "vs-pill--success",
        "PendingResubmission" => "vs-pill--warning",
        "UnderReview" => "vs-pill--info",
        _ => "vs-pill--neutral"
    };

    public static string RiskTone(string? risk) => risk switch
    {
        "Low" => "vs-pill--success",
        "Medium" => "vs-pill--warning",
        "High" => "vs-pill--danger",
        _ => "vs-pill--neutral"
    };

    public static string TransmittalTone(string? status) => status switch
    {
        "Sent" => "vs-pill--success",
        "Acknowledged" => "vs-pill--info",
        "Cancelled" => "vs-pill--danger",
        _ => "vs-pill--neutral"
    };

    public static string CommentActionTone(string? status) => status switch
    {
        "Closed" => "vs-pill--success",
        "Verified" => "vs-pill--info",
        "VendorResponsePending" => "vs-pill--warning",
        "NoActionRequired" => "vs-pill--neutral",
        _ => "vs-pill--danger"
    };

    /// <summary>فقط ترجمه‌ی نمایشی مقدار ذخیره‌شده (Status/Risk/...)؛ مقدار پایگاه‌داده هرگز تغییر نمی‌کند. اگر کلید نبود همان مقدار خام برمی‌گردد.</summary>
    public static string Loc(IStringLocalizer<SharedResource> localizer, string prefix, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var s = localizer[prefix + value];
        if (!s.ResourceNotFound) return s.Value;
        var shared = localizer[value];
        return shared.ResourceNotFound ? value : shared.Value;
    }
}
