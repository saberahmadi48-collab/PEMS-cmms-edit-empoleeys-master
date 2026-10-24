using PEMS.Application.DTOs;

namespace PEMS.Web.Areas.Admin.Models;

/// <summary>یک ردیف درخت مدیریت منو.</summary>
public sealed class NavigationMenuAdminRow
{
    public NavigationMenuItemDto Item { get; init; } = new();
    public int Depth { get; init; }
    public string Title { get; init; } = "";
    public string? Route { get; init; }
    /// <summary>پیوندی که هیچ شرط مجوزی جز دروازه‌ی بخش ندارد (کنترلر مقصدش هم دروازه ندارد).</summary>
    public bool NoPermissionSignal { get; init; }
    public bool IsFirst { get; init; }
    public bool IsLast { get; init; }
}

public sealed class NavigationMenuEditViewModel
{
    public NavigationMenuItemDto Item { get; init; } = new();
    public string DefaultTitle { get; init; } = "";
    public List<(string Key, string Title)> Parents { get; init; } = new();
    public NavigationMenuItemEditInput Input { get; init; } = new();
}

public sealed class NavigationMenuCreateViewModel
{
    public List<(string Key, string Title)> Parents { get; init; } = new();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public NavigationMenuCustomLinkInput Input { get; init; } = new();
}
