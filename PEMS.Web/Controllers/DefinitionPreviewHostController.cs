using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Definitions;
using PEMS.UI.Manifest;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// The preview host (SCRUM-792, 787): the live render of a component sample or of any version of a page definition, in either language, either direction and either theme, with sample or real data and no publish.
/// It is generic infrastructure like the page host: nothing here knows a page; the markup is written by <see cref="PageRenderer"/> from the definition. Needs <c>Definitions.View</c>; a draft is previewed with the rights of
/// the person who may edit it, never to anyone else. The result is a complete document so it can be opened in a tab or framed.
/// </summary>
[Route("preview")]
public sealed class DefinitionPreviewHostController(PageRenderer renderer, ComponentManifestCatalog catalog, IDefinitionService definitions, IDefinitionAccess access, ICurrentUserService user) : Controller
{
    public const string Permission = "Definitions.View";

    /// <summary>The options of the toggles: language (fa, en), direction (rtl, ltr; the language decides when absent), theme (light, dark).</summary>
    public sealed record PreviewOptions(string Culture, string Dir, string Theme)
    {
        public static PreviewOptions From(string? culture, string? dir, string? theme)
        {
            var c = culture is "en" ? "en" : "fa";
            return new PreviewOptions(c, dir is "rtl" or "ltr" ? dir : c == "fa" ? "rtl" : "ltr", theme is "dark" ? "dark" : "light");
        }
    }

    /// <summary>The page definition that shows one component with its sample options (SCRUM-792).</summary>
    public static JsonObject SamplePage(string componentKey, JsonNode? sample) => new()
    {
        ["layout"] = "detail-page",
        ["regions"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["id"] = "sample", ["component"] = componentKey, ["options"] = sample?.DeepClone() ?? new JsonObject() }) },
    };

    [HttpGet("component/{key}")]
    public async Task<IActionResult> Component(string key, string? culture, string? dir, string? theme, CancellationToken ct)
    {
        if (!await MayAsync(ct)) return Forbid();
        var manifest = catalog.All().FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
        if (manifest is null) return NotFound();
        var page = SamplePage(manifest.Key, manifest.Sample);
        return await RenderAsync("component", manifest.Key, page, null, PreviewOptions.From(culture, dir, theme), $"/preview/component/{Uri.EscapeDataString(manifest.Key)}", ct);
    }

    /// <summary>A version of a page definition (<c>ref</c> = appKey:kind:key, <c>version</c> a number, the draft when absent): the draft is rendered as it would be after publish.</summary>
    [HttpGet("definition")]
    public async Task<IActionResult> Definition(string @ref, int? version, string? culture, string? dir, string? theme, CancellationToken ct)
    {
        if (!await MayAsync(ct)) return Forbid();
        var parts = (@ref ?? "").Split(':', 3);
        if (parts.Length != 3 || parts[1] != "page") return BadRequest("Only a page definition can be previewed here (ref = app:page:key).");
        var page = await definitions.ListAsync(new DefinitionListQuery("page", parts[0], parts[2], null, [], 1, 20), ct);
        var summary = page.Items.FirstOrDefault(d => d.AppKey == parts[0] && d.Kind == "page" && d.Key == parts[2]);
        if (summary is null) return NotFound();
        var payload = version is { } n ? await definitions.GetVersionAsync(summary.Id, n, ct) : await definitions.GetDraftAsync(summary.Id, ct);
        if (!payload.Success) return NotFound();
        var root = JsonNode.Parse(payload.Value!.PayloadJson) as JsonObject;
        if (root is null) return BadRequest();
        var query = version is null ? "" : "&version=" + version;
        return await RenderAsync("page", summary.Key, root, summary.TitleJson, PreviewOptions.From(culture, dir, theme), $"/preview/definition?ref={Uri.EscapeDataString(@ref!)}{query}".Replace("&&", "&"), ct, parts[0]);
    }

    private async Task<bool> MayAsync(CancellationToken ct)
        => await access.CanOpenAsync(Permission, User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList(), ct);

    private async Task<IActionResult> RenderAsync(string kind, string key, JsonObject root, string? titleJson, PreviewOptions options, string baseUrl, CancellationToken ct, string appKey = "core")
    {
        var culture = CultureInfo.GetCultureInfo(options.Culture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        var roles = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var parameters = Request.Query.Where(q => q.Key.Length <= 64 && q.Key is not ("ref" or "version" or "culture" or "dir" or "theme")).ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
        var request = new PipelineRequest("page", key, appKey, options.Culture, roles, user.UserName, parameters);
        var resolved = new ResolvedDefinition(new PublishedDefinition("page", key, appKey, 0, root.ToJsonString(), "preview", titleJson), root.DeepClone());
        var rendered = await renderer.RenderAsync(resolved, request, culture, ct);
        return Content(Document(options, baseUrl, key, rendered), "text/html; charset=utf-8");
    }

    /// <summary>The preview document: toggles on top, the rendered page below, what was wrong with the definition listed (a problem hides the part, it never breaks the preview). Pure.</summary>
    public static string Document(PreviewOptions o, string baseUrl, string title, RenderedPage page)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        string Link(string label, PreviewOptions next, bool on)
        {
            var join = baseUrl.Contains((char)63) ? "&amp;" : "?";
            return $"<a class=\"btn btn-sm {(on ? "btn-primary" : "btn-outline-secondary")}\" href=\"{E(baseUrl)}{join}culture={next.Culture}&amp;dir={next.Dir}&amp;theme={next.Theme}\">{E(label)}</a>";
        }
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(o.Culture).Append("\" dir=\"").Append(o.Dir).Append("\" data-theme=\"").Append(o.Theme).Append("\" data-bs-theme=\"").Append(o.Theme == "dark" ? "dark" : "light").Append("\">");
        html.Append("<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(E(title)).Append("</title>");
        html.Append("<link rel=\"stylesheet\" href=\"/_content/PEMS.UI/pems-ui.css\"></head><body class=\"p-3\">");
        html.Append("<div class=\"d-flex flex-wrap gap-2 mb-3 pb-3 border-bottom\" role=\"toolbar\" dir=\"ltr\">");
        html.Append(Link("fa", o with { Culture = "fa", Dir = o.Dir }, o.Culture == "fa")).Append(Link("en", o with { Culture = "en" }, o.Culture == "en"));
        html.Append(Link("RTL", o with { Dir = "rtl" }, o.Dir == "rtl")).Append(Link("LTR", o with { Dir = "ltr" }, o.Dir == "ltr"));
        html.Append(Link("light", o with { Theme = "light" }, o.Theme == "light")).Append(Link("dark", o with { Theme = "dark" }, o.Theme == "dark"));
        html.Append("</div>");
        foreach (var problem in page.Problems) html.Append("<div class=\"alert alert-warning py-1\">").Append(E(problem)).Append("</div>");
        html.Append("<main>").Append(page.Html).Append("</main></body></html>");
        return html.ToString();
    }
}
