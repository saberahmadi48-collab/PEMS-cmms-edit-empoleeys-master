using System.Globalization;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-385: PemsDates writes exactly what the views' own format strings wrote, in every culture, and converts instants with daylight saving.</summary>
public sealed class PemsDatesTests
{
    private static readonly DateTime Sample = new(2026, 11, 5, 9, 7, 3);

    [Theory]
    [InlineData("en")]
    [InlineData("fa")]
    [InlineData("")]
    public void Every_format_equals_the_old_ToString_in_the_current_culture(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture == "" ? CultureInfo.InvariantCulture : new CultureInfo(culture);
            Assert.Equal(Sample.ToString("yyyy-MM-dd"), PemsDates.Date(Sample));
            Assert.Equal(Sample.ToString("yyyy-MM-dd HH:mm"), PemsDates.DateTime(Sample));
            Assert.Equal(Sample.ToString("yyyy-MM-dd HH:mm:ss"), PemsDates.DateTimeSeconds(Sample));
            Assert.Equal(Sample.ToString("HH:mm"), PemsDates.Time(Sample));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Dates_times_offsets_and_nulls_are_accepted_and_a_null_stays_null()
    {
        Assert.Equal("2026-11-05", PemsDates.Date(new DateOnly(2026, 11, 5)));
        Assert.Equal("09:07", PemsDates.Time(new TimeOnly(9, 7)));
        Assert.Equal("2026-11-05 09:07", PemsDates.DateTime(new DateTimeOffset(Sample, TimeSpan.FromHours(3.5))));
        Assert.Null(PemsDates.Date(null));
        DateTime? none = null;
        Assert.Equal("-", PemsDates.Date(none) ?? "-");
        dynamic late = Sample;   // a dynamic view model value binds at run time
        Assert.Equal("2026-11-05", (string?)PemsDates.Date(late));
    }

    [Fact]
    public void An_instant_is_shown_in_the_user_zone_with_daylight_saving()
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out _)) return;   // zone data missing on this machine
        var before = PemsDates.ToUserTime(new DateTimeOffset(2026, 3, 8, 6, 59, 0, TimeSpan.Zero), "America/New_York");   // 01:59 EST
        var after = PemsDates.ToUserTime(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), "America/New_York");      // 03:00 EDT, the hour 02:xx does not exist
        Assert.Equal(("01:59", TimeSpan.FromHours(-5)), (before.ToString("HH:mm"), before.Offset));
        Assert.Equal(("03:00", TimeSpan.FromHours(-4)), (after.ToString("HH:mm"), after.Offset));
        Assert.Equal(TimeSpan.FromHours(3.5), PemsDates.ToUserTime(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), "Asia/Tehran").Offset);
    }

    [Fact]
    public void An_unknown_or_empty_zone_gives_utc_not_an_error()
    {
        var utc = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(3.5));
        Assert.Equal(TimeSpan.Zero, PemsDates.ToUserTime(utc, "Nowhere/None").Offset);
        Assert.Equal(TimeSpan.Zero, PemsDates.ToUserTime(utc, null).Offset);
        Assert.Equal(utc.UtcDateTime, PemsDates.ToUserTime(utc, "").UtcDateTime);
    }
}
