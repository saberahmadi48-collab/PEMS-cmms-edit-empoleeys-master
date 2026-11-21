using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق CMMS Production Baseline v1 بند ۶ (CMMS-01: Asset Foundation): FunctionalLocation از
/// قبل در مدل دیتابیس بود (با ساختار درختی Parent/Child) ولی هیچ راه مدیریتی نداشت. طبق الگوی
/// MaterialCategory: لیست تخت با ستون Parent/Breadcrumb، نه یک Tree Widget جداگانه.
/// </summary>
public class FunctionalLocationController : Controller
{
    private readonly IFunctionalLocationService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;

    public FunctionalLocationController(IFunctionalLocationService service, ICurrentUserService currentUser,
        IPermissionService permissionService)
    {
        _service = service;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل درخت با
    // ToListAsync (بدون Skip/Take) بارگذاری می‌شد.
    // طبق ممیزی مستقل فاز ۶B Wave A (P0): این Action هیچ بررسی مجوزی نداشت.
    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "FunctionalLocation", "View"))
            return Forbid();

        ViewData["HelpKey"] = "FunctionalLocations.Index";

        ViewData["Title"] = "Functional Locations";
        ViewData["Section"] = "CMMS Integration";

        var items = await _service.GetPagedListAsync(page, DataGridPageSize.Normalize(pageSize));
        ViewBag.PickerList = await _service.GetPickerListAsync();

        return View(items);
    }

    // SCRUM-513: the nodes of the tree picker (pems-tree-select). GET /FunctionalLocation/TreeNodes?parentId=&q=&page=1 -> { items: [{ value, text, sub, hasChildren }], hasMore }.
    // The picker is used by forms of other modules (equipment, work request), so the rule is the View right of the locations, like the list.
    [HttpGet]
    public async Task<IActionResult> TreeNodes(int? parentId, string? q, int page = 1)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "FunctionalLocation", "View"))
            return Forbid();
        var result = await _service.GetTreeNodesAsync(parentId, q, page, 20);
        return Json(new
        {
            items = result.Items.Select(i => new { value = i.Id.ToString(), text = i.Label, sub = i.Sub, hasChildren = i.HasChildren }),
            hasMore = result.HasMore
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string code, string name, int? parentId, string? plant, string? area, string? unit, string? description)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "FunctionalLocation", "Create"))
            return Forbid();

        var (success, error, _) = await _service.CreateAsync(
            code, name, parentId, plant, area, unit, description, _currentUser.UserName);

        TempData[success ? "Message" : "Error"] = success ? "Functional Location created" : error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id, string code, string name, int? parentId, string? plant, string? area, string? unit, string? description)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "FunctionalLocation", "Edit"))
            return Forbid();

        var (success, error) = await _service.UpdateAsync(
            id, code, name, parentId, plant, area, unit, description, _currentUser.UserName);

        TempData[success ? "Message" : "Error"] = success ? "Functional Location updated" : error;
        return RedirectToAction(nameof(Index));
    }
}
