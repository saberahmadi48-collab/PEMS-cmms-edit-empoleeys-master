using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;
using PEMS.Web.Navigation;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Controllers;

/// <summary>
/// ==============================================================================
/// Endpoint واحد Global Search Framework (فاز A). این کنترلر عمداً از GlobalSearchController
/// جداست - آن یکی یک صفحه‌ی کامل HTML نتایج جستجو است (/GlobalSearch?q=...)، این یکی یک
/// API سبک JSON برای کامپوننت Autocomplete/Typeahead مشترک (pems-global-search.js) است که
/// قراره جایگزین Dropdown های خام بشه. هیچ Controller دیگری در سیستم نباید Endpoint جستجوی
/// مخصوص خودش بسازد؛ همه از همین یک مسیر با پارامتر type استفاده می‌کنند.
/// ==============================================================================
/// </summary>
public class SearchApiController : Controller
{
    private readonly IGlobalSearchService _searchService;
    private readonly CommandPaletteService _palette;
    private readonly IStringLocalizer<SharedResource>? _localizer;

    public SearchApiController(IGlobalSearchService searchService, CommandPaletteService palette, IStringLocalizer<SharedResource>? localizer = null)
    {
        _searchService = searchService;
        _palette = palette;
        _localizer = localizer;
    }

    // SCRUM-64 command palette: records of every module the user may open (access rule + row scope), max 5 per type.
    // GET /SearchApi/Palette?q=PI
    [HttpGet]
    public async Task<IActionResult> Palette(string? q, CancellationToken ct) =>
        Json(new { groups = await _palette.SearchAsync(q, Url, 5, ct) });

    // SCRUM-64: the actions the user may run (each checked against the rule of its page), theme and language switches.
    // GET /SearchApi/PaletteActions?returnUrl=/current/page
    [HttpGet]
    public async Task<IActionResult> PaletteActions(string? returnUrl) =>
        Json(new { items = await _palette.ActionsAsync(Url, returnUrl, CommandPaletteService.ThemeOf(Request)) });

    // GET /SearchApi?type=Equipment&q=PI&page=1&pageSize=20&projectId=
    [HttpGet]
    public async Task<IActionResult> Index(string type, string q, int page = 1, int pageSize = 20, int? projectId = null)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(q))
            return Json(new { entityType = type, query = q, page, pageSize, totalCount = 0, hasMore = false, items = Array.Empty<object>() });

        try
        {
            var result = await _searchService.SearchAsync(type, q, page, pageSize, projectId);
            foreach (var item in result.Items) item.SubLabel = SearchResultText.SubLabel(item, _localizer);
            return Json(result);
        }
        catch (InvalidOperationException ex)
        {
            // نوع موجودیت ثبت‌نشده - طبق اصل "بدون شکست خاموش"، خطای صریح ۴۰۰ برمی‌گردد
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /SearchApi/Entities - فهرست تمام موجودیت‌های ثبت‌شده (برای تست/مستندسازی/UI انتخاب دامنه)
    [HttpGet]
    public IActionResult Entities()
    {
        return Json(_searchService.GetRegisteredEntityTypes());
    }
}
