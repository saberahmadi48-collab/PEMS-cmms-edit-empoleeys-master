using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Web.Navigation;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

public sealed class NavigationMenuPageService(INavigationMenuService menu, IDynamicSidebarAvailability availability, IActionDescriptorCollectionProvider actions, IStringLocalizer<SharedResource> l) : INavigationMenuPageService
{
    private static readonly NavigationMenuResult Disabled = NavigationMenuResult.Fail("The dynamic menu is not enabled.");

    private string TitleOf(NavigationMenuItemDto i, string? culture) => DynamicSidebarBuilder.Title(i, string.Equals(culture, "fa", StringComparison.OrdinalIgnoreCase), k => l[k]);

    public async Task<List<NavigationMenuRowDto>> RowsAsync(string? culture)
    {
        if (!availability.IsEnabled) return [];
        var all = await menu.GetAllForAdminAsync();
        var byParent = all.ToLookup(i => i.ParentKey ?? "");
        var rows = new List<NavigationMenuRowDto>();
        void Add(string parent, int depth)
        {
            var siblings = byParent[parent].ToList();
            for (var n = 0; n < siblings.Count; n++)
            {
                var i = siblings[n];
                rows.Add(new NavigationMenuRowDto
                {
                    Key = i.Key, ParentKey = i.ParentKey, Depth = depth, Title = string.Concat(Enumerable.Repeat("— ", depth)) + TitleOf(i, culture), Kind = i.Kind, IsActive = i.IsActive, IsSystem = i.IsSystem, IsFirst = n == 0, IsLast = n == siblings.Count - 1,
                    Route = i.ExternalPath ?? (i.Controller == null ? null : $"{(string.IsNullOrEmpty(i.Area) ? "" : i.Area + "/")}{i.Controller}/{i.Action}"),
                    NoPermissionSignal = i.Kind == NavigationMenuItemKinds.Link && !HasPermissionSignal(i.VisibilityRule)
                });
                if (depth < 3) Add(i.Key, depth + 1);
            }
        }
        Add("", 0);
        // items whose parent no longer exists stay visible (they can be moved to a valid parent)
        var shown = rows.Select(r => r.Key).ToHashSet();
        rows.AddRange(all.Where(i => !shown.Contains(i.Key)).Select(i => new NavigationMenuRowDto { Key = i.Key, ParentKey = i.ParentKey, Title = TitleOf(i, culture), Kind = i.Kind, IsActive = i.IsActive, IsSystem = i.IsSystem, IsFirst = true, IsLast = true }));
        return rows;
    }

    public async Task<List<NavigationOptionRowDto>> ParentOptionsAsync(string? culture)
    {
        if (!availability.IsEnabled) return [];
        return (await menu.GetAllForAdminAsync()).Where(p => p.Kind != NavigationMenuItemKinds.Link).Select(p => new NavigationOptionRowDto { Value = p.Key, Label = (p.ParentKey != null ? "— " : "") + TitleOf(p, culture) }).ToList();
    }

    public async Task<List<NavigationOptionRowDto>> PermissionOptionsAsync()
        => availability.IsEnabled ? (await menu.GetViewPermissionNamesAsync()).Select(p => new NavigationOptionRowDto { Value = p, Label = p }).ToList() : [];

    public Task<List<NavigationOptionRowDto>> BadgeOptionsAsync()
        => Task.FromResult(NavigationBadges.All.Select(b => new NavigationOptionRowDto { Value = b.Key, Label = l[b.TitleKey] }).ToList());

    public async Task<List<NavigationMenuItemFormRowDto>> ItemOfAsync(string key)
    {
        if (!availability.IsEnabled) return [];
        var item = (await menu.GetAllForAdminAsync()).FirstOrDefault(i => i.Key == key);
        return item == null ? [] : [new NavigationMenuItemFormRowDto { Key = item.Key, ParentKey = item.ParentKey, TitleEn = item.TitleEn, TitleFa = item.TitleFa, Icon = item.Icon, BadgeKey = item.BadgeKey, IsActive = item.IsActive, RowVersion = item.RowVersion }];
    }

    public async Task<NavigationMenuResult> SaveAsync(NavigationMenuItemEditInput input)
    {
        if (!availability.IsEnabled) return Disabled;
        if (input.BadgeKey != null && NavigationBadges.Find(input.BadgeKey) == null) return NavigationMenuResult.Fail("Nav_Err_Badge");
        var result = await menu.UpdateAsync(input);
        return result.Success ? NavigationMenuResult.Ok() with { Error = "Nav_Saved" } : result;
    }

    public async Task<NavigationMenuResult> MoveAsync(string key, bool up) => availability.IsEnabled ? await menu.MoveAsync(key, up) : Disabled;

    public async Task<NavigationMenuResult> AddLinkAsync(NavigationMenuCustomLinkInput input)
    {
        if (!availability.IsEnabled) return Disabled;
        var permissions = await menu.GetViewPermissionNamesAsync();
        var result = await menu.AddCustomLinkAsync(input, p => Task.FromResult(permissions.Contains(p)), RouteExists);
        return result.Success ? NavigationMenuResult.Ok() with { Error = "Nav_Saved" } : result;
    }

    public async Task<NavigationMenuResult> DeleteAsync(string key)
    {
        if (!availability.IsEnabled) return Disabled;
        var result = await menu.DeleteCustomAsync(key);
        return result.Success ? NavigationMenuResult.Ok() with { Error = "Nav_Deleted" } : result;
    }

    /// <summary>An internal route is valid only when a real GET action exists in this application (in that area).</summary>
    public bool RouteExists(string? area, string controller, string action) =>
        actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any(d =>
            string.Equals(d.ControllerName, controller, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.ActionName, action, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.RouteValues.TryGetValue("area", out var a) ? a ?? "" : "", area ?? "", StringComparison.OrdinalIgnoreCase)
            && !d.EndpointMetadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>().Any(m => !m.HttpMethods.Contains("GET")));

    /// <summary>A rule that has nothing but a section gate (Module:) or Always has no permission signal.</summary>
    public static bool HasPermissionSignal(string rule) =>
        rule.Split('|', '&').Select(a => a.Trim()).Any(a => !(a == NavigationVisibilityRules.Always || a.StartsWith("Module:", StringComparison.Ordinal)));
}
