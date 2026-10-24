using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Web.Areas.Admin.Models;
using PEMS.Web.Navigation;
using PEMS.Web.Resources;

namespace PEMS.Web.Areas.Admin.Controllers;

/// <summary>
/// مدیریت سایدبار داده‌محور - فقط SystemAdmin (تصمیم مالک). مدیر ترتیب، والد، عنوان فارسی/انگلیسی، آیکون، نمایان/پنهان و شمارنده را
/// تغییر می‌دهد و پیوند سفارشی به مسیر داخلی موجود اضافه می‌کند. قاعده‌ی نمایش آیتم سیستمی فقط‌خواندنی است (از کد). منطق در
/// INavigationMenuService؛ کنترلر فقط اعتبارسنجی مسیر (که دانش Web است) و نگاشت View را انجام می‌دهد. با پرچم خاموش: 404.
/// </summary>
[Area("Admin")]
[Authorize(Roles = SystemRoles.SystemAdmin)]
public class NavigationMenuController : Controller
{
    private readonly INavigationMenuService _menu;
    private readonly IDynamicSidebarAvailability _availability;
    private readonly IActionDescriptorCollectionProvider _actions;
    private readonly IStringLocalizer<SharedResource> _l;

    public NavigationMenuController(INavigationMenuService menu, IDynamicSidebarAvailability availability,
        IActionDescriptorCollectionProvider actions, IStringLocalizer<SharedResource> localizer)
    {
        _menu = menu;
        _availability = availability;
        _actions = actions;
        _l = localizer;
    }

    private bool IsFa => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
    private string TitleOf(NavigationMenuItemDto i) => DynamicSidebarBuilder.Title(i, IsFa, k => _l[k]);

