using System.Runtime.CompilerServices;
using System.Text.Json;
using PEMS.Application.Common;

namespace PEMS.Tests.Common;

/// <summary>
/// SCRUM-206 / #368: the server's Jalali conversion (.NET PersianCalendar) must give the same dates as the browser's (Intl persian calendar, ICU), because the
/// date picker converts in the browser and the server stores the Gregorian date. The fixture holds the Gregorian date of 1 Farvardin for the years 1300 to 1500 as
/// the browser reports it; both sides agreeing on every year start means they agree on every day (months have fixed lengths inside a year).
/// </summary>
public sealed class JalaliDateTests
{
    private static string Here([CallerFilePath] string p = "") => p;

    private static Dictionary<int, DateTime> Starts()
    {
        var json = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Here())!, "..", "TestData", "jalali-year-starts.json"));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("starts").EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name), p => DateTime.ParseExact(p.Value.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Every_year_1300_to_1500_starts_on_the_same_gregorian_day_as_in_the_browser()
    {
        var starts = Starts();
        Assert.Equal(201, starts.Count);
        foreach (var (year, expected) in starts)
        {
            Assert.True(JalaliDate.TryToGregorian(year, 1, 1, out var actual), $"{year}/1/1 did not convert");
            Assert.True(expected == actual, $"1 Farvardin {year}: browser {expected:yyyy-MM-dd}, server {actual:yyyy-MM-dd}");
        }
    }

    [Theory]
    [InlineData(1403, true)]
    [InlineData(1404, false)]
    [InlineData(1407, false)]
    [InlineData(1408, true)]
    public void Esfand_30_exists_only_in_a_leap_year(int year, bool leap)
    {
        Assert.Equal(leap, JalaliDate.TryToGregorian(year, 12, 30, out _));
        Assert.True(JalaliDate.TryToGregorian(year, 12, 29, out _));
    }

    [Fact]
    public void The_last_day_of_a_leap_year_is_the_day_before_the_next_nowruz_and_a_date_has_no_time_part()
    {
        Assert.True(JalaliDate.TryToGregorian(1403, 12, 30, out var last));
        Assert.True(JalaliDate.TryToGregorian(1404, 1, 1, out var nowruz));
        Assert.Equal(new DateTime(2025, 3, 20), last);
        Assert.Equal(last.AddDays(1), nowruz);
        Assert.Equal(TimeSpan.Zero, last.TimeOfDay);   // midnight: a range "to" boundary adds a day instead of 23:59:59
    }

    [Fact]
    public void The_browser_script_converts_every_day_1300_to_1500_like_the_server_fixture()
    {
        // tools/js-tests/pems-date.test.js (SCRUM-225): 73,049 valid days must match the fixture and 1,351 impossible days must be refused. Needs Node 18+ on the PATH;
        // set PEMS_REQUIRE_NODE=1 on a build machine to fail instead of skipping when it is missing.
        var script = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "tools", "js-tests", "pems-date.test.js"));
        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "\"" + script + "\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.False(Environment.GetEnvironmentVariable("PEMS_REQUIRE_NODE") == "1", "node is required on this machine");
            return;
        }
        using (process!)
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "the script did not finish");
            Assert.True(process.ExitCode == 0, output);
        }
    }

    [Theory]
    [InlineData("1403/12/30", true)]
    [InlineData("۱۴۰۳/۱۲/۳۰", true)]
    [InlineData("1403-12-30", true)]
    [InlineData("1404/12/30", false)]
    [InlineData("1403/13/01", false)]
    [InlineData("1403/00/10", false)]
    [InlineData("03/12/30", false)]
    [InlineData("", false)]
    public void Typed_dates_are_parsed_tolerantly_and_impossible_ones_are_rejected(string text, bool valid)
        => Assert.Equal(valid, JalaliDate.TryParse(text, out _));
}
