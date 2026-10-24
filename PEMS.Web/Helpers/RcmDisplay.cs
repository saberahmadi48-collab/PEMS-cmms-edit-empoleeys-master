using System.Globalization;
using Microsoft.Extensions.Localization;

namespace PEMS.Web.Helpers;

/// <summary>
/// نمایش محلی‌شده‌ی کدهای پایدار RCM. در دیتابیس فقط کد ذخیره می‌شود؛ برچسب در لحظه‌ی نمایش از Resource
/// (کلید Rcm_&lt;Group&gt;_&lt;Code&gt;) می‌آید و اگر کلیدی نبود خودِ کد نمایش داده می‌شود (هرگز خالی نمی‌ماند).
/// </summary>
public static class RcmDisplay
{
    public static string Label(this IStringLocalizer localizer, string group, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var s = localizer[$"Rcm_{group}_{code}"];
        return s.ResourceNotFound ? code : s.Value;
    }

    /// <summary>نام انگلیسی/فارسی کاتالوگ بر اساس فرهنگ جاری (فارسی اگر موجود باشد).</summary>
    public static string Name(string? name, string? nameFa)
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" && !string.IsNullOrWhiteSpace(nameFa) ? nameFa! : (name ?? "");

    public static string ConsequenceBadge(string? code) => code switch
    {
        "Safety" => "bg-danger",
        "Environmental" => "bg-warning text-dark",
        "Hidden" => "bg-dark",
        "Operational" => "bg-info text-dark",
        _ => "bg-secondary"
    };

    public static string StatusBadge(string? status) => status switch
    {
        "Active" => "bg-success",
        "Approved" => "bg-primary",
        "InReview" => "bg-warning text-dark",
        "Draft" => "bg-secondary",
        "Superseded" => "bg-light text-dark border",
        "Retired" => "bg-dark",
        _ => "bg-secondary"
    };

    public static string CriticalityBadge(string? c) => c switch
    {
        "A" => "bg-danger",
        "B" => "bg-warning text-dark",
        "C" => "bg-secondary",
        _ => "bg-light text-dark border"
    };
}
