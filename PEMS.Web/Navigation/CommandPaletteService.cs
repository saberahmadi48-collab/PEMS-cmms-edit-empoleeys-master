using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Web.Controllers;
using PEMS.Web.Resources;

namespace PEMS.Web.Navigation;

/// <summary>A palette entry as the browser receives it (records and actions share it).</summary>
public sealed record CommandPaletteItem(string Label, string? Sub, string Url, string Icon);

/// <summary>Results of one entity type.</summary>
public sealed record CommandPaletteGroup(string Type, string Title, string Icon, int Total, IReadOnlyList<CommandPaletteItem> Items);

/// <summary>
/// SCRUM-64: builds what the command palette and the /GlobalSearch page show. Records come from
/// <see cref="IGlobalSearchService.SearchEverywhereAsync"/> (only providers whose access rule the user passes, each with its
/// row scope); actions come from <see cref="CommandPaletteActions"/> filtered by the same rule check. Controllers only call this.
/// </summary>
public sealed class CommandPaletteService
{
    private static readonly IReadOnlyDictionary<string, string> GroupIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Attachment"] = "bi-paperclip", ["Contract"] = "bi-file-earmark-text", ["Discipline"] = "bi-diagram-3",
        ["DocumentRevision"] = "bi-clock-history", ["EngineeringDocument"] = "bi-file-earmark-richtext", ["Equipment"] = "bi-gear",
        ["EquipmentTag"] = "bi-tag", ["HelpArticle"] = "bi-life-preserver", ["Letter"] = "bi-envelope-paper", ["MOC"] = "bi-arrow-repeat",
        ["MaterialRequisition"] = "bi-cart", ["Notification"] = "bi-bell", ["PartCatalogueRequest"] = "bi-box-seam",
        ["Project"] = "bi-kanban", ["SparePart"] = "bi-nut", ["TechnicalQuery"] = "bi-question-circle", ["VDRItem"] = "bi-journal-check",
        ["Vendor"] = "bi-building", ["WorkOrder"] = "bi-tools",
    };

    private readonly IGlobalSearchService _search;
    private readonly CommandPaletteActions _actions;
    private readonly IStringLocalizer<SharedResource> _text;

    public CommandPaletteService(IGlobalSearchService search, CommandPaletteActions actions, IStringLocalizer<SharedResource> text)
    {
        _search = search;
        _actions = actions;
        _text = text;
    }

    public async Task<IReadOnlyList<CommandPaletteGroup>> SearchAsync(string? query, IUrlHelper url, int perType = 5, CancellationToken ct = default)
    {
        var groups = await _search.SearchEverywhereAsync(query ?? "", perType, ct);
        return groups.Select(g =>
        {
            var icon = GroupIcons.TryGetValue(g.EntityType, out var i) ? i : "bi-search";
            var items = g.Items.Select(x => new CommandPaletteItem(x.Label, SearchResultText.SubLabel(x, _text), url.Content("~" + x.Url), icon)).ToList();
            return new CommandPaletteGroup(g.EntityType, GroupTitle(g.EntityType), icon, g.TotalCount, items);
        }).ToList();
    }

    public async Task<IReadOnlyList<CommandPaletteItem>> ActionsAsync(IUrlHelper url, string? returnUrl, string currentTheme)
    {
        var list = new List<CommandPaletteItem>();
        foreach (var a in _actions.All)
            if (await _search.IsAllowedAsync(a.Access))
                list.Add(new CommandPaletteItem(_text["Palette_Action_" + a.Key], null, a.Url ?? url.Action(a.Action, a.Controller, new { area = "" })!, a.Icon));

        var back = !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        foreach (var (theme, icon, key) in new[] { ("light", "bi-sun", "Theme_Light"), ("dark", "bi-moon-stars", "Theme_Dark"), ("turquoise", "bi-gem", "Theme_Turquoise") })
            if (theme != currentTheme)
                list.Add(new CommandPaletteItem(_text["Palette_Action_Theme", _text[key]], null,
                    url.Action("SetTheme", "Theme", new { area = "", theme, returnUrl = back })!, icon));

        var fa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        list.Add(new CommandPaletteItem(_text["Palette_Action_Language", fa ? "English" : "فارسی"], null,
            url.Action("SetCulture", "Culture", new { area = "", culture = fa ? "en" : "fa", returnUrl = back })!, "bi-translate"));
        return list;
    }

    public string GroupTitle(string entityType)
    {
        var text = _text["Palette_Group_" + entityType];
        return text.ResourceNotFound ? entityType : text.Value;
    }

    public static string ThemeOf(HttpRequest request) => ThemeController.Normalize(request.Cookies[ThemeController.CookieName]);
}
