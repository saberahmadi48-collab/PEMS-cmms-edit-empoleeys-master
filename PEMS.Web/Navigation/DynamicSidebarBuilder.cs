using System.Globalization;
using System.Security.Claims;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Web.Security;

namespace PEMS.Web.Navigation;

/// <summary>یک گره رندرشدنی سایدبار برای کاربر جاری (فقط آیتم‌های نمایان؛ بخش/زیرتیتر خالی حذف شده‌اند).</summary>
public sealed class SidebarNode
{
    public string Key { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Icon { get; init; }
    public string? Href { get; init; }
    public bool OpenInNewTab { get; init; }
    public bool IsActive { get; set; }
    public string? BadgeKey { get; init; }
    public string BadgeTone { get; init; } = "primary";
    public List<SidebarNode> Children { get; } = new();

    public bool IsSection => Kind == NavigationMenuItemKinds.Section;
    public bool IsSubheading => Kind == NavigationMenuItemKinds.Subheading;
    public IEnumerable<SidebarNode> Descendants() => Children.SelectMany(c => new[] { c }.Concat(c.Descendants()));
}

/// <summary>درخواست جاری برای تعیین آیتم فعال.</summary>
public sealed record SidebarRouteContext(string? Area, string Controller, string Action);

/// <summary>
/// ساخت سایدبار داده‌محور برای کاربر جاری. همان لایه‌های تصمیم سایدبار قدیمی را بازاستفاده می‌کند (NavigationVisibilityResolver با
/// Override مدیر، پرچم‌های مجوزمحور QuickSetup/ImportCenter/PartCatalogue/Replenishment/DisciplineMaster) و برای هر پیوند مجوز همان
/// کنترلر مقصد را هم می‌سنجد. Hidden ≠ Unauthorized: این کلاس هیچ درخواستی را رد نمی‌کند.
/// </summary>
public class DynamicSidebarBuilder
{
    private readonly INavigationMenuService _menu;
    private readonly NavigationVisibilityResolver _visibility;
    private readonly IPermissionService _permissions;
    private readonly IRoleModuleActionService _roleActions;
    private readonly IReplenishmentAvailability _replenishment;

    public DynamicSidebarBuilder(INavigationMenuService menu, NavigationVisibilityResolver visibility, IPermissionService permissions,
        IRoleModuleActionService roleActions, IReplenishmentAvailability replenishment)
    {
        _menu = menu;
        _visibility = visibility;
        _permissions = permissions;
        _roleActions = roleActions;
        _replenishment = replenishment;
    }

    public async Task<List<SidebarNode>> BuildAsync(ClaimsPrincipal user, IReadOnlyList<string> roles, SidebarRouteContext route,
        Func<NavigationMenuItemDto, string?> href, Func<string, string> localize)
    {
        var evaluator = await CreateEvaluatorAsync(user, roles);
        var items = await _menu.GetMenuAsync();
        var byParent = items.ToLookup(i => i.ParentKey ?? "");
        var isFa = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";

        async Task<List<SidebarNode>> BuildLevel(string parentKey, int depth)
        {
            var nodes = new List<SidebarNode>();
            if (depth > 3) return nodes;   // دفاع در برابر داده‌ی نادرست (چرخه)
            foreach (var item in byParent[parentKey])
            {
                if (!await evaluator.EvaluateAsync(item.VisibilityRule)) continue;

                var badge = NavigationBadges.Find(item.BadgeKey);
                var node = new SidebarNode
                {
                    Key = item.Key,
                    Kind = item.Kind,
                    Title = Title(item, isFa, localize),
                    Icon = item.Icon,
                    Href = item.Kind == NavigationMenuItemKinds.Link ? href(item) : null,
                    OpenInNewTab = item.OpenInNewTab,
                    BadgeKey = badge?.Key,
                    BadgeTone = badge?.Tone ?? "primary"
                };

                if (item.Kind != NavigationMenuItemKinds.Link)
                {
                    node.Children.AddRange(await BuildLevel(item.Key, depth + 1));
                    // بخش/زیرتیتر بدون هیچ پیوند نمایان رندر نمی‌شود (تیتر بدون آیتم گمراه‌کننده است)
                    if (!node.Descendants().Any(d => d.Kind == NavigationMenuItemKinds.Link)) continue;
                }
                nodes.Add(node);
            }
            return nodes;
        }

        var roots = await BuildLevel("", 0);
        MarkActive(roots, items, route);
        return roots;
    }

