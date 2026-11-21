using System.Globalization;

namespace PEMS.Application.Common;

/// <summary>
/// تبدیل ردیف AuditLog (ChangedFieldsSummary با قالب «Field: old -> new | Field2: old -> new») به یک جمله‌ی خوانا برای
/// تایم‌لاین نامه (Phase 3). فقط تغییرات ResponseRequired / ResponseDueDate را توصیف می‌کند؛ بقیه‌ی فیلدها نادیده‌اند.
/// مقدار تاریخ در ممیزی با فرهنگ سرور ذخیره شده، پس با فرهنگ جاری و Invariant هر دو تلاش می‌شود.
/// </summary>
public static class LetterResponseHistory
{
    private static (string Old, string New)? Find(string[] parts, string field)
    {
        var prefix = field + ": ";
        var part = parts.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.Ordinal));
        if (part == null) return null;

        var body = part[prefix.Length..];
        var i = body.IndexOf(" -> ", StringComparison.Ordinal);
        return i < 0 ? null : (body[..i].Trim(), body[(i + 4)..].Trim());
    }

    private static string Day(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "-";
        return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)
               || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            ? d.ToString("yyyy-MM-dd")
            : raw;
    }

    /// <summary>null یعنی این ردیف ممیزی به پاسخ‌خواهی مربوط نیست.</summary>
    public static string? Describe(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return null;

        var parts = summary.Split(" | ");
        var required = Find(parts, "ResponseRequired");
        var due = Find(parts, "ResponseDueDate");

        if (required is { } r && !string.Equals(r.Old, r.New, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(r.New, "True", StringComparison.OrdinalIgnoreCase))
                return due is { } d ? $"Response requirement set - due {Day(d.New)}" : "Response requirement set";
            return "Response requirement removed";
        }

        if (due is { } change && change.Old != change.New)
            return $"Response due date changed from {Day(change.Old)} to {Day(change.New)}";

        return null;
    }
}
