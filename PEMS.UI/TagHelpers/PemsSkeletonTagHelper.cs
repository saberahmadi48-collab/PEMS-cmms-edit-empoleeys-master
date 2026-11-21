using PEMS.UI.Rendering;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 Skeleton: the grey placeholder shown while a part of the page is still loading. One markup for Razor
// (<pems-skeleton>), for script-filled panels (PemsSkeleton.show in pems-skeleton.js) and for the Blazor PemsSkeleton.
// Styles: pems-ui.css (pems-skeleton*), loaded by the layout; the pulse stops when the user asks for reduced motion.
// Usage: docs/design/LIST_PAGE_COMPONENT.md (Skeleton).

/// <summary>The skeleton markup, built once so the tag helper, the script and the tests agree on it.</summary>
public static class PemsSkeletonMarkup
{
    public static readonly string[] Kinds = ["text", "title", "card", "kpi", "table", "form"];

    /// <summary>
    /// A status region (role="status", aria-busy) with the screen-reader <paramref name="label"/> and the bars of
    /// <paramref name="kind"/>: text (<paramref name="lines"/> lines, the last one shorter), title, card (title and three
    /// lines in a card), kpi (icon square and two bars in a tile), table (<paramref name="rows"/> rows of
    /// <paramref name="cols"/> cells), form (<paramref name="rows"/> label + field pairs). An unknown kind is text.
    /// </summary>
    public static string Build(string? kind, int lines = 3, int rows = 5, int cols = 4, string? label = null, string? id = null)
    {
        var k = Array.IndexOf(Kinds, kind) >= 0 ? kind! : "text";
        lines = Math.Clamp(lines, 1, 20); rows = Math.Clamp(rows, 1, 30); cols = Math.Clamp(cols, 1, 12);
        var sb = new StringBuilder();
        sb.Append(id is null ? "<div class=\"pems-skeleton pems-skeleton--" : "<div id=\"" + WebUtility.HtmlEncode(id) + "\" class=\"pems-skeleton pems-skeleton--").Append(k).Append("\" role=\"status\" aria-busy=\"true\" aria-live=\"polite\">");
        if (!string.IsNullOrWhiteSpace(label)) sb.Append("<span class=\"visually-hidden\">").Append(WebUtility.HtmlEncode(label)).Append("</span>");
        static string Bar(string extra = "") => "<span class=\"pems-skeleton__line" + extra + "\" aria-hidden=\"true\"></span>";
        switch (k)
        {
            case "title":
                sb.Append(Bar(" pems-skeleton__line--title"));
                break;
            case "card":
                sb.Append(Bar(" pems-skeleton__line--title"));
                for (var i = 0; i < 3; i++) sb.Append(Bar(i == 2 ? " pems-skeleton__line--short" : ""));
                break;
            case "kpi":
                sb.Append(Bar(" pems-skeleton__line--square"));
                sb.Append("<span class=\"pems-skeleton__stack\">").Append(Bar(" pems-skeleton__line--value")).Append(Bar(" pems-skeleton__line--short")).Append("</span>");
                break;
            case "table":
                for (var r = 0; r < rows; r++)
                {
                    sb.Append("<span class=\"pems-skeleton__row\">");
                    for (var c = 0; c < cols; c++) sb.Append(Bar());
                    sb.Append("</span>");
                }
                break;
            case "form":
                for (var r = 0; r < rows; r++) sb.Append(Bar(" pems-skeleton__line--label")).Append(Bar(" pems-skeleton__line--field"));
                break;
            default:
                for (var i = 0; i < lines; i++) sb.Append(Bar(i == lines - 1 && lines > 1 ? " pems-skeleton__line--short" : ""));
                break;
        }
        return sb.Append("</div>").ToString();
    }
}

/// <summary>
/// <c>&lt;pems-skeleton kind="table" rows="5" cols="4" label="@Localizer["Loading"]" /&gt;</c>: the placeholder a page
/// renders where content arrives later (a script replaces it). Kinds: text, title, card, kpi, table, form.
/// </summary>
[HtmlTargetElement("pems-skeleton", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.Skeleton)]
public sealed class PemsSkeletonTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("kind")] public string? Kind { get; set; }
    [HtmlAttributeName("lines")] public int Lines { get; set; } = 3;
    [HtmlAttributeName("rows")] public int Rows { get; set; } = 5;
    [HtmlAttributeName("cols")] public int Cols { get; set; } = 4;
    [HtmlAttributeName("label")] public string? Label { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var id = output.Attributes.TryGetAttribute("id", out var a) ? a.Value?.ToString() : null;
        var parameters = new Dictionary<string, object?>
        {
            [nameof(PemsSkeleton.Kind)] = Kind ?? "text", [nameof(PemsSkeleton.Lines)] = Lines, [nameof(PemsSkeleton.Rows)] = Rows,
            [nameof(PemsSkeleton.Cols)] = Cols, [nameof(PemsSkeleton.Id)] = id
        };
        if (!string.IsNullOrWhiteSpace(Label)) parameters[nameof(PemsSkeleton.Label)] = PEMS.UI.Abstractions.TranslatableText.Literal(Label);
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsSkeleton>(parameters));
    }
}
