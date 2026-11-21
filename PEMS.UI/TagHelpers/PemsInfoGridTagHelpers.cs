using PEMS.UI.Components;
using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 InfoGrid: the label / value block at the top of a detail page (code, dates, people, contact). A description list
// laid out as a responsive grid, so screen readers read each label with its value. The page keeps its own values (links,
// badges, conditions); an empty value shows a dash. Styles: _content/PEMS.UI/pems-ui.css (pems-info*), loaded by the layout.
// Usage: docs/design/LIST_PAGE_COMPONENT.md (InfoGrid).

/// <summary>
/// The grid: <c>&lt;dl class="pems-info"&gt;</c> around its <c>pems-info-item</c>s, each label over its value. With
/// <c>layout="rows"</c> each label sits beside its value (side panels, long technical lists); at phone width the label goes
/// over the value again. A class or id on the tag is kept.
/// </summary>
[HtmlTargetElement("pems-info-grid")]
[PemsFacade(UiKeys.InfoGrid)]
public sealed class PemsInfoGridTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("layout")] public string? Layout { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        var options = new InfoGridOptions { Layout = Layout, CssClass = BlockTagHelper.PageClass(output), Attributes = BlockTagHelper.PageAttributes(output, "class") };
        output.Attributes.Clear();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsInfoGrid>(new Dictionary<string, object?>
        {
            [nameof(PemsInfoGrid.Options)] = options,
            [nameof(PemsInfoGrid.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>
/// One label and its value (child content). <c>wide</c> takes the whole row; <c>num</c> sets numbers, codes and dates in a
/// fixed-width, left-to-right font; <c>mono</c> sets a code in a small muted fixed-width font; <c>ltr</c> keeps a phone number
/// or address left to right in Persian pages; <c>tone="danger"</c> colours the value. An empty value shows <c>empty-text</c>
/// (a dash by default).
/// </summary>
[HtmlTargetElement("pems-info-item", ParentTag = "pems-info-grid")]
[PemsFacade(UiKeys.InfoItem)]
public sealed class PemsInfoItemTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("wide")] public bool Wide { get; set; }
    [HtmlAttributeName("num")] public bool Num { get; set; }
    [HtmlAttributeName("mono")] public bool Mono { get; set; }
    [HtmlAttributeName("ltr")] public bool Ltr { get; set; }
    [HtmlAttributeName("tone")] public string? Tone { get; set; }
    [HtmlAttributeName("empty-text")] public string EmptyText { get; set; } = "-";
    [HtmlAttributeName("value-id")] public string? ValueId { get; set; }
    /// <summary>The page's own class on the value (a hook such as pre-wrapped text); never a replacement for an option.</summary>
    [HtmlAttributeName("value-class")] public string? ValueClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();
        var options = new InfoItemOptions
        {
            Label = Label, Wide = Wide, Num = Num, Mono = Mono, Ltr = Ltr, Tone = Tone, EmptyText = EmptyText, ValueId = ValueId, ValueClass = ValueClass,
            Attributes = BlockTagHelper.PageAttributes(output, "class")
        };
        output.Attributes.Clear();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsInfoItem>(new Dictionary<string, object?>
        {
            [nameof(PemsInfoItem.Options)] = options,
            [nameof(PemsInfoItem.Empty)] = content.IsEmptyOrWhiteSpace,
            [nameof(PemsInfoItem.ChildContent)] = BlockTagHelper.Markup(content.GetContent())
        }));
    }
}
