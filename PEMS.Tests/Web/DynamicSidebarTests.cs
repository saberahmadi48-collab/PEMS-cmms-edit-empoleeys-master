using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Navigation;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// سایدبار داده‌محور (تصمیم‌های مالک: Migration، فقط SystemAdmin، آیتم بدون سیگنال مجوز نمایان، شمارنده‌های شخصی و ماژولی).
///  1) هم‌ارزی ساختاری کاتالوگ با سایدبار قدیمی (مسیر، آیکون، کلید عنوان، ترتیب) - خوانده‌شده از خود _SidebarMenu.cshtml؛
///  2) سلامت کاتالوگ (کلید یکتا، والد معتبر، قاعده‌ی معتبر، مسیر موجود، کلید بومی‌سازی در هر دو زبان)؛
///  3) رفتار نمایش برای نقش‌های نماینده، آیتم فعال، Override مدیر، سرکوب زیرتیتر خالی؛
///  4) سرویس مدیریت (همگام‌سازی افزایشی، اعتبارسنجی، کش)، شمارنده‌ها و کنترلر مدیریت.
/// </summary>
public class DynamicSidebarTests
{
    // ==============================================================================
    // 1) هم‌ارزی ساختاری با سایدبار قدیمی
    // ==============================================================================

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private sealed record LegacyLink(string Target, string Icon, string TitleKey);

