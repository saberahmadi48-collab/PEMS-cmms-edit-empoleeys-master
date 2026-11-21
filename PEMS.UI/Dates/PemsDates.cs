using System.Globalization;

namespace PEMS.UI;

/// <summary>
/// SCRUM-385: the one place where a view turns a date or an instant into display text. Views call <c>@PemsDates.Date(x)</c>, <c>DateTime</c> or <c>Time</c> and never write a format string,
/// so the calendar, the user's time zone and the presets can change here without touching 400 view expressions. Today the output is exactly what
/// <c>x.ToString("yyyy-MM-dd")</c> wrote (current culture); a null value gives null (the old <c>x?.ToString(...)</c>), which a page can still replace with <c>?? "-"</c>.
/// The values are DateTime, DateTimeOffset, DateOnly, TimeOnly or null; a dynamic value works because the parameter is <c>object</c>.
/// </summary>
public static class PemsDates
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateTimeSecondsFormat = "yyyy-MM-dd HH:mm:ss";
    public const string TimeFormat = "HH:mm";

    /// <summary>A date: <c>2026-11-15</c>.</summary>
    public static string? Date(object? value) => Format(value, DateFormat);

    /// <summary>A date and a time to the minute: <c>2026-11-15 09:30</c>.</summary>
    public static string? DateTime(object? value) => Format(value, DateTimeFormat);

    /// <summary>A date and a time to the second: <c>2026-11-15 09:30:12</c>.</summary>
    public static string? DateTimeSeconds(object? value) => Format(value, DateTimeSecondsFormat);

    /// <summary>A time to the minute: <c>09:30</c>.</summary>
    public static string? Time(object? value) => Format(value, TimeFormat);

    private static string? Format(object? value, string format) => value switch
    {
        null => null,
        IFormattable f => f.ToString(format, CultureInfo.CurrentCulture),
        _ => value.ToString()
    };

    /// <summary>
    /// An instant (UTC) shown in a user's time zone, daylight saving included: <c>ToUserTime(utc, "America/New_York")</c>. A zone id that is unknown on this machine gives the instant
    /// unchanged (UTC) instead of an exception. Not yet used by the views: the stored values are local server times until the storage decision of SCRUM-385 is taken.
    /// </summary>
    public static DateTimeOffset ToUserTime(DateTimeOffset instant, string? zoneId)
        => !string.IsNullOrWhiteSpace(zoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone) ? TimeZoneInfo.ConvertTime(instant, zone) : instant.ToUniversalTime();
}
