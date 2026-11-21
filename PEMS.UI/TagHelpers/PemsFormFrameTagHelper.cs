using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for the page's form element (SCRUM-510): <c>&lt;pems-form asp-action="Save" method="post" class="row g-2"&gt;...&lt;/pems-form&gt;</c>. It derives from the built-in form tag helper,
/// so <c>asp-action</c>, <c>asp-controller</c>, <c>asp-route-*</c>, <c>asp-antiforgery</c> and the antiforgery token work exactly as on a <c>form</c>; the registered
/// <c>form-frame</c> component writes the element with the page's class and attributes. (The registry key <c>form</c> is the descriptor-driven form.)
/// </summary>
[HtmlTargetElement("pems-form")]
[PemsFacade(UiKeys.FormFrame)]
public sealed class PemsFormFrameTagHelper(IHtmlGenerator generator, IPemsComponentRenderer renderer) : FormTagHelper(generator)
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        base.Process(context, output);
        var (css, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsFormFrame>(renderer, output, new FormFrameOptions { PageClass = css, Attributes = attrs }, await ListRender.InnerAfterFramework(output));
    }
}
