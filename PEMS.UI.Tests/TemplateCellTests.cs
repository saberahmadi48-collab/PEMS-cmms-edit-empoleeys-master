using System.Net;
using PEMS.UI.Tests.Host;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-570: a data-table cell written as a Razor template (<c>DataCell.Template</c>) keeps its tag helpers: the form, its antiforgery token, the button and the encoded row values.</summary>
public sealed class TemplateCellTests(SpikeHost host, ITestOutputHelper output) : IClassFixture<SpikeHost>
{
    [Fact]
    public async Task A_template_cell_renders_the_form_with_antiforgery_and_the_row_value_per_row()
    {
        var response = await host.Client.GetAsync("/b/template-cell");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<form ").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count);
        Assert.Contains("name=\"key\" value=\"alpha\"", html);
        Assert.Contains("value=\"beta &amp; &lt;gamma&gt;\"", html);
        Assert.Contains("ALPHA", html);
        Assert.Contains("class=\"d-inline\"", html);
    }

    [Fact]
    public async Task A_thousand_template_rows_cost_about_the_same_as_the_hand_written_table()
    {
        async Task<(double Ms, string Html)> Time(string url)
        {
            await host.Client.GetAsync(url);   // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var html = await (await host.Client.GetAsync(url)).Content.ReadAsStringAsync();
            return (sw.Elapsed.TotalMilliseconds, html);
        }
        var hand = await Time("/b/perf-hand");
        var template = await Time("/b/perf-template");
        output.WriteLine($"hand {hand.Ms:F0} ms ({hand.Html.Length} bytes), template {template.Ms:F0} ms ({template.Html.Length} bytes)");
        Assert.Equal(1000, System.Text.RegularExpressions.Regex.Matches(template.Html, "<form ").Count);
        Assert.Equal(1000, System.Text.RegularExpressions.Regex.Matches(hand.Html, "<form ").Count);
        Assert.True(template.Ms < Math.Max(hand.Ms * 3, 1500), $"template {template.Ms:F0} ms vs hand {hand.Ms:F0} ms");
    }
}
