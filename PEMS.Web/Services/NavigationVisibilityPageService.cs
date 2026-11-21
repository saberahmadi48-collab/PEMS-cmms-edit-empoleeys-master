using Microsoft.AspNetCore.Identity;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

public sealed class NavigationVisibilityPageService(NavigationVisibilityResolver resolver, INavigationVisibilityOverrideService overrides, RoleManager<ApplicationRole> roles, UserManager<ApplicationUser> users) : INavigationVisibilityPageService
{
    private static string NodeKey(string module, string? item) => string.IsNullOrEmpty(item) ? module : module + "~" + item;

    private static NavigationVisibilityRowDto Row(string roleName, int? userId, NavigationVisibilityRowDescription d) => new()
    {
        RoleName = roleName, UserId = userId, Node = NodeKey(d.ModuleKey, d.ItemKey), ModuleKey = d.ModuleKey, ItemKey = d.ItemKey ?? string.Empty, DefaultVisible = d.DefaultVisible,
        Override = d.OverrideVisible is null ? string.Empty : d.OverrideVisible.Value ? "Show" : "Hide", EffectiveVisible = d.EffectiveVisible, Source = d.DefaultSource
    };

    public async Task<List<NavigationVisibilityRowDto>> RoleRowsAsync(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return [];
        return (await resolver.DescribeForRoleAsync(roleName.Trim())).Select(d => Row(roleName.Trim(), null, d)).ToList();
    }

    /// <summary>What a user sees is what any of the roles of the user shows (the union, as the sidebar resolves it); the source lists the roles that show the node.</summary>
    public async Task<List<NavigationVisibilityRowDto>> UserRowsAsync(int userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user == null) return [];
        var names = (await users.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var perRole = new List<(string Role, NavigationVisibilityRowDescription Row)>();
        foreach (var role in names) perRole.AddRange((await resolver.DescribeForRoleAsync(role)).Select(d => (role, d)));
        return perRole.GroupBy(x => NodeKey(x.Row.ModuleKey, x.Row.ItemKey)).Select(g => new NavigationVisibilityRowDto
        {
            UserId = userId, Node = g.Key, ModuleKey = g.First().Row.ModuleKey, ItemKey = g.First().Row.ItemKey ?? string.Empty, DefaultVisible = g.Any(x => x.Row.DefaultVisible),
            Override = string.Join(",", g.Where(x => x.Row.OverrideVisible.HasValue).Select(x => x.Role + ":" + (x.Row.OverrideVisible!.Value ? "Show" : "Hide"))), EffectiveVisible = g.Any(x => x.Row.EffectiveVisible),
            Source = string.Join(",", g.Where(x => x.Row.EffectiveVisible).Select(x => x.Role))
        }).OrderBy(r => r.Node, StringComparer.Ordinal).ToList();
    }

    public async Task<List<NavigationNodeRowDto>> NodesAsync()
    {
        var anyRole = roles.Roles.Select(r => r.Name).FirstOrDefault(n => n != null) ?? "SystemAdmin";
        return (await resolver.DescribeForRoleAsync(anyRole)).Select(d => new NavigationNodeRowDto { Node = NodeKey(d.ModuleKey, d.ItemKey), Label = NodeKey(d.ModuleKey, d.ItemKey).Replace("~", " / ") }).ToList();
    }

    public async Task<List<NavigationOverridesRowDto>> OverridesOfRoleAsync(string roleName)
    {
        var rows = await RoleRowsAsync(roleName);
        return [new NavigationOverridesRowDto { RoleName = roleName, Cells = string.Join(",", rows.Where(r => r.Override != string.Empty).Select(r => r.Node + "|" + r.Override)) }];
    }

    /// <summary>The ticked cells are the overrides of the role; a node with no ticked cell goes back to its default (the override is cleared).</summary>
    public async Task<NavigationSaveResult> SetRoleOverridesAsync(string roleName, string? cells, string? actingUser)
    {
        var role = await roles.FindByNameAsync(roleName ?? string.Empty);
        if (role == null) return new NavigationSaveResult(false, "The role does not exist.");
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cell in (cells ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = cell.Split('|');
            if (parts.Length == 2 && (parts[1] is "Show" or "Hide")) wanted[parts[0]] = parts[1];      // a node ticked as Show and Hide: the last one wins
        }
        foreach (var d in await resolver.DescribeForRoleAsync(role.Name!))
        {
            var node = NodeKey(d.ModuleKey, d.ItemKey);
            var item = string.IsNullOrEmpty(d.ItemKey) ? null : d.ItemKey;
            if (wanted.TryGetValue(node, out var state))
            {
                var visible = state == "Show";
                if (d.OverrideVisible != visible) await overrides.SetOverrideAsync(role.Id, d.ModuleKey, item, visible, null, actingUser ?? "system");
            }
            else if (d.HasOverride) await overrides.ClearOverrideAsync(role.Id, d.ModuleKey, item);
        }
        return new NavigationSaveResult(true, null);
    }
}
