using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>Shared helpers of the small-block facades: tone parsing and the page-markup slot.</summary>
internal static class BlockTagHelper
{
    public static Tone? ParseTone(string? tone) =>
        string.IsNullOrWhiteSpace(tone) || tone.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : Enum.Parse<Tone>(tone, ignoreCase: true);

    public static RenderFragment Markup(string html) => b => b.AddMarkupContent(0, html);

    /// <summary>The attributes the page wrote on the tag except <paramref name="except"/> (the facade handles those), values decoded because the component encodes them again.</summary>
    public static IReadOnlyDictionary<string, object>? PageAttributes(TagHelperOutput output, params string[] except)
    {
        var rest = output.Attributes.Where(a => !except.Contains(a.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (rest.Count == 0) return null;
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var a in rest) map[a.Name] = (object)System.Net.WebUtility.HtmlDecode(a.Value switch
        {
            null => "",
            Microsoft.AspNetCore.Html.IHtmlContent html => ToHtml(html),
            _ => a.Value.ToString() ?? ""
        });
        return map;
    }

    /// <summary>The class the page wrote on the tag (kept after the component's classes), or null.</summary>
    public static string? PageClass(TagHelperOutput output) => output.Attributes.TryGetAttribute("class", out var c) ? c.Value?.ToString() : null;

    private static string ToHtml(Microsoft.AspNetCore.Html.IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    public static void Replace(TagHelperOutput output, string html)
    {
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(html.Trim());
    }
}

/// <summary>MVC facade for <see cref="PemsCard"/>: <c>&lt;pems-card css="shadow-sm border-0 h-100"&gt;card-body markup&lt;/pems-card&gt;</c>.</summary>
[HtmlTargetElement("pems-card")]
[PemsFacade(UiKeys.Card)]
public sealed class PemsCardTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsCard>(new Dictionary<string, object?>
        {
            [nameof(PemsCard.Options)] = new CardOptions { CssClass = Css },
            [nameof(PemsCard.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>
/// MVC facade for <see cref="PemsProgress"/>: <c>&lt;pems-progress value="@x" max="100" tone="success" height="8px" css="mb-3"&gt;text in the bar&lt;/pems-progress&gt;</c>.
/// The component writes the track, the bar width and the progressbar attributes.
/// </summary>
[HtmlTargetElement("pems-progress")]
[PemsFacade(UiKeys.Progress)]
public sealed class PemsProgressTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    public sealed class SegmentSlot { public List<ProgressSegment> Items { get; } = []; }

    [HtmlAttributeName("value")] public double Value { get; set; }
    [HtmlAttributeName("max")] public double Max { get; set; } = 100;
    /// <summary>neutral, primary, danger, success, warning, info, light, dark, or none/empty for the default bar.</summary>
    [HtmlAttributeName("tone")] public string? Tone { get; set; }
    [HtmlAttributeName("height")] public string? Height { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    [HtmlAttributeName("style")] public string? Style { get; set; }
    [HtmlAttributeName("label")] public string? Label { get; set; }
    [HtmlAttributeName("title")] public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var segments = new SegmentSlot();
        context.Items[typeof(SegmentSlot)] = segments;
        var inner = (await output.GetChildContentAsync()).GetContent();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsProgress>(new Dictionary<string, object?>
        {
            [nameof(PemsProgress.Options)] = new ProgressOptions
            {
                Value = Value, Max = Max, Tone = BlockTagHelper.ParseTone(Tone), Height = Height, CssClass = Css, Style = Style,
                Label = string.IsNullOrEmpty(Label) ? default(Abstractions.TranslatableText?) : Label, Title = Title,
                Segments = segments.Items.Count == 0 ? null : segments.Items
            },
            [nameof(PemsProgress.ChildContent)] = BlockTagHelper.Markup(inner.Trim())
        }));
    }
}

/// <summary>One more bar of a stacked progress: <c>&lt;pems-progress value="@a" tone="success"&gt;&lt;pems-progress-segment value="@b" tone="neutral" /&gt;&lt;/pems-progress&gt;</c>; writes nothing itself.</summary>
[HtmlTargetElement("pems-progress-segment", ParentTag = "pems-progress")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsProgressSegmentTagHelper : TagHelper
{
    [HtmlAttributeName("value")] public double Value { get; set; }
    [HtmlAttributeName("tone")] public string? Tone { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(PemsProgressTagHelper.SegmentSlot), out var slot))
            ((PemsProgressTagHelper.SegmentSlot)slot).Items.Add(new ProgressSegment(Value, BlockTagHelper.ParseTone(Tone)));
        output.SuppressOutput();
    }
}

/// <summary>MVC facade for <see cref="PemsAccordion"/>: <c>&lt;pems-accordion id="a" css="mb-3"&gt;&lt;pems-accordion-item ...&gt;</c>.</summary>
[HtmlTargetElement("pems-accordion")]
[PemsFacade(UiKeys.Accordion)]
public sealed class PemsAccordionTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = "";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsAccordion>(new Dictionary<string, object?>
        {
            [nameof(PemsAccordion.Options)] = new AccordionOptions { Id = Id, CssClass = Css },
            [nameof(PemsAccordion.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>
/// One accordion item: <c>&lt;pems-accordion-item target="accA" parent="a"&gt;&lt;pems-accordion-header&gt;button content&lt;/pems-accordion-header&gt; body markup&lt;/pems-accordion-item&gt;</c>.
/// </summary>
[HtmlTargetElement("pems-accordion-item")]
[PemsFacade(UiKeys.AccordionItem)]
public sealed class PemsAccordionItemTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    public sealed class HeaderSlot { public string Html { get; set; } = ""; }

    [HtmlAttributeName("target")] public string Target { get; set; } = "";
    [HtmlAttributeName("parent")] public string? Parent { get; set; }
    [HtmlAttributeName("button-css")] public string? ButtonCss { get; set; }
    [HtmlAttributeName("body-css")] public string? BodyCss { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var slot = new HeaderSlot();
        context.Items[typeof(HeaderSlot)] = slot;
        var body = (await output.GetChildContentAsync()).GetContent();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsAccordionItem>(new Dictionary<string, object?>
        {
            [nameof(PemsAccordionItem.Options)] = new AccordionItemOptions { Target = Target, ParentId = Parent, ButtonCssClass = ButtonCss, BodyCssClass = BodyCss },
            [nameof(PemsAccordionItem.Header)] = BlockTagHelper.Markup(slot.Html),
            [nameof(PemsAccordionItem.ChildContent)] = BlockTagHelper.Markup(body)
        }));
    }
}

/// <summary>The button content of a <see cref="PemsAccordionItemTagHelper"/>; writes nothing itself.</summary>
[HtmlTargetElement("pems-accordion-header", ParentTag = "pems-accordion-item")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsAccordionHeaderTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        if (context.Items.TryGetValue(typeof(PemsAccordionItemTagHelper.HeaderSlot), out var slot)) ((PemsAccordionItemTagHelper.HeaderSlot)slot).Html = inner.Trim();
        output.SuppressOutput();
    }
}

/// <summary>MVC facade for <see cref="PemsListGroup"/>: <c>&lt;pems-list-group flush="true"&gt;&lt;a class="list-group-item ..."&gt;...&lt;/pems-list-group&gt;</c>.</summary>
[HtmlTargetElement("pems-list-group")]
[PemsFacade(UiKeys.ListGroup)]
public sealed class PemsListGroupTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("flush")] public bool Flush { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsListGroup>(new Dictionary<string, object?>
        {
            [nameof(PemsListGroup.Options)] = new ListGroupOptions { Flush = Flush, CssClass = Css },
            [nameof(PemsListGroup.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>MVC facade for <see cref="PemsSpinner"/>: <c>&lt;pems-spinner size="small" tone="neutral"&gt;&lt;/pems-spinner&gt;</c>.</summary>
[HtmlTargetElement("pems-spinner")]
[PemsFacade(UiKeys.Spinner)]
public sealed class PemsSpinnerTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("grow")] public bool Grow { get; set; }
    /// <summary>small, medium or large.</summary>
    [HtmlAttributeName("size")] public string Size { get; set; } = "medium";
    [HtmlAttributeName("tone")] public string? Tone { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsSpinner>(new Dictionary<string, object?>
        {
            [nameof(PemsSpinner.Options)] = new SpinnerOptions
            {
                Grow = Grow, Size = Enum.Parse<Size>(Size, ignoreCase: true), Tone = BlockTagHelper.ParseTone(Tone), CssClass = Css,
                Attributes = OverlayTagHelper.All(output)
            }
        }));
    }
}
