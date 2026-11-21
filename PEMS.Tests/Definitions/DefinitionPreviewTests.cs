using System.Text.Json.Nodes;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-792, 787: the preview of a component sample or a page version in a language, direction and theme. The render itself is the page renderer; these tests cover the options and the document.</summary>
public sealed class DefinitionPreviewTests
{
    [Theory]
    [InlineData(null, null, null, "fa", "rtl", "light")]
    [InlineData("en", null, null, "en", "ltr", "light")]
    [InlineData("en", "rtl", "dark", "en", "rtl", "dark")]
    [InlineData("xx", "up", "neon", "fa", "rtl", "light")]
    public void The_toggles_default_from_the_language_and_unknown_values_fall_back(string? culture, string? dir, string? theme, string c, string d, string t)
        => Assert.Equal(new DefinitionPreviewHostController.PreviewOptions(c, d, t), DefinitionPreviewHostController.PreviewOptions.From(culture, dir, theme));

    [Fact]
    public void A_sample_page_holds_the_component_with_its_sample_options()
    {
        var page = DefinitionPreviewHostController.SamplePage("kpi-tile", JsonNode.Parse("""{ "value": "12" }"""));
        var instance = page["regions"]!["content"]![0]!;
        Assert.Equal("kpi-tile", (string)instance["component"]!);
        Assert.Equal("12", (string)instance["options"]!["value"]!);
        Assert.Equal("{}", DefinitionPreviewHostController.SamplePage("x", null)["regions"]!["content"]![0]!["options"]!.ToJsonString());
    }

    [Fact]
    public void The_document_sets_language_direction_and_theme_lists_problems_and_encodes_the_title()
    {
        var html = DefinitionPreviewHostController.Document(new DefinitionPreviewHostController.PreviewOptions("en", "rtl", "dark"), "/preview/definition?ref=core:page:x", "<b>t</b>",
            new RenderedPage("<p>hello</p>", null, ["The layout \"x\" does not exist."]));
        Assert.Contains("lang=\"en\" dir=\"rtl\" data-theme=\"dark\"", html);
        Assert.Contains("<p>hello</p>", html);
        Assert.Contains("&lt;b&gt;t&lt;/b&gt;", html);                                   // the title is encoded
        Assert.Contains("does not exist.", html);
        Assert.Contains("culture=fa&amp;dir=rtl&amp;theme=dark", html);                  // a toggle keeps the other two choices
        Assert.Contains("href=\"/preview/definition?ref=core:page:x&amp;culture=en&amp;dir=ltr&amp;theme=dark\"", html);
    }
}
