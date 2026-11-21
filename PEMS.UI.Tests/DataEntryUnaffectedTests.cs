using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;
using PEMS.UI.Tests.Host;

namespace PEMS.UI.Tests;

/// <summary>
/// Persian digits and the Jalali calendar are DISPLAY formatting (ADR-0008). What a user types, posts or has stored must reach the server and
/// come back unchanged in every culture. The test host is set up like PEMS.Web (cultures "en" and "fa", Gregorian calendar on "fa").
/// Comparisons are ordinal on purpose: a culture-sensitive comparison can treat a Persian digit as equal to a Latin one.
/// </summary>
public sealed class DataEntryUnaffectedTests(SpikeHost host, ITestOutputHelper output) : IClassFixture<SpikeHost>
{
    private const string Fa = "?culture=fa&ui-culture=fa";

    private static string Plain(string html) => WebUtility.HtmlDecode(html);
    private static void Has(string expected, string actual) => Assert.Contains(expected, actual, StringComparison.Ordinal);
    private static void HasNot(string unexpected, string actual) => Assert.DoesNotContain(unexpected, actual, StringComparison.Ordinal);
    private static readonly Regex PersianDigit = new("[۰-۹٠-٩]", RegexOptions.Compiled);

    private async Task<string> PostAsync(string query, IDictionary<string, string> fields)
    {
        var (_, token, cookie) = await host.GetFormAsync("/b/form" + query);
        var response = await host.PostFormAsync("/b/form" + query, token, cookie, fields);
        return await response.Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string> Valid(string quantity = "5", string due = "2026-10-27", string price = "12") =>
        new() { ["Name"] = "PT-0001", ["Quantity"] = quantity, ["Due"] = due, ["Price"] = price };

    // ---- what the user typed comes back as typed
    [Fact]
    public async Task Input_values_are_echoed_exactly_as_typed_under_the_persian_culture()
    {
        var html = Plain(await PostAsync(Fa, new Dictionary<string, string>
            { ["Name"] = "", ["Quantity"] = "500", ["Due"] = "2026-10-27", ["Price"] = "12.5" }));      // invalid, so the form is shown again
        Has("value=\"500\"", html);
        Has("value=\"2026-10-27\"", html);        // ISO: what a date input needs; not Jalali, not Persian digits
        Has("value=\"12.5\"", html);              // a dot and Latin digits: what a number input submits
        Assert.DoesNotMatch(PersianDigit, html[html.IndexOf("<form", StringComparison.Ordinal)..]);   // no Persian digit anywhere in the form markup
    }

    // ---- what is posted binds to the same values in every culture
    [Theory]
    [InlineData("")]
    [InlineData(Fa)]
    public async Task Latin_digits_integers_and_iso_dates_bind_to_the_same_values_in_every_culture(string query)
    {
        var body = await PostAsync(query, Valid());
        output.WriteLine($"culture query '{query}': {body}");
        Assert.Equal("saved:PT-0001:5:2026-10-27:False::12", body);      // the Gregorian date 2026-10-27, not a Persian-calendar date
    }

    [Fact]
    public void The_gregorian_override_for_fa_is_still_needed_and_protects_dates()
    {
        // .NET's "fa" culture uses the Persian calendar, so an ISO date would silently become year 2648; PEMS.Web keeps Gregorian (Program.cs).
        var plain = new CultureInfo("fa");
        Assert.True(DateTime.TryParse("2026-10-27", plain, DateTimeStyles.None, out var wrong));
        Assert.NotEqual(2026, wrong.Year);

        var overridden = new CultureInfo("fa");
        overridden.DateTimeFormat.Calendar = new GregorianCalendar();
        Assert.True(DateTime.TryParse("2026-10-27", overridden, DateTimeStyles.None, out var right));
        Assert.Equal(new DateTime(2026, 10, 27), right);
    }

    // ---- SCRUM-421 (fixed): the binder accepts what a Persian page really posts and what a Persian user may type
    [Fact]
    public async Task A_decimal_with_a_dot_binds_under_the_persian_culture_as_it_does_in_English()
    {
        Assert.Equal("saved:PT-0001:5:2026-10-27:False::12.5", await PostAsync(Fa, Valid(price: "12.5")));
        Assert.Equal("saved:PT-0001:5:2026-10-27:False::12.5", await PostAsync("", Valid(price: "12.5")));
    }

    [Fact]
    public async Task Typed_persian_digits_bind_to_the_same_values_as_latin_digits()
    {
        var persian = await PostAsync(Fa, Valid(quantity: "۵", due: "۲۰۲۶-۱۰-۲۷", price: "۱۲"));
        Assert.Equal("saved:PT-0001:5:2026-10-27:False::12", persian);
    }

    // ---- display formatting never rewrites stored text or input values
    [Fact]
    public async Task Stored_text_that_contains_digits_is_never_rewritten_when_displayed()
    {
        var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var view = new ViewDescriptor { Id = "v", Title = "T", Columns = [new ColumnDescriptor { Key = "tag", Header = "Tag" }, new ColumnDescriptor { Key = "n", Header = "N" }, new ColumnDescriptor { Key = "d", Header = "D" }] };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["tag"] = "PT-0001", ["n"] = "12.50", ["d"] = "2026-10-27" }       // text: shown exactly
        };
        var html = Plain(await renderer.RenderAsync("table", new Dictionary<string, object?> { ["View"] = view, ["Rows"] = rows }, new CultureInfo("fa")));
        Has(">PT-0001<", html);
        Has(">12.50<", html);
        Has(">2026-10-27<", html);
        Assert.DoesNotMatch(PersianDigit, html);
    }

    [Fact]
    public async Task A_field_value_is_never_run_through_the_display_formatter()
    {
        var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        async Task<string> Field(FieldKind kind, string value) => await renderer.RenderAsync("field", new Dictionary<string, object?>
            { ["Descriptor"] = new FieldDescriptor { Name = "x", Label = "X", Kind = kind }, ["Value"] = value }, new CultureInfo("fa"));
        Has("value=\"1234.5\"", await Field(FieldKind.Number, "1234.5"));
        Has("value=\"2026-10-27\"", await Field(FieldKind.Date, "2026-10-27"));
        Has("value=\"PT-0001\"", await Field(FieldKind.Text, "PT-0001"));
    }

    [Fact]
    public async Task Only_typed_values_shown_as_data_are_formatted_and_only_for_display()
    {
        var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var view = new ViewDescriptor { Id = "v", Title = "T", Columns = [new ColumnDescriptor { Key = "d", Header = "D" }, new ColumnDescriptor { Key = "n", Header = "N" }] };
        var rows = new List<IReadOnlyDictionary<string, object?>>
            { new Dictionary<string, object?> { ["d"] = new DateOnly(2026, 10, 27), ["n"] = 1234 } };
        Task<string> Render(string c) => renderer.RenderAsync("table", new Dictionary<string, object?> { ["View"] = view, ["Rows"] = rows }, new CultureInfo(c));
        var fa = Plain(await Render("fa"));
        var en = Plain(await Render("en"));
        Has(">۱۴۰۵/۰۸/۰۵<", fa);              // a real date value: Jalali with Persian digits for display
        Has(">2026-10-27<", en);               // English: ISO with Latin digits
        Has(">1,234<", en);
        Assert.DoesNotMatch(PersianDigit, en);
    }
}
