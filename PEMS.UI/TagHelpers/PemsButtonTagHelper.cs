using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for the button classes: <c>&lt;pems-button tone="neutral" size="small" outline="true" css="w-100"&gt;text&lt;/pems-button&gt;</c> becomes a
/// <c>button</c> (or an <c>a</c> with <c>as="a"</c>). The element keeps every other attribute the page wrote (type, id, href, data-*, onclick, disabled) and its content;
/// the classes come from <see cref="ButtonState"/> inside the registered <c>button-frame</c> component, the same code <see cref="PemsButton"/> uses, so no view writes <c>class="btn btn-*"</c> by hand.
/// A link can carry <c>asp-controller</c>, <c>asp-action</c>, <c>asp-route-*</c> and the other routing attributes of the built-in anchor helper (this class derives from it).
/// </summary>
[HtmlTargetElement("pems-button")]
[PemsFacade(UiKeys.ButtonFrame)]
public sealed class PemsButtonTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : AnchorTagHelper(generator)
{
    /// <summary>neutral, primary, danger, success, warning, info, light or dark (lower case in the page); "none" when the page passes the colour class in <c>css</c> (btn-pems, btn-link).</summary>
    [HtmlAttributeName("tone")] public string Tone { get; set; } = "primary";
    /// <summary>small, medium or large.</summary>
    [HtmlAttributeName("size")] public string Size { get; set; } = "medium";
    [HtmlAttributeName("outline")] public bool Outline { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }
    /// <summary>Writes the disabled attribute when true (a bool attribute value would print disabled="False").</summary>
    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }
    /// <summary>The element to write: <c>button</c> (default), <c>a</c>, <c>label</c> or <c>span</c>.</summary>
    [HtmlAttributeName("as")] public string As { get; set; } = "button";
    /// <summary>A button that carries only the page's own classes (no <c>btn</c> classes): tab links, icon buttons, menu items. Tone, size and outline are ignored.</summary>
    [HtmlAttributeName("plain")] public bool Plain { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // SCRUM-528: the routing attributes bind to this class, so the anchor helper must build the href here (it never runs on its own for pems-button).
        if (HasRouting) base.Process(context, output);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsButtonFrame>(renderer, output,
            new ButtonFrameOptions { Tone = Tone, Size = Size, Outline = Outline, Css = Css, Disabled = Disabled, As = As, Plain = Plain, PageClass = css, Attributes = attrs }, await ListRender.Inner(output));
    }

    private bool HasRouting =>
        Action != null || Controller != null || Area != null || Page != null || PageHandler != null || Route != null
        || Protocol != null || Host != null || Fragment != null || RouteValues.Count > 0;
}
