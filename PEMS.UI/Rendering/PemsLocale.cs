using System.Globalization;
using PEMS.UI.Abstractions;

namespace PEMS.UI.Rendering;

/// <summary>
/// Default <see cref="IPemsLocale"/>: the culture is the current UI culture (the host sets it per request, the renderer per call).
/// Dates are shown as yyyy/MM/dd in the Persian calendar for Persian cultures and as ISO yyyy-MM-dd otherwise; numbers use the culture's
/// separators. Date and time values are shown as given: converting a stored UTC value to the user's time zone is the caller's job.
/// </summary>
public sealed class PemsLocale(PemsUiOptions options) : IPemsLocale
{
    private static readonly PersianCalendar Persian = new();

    public CultureInfo Culture => CultureInfo.CurrentUICulture;
    public string DefaultLanguage => options.DefaultLanguage;
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;

    public string Text(TranslatableText? text) => text?.Resolve(Culture, options.DefaultLanguage) ?? "";

    public string Format(object? value, ValueKind kind = ValueKind.Auto)
    {
        switch (value)
        {
            case null:
                return "";
            case string s:
                return s;
            case DateOnly d when kind is ValueKind.Auto or ValueKind.Date or ValueKind.DateTime:
                return Digits(FormatDate(d.ToDateTime(TimeOnly.MinValue), withTime: false));
            case DateTime dt when kind is ValueKind.Auto or ValueKind.Date or ValueKind.DateTime:
                return Digits(FormatDate(dt, withTime: kind == ValueKind.DateTime || (kind == ValueKind.Auto && dt.TimeOfDay != TimeSpan.Zero)));
            case DateTimeOffset dto when kind is ValueKind.Auto or ValueKind.Date or ValueKind.DateTime:
                return Digits(FormatDate(dto.DateTime, withTime: kind == ValueKind.DateTime || (kind == ValueKind.Auto && dto.TimeOfDay != TimeSpan.Zero)));
            case decimal or double or float:
                return Digits(((IFormattable)value).ToString("#,##0.##########", Culture));
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Digits(((IFormattable)value).ToString("#,##0", Culture));
            case bool b:
                return b ? "true" : "false";
            case IFormattable f:
                return f.ToString(null, Culture);
            default:
                return value.ToString() ?? "";
        }
    }

    private string FormatDate(DateTime date, bool withTime)
    {
        var usePersian = options.Calendar == CalendarMode.Persian
            || (options.Calendar == CalendarMode.Auto && Culture.TwoLetterISOLanguageName == "fa");
        var inPersianRange = date >= Persian.MinSupportedDateTime && date <= Persian.MaxSupportedDateTime;
        var day = usePersian && inPersianRange
            ? $"{Persian.GetYear(date):0000}/{Persian.GetMonth(date):00}/{Persian.GetDayOfMonth(date):00}"
            : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return withTime ? day + " " + date.ToString("HH:mm", CultureInfo.InvariantCulture) : day;
    }

    public string Digits(string s)
    {
        if (!options.UseNativeDigits || Culture.TwoLetterISOLanguageName != "fa") return s;
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++) span[i] = src[i] is >= '0' and <= '9' ? (char)('۰' + (src[i] - '0')) : src[i];
        });
    }
}
