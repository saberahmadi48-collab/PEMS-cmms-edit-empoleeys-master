using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Components;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-53 / #31: Badge and Button write the markup the pages wrote by hand (Bootstrap skin, the default); the Pems skin keeps the library classes.</summary>
public sealed class BadgeButtonSkinTests
{
    private static async Task<string> Badge(string tone, string? css, string inner, TagHelperAttributeList? attributes = null)
    {
        await using var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var helper = new PemsBadgeTagHelper(provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>()) { Tone = tone, Css = css };
        var context = new TagHelperContext("pems-badge", new TagHelperAttributeList(), new Dictionary<object, object>(), "b");
        var output = new TagHelperOutput("pems-badge", attributes ?? new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(inner)));
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    // The class lists below are the ones the views carry today (counted from PEMS.Web/Views).
    [Theory]
    [InlineData("neutral", null, "badge bg-secondary")]
    [InlineData("danger", null, "badge bg-danger")]
    [InlineData("success", null, "badge bg-success")]
    [InlineData("warning", null, "badge bg-warning text-dark")]
    [InlineData("info", null, "badge bg-info text-dark")]
    [InlineData("light", null, "badge bg-light text-dark border")]
    [InlineData("dark", null, "badge bg-dark")]
    [InlineData("danger", "ms-1", "badge bg-danger ms-1")]
    public async Task The_tag_helper_writes_the_badge_classes_the_pages_wrote(string tone, string? css, string expected)
        => Assert.Equal($"<span class=\"{expected}\">Active <b>2</b></span>", await Badge(tone, css, "Active <b>2</b>"));

    [Fact]
    public async Task Tone_none_writes_only_the_classes_the_page_passes()
        => Assert.Equal("<span class=\"badge text-bg-success fs-6\">Ready</span>", await Badge("none", "text-bg-success fs-6", "Ready"));

    [Fact]
    public async Task Other_attributes_of_the_tag_stay_on_the_element_and_are_encoded_once()
    {
        var attributes = new TagHelperAttributeList { new("title", "A & \"B\""), new("style", "min-width:70px;") };
        Assert.Equal("<span class=\"badge bg-danger\" title=\"A &amp; &quot;B&quot;\" style=\"min-width:70px;\">3</span>", await Badge("danger", null, "3", attributes));
    }

    [Fact]
    public async Task The_pems_skin_keeps_the_library_classes()
    {
        await using var provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        var renderer = provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var html = await renderer.RenderAsync<PemsBadge>(new Dictionary<string, object?> { ["Options"] = new BadgeOptions { Label = "Active", Tone = Tone.Success, Skin = ComponentSkin.Pems } });
        Assert.Contains("class=\"pems-badge pems-badge--success\"", html);
    }

    [Theory]
    [InlineData(Tone.Neutral, Size.Small, true, null, "btn btn-sm btn-outline-secondary")]
    [InlineData(Tone.Primary, Size.Small, false, null, "btn btn-sm btn-pems-solid")]
    [InlineData(Tone.Primary, Size.Medium, false, "w-100", "btn btn-pems-solid w-100")]
    [InlineData(Tone.Primary, Size.Small, true, null, "btn btn-sm btn-outline-primary")]
    [InlineData(Tone.Danger, Size.Small, true, null, "btn btn-sm btn-outline-danger")]
    [InlineData(Tone.Success, Size.Small, false, null, "btn btn-sm btn-success")]
    [InlineData(Tone.Warning, Size.Medium, false, null, "btn btn-warning")]
    public void The_button_classes_are_the_ones_the_pages_wrote(Tone tone, Size size, bool outline, string? css, string expected)
        => Assert.Equal(expected, ButtonState.From(new ButtonOptions { Label = "Go", Tone = tone, Size = size, Outline = outline, CssClass = css }).Css);

    [Fact]
    public void The_pems_button_skin_keeps_the_library_classes()
        => Assert.Equal("pems-btn pems-btn--danger pems-btn--small", ButtonState.From(new ButtonOptions { Label = "Go", Tone = Tone.Danger, Size = Size.Small, Skin = ComponentSkin.Pems }).Css);
}

