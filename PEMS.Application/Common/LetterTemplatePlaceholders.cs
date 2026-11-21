using System.Net;
using System.Text.RegularExpressions;

namespace PEMS.Application.Common;

/// <summary>
/// نشانه‌های جایگزینی قالب نامه (Phase 3)، مثل {{Date}}. فقط نشانه‌های فهرست‌شده مجازند (خطای تایپی هنگام ذخیره‌ی قالب گرفته می‌شود)،
/// نشانه‌ی شناخته‌شده‌ای که در آن صفحه مقدار ندارد دست‌نخورده می‌ماند تا کاربر آن را ببیند، و مقدار جایگزین داخل متن HTML
/// همیشه HTML-Encode می‌شود (موضوع/نام‌ها ورودی کاربر هستند) و متن نهایی بعد از ثبت باز هم از Sanitizer عبور می‌کند.
/// </summary>
public static class LetterTemplatePlaceholders
{
    // در ایجاد نامه و پاسخ هر دو در دسترس‌اند
    public static readonly string[] General = { "Date", "UserName", "LetterType", "SenderName", "SenderTitle", "SenderDepartment" };

    // فقط در صفحه‌ی پاسخ (نامه‌ی مادر وجود دارد)
    public static readonly string[] Reply = { "ParentNumber", "ParentSubject", "ParentDate", "Counterparty", "ParentProject" };

    public static IReadOnlyList<string> All { get; } = General.Concat(Reply).ToArray();

    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z]+)\s*\}\}", RegexOptions.Compiled);

    private static bool IsKnown(string name) => All.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>نام نشانه‌های نامعتبر (بدون تکرار) در متن؛ خالی یعنی همه مجازند.</summary>
    public static IReadOnlyList<string> Unknown(string? text)
        => string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : Token.Matches(text).Select(m => m.Groups[1].Value).Where(n => !IsKnown(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string Apply(string? text, IReadOnlyDictionary<string, string> values, bool htmlEncode)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return Token.Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (!lookup.TryGetValue(name, out var value)) return m.Value;   // ناشناخته یا بدون مقدار: دست‌نخورده
            return htmlEncode ? WebUtility.HtmlEncode(value) : value;
        });
    }
}
