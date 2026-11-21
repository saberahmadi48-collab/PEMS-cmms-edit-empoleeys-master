using System.Globalization;

namespace PEMS.Licensing.Evaluation;

/// <summary>
/// Jalali input to UTC instants (design 5.3): a Jalali end date means the end of that day in Asia/Tehran, stored as the
/// exclusive UTC instant of the next local midnight. Uses the time-zone database, not a fixed offset.
/// </summary>
public static class LicenseDates
{
    public static TimeZoneInfo Tehran()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new TimeZoneNotFoundException("Asia/Tehran time zone is not available on this machine.");
    }

    /// <summary>Exclusive end instant: the licence is valid through the whole Jalali day, expired from the next Tehran midnight.</summary>
    public static DateTimeOffset EndOfJalaliDayUtc(int year, int month, int day)
    {
        var gregorian = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
        return StartOfLocalDayUtc(gregorian.AddDays(1));
    }

    public static DateTimeOffset StartOfJalaliDayUtc(int year, int month, int day)
        => StartOfLocalDayUtc(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));

    private static DateTimeOffset StartOfLocalDayUtc(DateTime localMidnight)
    {
        var local = DateTime.SpecifyKind(localMidnight.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Tehran()), TimeSpan.Zero);
    }

    public static bool TryParseJalali(string? text, out int year, out int month, out int day)
    {
        year = month = day = 0;
        var parts = text?.Trim().Split('/', '-', '.');
        if (parts is not { Length: 3 }) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out day)) return false;
        try { new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static string ToJalaliString(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Tehran());
        var pc = new PersianCalendar();
        return $"{pc.GetYear(local.DateTime):0000}/{pc.GetMonth(local.DateTime):00}/{pc.GetDayOfMonth(local.DateTime):00}";
    }
}
