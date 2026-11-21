using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>Batch 5: <c>content-blocks</c> renders a text page from a definition: both languages, escaped text, and only links of this site.</summary>
public sealed class ContentBlocksTests
{
    private static string Render(ContentBlocksOptions options, string culture = "en")
        => UiTestRenderer.Instance.RenderAsync("content-blocks", new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    private static ContentBlocksOptions Sample()
        => System.Text.Json.JsonSerializer.Deserialize<ContentBlocksOptions>(ComponentSamples.Get("content-blocks")!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;

    [Fact]
    public void The_sample_renders_every_block_in_both_languages()
    {
        var en = Render(Sample());
        var fa = System.Net.WebUtility.HtmlDecode(Render(Sample(), "fa"));
        Assert.Contains("<h2 class=\"h3 mb-3\">Notes</h2>", en);
        Assert.Contains("alert alert-warning", en);
        Assert.Contains("<li>Second</li>", en);
        Assert.Contains("<a class=\"link-primary\" href=\"/Help\">Help</a>", en);
        Assert.Contains("<hr", en);
        Assert.Contains("یادداشت‌ها", fa);
        Assert.Contains("<li>دوم</li>", fa);
    }

    [Fact]
    public void Text_is_escaped_and_a_link_off_this_site_is_dropped()
    {
        var options = new ContentBlocksOptions
        {
            Blocks =
            [
                new ContentBlock { Type = "text", Text = PEMS.UI.Abstractions.TranslatableText.Literal("<script>alert(1)</script>") },
                new ContentBlock { Type = "link", Href = "https://evil.example/x", Text = PEMS.UI.Abstractions.TranslatableText.Literal("Out") },
                new ContentBlock { Type = "link", Href = "//evil.example/x", Text = PEMS.UI.Abstractions.TranslatableText.Literal("Out2") },
            ],
        };
        var html = Render(options);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("evil.example", html);
    }

    [Fact]
    public void Cards_show_icon_title_text_and_a_link_and_an_unsafe_icon_or_link_is_dropped()
    {
        var options = new ContentBlocksOptions
        {
            Blocks =
            [
                new ContentBlock
                {
                    Type = "cards", Columns = 4,
                    Cards =
                    [
                        new ContentCard { Icon = "bi-people", Title = PEMS.UI.Abstractions.TranslatableText.Literal("Users"), Text = PEMS.UI.Abstractions.TranslatableText.Literal("Create logins."), Links = [new ContentLink { Href = "/Admin/Users" }] },
                        new ContentCard { Icon = "x\" onclick=\"1", Title = PEMS.UI.Abstractions.TranslatableText.Literal("Bad"), Links = [new ContentLink { Href = "javascript:alert(1)" }] },
                    ],
                },
            ],
        };
        var html = Render(options);
        Assert.Contains("col-md-3 col-sm-6", html);
        Assert.Contains("bi bi-people fs-4 text-muted", html);
        Assert.Contains("<a class=\"btn btn-sm btn-outline-primary\" href=\"/Admin/Users\">Open</a>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("onclick", html);
    }

    [Fact]
    public void No_blocks_shows_the_empty_text()
        => Assert.Contains("Nothing to show", Render(new ContentBlocksOptions()));
}
