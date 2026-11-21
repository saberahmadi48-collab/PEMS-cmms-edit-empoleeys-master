using System.Globalization;

namespace PEMS.Application.Common;

/// <summary>
/// تبدیل‌های تاریخ جلالی (شمسی) برای شماره‌گذاری و نمایش. ذخیره‌سازی در پایگاه داده همیشه میلادی است؛ جلالی فقط برای سال شماره‌ی نامه‌ی وارده
/// (سال شمسی از ۱ فروردین شروع می‌شود) و نمایش به کاربر استفاده می‌شود.
/// </summary>
public static class JalaliDate
{
    private static readonly PersianCalendar Calendar = new();

    public static int Year(DateTime date) => Calendar.GetYear(date);

    public static (int Year, int Month, int Day) Parts(DateTime date)
        => (Calendar.GetYear(date), Calendar.GetMonth(date), Calendar.GetDayOfMonth(date));

    /// <summary>نمایش «۱۴۰۵/۰۲/۰۵» با ارقام لاتین (yyyy/MM/dd).</summary>
    public static string Format(DateTime date)
    {
        var (y, m, d) = Parts(date);
        return $"{y:D4}/{m:D2}/{d:D2}";
    }

    public static bool TryToGregorian(int year, int month, int day, out DateTime result)
    {
        result = default;
        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1) return false;
        if (day > Calendar.GetDaysInMonth(year, month)) return false;
        try
        {
            result = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>خواندن «yyyy/MM/dd» یا «yyyy-MM-dd» جلالی (ارقام فارسی/عربی هم پذیرفته می‌شوند).</summary>
    public static bool TryParse(string? text, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = new string(text.Trim().Select(c =>
            c >= '۰' && c <= '۹' ? (char)('0' + (c - '۰'))
            : c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠'))
            : c).ToArray());

        var parts = normalized.Split('/', '-');
        if (parts.Length != 3) return false;
        return int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var d)
               && TryToGregorian(y, m, d, out result);
    }
}
