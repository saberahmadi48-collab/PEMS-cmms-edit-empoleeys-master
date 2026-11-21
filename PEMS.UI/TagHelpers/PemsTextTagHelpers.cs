using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

// SCRUM-522: the text elements of a page as tag helpers: heading, paragraph, link, bullet list, bullet, divider. Each writes today's element with the page's own class and
// attributes kept (the page's class goes after the component's); the registry components write the markup.

/// <summary>A heading: <c>&lt;pems-heading level="6" class="mb-3"&gt;Title&lt;/pems-heading&gt;</c> writes <c>&lt;h6 class="mb-3"&gt;Title&lt;/h6&gt;</c>.</summary>
[HtmlTargetElement("pems-heading")]
[PemsFacade(UiKeys.Heading)]
public sealed class PemsHeadingTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("level")] public int Level { get; set; } = 2;
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsHeading>(renderer, output, new HeadingOptions { Level = Level, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A paragraph: <c>&lt;pems-text class="small text-muted"&gt;...&lt;/pems-text&gt;</c> writes <c>&lt;p class="small text-muted"&gt;</c>.</summary>
[HtmlTargetElement("pems-text")]
[PemsFacade(UiKeys.Text)]
public sealed class PemsTextTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsText>(renderer, output, new TextBlockOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>
/// A link: <c>&lt;pems-link asp-action="Details" asp-route-id="@x"&gt;text&lt;/pems-link&gt;</c> writes <c>&lt;a href="..."&gt;</c>. The routing attributes of the anchor helper work
/// (the built-in helper computes the href, the component writes the element); a plain <c>href</c>, <c>target</c>, <c>data-*</c> and <c>class</c> are kept.
/// </summary>
[HtmlTargetElement("pems-link")]
[PemsFacade(UiKeys.Link)]
public sealed class PemsLinkTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : AnchorTagHelper(generator)
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Action is not null || Controller is not null || Area is not null || Page is not null || PageHandler is not null || Route is not null || Host is not null || Protocol is not null || Fragment is not null || RouteValues.Count > 0)
            base.Process(context, output);
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsLink>(renderer, output, new LinkOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A list: <c>&lt;pems-bullet-list class="mb-0"&gt;&lt;pems-bullet&gt;...&lt;/pems-bullet&gt;&lt;/pems-bullet-list&gt;</c> writes <c>&lt;ul class="mb-0"&gt;&lt;li&gt;</c> (<c>ordered="true"</c> writes <c>ol</c>).</summary>
[HtmlTargetElement("pems-bullet-list")]
[PemsFacade(UiKeys.BulletList)]
public sealed class PemsBulletListTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("ordered")] public bool Ordered { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsBulletList>(renderer, output, new BulletListOptions { Ordered = Ordered, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>One list item: <c>&lt;pems-bullet&gt;text&lt;/pems-bullet&gt;</c> writes <c>&lt;li&gt;</c>.</summary>
[HtmlTargetElement("pems-bullet")]
[PemsFacade(UiKeys.Bullet)]
public sealed class PemsBulletTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsBullet>(renderer, output, new BulletOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>A divider: <c>&lt;pems-divider class="my-3" /&gt;</c> writes <c>&lt;hr class="my-3" /&gt;</c>.</summary>
[HtmlTargetElement("pems-divider")]
[PemsFacade(UiKeys.Divider)]
public sealed class PemsDividerTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsDivider>(renderer, output, new DividerOptions { Css = Css, PageClass = pageClass, Attributes = attrs });
    }
}

/// <summary>The stage diagram: <c>&lt;pems-stage-diagram steps="@steps" /&gt;</c> (variant <c>wo</c> for the work-order look) writes the circles and connectors; the page computes every state.</summary>
[HtmlTargetElement("pems-stage-diagram")]
[PemsFacade(UiKeys.StageDiagram)]
public sealed class PemsStageDiagramTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("steps")] public IReadOnlyList<StageStep>? Steps { get; set; }
    /// <summary>wf (default), wo or vs.</summary>
    [HtmlAttributeName("variant")] public string Variant { get; set; } = "wf";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => await ListRender.Replace<PemsStageDiagram>(renderer, output, new StageDiagramOptions { Variant = Variant, Steps = Steps });
}

/// <summary>
/// An entry of a side panel: <c>&lt;pems-side-link icon="bi-inbox" count="@n" active="@a" asp-action="Index"&gt;Inbox&lt;/pems-side-link&gt;</c> writes the icon, the label and the count in the
/// <c>look</c> the page names (<c>cartable</c> default: <c>a.ct-folder</c>; <c>pems</c>: <c>a.pems-sidenav__item</c>). The anchor routing attributes work.
/// </summary>
[HtmlTargetElement("pems-side-link")]
[PemsFacade(UiKeys.SideLink)]
public sealed class PemsSideLinkTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : AnchorTagHelper(generator)
{
    [HtmlAttributeName("look")] public string Look { get; set; } = "cartable";
    [HtmlAttributeName("active")] public bool Active { get; set; }
    [HtmlAttributeName("icon")] public string? Icon { get; set; }
    [HtmlAttributeName("count")] public int? Count { get; set; }
    [HtmlAttributeName("hot")] public string? Hot { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Action is not null || Controller is not null || Area is not null || Page is not null || PageHandler is not null || Route is not null || Host is not null || Protocol is not null || Fragment is not null || RouteValues.Count > 0)
            base.Process(context, output);
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsSideLink>(renderer, output, new SideLinkOptions { Look = Look, Active = Active, Icon = Icon, Count = Count, Hot = Hot, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}