    /// <summary>عنوان: جایگزین مدیر به زبان جاری، وگرنه کلید بومی‌سازی پیش‌فرض.</summary>
    public static string Title(NavigationMenuItemDto item, bool isFa, Func<string, string> localize)
    {
        var custom = isFa ? item.TitleFa : item.TitleEn;
        return !string.IsNullOrWhiteSpace(custom) ? custom! : localize(item.TitleKey);
    }

    private async Task<NavigationRuleEvaluator> CreateEvaluatorAsync(ClaimsPrincipal user, IReadOnlyList<string> roles)
    {
        // دقیقاً همان محاسبه‌های بالای _SidebarMenu.cshtml
        var pc = new Dictionary<string, bool>();
        foreach (var a in new[] { "Create", "Review", "Approve", "Admin" })
            pc[a] = await _roleActions.HasActionAsync(roles, "PartCatalogue", a);

        var canSeeQuickSetup = await _permissions.HasPermissionAsync(roles, "Setup", "View");
        var canSeeImportCenter = ImportCatalog.VisibleByRole(user).Any();
        var snapshot = await _visibility.ResolveAsync(roles, new Dictionary<string, bool>
        {
            ["QuickSetup"] = canSeeQuickSetup,
            ["ImportCenter"] = canSeeImportCenter
        });

        var flags = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["PartCatalogue"] = pc.Values.Any(v => v),
            ["PartCatalogueAdmin"] = pc["Admin"],
            ["Replenishment"] = _replenishment.IsEnabled && await _permissions.HasPermissionAsync(roles, "ReplenishmentRecommendation", "View"),
            ["DisciplineMaster"] = await _permissions.HasPermissionAsync(roles, "DisciplineMaster", "View"),
        };

        return new NavigationRuleEvaluator(snapshot, flags, roles, _permissions, _roleActions);
    }

    private static void MarkActive(List<SidebarNode> roots, IReadOnlyList<NavigationMenuItemDto> items, SidebarRouteContext route)
    {
        var visibleKeys = roots.SelectMany(r => new[] { r }.Concat(r.Descendants())).Where(n => n.Kind == NavigationMenuItemKinds.Link)
            .Select(n => n.Key).ToHashSet();
        var visibleLinks = items.Where(i => visibleKeys.Contains(i.Key)).ToList();
        var activeKeys = ActiveLinkKeys(visibleLinks, route);

        bool Visit(SidebarNode node)
        {
            var self = node.Kind == NavigationMenuItemKinds.Link && activeKeys.Contains(node.Key);
            var child = false;
            foreach (var c in node.Children) child |= Visit(c);
            var item = items.FirstOrDefault(i => i.Key == node.Key);
            var extra = item != null && (ExtraMatch(item.ExtraActiveControllers, route.Controller)
                                         || (!string.IsNullOrEmpty(item.ActiveArea) && Eq(item.ActiveArea, route.Area)));
            node.IsActive = self || child || (node.Kind != NavigationMenuItemKinds.Subheading && extra);
            return node.IsActive;
        }
        foreach (var r in roots) Visit(r);
    }

    /// <summary>
    /// پیوند فعال: هم‌کنترلر/هم‌Area؛ اگر پیوندی دقیقاً همین Action را دارد فقط همان، وگرنه اگر فقط یک پیوند برای این کنترلر هست همان،
    /// وگرنه پیوند علامت‌خورده با IsDefaultForController (مثل Letter/Index در برابر MyReferrals).
    /// </summary>
    public static HashSet<string> ActiveLinkKeys(IReadOnlyList<NavigationMenuItemDto> links, SidebarRouteContext route)
    {
        var candidates = links.Where(l => l.Controller != null && Eq(l.Controller, route.Controller) && Eq(l.Area ?? "", route.Area ?? "")).ToList();
        var exact = candidates.Where(l => Eq(l.Action ?? "Index", route.Action)).ToList();
        var chosen = exact.Count > 0 ? exact
            : candidates.Count == 1 ? candidates
            : candidates.Where(l => l.IsDefaultForController).ToList();

        var keys = chosen.Select(l => l.Key).ToHashSet();
        foreach (var l in links.Where(l => ExtraMatch(l.ExtraActiveControllers, route.Controller))) keys.Add(l.Key);
        return keys;
    }

