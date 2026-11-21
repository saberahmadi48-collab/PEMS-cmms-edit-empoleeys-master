using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Definitions;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>The one generic host of the page engine (SCRUM-438, ADR-0011 point 8, ADR-0014): <c>/p/{key}</c> runs the pipeline for a page definition (resolve, authorize, hydrate) and renders it. The reserved prefix never shadows a code route; the page is whatever its definition says.</summary>
[Route("p/{key}")]
public sealed class PageHostController(DefinitionPipeline pipeline, PageRenderer renderer, ICurrentUserService currentUser, ILogger<PageHostController> log, PEMS.Application.Definitions.PipelineBudgets? budgets = null) : Controller
{
    private static readonly Regex KeyShape = new("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.Compiled);

    [HttpGet("")]
    public async Task<IActionResult> Index(string key, CancellationToken ct)
    {
        if (!KeyShape.IsMatch(key)) return NotFound();
        var culture = CultureInfo.CurrentUICulture;
        var roles = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        // the values a route or the query string gives the page (an id, a filter); a page reads them as params.id in its expressions (SCRUM-768)
        var parameters = Request.Query.Where(q => q.Key.Length <= 64).ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal);
        var request = new PipelineRequest("page", key, "core", culture.TwoLetterISOLanguageName, roles, currentUser.UserName, parameters);

        var result = await pipeline.RunAsync(request, ct);
        if (result.Status == PipelineStatus.NotFound) return NotFound();
        if (result.Status == PipelineStatus.Forbidden) return Forbid();

        var (page, renderTime) = await HostDiagnostics.TimeAsync(() => renderer.RenderAsync(result.Resolved!, request, culture, ct));
        HostDiagnostics.Report(log, HttpContext.TraceIdentifier, "page", key, new Dictionary<string, TimeSpan>(result.Timings) { ["render"] = renderTime }, result.Counts, budgets);   // SCRUM-577
        foreach (var problem in page.Problems) log.LogWarning("Page definition {Key}: {Problem}", key, problem);
        ViewData["Title"] = page.Title ?? key;
        return View("Index", page);
    }
}