    public async Task<IActionResult> Index()
    {
        if (!_availability.IsEnabled) return NotFound();
        ViewData["Title"] = _l["Nav_Admin_Title"];
        ViewData["Section"] = "Administration";

        var all = await _menu.GetAllForAdminAsync();
        var byParent = all.ToLookup(i => i.ParentKey ?? "");
        var rows = new List<NavigationMenuAdminRow>();

        void Add(string parent, int depth)
        {
            var siblings = byParent[parent].ToList();
            for (var n = 0; n < siblings.Count; n++)
            {
                var i = siblings[n];
                rows.Add(new NavigationMenuAdminRow
                {
                    Item = i, Depth = depth, Title = TitleOf(i), IsFirst = n == 0, IsLast = n == siblings.Count - 1,
                    Route = i.ExternalPath ?? (i.Controller == null ? null : $"{(string.IsNullOrEmpty(i.Area) ? "" : i.Area + "/")}{i.Controller}/{i.Action}"),
                    NoPermissionSignal = i.Kind == NavigationMenuItemKinds.Link && !HasPermissionSignal(i.VisibilityRule)
                });
                if (depth < 3) Add(i.Key, depth + 1);
            }
        }
        Add("", 0);

        // آیتم‌هایی که والدشان دیگر وجود ندارد هم دیده شوند (قابل‌انتقال به والد معتبر)
        var shown = rows.Select(r => r.Item.Key).ToHashSet();
        rows.AddRange(all.Where(i => !shown.Contains(i.Key)).Select(i => new NavigationMenuAdminRow { Item = i, Depth = 0, Title = TitleOf(i), IsFirst = true, IsLast = true }));

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string key)
    {
        if (!_availability.IsEnabled) return NotFound();
        var item = (await _menu.GetAllForAdminAsync()).FirstOrDefault(i => i.Key == key);
        if (item == null) return NotFound();
        ViewData["Title"] = _l["Nav_Admin_Edit"];
        ViewData["Section"] = "Administration";
        return View(await EditModelAsync(item, new NavigationMenuItemEditInput
        {
            Key = item.Key, ParentKey = item.ParentKey, TitleEn = item.TitleEn, TitleFa = item.TitleFa, Icon = item.Icon,
            BadgeKey = item.BadgeKey, IsActive = item.IsActive, RowVersion = item.RowVersion
        }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(NavigationMenuItemEditInput input)
    {
        if (!_availability.IsEnabled) return NotFound();
        if (input.BadgeKey != null && NavigationBadges.Find(input.BadgeKey) == null)
            return await EditFailedAsync(input, "Nav_Err_Badge");

        var result = await _menu.UpdateAsync(input);
        if (!result.Success) return await EditFailedAsync(input, result.Error!);

        TempData["Success"] = _l["Nav_Saved"].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string key, bool up)
    {
        if (!_availability.IsEnabled) return NotFound();
        var result = await _menu.MoveAsync(key, up);
        if (!result.Success) TempData["Error"] = _l[result.Error!].Value;
        return RedirectToAction(nameof(Index), null, "row-" + key);
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? parentKey)
    {
        if (!_availability.IsEnabled) return NotFound();
        ViewData["Title"] = _l["Nav_Admin_AddLink"];
        ViewData["Section"] = "Administration";
        return View(await CreateModelAsync(new NavigationMenuCustomLinkInput { ParentKey = parentKey ?? "", Action = "Index" }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NavigationMenuCustomLinkInput input)
    {
        if (!_availability.IsEnabled) return NotFound();
        var permissions = await _menu.GetViewPermissionNamesAsync();
        var result = await _menu.AddCustomLinkAsync(input,
            p => Task.FromResult(permissions.Contains(p)),
            RouteExists);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, _l[result.Error!]);
            ViewData["Title"] = _l["Nav_Admin_AddLink"];
            ViewData["Section"] = "Administration";
            return View(await CreateModelAsync(input));
        }
        TempData["Success"] = _l["Nav_Saved"].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string key)
    {
        if (!_availability.IsEnabled) return NotFound();
        var result = await _menu.DeleteCustomAsync(key);
        if (result.Success) TempData["Success"] = _l["Nav_Deleted"].Value;
        else TempData["Error"] = _l[result.Error!].Value;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>مسیر داخلی فقط وقتی معتبر است که یک Action واقعی با GET (بدون Post-only) در همین برنامه وجود داشته باشد.</summary>
    public bool RouteExists(string? area, string controller, string action) =>
        _actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any(d =>
            string.Equals(d.ControllerName, controller, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.ActionName, action, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.RouteValues.TryGetValue("area", out var a) ? a ?? "" : "", area ?? "", StringComparison.OrdinalIgnoreCase)
            && !d.EndpointMetadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>().Any(m => !m.HttpMethods.Contains("GET")));

    /// <summary>قاعده‌ای که جز دروازه‌ی بخش (Module) یا Always چیزی ندارد = بدون سیگنال مجوز.</summary>
    public static bool HasPermissionSignal(string rule) =>
        rule.Split('|', '&').Select(a => a.Trim()).Any(a => !(a == NavigationVisibilityRules.Always || a.StartsWith("Module:", StringComparison.Ordinal)));

    private async Task<NavigationMenuEditViewModel> EditModelAsync(NavigationMenuItemDto item, NavigationMenuItemEditInput input)
    {
        var all = await _menu.GetAllForAdminAsync();
        // والد مجاز بر اساس نوع: بخش ← فقط سطح بالا؛ زیرتیتر ← بخش؛ پیوند ← بخش یا زیرتیتر یا سطح بالا
        var parents = all.Where(p => p.Key != item.Key && item.Kind switch
            {
                NavigationMenuItemKinds.Section => false,
                NavigationMenuItemKinds.Subheading => p.Kind == NavigationMenuItemKinds.Section,
                _ => p.Kind is NavigationMenuItemKinds.Section or NavigationMenuItemKinds.Subheading
            })
            .Select(p => (p.Key, (p.ParentKey != null ? "— " : "") + TitleOf(p))).ToList();
        return new NavigationMenuEditViewModel { Item = item, DefaultTitle = _l[item.TitleKey], Parents = parents, Input = input };
    }

    private async Task<IActionResult> EditFailedAsync(NavigationMenuItemEditInput input, string errorKey)
    {
        var item = (await _menu.GetAllForAdminAsync()).FirstOrDefault(i => i.Key == input.Key);
        if (item == null) return NotFound();
        ModelState.AddModelError(string.Empty, _l[errorKey]);
        ViewData["Title"] = _l["Nav_Admin_Edit"];
        ViewData["Section"] = "Administration";
        return View("Edit", await EditModelAsync(item, input));
    }

    private async Task<NavigationMenuCreateViewModel> CreateModelAsync(NavigationMenuCustomLinkInput input)
    {
        var all = await _menu.GetAllForAdminAsync();
        return new NavigationMenuCreateViewModel
        {
            Parents = all.Where(p => p.Kind != NavigationMenuItemKinds.Link)
                .Select(p => (p.Key, (p.ParentKey != null ? "— " : "") + TitleOf(p))).ToList(),
            Permissions = await _menu.GetViewPermissionNamesAsync(),
            Input = input
        };
    }
}
