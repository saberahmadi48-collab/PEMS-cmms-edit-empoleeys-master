using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// A Bootstrap icon: <c>&lt;pems-icon name="bi-gear" css="me-1" aria-hidden="true" /&gt;</c> writes <c>&lt;i class="bi bi-gear me-1" aria-hidden="true"&gt;&lt;/i&gt;</c>.
/// Every other attribute the page wrote (title, aria-*, data-*, id) is kept. No view writes <c>&lt;i class="bi ..."&gt;</c> by hand.
/// </summary>
[HtmlTargetElement("pems-icon")]
[PemsFacade(UiKeys.Icon)]
public sealed class PemsIconTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>The icon class, e.g. bi-gear.</summary>
    [HtmlAttributeName("name")] public string? Name { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        return ListRender.Replace<PemsIcon>(renderer, output, new IconOptions { Name = Name, Css = Css, PageClass = pageClass, Attributes = attrs });
    }
}
