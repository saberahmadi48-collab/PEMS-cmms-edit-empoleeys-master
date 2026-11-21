namespace PEMS.Application.Interfaces;

/// <summary>One navigation node (module, or module item) as one role or one user sees it; Node is the key of the node: module, or module~item.</summary>
public class NavigationVisibilityRowDto
{
    public string RoleName { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public string Node { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public bool DefaultVisible { get; set; }
    public string Override { get; set; } = string.Empty;
    public bool EffectiveVisible { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class NavigationNodeRowDto { public string Node { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

public class NavigationOverridesRowDto { public string RoleName { get; set; } = string.Empty; public string Cells { get; set; } = string.Empty; }

public sealed record NavigationSaveResult(bool Success, string? Error);

/// <summary>SCRUM-637: the Admin navigation visibility pages as operations: what a role or a user sees, the nodes, the overrides of a role (cells "node|Show" and "node|Hide") and one save of them. The rules stay in the resolver and the override service.</summary>
public interface INavigationVisibilityPageService
{
    [PEMS.Application.Common.PemsOperation("NavigationVisibility", "RoleRows", Roles = "SystemAdmin")]
    Task<List<NavigationVisibilityRowDto>> RoleRowsAsync(string roleName);

    [PEMS.Application.Common.PemsOperation("NavigationVisibility", "UserRows", Roles = "SystemAdmin")]
    Task<List<NavigationVisibilityRowDto>> UserRowsAsync(int userId);

    [PEMS.Application.Common.PemsOperation("NavigationVisibility", "Nodes", Roles = "SystemAdmin")]
    Task<List<NavigationNodeRowDto>> NodesAsync();

    [PEMS.Application.Common.PemsOperation("NavigationVisibility", "OverridesOfRole", Roles = "SystemAdmin")]
    Task<List<NavigationOverridesRowDto>> OverridesOfRoleAsync(string roleName);

    [PEMS.Application.Common.PemsOperation("NavigationVisibility", "SetRoleOverrides", Roles = "SystemAdmin")]
    Task<NavigationSaveResult> SetRoleOverridesAsync(string roleName, string? cells, string? actingUser);
}