public sealed class ButtonTagHelperTests
{
    private static string Run(PemsButtonTagHelper helper, TagHelperAttributeList attributes, string inner)
    {
        var context = new TagHelperContext("pems-button", new TagHelperAttributeList(), new Dictionary<object, object>(), "c");
        var output = new TagHelperOutput("pems-button", attributes, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(inner)));
        output.Content.SetHtmlContent(inner);
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void A_button_keeps_its_attributes_and_content_and_gets_the_classes()
        => Assert.Equal("<button class=\"btn btn-sm btn-outline-secondary w-100\" type=\"submit\" id=\"go\"><i class=\"bi bi-x\"></i> Cancel</button>",
            Run(new PemsButtonTagHelper(null!, UiTestRenderer.Instance) { Tone = "neutral", Size = "small", Outline = true, Css = "w-100" },
                [new("type", "submit"), new("id", "go")], "<i class=\"bi bi-x\"></i> Cancel"));

    [Fact]
    public void A_link_is_written_with_as_a()
        => Assert.Equal("<a class=\"btn btn-pems-solid\" href=\"/x\">Open</a>", Run(new PemsButtonTagHelper(null!, UiTestRenderer.Instance) { As = "a" }, [new("href", "/x")], "Open"));

    // SCRUM-528: asp-action / asp-route-* bind to this helper, so it must build the href itself (Cancel and Open links went nowhere).
    [Fact]
    public void A_routed_link_gets_its_href_from_the_anchor_helper()
    {
        var helper = new PemsButtonTagHelper(LinkGenerator.Create(), UiTestRenderer.Instance) { As = "a", Tone = "neutral", Outline = true, Action = "Details" };
        helper.RouteValues["id"] = "7";
        Assert.Equal("<a class=\"btn btn-outline-secondary\" href=\"/Details?id=7\">Cancel</a>", Run(helper, [], "Cancel"));
    }

    [Fact]
    public void A_link_without_routing_does_not_call_the_generator()
        => Assert.Equal("<a class=\"btn btn-pems-solid\" href=\"/x\">Open</a>", Run(new PemsButtonTagHelper(LinkGenerator.Create(), UiTestRenderer.Instance) { As = "a" }, [new("href", "/x")], "Open"));

    /// <summary>Fake generator: only GenerateActionLink is used by the anchor helper for an action link.</summary>
    public class LinkGenerator : System.Reflection.DispatchProxy
    {
        public static Microsoft.AspNetCore.Mvc.ViewFeatures.IHtmlGenerator Create() => Create<Microsoft.AspNetCore.Mvc.ViewFeatures.IHtmlGenerator, LinkGenerator>();

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "GenerateActionLink") throw new NotSupportedException(method?.Name);
            var values = args!.OfType<Microsoft.AspNetCore.Routing.RouteValueDictionary>().FirstOrDefault();
            var tag = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("a");
            tag.MergeAttribute("href", "/" + args![2] + (values is { Count: > 0 } ? "?" + string.Join("&", values.Select(v => v.Key + "=" + v.Value)) : ""));
            return tag;
        }
    }

    [Fact]
    public void Tone_none_writes_only_the_size_and_the_classes_the_page_passes()
        => Assert.Equal("<button class=\"btn btn-sm btn-pems p-0\" type=\"submit\">Go</button>",
            Run(new PemsButtonTagHelper(null!, UiTestRenderer.Instance) { Tone = "none", Size = "small", Css = "btn-pems p-0" }, [new("type", "submit")], "Go"));

    [Fact]
    public void A_label_can_look_like_a_button()
        => Assert.Equal("<label class=\"btn btn-sm btn-outline-secondary\" for=\"x\">All</label>",
            Run(new PemsButtonTagHelper(null!, UiTestRenderer.Instance) { As = "label", Tone = "neutral", Size = "small", Outline = true }, [new("for", "x")], "All"));

    [Theory]
    [InlineData(true, "<button class=\"btn btn-pems-solid\" disabled=\"disabled\">Go</button>")]
    [InlineData(false, "<button class=\"btn btn-pems-solid\">Go</button>")]
    public void The_disabled_property_writes_the_attribute_only_when_true(bool disabled, string expected)
        => Assert.Equal(expected, Run(new PemsButtonTagHelper(null!, UiTestRenderer.Instance) { Disabled = disabled }, [], "Go"));
}
