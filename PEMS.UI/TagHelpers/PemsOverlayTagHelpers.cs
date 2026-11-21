using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>Shared by the overlay frames: the id and the other attributes of the page's tag, as the component wants them.</summary>
internal static class OverlayTagHelper
{
    public static (string Id, IReadOnlyDictionary<string, object>? Others) Split(TagHelperOutput output)
    {
        var id = "";
        var rest = new Dictionary<string, object>();
        foreach (var a in output.Attributes)
        {
            var value = a.Value switch
            {
                null => "",
                IHtmlContent html => WebUtility.HtmlDecode(ToHtml(html)),
                _ => a.Value.ToString() ?? ""
            };
            if (a.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) id = value;
            else rest[a.Name] = value;
        }
        output.Attributes.Clear();
        return (id, rest.Count == 0 ? null : rest);
    }

    /// <summary>Every attribute of the page's tag (id included), values decoded for the component.</summary>
    public static IReadOnlyDictionary<string, object>? All(TagHelperOutput output)
    {
        if (output.Attributes.Count == 0) return null;
        var all = new Dictionary<string, object>();
        foreach (var a in output.Attributes)
            all[a.Name] = a.Value switch { null => "", IHtmlContent html => WebUtility.HtmlDecode(ToHtml(html)), _ => a.Value.ToString() ?? "" };
        return all;
    }

    private static string ToHtml(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}

/// <summary>
/// MVC facade for the modal frame: <c>&lt;pems-modal id="x" size="large" tabindex="-1"&gt;&lt;form&gt;header, body, footer&lt;/form&gt;&lt;/pems-modal&gt;</c> writes
/// <c>modal fade</c>, the dialog (<c>modal-dialog modal-lg</c>) and <c>modal-content</c> around the page's own markup; id, tabindex, aria-* and data-* stay on the modal.
/// </summary>
[HtmlTargetElement("pems-modal")]
[PemsFacade(UiKeys.Modal)]
public sealed class PemsModalTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>small, medium (default), large or extralarge.</summary>
    [HtmlAttributeName("size")] public string Size { get; set; } = "medium";
    /// <summary>Extra classes on the dialog (modal-dialog-scrollable, modal-dialog-centered).</summary>
    [HtmlAttributeName("dialog-css")] public string? DialogCss { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        var (id, rest) = OverlayTagHelper.Split(output);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsModal>(new Dictionary<string, object?>
        {
            [nameof(PemsModal.Options)] = new ModalOptions
            {
                Id = id, Title = "", CloseLabel = "", Frame = true, Size = Enum.Parse<OverlaySize>(Size, ignoreCase: true),
                DialogCssClass = DialogCss, CssClass = Css, Attributes = rest
            },
            [nameof(PemsModal.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>MVC facade for the side panel frame: <c>&lt;pems-offcanvas id="menu" side="start" tabindex="-1"&gt;header and body markup&lt;/pems-offcanvas&gt;</c>.</summary>
[HtmlTargetElement("pems-offcanvas")]
[PemsFacade(UiKeys.Drawer)]
public sealed class PemsOffcanvasTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    /// <summary>end (default) or start.</summary>
    [HtmlAttributeName("side")] public string Side { get; set; } = "end";
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = (await output.GetChildContentAsync()).GetContent();
        var (id, rest) = OverlayTagHelper.Split(output);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsDrawer>(new Dictionary<string, object?>
        {
            [nameof(PemsDrawer.Options)] = new DrawerOptions
            {
                Id = id, Title = "", CloseLabel = "", Frame = true, Side = Enum.Parse<DrawerSide>(Side, ignoreCase: true), CssClass = Css, Attributes = rest
            },
            [nameof(PemsDrawer.ChildContent)] = BlockTagHelper.Markup(inner)
        }));
    }
}

/// <summary>
/// The header of a modal: <c>&lt;pems-modal-header level="6" close="true" close-label="@Localizer["Close"]"&gt;Title&lt;/pems-modal-header&gt;</c> writes
/// <c>&lt;div class="modal-header"&gt;&lt;h6 class="modal-title"&gt;Title&lt;/h6&gt;&lt;button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"&gt;&lt;/button&gt;&lt;/div&gt;</c>.
/// </summary>
[HtmlTargetElement("pems-modal-header")]
[PemsFacade(UiKeys.ModalHeader)]
public sealed class PemsModalHeaderTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("level")] public int Level { get; set; } = 6;
    [HtmlAttributeName("title-css")] public string? TitleCss { get; set; }
    [HtmlAttributeName("title-id")] public string? TitleId { get; set; }
    [HtmlAttributeName("close")] public bool Close { get; set; }
    [HtmlAttributeName("close-label")] public string? CloseLabel { get; set; }
    [HtmlAttributeName("close-css")] public string? CloseCss { get; set; }
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsModalHeader>(renderer, output, new ModalHeaderOptions { Level = Level, TitleCss = TitleCss, TitleId = TitleId, Close = Close, CloseLabel = CloseLabel, CloseCss = CloseCss, Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The body of a modal: <c>&lt;pems-modal-body css="pb-0"&gt;</c> writes <c>&lt;div class="modal-body pb-0"&gt;</c>.</summary>
[HtmlTargetElement("pems-modal-body")]
[PemsFacade(UiKeys.ModalBody)]
public sealed class PemsModalBodyTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsModalBody>(renderer, output, new ModalBodyOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}

/// <summary>The footer of a modal: <c>&lt;pems-modal-footer&gt;buttons&lt;/pems-modal-footer&gt;</c> writes <c>&lt;div class="modal-footer"&gt;</c>.</summary>
[HtmlTargetElement("pems-modal-footer")]
[PemsFacade(UiKeys.ModalFooter)]
public sealed class PemsModalFooterTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("css")] public string? Css { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var (pageClass, attrs) = ListRender.Page(output);
        await ListRender.Replace<PemsModalFooter>(renderer, output, new ModalFooterOptions { Css = Css, PageClass = pageClass, Attributes = attrs }, await ListRender.Inner(output));
    }
}
