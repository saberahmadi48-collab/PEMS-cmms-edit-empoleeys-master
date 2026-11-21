using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for <see cref="PemsAlert"/>: <c>&lt;pems-alert tone="danger" css="py-2 px-3 small"&gt;page markup&lt;/pems-alert&gt;</c>.
/// The markup between the tags stays in the page (it carries the page's own localised texts and links); the component supplies the alert
/// element, its tone class and its role, so no view writes <c>class="alert alert-*"</c> by hand.
/// </summary>
[HtmlTargetElement("pems-alert")]
[PemsFacade(UiKeys.Alert)]
public sealed class PemsAlertTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>info, success, warning, danger, primary, secondary, light, or none for the bare <c>alert</c> class (lower case in the page).</summary>
    [HtmlAttributeName("tone")] public string Tone { get; set; } = "info";
    [HtmlAttributeName("css")] public string? Css { get; set; }
    [HtmlAttributeName("trace-id")] public string? TraceId { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        var html = await renderer.RenderAsync<PemsAlert>(new Dictionary<string, object?>
        {
            [nameof(PemsAlert.Options)] = new AlertOptions { Tone = Enum.Parse<AlertTone>(Tone, ignoreCase: true), CssClass = Css, TraceId = TraceId, Attributes = OverlayTagHelper.All(output) },
            [nameof(PemsAlert.ChildContent)] = (RenderFragment)(b => b.AddMarkupContent(0, inner))
        });
        output.Attributes.Clear();
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(html);
    }
}
