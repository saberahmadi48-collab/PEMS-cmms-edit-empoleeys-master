using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>One row of the menu tree of the admin page: Depth indents the title; IsFirst and IsLast say which move buttons apply.</summary>
public class NavigationMenuRowDto
{
    public string Key { get; set; } = string.Empty;
    public string? ParentKey { get; set; }
    public int Depth { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Route { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public bool NoPermissionSignal { get; set; }
    public bool IsFirst { get; set; }
    public bool IsLast { get; set; }
}

public class NavigationOptionRowDto { public string Value { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

/// <summary>The values of the edit form of one menu item (the same names as the form fields).</summary>
public class NavigationMenuItemFormRowDto
{
    public string Key { get; set; } = string.Empty;
    public string? ParentKey { get; set; }
    public string? TitleEn { get; set; }
    public string? TitleFa { get; set; }
    public string? Icon { get; set; }
    public string? BadgeKey { get; set; }
    public bool IsActive { get; set; }
    public string? RowVersion { get; set; }
}

/// <summary>SCRUM-636: the Admin menu editor as operations (the rules stay in INavigationMenuService; the route and badge checks of the old controller are here). Only when the dynamic menu is enabled.</summary>
public interface INavigationMenuPageService
{
    [PEMS.Application.Common.PemsOperation("NavigationMenu", "Rows", Roles = "SystemAdmin")]
    Task<List<NavigationMenuRowDto>> RowsAsync(string? culture);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "ParentOptions", Roles = "SystemAdmin")]
    Task<List<NavigationOptionRowDto>> ParentOptionsAsync(string? culture);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "PermissionOptions", Roles = "SystemAdmin")]
    Task<List<NavigationOptionRowDto>> PermissionOptionsAsync();

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "BadgeOptions", Roles = "SystemAdmin")]
    Task<List<NavigationOptionRowDto>> BadgeOptionsAsync();

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "ItemOf", Roles = "SystemAdmin")]
    Task<List<NavigationMenuItemFormRowDto>> ItemOfAsync(string key);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "Save", Roles = "SystemAdmin")]
    Task<NavigationMenuResult> SaveAsync(NavigationMenuItemEditInput input);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "Move", Roles = "SystemAdmin")]
    Task<NavigationMenuResult> MoveAsync(string key, bool up);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "AddLink", Roles = "SystemAdmin")]
    Task<NavigationMenuResult> AddLinkAsync(NavigationMenuCustomLinkInput input);

    [PEMS.Application.Common.PemsOperation("NavigationMenu", "Delete", Roles = "SystemAdmin")]
    Task<NavigationMenuResult> DeleteAsync(string key);
}
