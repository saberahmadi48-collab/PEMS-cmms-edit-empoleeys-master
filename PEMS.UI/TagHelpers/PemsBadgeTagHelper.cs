using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for <see cref="PemsBadge"/>: <c>&lt;pems-badge tone="warning" css="ms-1"&gt;page text&lt;/pems-badge&gt;</c>.
/// The text between the tags stays in the page (it is a status name or a count the page computed); the component supplies the element and its colour classes.
/// </summary>
[HtmlTargetElement("pems-badge")]
[PemsFacade(UiKeys.Badge)]
public sealed class PemsBadgeTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>neutral, primary, danger, success, warning, info, light or dark (lower case in the page); "none" when the page passes the colour classes in <c>css</c>.</summary>
    [HtmlAttributeName("tone")] public string Tone { get; set; } = "neutral";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        var html = await renderer.RenderAsync<PemsBadge>(new Dictionary<string, object?>
        {
            [nameof(PemsBadge.Options)] = new BadgeOptions
            {
                Label = "",
                Tone = Tone.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : Enum.Parse<Tone>(Tone, ignoreCase: true),
                CssClass = Css,
                Attributes = output.Attributes.Count == 0 ? null : output.Attributes.ToDictionary(a => a.Name, a => (object)Raw(a.Value))
            },
            [nameof(PemsBadge.ChildContent)] = (RenderFragment)(b => b.AddMarkupContent(0, inner))
        });
        output.Attributes.Clear();
        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        // The component file ends in a newline; an inline badge must not add whitespace between it and the text around it.
        output.Content.SetHtmlContent(html.Trim());
    }

    /// <summary>The component encodes attribute values itself, so markup the page already encoded is decoded first.</summary>
    private static string Raw(object? value) => value switch
    {
        null => "",
        IHtmlContent html => WebUtility.HtmlDecode(ToHtml(html)),
        _ => value.ToString() ?? ""
    };

    private static string ToHtml(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