    private static bool ExtraMatch(string? csv, string controller) =>
        !string.IsNullOrEmpty(csv) && csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(c => Eq(c, controller));

    private static bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// ارزیابی قاعده‌ی نمایش: «|» = یا، «&amp;» = و. اتم ناشناخته = false (Fail-closed). نتیجه‌ی هر اتم در طول همین درخواست کش می‌شود.
/// </summary>
public class NavigationRuleEvaluator
{
    private readonly NavigationVisibilitySnapshot _snapshot;
    private readonly IReadOnlyDictionary<string, bool> _flags;
    private readonly IReadOnlyList<string> _roles;
    private readonly IPermissionService _permissions;
    private readonly IRoleModuleActionService _roleActions;
    private readonly Dictionary<string, bool> _memo = new(StringComparer.Ordinal);

    public NavigationRuleEvaluator(NavigationVisibilitySnapshot snapshot, IReadOnlyDictionary<string, bool> flags, IReadOnlyList<string> roles,
        IPermissionService permissions, IRoleModuleActionService roleActions)
    {
        _snapshot = snapshot;
        _flags = flags;
        _roles = roles;
        _permissions = permissions;
        _roleActions = roleActions;
    }

    public async Task<bool> EvaluateAsync(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return false;
        foreach (var term in rule.Split('|', StringSplitOptions.TrimEntries))
        {
            var all = true;
            foreach (var atom in term.Split('&', StringSplitOptions.TrimEntries))
                if (!await AtomAsync(atom)) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    /// <summary>اعتبارسنجی نحوی قاعده (برای آزمون کاتالوگ و مدیریت)؛ خطاها را برمی‌گرداند.</summary>
    public static IReadOnlyList<string> Validate(string? rule)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(rule)) { errors.Add("empty rule"); return errors; }
        foreach (var atom in rule.Split('|', StringSplitOptions.TrimEntries).SelectMany(t => t.Split('&', StringSplitOptions.TrimEntries)))
        {
            var (kind, arg) = SplitAtom(atom);
            var ok = kind switch
            {
                "Always" => arg == null,
                "Module" or "CmmsItem" or "Flag" or "Role" => !string.IsNullOrWhiteSpace(arg),
                "Perm" or "RoleAction" => arg != null && arg.Split('.') is { Length: 2 } p && p.All(x => x.Length > 0),
                _ => false
            };
            if (!ok) errors.Add($"invalid atom '{atom}'");
        }
        return errors;
    }

    private static (string Kind, string? Arg) SplitAtom(string atom)
    {
        var i = atom.IndexOf(':');
        return i < 0 ? (atom, null) : (atom[..i], atom[(i + 1)..]);
    }

    private async Task<bool> AtomAsync(string atom)
    {
        if (_memo.TryGetValue(atom, out var cached)) return cached;
        var (kind, arg) = SplitAtom(atom);
        bool value;
        switch (kind)
        {
            case "Always" when arg == null: value = true; break;
            case "Module": value = arg != null && _snapshot.IsModuleVisible(arg); break;
            case "CmmsItem": value = arg != null && _snapshot.IsCmmsItemVisible(arg); break;
            case "Flag": value = arg != null && _flags.TryGetValue(arg, out var f) && f; break;
            case "Role": value = arg != null && _roles.Contains(arg); break;
            case "Perm" when arg?.Split('.') is { Length: 2 } p:
                value = await _permissions.HasPermissionAsync(_roles, p[0], p[1]); break;
            case "RoleAction" when arg?.Split('.') is { Length: 2 } r:
                value = await _roleActions.HasActionAsync(_roles, r[0], r[1]); break;
            default: value = false; break;   // اتم ناشناخته/نادرست: پنهان (Fail-closed)
        }
        _memo[atom] = value;
        return value;
    }
}
