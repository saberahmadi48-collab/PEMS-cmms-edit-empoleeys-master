using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>Batch 5: <c>page-filters</c> draws a GET form from a definition: both languages, the value selected, the reset link only for a path of this site.</summary>
public sealed class PageFiltersTests
{
    private static string Render(PageFiltersOptions options, string culture = "en")
        => UiTestRenderer.Instance.RenderAsync("page-filters", new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    private static PageFiltersOptions Sample()
        => System.Text.Json.JsonSerializer.Deserialize<PageFiltersOptions>(ComponentSamples.Get("page-filters")!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;

    [Fact]
    public void The_sample_renders_the_form_with_the_value_selected_in_both_languages()
    {
        var en = Render(Sample());
        var fa = System.Net.WebUtility.HtmlDecode(Render(Sample(), "fa"));
        Assert.Contains("<form method=\"get\"", en);
        Assert.Contains("<option value=\"2\" selected", en);
        Assert.Contains("type=\"date\"", en);
        Assert.Contains("href=\"/p/report\"", en);
        Assert.Contains("پروژه", fa);
        Assert.Contains("واحد ۲", fa);
    }

    [Fact]
    public void A_reset_link_off_this_site_is_dropped()
    {
        var html = Render(Sample() with { ResetUrl = "https://evil.example/x" });
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("btn-outline-secondary", html);
    }
}
