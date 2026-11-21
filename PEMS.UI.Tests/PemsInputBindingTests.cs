using PEMS.UI.Tests.Host;

namespace PEMS.UI.Tests;

/// <summary>
/// SCRUM-421: model binding of numbers and dates on Persian pages. The host is set up like PEMS.Web (cultures en and fa, Gregorian on fa)
/// and registers the same binder PEMS.Web registers. A browser number input posts "12.5" with a dot; a Persian user may also type
/// Persian digits and the Persian decimal mark. English behaviour must not change.
/// </summary>
public sealed class PemsInputBindingTests(SpikeHost host) : IClassFixture<SpikeHost>
{
    private const string Fa = "?culture=fa&ui-culture=fa";

    private async Task<Dictionary<string, string>> PostTypedAsync(string query, IDictionary<string, string> fields)
    {
        var (_, token, cookie) = await host.GetFormAsync("/b/form" + query);
        var response = await host.PostFormAsync("/b/typed" + query, token, cookie, fields);
        var body = await response.Content.ReadAsStringAsync();
        return body.Split('|').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
    }

    // ---- what the browser submits binds in every culture
    [Theory]
    [InlineData("")]
    [InlineData(Fa)]
    public async Task What_a_browser_submits_binds_to_the_same_values_in_English_and_Persian(string query)
    {
        var r = await PostTypedAsync(query, new Dictionary<string, string>
        {
            ["Quantity"] = "5", ["OptionalCount"] = "7", ["Big"] = "9876543210", ["Price"] = "12.5", ["OptionalPrice"] = "0.25",
            ["Ratio"] = "3.75", ["OptionalRatio"] = "1e-2", ["Due"] = "2026-10-27", ["Day"] = "2026-10-28"
        });
        Assert.Equal("", r["errors"]);
        Assert.Equal("5", r["Quantity"]);
        Assert.Equal("7", r["OptionalCount"]);
        Assert.Equal("9876543210", r["Big"]);
        Assert.Equal("12.5", r["Price"]);
        Assert.Equal("0.25", r["OptionalPrice"]);
        Assert.Equal("3.75", r["Ratio"]);
        Assert.Equal("0.01", r["OptionalRatio"]);
        Assert.Equal("2026-10-27 00:00", r["Due"]);
        Assert.Equal("2026-10-28", r["Day"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Fa)]
    public async Task Empty_values_give_null_for_nullable_and_the_standard_error_for_required_numbers(string query)
    {
        var r = await PostTypedAsync(query, new Dictionary<string, string>
            { ["Quantity"] = "", ["OptionalCount"] = "", ["OptionalPrice"] = " ", ["Due"] = "", ["Day"] = "" });
        Assert.Equal("null", r["OptionalCount"]);
        Assert.Equal("null", r["OptionalPrice"]);
        Assert.Equal("null", r["Due"]);
        Assert.Equal("null", r["Day"]);
        Assert.Contains("Quantity=", r["errors"]);                 // a non-nullable number cannot be empty
    }

    // ---- what a Persian user may type
    [Fact]
    public async Task Persian_digits_and_the_persian_decimal_mark_are_accepted_on_a_persian_page()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string>
        {
            ["Quantity"] = "۵", ["Big"] = "۹۸۷۶۵۴۳۲۱۰", ["Price"] = "۱۲٫۵", ["Ratio"] = "۳٫۷۵", ["Due"] = "۲۰۲۶-۱۰-۲۷", ["Day"] = "۲۰۲۶-۱۰-۲۸"
        });
        Assert.Equal("", r["errors"]);
        Assert.Equal("5", r["Quantity"]);
        Assert.Equal("9876543210", r["Big"]);
        Assert.Equal("12.5", r["Price"]);
        Assert.Equal("3.75", r["Ratio"]);
        Assert.Equal("2026-10-27 00:00", r["Due"]);
        Assert.Equal("2026-10-28", r["Day"]);
    }

    [Fact]
    public async Task Arabic_indic_digits_work_the_same_way()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Quantity"] = "٥", ["Price"] = "١٢٫٥" });
        Assert.Equal("", r["errors"]);
        Assert.Equal("5", r["Quantity"]);
        Assert.Equal("12.5", r["Price"]);
    }

    [Fact]
    public async Task The_persian_thousands_mark_is_ignored()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Big"] = "۱٬۲۳۴" });
        Assert.Equal("", r["errors"]);
        Assert.Equal("1234", r["Big"]);
    }

    // ---- edges that every page can hit (SCRUM-421 QA line 9): same behaviour in English and Persian
    [Theory]
    [InlineData("")]
    [InlineData(Fa)]
    public async Task Negative_numbers_signs_and_exponents_bind(string query)
    {
        var r = await PostTypedAsync(query, new Dictionary<string, string>
            { ["Quantity"] = "-5", ["Big"] = "+7", ["Price"] = "-12.5", ["Ratio"] = "-3.5e2", ["OptionalPrice"] = "-0.01" });
        Assert.Equal("", r["errors"]);
        Assert.Equal("-5", r["Quantity"]);
        Assert.Equal("7", r["Big"]);
        Assert.Equal("-12.5", r["Price"]);
        Assert.Equal("-350", r["Ratio"]);
        Assert.Equal("-0.01", r["OptionalPrice"]);
    }

    [Fact]
    public async Task Persian_negative_numbers_bind()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Quantity"] = "-۵", ["Price"] = "-۱۲٫۵" });
        Assert.Equal("", r["errors"]);
        Assert.Equal("-5", r["Quantity"]);
        Assert.Equal("-12.5", r["Price"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Fa)]
    public async Task A_latin_thousands_comma_is_refused_exactly_as_before(string query)
    {
        // The built-in converters do not accept a thousands separator for integers or decimals; the binder must not start to.
        var r = await PostTypedAsync(query, new Dictionary<string, string> { ["Quantity"] = "1,234", ["Price"] = "1,234.5" });
        Assert.Contains("Quantity=The value '1,234' is not valid for Quantity.", r["errors"]);
        Assert.Contains("Price=The value '1,234.5' is not valid for Price.", r["errors"]);
    }

    [Fact]
    public async Task Surrounding_spaces_are_ignored_and_inner_spaces_are_not()
    {
        var r = await PostTypedAsync("", new Dictionary<string, string> { ["Quantity"] = " 5 ", ["Price"] = "1 2" });
        Assert.Equal("5", r["Quantity"]);
        Assert.Contains("Price=The value '1 2' is not valid for Price.", r["errors"]);
    }

    [Fact]
    public async Task A_value_that_was_not_posted_leaves_the_property_untouched()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Quantity"] = "3" });          // everything else missing
        Assert.Equal("", r["errors"]);
        Assert.Equal("null", r["OptionalCount"]);
        Assert.Equal("0", r["Price"]);
        Assert.Equal("null", r["Due"]);
    }

    // ---- garbage is still garbage, with MVC's own message
    [Theory]
    [InlineData("", "abc")]
    [InlineData(Fa, "abc")]
    [InlineData(Fa, "12.5.6")]
    [InlineData("", "99999999999999999999999999999")]          // overflow for int
    public async Task Invalid_numbers_are_rejected_with_the_standard_message(string query, string value)
    {
        var r = await PostTypedAsync(query, new Dictionary<string, string> { ["Quantity"] = value });
        Assert.Contains($"Quantity=The value '{value}' is not valid for Quantity.", r["errors"]);
    }

    [Fact]
    public async Task A_decimal_value_is_not_accepted_for_an_integer_field()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Quantity"] = "5.5" });
        Assert.Contains("Quantity=The value '5.5' is not valid for Quantity.", r["errors"]);
    }

    [Theory]
    [InlineData("", "2026-13-45")]
    [InlineData(Fa, "not a date")]
    public async Task Invalid_dates_are_rejected_with_the_standard_message(string query, string value)
    {
        var r = await PostTypedAsync(query, new Dictionary<string, string> { ["Quantity"] = "1", ["Due"] = value });
        Assert.Contains($"Due=The value '{value}' is not valid for Due.", r["errors"]);
    }

    // ---- the date a user picks is the date that is stored, whatever the calendar of the culture
    [Fact]
    public async Task An_iso_date_never_turns_into_a_persian_calendar_date()
    {
        var r = await PostTypedAsync(Fa, new Dictionary<string, string> { ["Quantity"] = "1", ["Due"] = "2026-10-27T14:30" });
        Assert.Equal("", r["errors"]);
        Assert.Equal("2026-10-27 14:30", r["Due"]);
    }
}
