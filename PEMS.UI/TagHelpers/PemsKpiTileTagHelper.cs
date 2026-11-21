using PEMS.UI.Rendering;
using PEMS.UI.Components;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 KpiTile: one number with its label (report headers, dashboards, detail pages). Renders the kpi-card markup the
// retired `_KpiCard` partial rendered (styles: admin.css .kpi-card / .kpi-icon / .kpi-value / .kpi-label / .bg-icon-*), so a page
// looks the same; the value is the page's own text (counts, decimals, %, units). Usage: docs/design/LIST_PAGE_COMPONENT.md (KpiTile).

/// <summary>
/// A KPI tile: <c>label</c>, the value as child content (a dash when empty), an <c>icon</c> (Bootstrap icon name) in a
/// coloured square (<c>color</c>: blue, green, amber, red, purple, slate, orange ... or a full <c>bg-icon-*</c> class),
/// an optional <c>sub</c> line under the label, <c>href</c> to make the whole tile a link, and <c>hint</c> as its tool tip.
/// </summary>
[HtmlTargetElement("pems-kpi-tile")]
[PemsFacade(UiKeys.KpiTile)]
public sealed class PemsKpiTileTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "";
    [HtmlAttributeName("icon")] public string? Icon { get; set; }
    [HtmlAttributeName("color")] public string? Color { get; set; }
    [HtmlAttributeName("sub")] public string? Sub { get; set; }
    [HtmlAttributeName("href")] public string? Href { get; set; }
    [HtmlAttributeName("hint")] public string? Hint { get; set; }

    /// <summary>A small tile for a detail page (tighter, smaller number, no lift on hover); usually without an icon.</summary>
    [HtmlAttributeName("compact")] public bool Compact { get; set; }

    /// <summary>Colours the value: <c>danger</c>, <c>success</c> or <c>warning</c> (anything else: the default colour).</summary>
    [HtmlAttributeName("tone")] public string? Tone { get; set; }

    /// <summary>
    /// A tile built in code (a controller's or a page's list of tiles): its title, count (a dash for zero), icon, colour, link
    /// and hint fill the tile; an attribute written on the tag wins.
    /// </summary>
    [HtmlAttributeName("model")] public PEMS.Web.Models.KpiCardViewModel? Model { get; set; }

    /// <summary>The colour class of the icon square: <c>green</c> becomes <c>bg-icon-green</c>; a full class is kept.</summary>
    public static string ColorClass(string? color) =>
        string.IsNullOrWhiteSpace(color) ? "bg-icon-blue" : color.StartsWith("bg-icon-", StringComparison.Ordinal) ? color : "bg-icon-" + color;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Model != null)
        {
            if (string.IsNullOrEmpty(Label)) Label = Model.Title;
            Icon ??= Model.IconClass;
            Color ??= Model.ColorClass;
            Href ??= Model.Url;
            Hint ??= Model.Hint;
        }
        var content = await output.GetChildContentAsync();
        var empty = content.IsEmptyOrWhiteSpace;
        var value = empty && Model != null ? System.Net.WebUtility.HtmlEncode(Model.DisplayCount) : content.GetContent();
        var options = new KpiTileOptions
        {
            Label = Label, Icon = string.IsNullOrEmpty(Icon) ? null : Icon, IconClass = ColorClass(Color), Sub = Sub, Href = Href, Hint = Hint, Compact = Compact, Tone = Tone,
            CssClass = BlockTagHelper.PageClass(output), Attributes = BlockTagHelper.PageAttributes(output, "class", string.IsNullOrEmpty(Href) ? "" : "href", string.IsNullOrEmpty(Hint) ? "" : "title")
        };
        output.Attributes.Clear();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsKpiTile>(new Dictionary<string, object?>
        {
            [nameof(PemsKpiTile.Options)] = options,
            [nameof(PemsKpiTile.Empty)] = empty && Model == null,
            [nameof(PemsKpiTile.ChildContent)] = BlockTagHelper.Markup(value)
        }));
    }
}