    /// <summary>هر &lt;a&gt; سایدبار قدیمی: مقصد (Area/Controller/Action یا href)، آیکون و کلید Localizer، به ترتیب فایل.</summary>
    private static List<LegacyLink> LegacyLinks()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml"));
        var nav = src[src.IndexOf("<nav class=\"pems-sidebar\"", StringComparison.Ordinal)..src.IndexOf("</nav>", StringComparison.Ordinal)];
        var links = new List<LegacyLink>();
        foreach (Match m in Regex.Matches(nav, @"<a\s(?<tag>[^>]*)>\s*<i class=""bi (?<icon>bi-[a-z0-9-]+)""></i>\s*@Localizer\[""(?<title>[^""]+)""\]", RegexOptions.Singleline))
        {
            var tag = m.Groups["tag"].Value;
            string target;
            var c = Regex.Match(tag, @"asp-controller=""([^""]+)""");
            if (c.Success)
            {
                var a = Regex.Match(tag, @"asp-action=""([^""]+)""");
                var ar = Regex.Match(tag, @"asp-area=""([^""]*)""");
                target = $"{(ar.Success ? ar.Groups[1].Value : "")}/{c.Groups[1].Value}/{(a.Success ? a.Groups[1].Value : "Index")}";
            }
            else
            {
                var cs = Regex.Match(tag, @"ComingSoon\(""([^""]+)"", ""([^""]+)""\)");
                target = cs.Success ? "/ComingSoon?title=" + Uri.EscapeDataString(cs.Groups[1].Value) + "&section=" + Uri.EscapeDataString(cs.Groups[2].Value)
                                    : Regex.Match(tag, @"href=""([^""]+)""").Groups[1].Value;
            }
            links.Add(new LegacyLink(target, m.Groups["icon"].Value, m.Groups["title"].Value));
        }
        // Disc Master دو بار در فایل قدیمی است (بخش جایگزین برای غیرادمین + Administration کامل)؛ همان یک پیوند منطقی، جای دوم ملاک ترتیب است
        return links.Where((l, i) => links.FindLastIndex(x => x.Target == l.Target) == i).ToList();
    }

    private static List<NavigationMenuItemDefinition> CatalogLinksInOrder()
    {
        var all = NavigationMenuDefaults.All;
        var result = new List<NavigationMenuItemDefinition>();
        void Walk(string? parent)
        {
            foreach (var d in all.Where(x => x.ParentKey == parent).OrderBy(x => x.SortOrder))
            {
                if (d.Kind == NavigationMenuItemKinds.Link) result.Add(d);
                else Walk(d.Key);
            }
        }
        Walk(null);
        return result;
    }

    private static string Target(NavigationMenuItemDefinition d) => d.ExternalPath ?? $"{d.Area ?? ""}/{d.Controller}/{d.Action}";

    /// <summary>تنها پیوند تازه‌ی کاتالوگ نسبت به سایدبار قدیمی: ورودی همین صفحه‌ی مدیریت منو.</summary>
    private static readonly string[] IntentionalAdditions = { "admin.navmenu" };

    [Fact]
    public void Catalog_ReproducesEveryLegacyLink_WithTheSameTargetIconTitleAndOrder()
    {
        var legacy = LegacyLinks();
        var catalog = CatalogLinksInOrder().Where(d => !IntentionalAdditions.Contains(d.Key)).ToList();

        Assert.True(legacy.Count > 90, $"parser found only {legacy.Count} legacy links");
        Assert.Equal(legacy.Select(l => l.Target), catalog.Select(Target));
        Assert.Equal(legacy.Select(l => l.Icon), catalog.Select(d => d.Icon));
        Assert.Equal(legacy.Select(l => l.TitleKey), catalog.Select(d => d.TitleKey));
    }

    // ==============================================================================
    // 2) سلامت کاتالوگ
    // ==============================================================================

    [Fact]
    public void Catalog_KeysAreUnique_ParentsExistAndAreContainers_RulesAndIconsAndBadgesAreValid()
    {
        var all = NavigationMenuDefaults.All;
        Assert.Equal(all.Count, all.Select(d => d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var d in all)
        {
            Assert.Empty(NavigationRuleEvaluator.Validate(d.VisibilityRule));
            Assert.True(NavigationMenuService.IsValidIcon(d.Icon), d.Key + ": " + d.Icon);
            if (d.BadgeKey != null) Assert.NotNull(NavigationBadges.Find(d.BadgeKey));
            if (d.ParentKey == null) { Assert.NotEqual(NavigationMenuItemKinds.Subheading, d.Kind); continue; }
            var parent = all.Single(p => p.Key == d.ParentKey);
            Assert.NotEqual(NavigationMenuItemKinds.Link, parent.Kind);
            if (d.Kind == NavigationMenuItemKinds.Subheading) Assert.Equal(NavigationMenuItemKinds.Section, parent.Kind);
            Assert.NotEqual(NavigationMenuItemKinds.Section, d.Kind);
        }
    }

    [Fact]
    public void Catalog_EveryLinkTargetsAnExistingGetAction()
    {
        var controllers = typeof(PEMS.Web.Controllers.HomeController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        foreach (var d in NavigationMenuDefaults.All.Where(x => x.Kind == NavigationMenuItemKinds.Link && x.ExternalPath == null))
        {
            var type = controllers.SingleOrDefault(t => t.Name == d.Controller + "Controller"
                && string.Equals(t.GetCustomAttribute<AreaAttribute>()?.RouteValue ?? "", d.Area ?? "", StringComparison.OrdinalIgnoreCase));
            Assert.True(type != null, $"{d.Key}: controller {d.Area}/{d.Controller} not found");
            var action = type!.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == d.Action).ToList();
            Assert.True(action.Any(m => m.GetCustomAttribute<HttpPostAttribute>() == null), $"{d.Key}: GET action {d.Action} not found");
        }
    }

    [Fact]
    public void Catalog_EveryTitleAndBadgeKey_ExistsInBothLanguages()
    {
        HashSet<string> Keys(string file) => XDocument.Load(Path.Combine(Root(), "PEMS.Web", "Resources", file))
            .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        var en = Keys("SharedResource.resx");
        var fa = Keys("SharedResource.fa.resx");
        var needed = NavigationMenuDefaults.All.Select(d => d.TitleKey).Concat(NavigationBadges.All.Select(b => b.TitleKey)).Distinct();
        foreach (var k in needed)
        {
            Assert.True(fa.Contains(k), "missing fa: " + k);
            Assert.True(en.Contains(k) || !Regex.IsMatch(k, "^[A-Z][a-z]+_"), "missing en: " + k);   // کلیدهای جمله‌ای انگلیسی همان متن خود را نمایش می‌دهند
        }
    }

    // ==============================================================================
    // 3) رفتار نمایش
    // ==============================================================================

    private sealed class Repl : IReplenishmentAvailability { public bool IsEnabled { get; set; } }

    private sealed class Env
    {
        public PEMSDbContext Ctx = null!;
        public NavigationMenuService Menu = null!;
        public DynamicSidebarBuilder Builder = null!;
        public PermissionService Permissions = null!;
        public AccessControlService Access = null!;
    }

    private static async Task<Env> EnvAsync(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("DynSidebar_" + name);
        foreach (var roleName in SystemRoles.All.Concat(new[] { "Test.Contracts", "Test.DiscMaster", "Test.Requester" }))
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var access = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, access);
        await permissions.SeedDefaultsAsync();
        var roleActions = new RoleModuleActionService(ctx, access);
        await roleActions.SeedDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var menu = new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions()));
        await menu.SyncDefaultsAsync(NavigationMenuDefaults.All);
        var builder = new DynamicSidebarBuilder(menu, new NavigationVisibilityResolver(new NavigationVisibilityOverrideService(ctx), access),
            permissions, roleActions, new Repl());
        return new Env { Ctx = ctx, Menu = menu, Builder = builder, Permissions = permissions, Access = access };
    }

    private static async Task GrantAsync(Env env, string role, string module, Action<RolePermission> flags)
    {
        var row = new RolePermission { RoleName = role, ModuleName = module, CanView = false, CreatedDate = DateTime.Now };
        flags(row);
        env.Ctx.RolePermissions.Add(row);
        await env.Ctx.SaveChangesAsync();
        var services = new ServiceCollection(); services.AddSingleton(env.Ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());
    }

    private static async Task<List<SidebarNode>> BuildAsync(Env env, string[] roles, string controller = "Home", string action = "Index", string? area = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));
        return await env.Builder.BuildAsync(principal, roles, new SidebarRouteContext(area, controller, action),
            i => i.ExternalPath ?? $"/{i.Area}/{i.Controller}/{i.Action}", k => k);
    }

    private static List<SidebarNode> Flat(List<SidebarNode> roots) => roots.SelectMany(r => new[] { r }.Concat(r.Descendants())).ToList();
    private static HashSet<string> Keys(List<SidebarNode> roots) => Flat(roots).Select(n => n.Key).ToHashSet();

    [Fact]
    public async Task UserWithoutRoles_SeesOnlyDashboardAndMyWork()
    {
        var env = await EnvAsync(nameof(UserWithoutRoles_SeesOnlyDashboardAndMyWork));
        var keys = Keys(await BuildAsync(env, Array.Empty<string>()));
        Assert.Equal(new[] { "home", "mywork" }.ToHashSet(), keys);
    }

    [Fact]
    public async Task SystemAdmin_SeesEveryBuiltInLink_ExceptTheDisabledReplenishmentFeature()
    {
        var env = await EnvAsync(nameof(SystemAdmin_SeesEveryBuiltInLink_ExceptTheDisabledReplenishmentFeature));
        var keys = Keys(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }));
        var expected = NavigationMenuDefaults.All.Where(d => d.Kind == NavigationMenuItemKinds.Link && d.Key != "wh.replenishment").Select(d => d.Key);
        Assert.Empty(expected.Except(keys));
        Assert.DoesNotContain("wh.replenishment", keys);
    }

    [Fact]
    public async Task PerItemPermission_HidesLinksTheUserCannotOpen_InsideAVisibleSection()
    {
        var env = await EnvAsync(nameof(PerItemPermission_HidesLinksTheUserCannotOpen_InsideAVisibleSection));
        var roleId = env.Ctx.Roles.Single(r => r.Name == "Test.Contracts").Id;
        env.Ctx.NavigationVisibilityOverrides.Add(new NavigationVisibilityOverride { RoleId = roleId, ModuleKey = "ContractManagement", IsVisible = true });
        await env.Ctx.SaveChangesAsync();
        await GrantAsync(env, "Test.Contracts", "Contracts", r => r.CanView = true);

        var keys = Keys(await BuildAsync(env, new[] { "Test.Contracts" }));

        Assert.Contains("sec.contract", keys);
        Assert.Contains("contract.contracts", keys);
        Assert.DoesNotContain("contract.vendors", keys);    // بدون Vendors.View
        Assert.DoesNotContain("contract.po", keys);         // بدون PurchaseOrder.View
        Assert.Contains("contract.milestones", keys);       // بدون سیگنال مجوز: مثل امروز نمایان
    }

    [Fact]
    public async Task AdminModuleOverride_HidesTheWholeSection()
    {
        var env = await EnvAsync(nameof(AdminModuleOverride_HidesTheWholeSection));
        var roleId = env.Ctx.Roles.Single(r => r.Name == SystemRoles.SystemAdmin).Id;
        env.Ctx.NavigationVisibilityOverrides.Add(new NavigationVisibilityOverride { RoleId = roleId, ModuleKey = "QualityControl", IsVisible = false });
        await env.Ctx.SaveChangesAsync();

        var keys = Keys(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }));

        Assert.DoesNotContain("sec.quality", keys);
        Assert.DoesNotContain("quality.qc", keys);
    }

    [Fact]
    public async Task DisciplineMasterOnly_SeesAdministrationWithOnlyTheDisciplineLink()
    {
        var env = await EnvAsync(nameof(DisciplineMasterOnly_SeesAdministrationWithOnlyTheDisciplineLink));
        await GrantAsync(env, "Test.DiscMaster", "DisciplineMaster", r => r.CanView = true);

        var roots = await BuildAsync(env, new[] { "Test.DiscMaster" });
        var admin = Assert.Single(roots, n => n.Key == "sec.admin");

        Assert.Equal(new[] { "admin.sub.org", "admin.disciplines" }, admin.Descendants().Select(d => d.Key));
    }

    [Fact]
    public async Task PartCatalogueRequester_SeesWarehouseWithOnlyThePartCatalogueLink()
    {
        var env = await EnvAsync(nameof(PartCatalogueRequester_SeesWarehouseWithOnlyThePartCatalogueLink));
        env.Ctx.RoleModuleActions.Add(new RoleModuleAction { RoleName = "Test.Requester", Module = "PartCatalogue", Action = "Create" });
        await env.Ctx.SaveChangesAsync();
        var services = new ServiceCollection(); services.AddSingleton(env.Ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var roots = await BuildAsync(env, new[] { "Test.Requester" });
        var wh = Assert.Single(roots, n => n.Key == "sec.inventory");

        Assert.Equal(new[] { "wh.partcatalogue" }, wh.Descendants().Select(d => d.Key));
    }

    [Fact]
    public async Task HiddenItems_AreNotRendered_AndAnEmptySubheadingDisappears()
    {
        var env = await EnvAsync(nameof(HiddenItems_AreNotRendered_AndAnEmptySubheadingDisappears));
        foreach (var key in new[] { "cmms.strategy", "cmms.plan", "cmms.backlog", "cmms.calendar" })
            Assert.True((await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = key, ParentKey = "cmms.sub.planning", IsActive = false, Icon = "bi-gear" })).Success);

        var keys = Keys(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }));

        Assert.DoesNotContain("cmms.sub.planning", keys);
        Assert.DoesNotContain("cmms.plan", keys);
        Assert.Contains("cmms.workorder", keys);
    }

    [Fact]
    public async Task Reordering_IsReflectedInTheRenderedOrder()
    {
        var env = await EnvAsync(nameof(Reordering_IsReflectedInTheRenderedOrder));
        Assert.True((await env.Menu.MoveAsync("eng.projects", up: true)).Success);

        var eng = Assert.Single(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }), n => n.Key == "sec.eng");

        Assert.Equal(new[] { "eng.projects", "eng.dashboard" }, eng.Children.Take(2).Select(c => c.Key));
    }

    [Theory]
    [InlineData("Letter", "MyReferrals", null, "corr.referrals", "corr.letters")]
    [InlineData("Letter", "Details", null, "corr.letters", "corr.referrals")]
    [InlineData("CmmsOperations", "Backlog", null, "cmms.backlog", "cmms.operations")]
    [InlineData("CmmsOperations", "Kpis", null, "cmms.operations", "cmms.backlog")]
    [InlineData("Organization", "Positions", "Admin", "admin.positions", "admin.org")]
    [InlineData("Organization", "Index", "Admin", "admin.org", "admin.positions")]
    [InlineData("Discipline", "Manage", null, "admin.disciplines", "eng.disciplines")]
    [InlineData("MaterialItem", "Details", "Warehouse", "wh.material", "wh.dashboard")]
    public async Task ActiveLink_FollowsControllerActionAndDefaultRules(string controller, string action, string? area, string active, string inactive)
    {
        var env = await EnvAsync($"active_{controller}_{action}");
        var flat = Flat(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }, controller, action, area));

        Assert.True(flat.Single(n => n.Key == active).IsActive, active);
        Assert.False(flat.Single(n => n.Key == inactive).IsActive, inactive);
    }

    [Fact]
    public async Task Section_OpensForAPageWithoutItsOwnLink_AndForAnyAdminAreaPage()
    {
        var env = await EnvAsync(nameof(Section_OpensForAPageWithoutItsOwnLink_AndForAnyAdminAreaPage));
        var cmms = Flat(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }, "FailureModeCatalog", "Index"));
        Assert.True(cmms.Single(n => n.Key == "sec.cmms").IsActive);
        Assert.False(cmms.Single(n => n.Key == "sec.eng").IsActive);

        var admin = Flat(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }, "SomeAdminPage", "Index", "Admin"));
        Assert.True(admin.Single(n => n.Key == "sec.admin").IsActive);
    }

    [Fact]
    public async Task ImportCenterLink_IsActiveOnAnyStandaloneImportPage()
    {
        var env = await EnvAsync(nameof(ImportCenterLink_IsActiveOnAnyStandaloneImportPage));
        var importController = ImportCatalog.StandaloneImportControllers.First(c => c != "ImportCenter");
        var flat = Flat(await BuildAsync(env, new[] { SystemRoles.SystemAdmin }, importController, "Index"));
        Assert.True(flat.Single(n => n.Key == "importcenter").IsActive);
    }

    [Fact]
    public async Task AdminTitleOverride_IsUsedForTheCurrentLanguage()
    {
        var env = await EnvAsync(nameof(AdminTitleOverride_IsUsedForTheCurrentLanguage));
        var prev = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            Assert.True((await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "mywork", TitleEn = "My Inbox", TitleFa = "کارتابل من", IsActive = true, Icon = "bi-inbox" })).Success);
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fa-IR");
            var fa = Flat(await BuildAsync(env, Array.Empty<string>())).Single(n => n.Key == "mywork");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
            var en = Flat(await BuildAsync(env, Array.Empty<string>())).Single(n => n.Key == "mywork");

            Assert.Equal("کارتابل من", fa.Title);
            Assert.Equal("My Inbox", en.Title);
            Assert.Equal("bi-inbox", en.Icon);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = prev; }
    }

    [Theory]
    [InlineData("Always", true)]
    [InlineData("Flag:Nope", false)]
    [InlineData("Bogus:Thing", false)]
    [InlineData("Perm:NoSuchResource.View", false)]
    [InlineData("Flag:On|Flag:Nope", true)]
    [InlineData("Flag:On&Flag:Nope", false)]
    [InlineData("Role:Tester&Flag:On", true)]
    [InlineData("", false)]
    public async Task RuleEvaluator_OrAndAndUnknownAtomsFailClosed(string rule, bool expected)
    {
        var env = await EnvAsync("rule_" + Math.Abs(rule.GetHashCode()));
        var evaluator = new NavigationRuleEvaluator(new NavigationVisibilitySnapshot(new HashSet<string>(), new HashSet<string>()),
            new Dictionary<string, bool> { ["On"] = true }, new[] { "Tester" }, env.Permissions, new RoleModuleActionService(env.Ctx, env.Access));

        Assert.Equal(expected, await evaluator.EvaluateAsync(rule));
    }

    // ==============================================================================
    // 4) سرویس مدیریت
    // ==============================================================================

    [Fact]
    public async Task Sync_IsAdditive_UpdatesCodeOwnedFields_AndNeverOverwritesAdminChanges()
    {
        var env = await EnvAsync(nameof(Sync_IsAdditive_UpdatesCodeOwnedFields_AndNeverOverwritesAdminChanges));
        var row = env.Ctx.NavigationMenuItems.Single(x => x.Key == "eng.vdr");
        row.TitleFa = "مدارک فروشنده"; row.Icon = "bi-star"; row.SortOrder = 5; row.IsActive = false; row.BadgeKey = NavigationBadges.TasksOpen;
        await env.Ctx.SaveChangesAsync();

        var changedDefs = NavigationMenuDefaults.All.Select(d => d.Key == "eng.vdr" ? d with { VisibilityRule = "Perm:VDR.View", Icon = "bi-x" } : d).ToList();
        var changed = await env.Menu.SyncDefaultsAsync(changedDefs);

        Assert.Equal(1, changed);
        row = env.Ctx.NavigationMenuItems.Single(x => x.Key == "eng.vdr");
        Assert.Equal("Perm:VDR.View", row.VisibilityRule);          // مالکیت کد
        Assert.Equal("مدارک فروشنده", row.TitleFa);                // مالکیت مدیر
        Assert.Equal("bi-star", row.Icon);
        Assert.Equal(5, row.SortOrder);
        Assert.False(row.IsActive);
        Assert.Equal(NavigationBadges.TasksOpen, row.BadgeKey);
        Assert.Equal(0, await env.Menu.SyncDefaultsAsync(changedDefs));   // Idempotent
    }

    [Fact]
    public async Task Update_ValidatesParentKindIconAndConcurrencyAndInvalidatesTheCache()
    {
        var env = await EnvAsync(nameof(Update_ValidatesParentKindIconAndConcurrencyAndInvalidatesTheCache));
        Assert.Equal("Nav_Err_Parent", (await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "sec.eng", ParentKey = "sec.ai", IsActive = true })).Error);
        Assert.Equal("Nav_Err_Parent", (await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "cmms.sub.planning", ParentKey = null, IsActive = true })).Error);
        Assert.Equal("Nav_Err_Parent", (await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "eng.vdr", ParentKey = "eng.mdb", IsActive = true })).Error);
        Assert.Equal("Nav_Err_Icon", (await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "eng.vdr", ParentKey = "sec.eng", Icon = "bi-x\" onmouseover=\"x", IsActive = true })).Error);

        var before = await env.Menu.GetMenuAsync();
        Assert.Contains(before, i => i.Key == "eng.vdr");
        Assert.True((await env.Menu.UpdateAsync(new NavigationMenuItemEditInput { Key = "eng.vdr", ParentKey = "sec.contract", IsActive = true, Icon = "bi-journal-check" })).Success);
        var moved = (await env.Menu.GetMenuAsync()).Single(i => i.Key == "eng.vdr");
        Assert.Equal("sec.contract", moved.ParentKey);   // کش باطل شده است
    }

    [Fact]
    public async Task CustomLink_RequiresAnExistingRouteAndPermission_AndOnlyCustomLinksCanBeDeleted()
    {
        var env = await EnvAsync(nameof(CustomLink_RequiresAnExistingRouteAndPermission_AndOnlyCustomLinksCanBeDeleted));
        Func<string, Task<bool>> perms = p => Task.FromResult(p == "WorkOrder.View");
        Func<string?, string, string, bool> routes = (a, c, x) => a == null && c == "WorkOrder" && x == "Index";
        NavigationMenuCustomLinkInput Input(Action<NavigationMenuCustomLinkInput>? edit = null)
        {
            var i = new NavigationMenuCustomLinkInput { ParentKey = "sec.cmms", TitleEn = "My WOs", TitleFa = "دستورکارهای من", Controller = "WorkOrder", Action = "Index", RequiredPermission = "WorkOrder.View" };
            edit?.Invoke(i);
            return i;
        }

        Assert.Equal("Nav_Err_Route", (await env.Menu.AddCustomLinkAsync(Input(i => i.Controller = "Nope"), perms, routes)).Error);
        Assert.Equal("Nav_Err_Route", (await env.Menu.AddCustomLinkAsync(Input(i => i.Controller = "https://evil.example"), perms, routes)).Error);
        Assert.Equal("Nav_Err_Permission", (await env.Menu.AddCustomLinkAsync(Input(i => i.RequiredPermission = "Invented.View"), perms, routes)).Error);
        Assert.Equal("Nav_Err_Parent", (await env.Menu.AddCustomLinkAsync(Input(i => i.ParentKey = "eng.vdr"), perms, routes)).Error);
        Assert.Equal("Nav_Err_TitleRequired", (await env.Menu.AddCustomLinkAsync(Input(i => i.TitleFa = " "), perms, routes)).Error);

        Assert.True((await env.Menu.AddCustomLinkAsync(Input(), perms, routes)).Success);
        var custom = env.Ctx.NavigationMenuItems.Single(x => !x.IsSystem);
        Assert.Equal("Perm:WorkOrder.View", custom.VisibilityRule);
        Assert.StartsWith(NavigationMenuService.CustomKeyPrefix, custom.Key);

        Assert.Equal("Nav_Err_SystemItem", (await env.Menu.DeleteCustomAsync("eng.vdr")).Error);
        Assert.True((await env.Menu.DeleteCustomAsync(custom.Key)).Success);
        Assert.True(env.Ctx.NavigationMenuItems.Any(x => x.Key == "eng.vdr"));
    }

    // ==============================================================================
    // شمارنده‌ها
    // ==============================================================================

    private class Stub<T> : DispatchProxy where T : class
    {
        public Dictionary<string, Func<object?[]?, object?>> On { get; } = new();
        protected override object? Invoke(MethodInfo? m, object?[]? args) =>
            On.TryGetValue(m!.Name, out var h) ? h(args) : throw new InvalidOperationException("unexpected call " + m.Name);
        public static (T Proxy, Stub<T> Stub) New() { var p = Create<T, Stub<T>>(); return (p, (Stub<T>)(object)p); }
    }

    [Fact]
    public async Task Badges_PersonalCountsAlways_ModuleCountsOnlyWithPermission_UnknownKeysIgnored()
    {
        var (myWork, mw) = Stub<IMyWorkService>.New();
        mw.On["GetSummaryAsync"] = _ => Task.FromResult(new MyApprovalsSummaryDto { Total = 7, Overdue = 2, ByModule = new Dictionary<string, int> { ["PartCatalogue"] = 3 } });
        var (notifications, ns) = Stub<INotificationService>.New();
        ns.On["GetUnreadCountAsync"] = _ => Task.FromResult(4);
        var (tasks, ts) = Stub<ITaskService>.New();
        ts.On["GetOpenActionItemCountAsync"] = _ => Task.FromResult(5);
        var (counts, cs) = Stub<INavigationCountService>.New();
        cs.On["CountOpenWorkOrdersAsync"] = _ => Task.FromResult(11);
        cs.On["CountStoreRequisitionsAwaitingApprovalAsync"] = _ => Task.FromResult(9);
        var permissions = new FakePermissionService();
        permissions.Denied.Add(("StoreRequisition", "Approve"));

        var svc = new NavigationBadgeService(myWork, notifications, tasks, counts, permissions);
        var result = await svc.GetCountsAsync(new[]
        {
            NavigationBadges.MyWorkPending, NavigationBadges.MyWorkOverdue, NavigationBadges.MyWorkPartCatalogue, NavigationBadges.MyWorkContract,
            NavigationBadges.NotificationsUnread, NavigationBadges.TasksOpen, NavigationBadges.WorkOrdersOpen,
            NavigationBadges.StoreRequisitionsAwaitingApproval, "sql:select 1"
        }, userId: 1, employeeId: 10, roles: new[] { "X" });

        Assert.Equal(7, result[NavigationBadges.MyWorkPending]);
        Assert.Equal(2, result[NavigationBadges.MyWorkOverdue]);
        Assert.Equal(3, result[NavigationBadges.MyWorkPartCatalogue]);
        Assert.Equal(0, result[NavigationBadges.MyWorkContract]);
        Assert.Equal(4, result[NavigationBadges.NotificationsUnread]);
        Assert.Equal(5, result[NavigationBadges.TasksOpen]);
        Assert.Equal(11, result[NavigationBadges.WorkOrdersOpen]);
        Assert.False(result.ContainsKey(NavigationBadges.StoreRequisitionsAwaitingApproval));   // بدون مجوز: محاسبه هم نمی‌شود
        Assert.False(result.ContainsKey("sql:select 1"));
    }

    [Fact]
    public async Task Badges_WithoutALinkedEmployee_SkipMyWorkWithoutFailing()
    {
        var (myWork, _) = Stub<IMyWorkService>.New();        // هر فراخوانی = خطا؛ نباید صدا زده شود
        var (notifications, ns) = Stub<INotificationService>.New();
        ns.On["GetUnreadCountAsync"] = _ => Task.FromResult(1);
        var (tasks, _) = Stub<ITaskService>.New();
        var (counts, _) = Stub<INavigationCountService>.New();
        var svc = new NavigationBadgeService(myWork, notifications, tasks, counts, new FakePermissionService());

        var result = await svc.GetCountsAsync(new[] { NavigationBadges.MyWorkPending, NavigationBadges.NotificationsUnread }, 1, null, Array.Empty<string>());

        Assert.False(result.ContainsKey(NavigationBadges.MyWorkPending));
        Assert.Equal(1, result[NavigationBadges.NotificationsUnread]);
    }

    // ==============================================================================
    // کنترلر مدیریت
    // ==============================================================================

    private sealed class Flag : IDynamicSidebarAvailability { public bool IsEnabled { get; set; } }

    private sealed class Descriptors : IActionDescriptorCollectionProvider
    {
        public ActionDescriptorCollection ActionDescriptors { get; }
        public Descriptors(params ControllerActionDescriptor[] items) => ActionDescriptors = new ActionDescriptorCollection(items, 1);
    }

    private static ControllerActionDescriptor Action(string controller, string action, string? area = null, string? httpMethod = null)
    {
        var d = new ControllerActionDescriptor { ControllerName = controller, ActionName = action, RouteValues = new Dictionary<string, string?>() };
        if (area != null) d.RouteValues["area"] = area;
        d.EndpointMetadata = httpMethod == null ? new List<object>() : new List<object> { new HttpMethodMetadata(new[] { httpMethod }) };
        return d;
    }

    [Fact]
    public void AdminController_IsSystemAdminOnly()
    {
        var attr = typeof(NavigationMenuController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(SystemRoles.SystemAdmin, attr!.Roles);
    }

    [Fact]
    public async Task AdminController_ReturnsNotFound_WhenTheFeatureFlagIsOff()
    {
        var env = await EnvAsync(nameof(AdminController_ReturnsNotFound_WhenTheFeatureFlagIsOff));
        var c = new NavigationMenuController(env.Menu, new Flag { IsEnabled = false }, new Descriptors(), new PassThroughLocalizer());

        Assert.IsType<NotFoundResult>(await c.Index());
        Assert.IsType<NotFoundResult>(await c.Edit("eng.vdr"));
        Assert.IsType<NotFoundResult>(await c.Delete("eng.vdr"));
    }

    [Fact]
    public async Task AdminController_RouteCheck_AcceptsOnlyExistingGetActionsInTheRightArea()
    {
        var env = await EnvAsync(nameof(AdminController_RouteCheck_AcceptsOnlyExistingGetActionsInTheRightArea));
        var c = new NavigationMenuController(env.Menu, new Flag { IsEnabled = true },
            new Descriptors(Action("WorkOrder", "Index"), Action("WorkOrder", "Delete", httpMethod: "POST"), Action("MaterialItem", "Index", "Warehouse")),
            new PassThroughLocalizer());

        Assert.True(c.RouteExists(null, "WorkOrder", "Index"));
        Assert.False(c.RouteExists(null, "WorkOrder", "Delete"));
        Assert.False(c.RouteExists(null, "MaterialItem", "Index"));
        Assert.True(c.RouteExists("Warehouse", "MaterialItem", "Index"));
        Assert.False(c.RouteExists(null, "Nope", "Index"));
    }

    [Theory]
    [InlineData("Always", false)]
    [InlineData("Module:EngineeringManagement", false)]
    [InlineData("Module:Inventory&Perm:IRN.View", true)]
    [InlineData("CmmsItem:WorkOrder", true)]
    [InlineData("Flag:PartCatalogue", true)]
    public void NoPermissionSignal_MeansOnlySectionGateOrAlways(string rule, bool hasSignal)
        => Assert.Equal(hasSignal, NavigationMenuController.HasPermissionSignal(rule));

    private sealed class PassThroughLocalizer : Microsoft.Extensions.Localization.IStringLocalizer<PEMS.Web.Resources.SharedResource>
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => new(name, name);
        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<Microsoft.Extensions.Localization.LocalizedString>();
    }
}
