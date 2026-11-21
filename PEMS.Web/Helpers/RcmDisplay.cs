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
        "Safety" => "danger",
        "Environmental" => "warning",
        "Hidden" => "dark",
        "Operational" => "info",
        _ => "neutral"
    };

    public static string StatusBadge(string? status) => status switch
    {
        "Active" => "success",
        "Approved" => "primary",
        "InReview" => "warning",
        "Draft" => "neutral",
        "Superseded" => "light",
        "Retired" => "dark",
        _ => "neutral"
    };

    public static string CriticalityBadge(string? c) => c switch
    {
        "A" => "danger",
        "B" => "warning",
        "C" => "neutral",
        _ => "light"
    };
}
