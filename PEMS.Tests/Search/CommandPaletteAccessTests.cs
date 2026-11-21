using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.SearchProviders;
using PEMS.Tests.TestSupport;
using PEMS.Web.Navigation;
using PEMS.Web.Resources;

namespace PEMS.Tests.Search;

/// <summary>
/// SCRUM-64: the command palette and the /GlobalSearch page never show a record, menu or action the user cannot open.
/// (1) every search provider declares the access rule of its page (the contract), pinned here so a change is deliberate;
/// (2) GlobalSearchService.SearchEverywhereAsync does not run a provider whose rule the user fails, and drops results
/// without a local link; (3) with the real seeded permission matrix, out-of-scope vendors, work orders and confidential
/// letters do not appear; (4) palette actions follow the same rule check.
/// </summary>
public class CommandPaletteAccessTests
{
    // ---------------------------------------------------------------- (1) the contract

    // The rule of the page each provider's results open. Changing a rule means changing the page's access too.
    private static readonly Dictionary<string, string> ExpectedRules = new()
    {
        ["Attachment"] = "any user + row scope",
        ["Contract"] = "Contracts/View",
        ["Department"] = "roles: SystemAdmin, EngineeringManager (lookup only)",
        ["Discipline"] = "DisciplineWorkspace/View",
        ["DocumentRevision"] = "any user + row scope",
        ["Employee"] = "roles: SystemAdmin, EngineeringManager (lookup only)",
        ["EngineeringDocument"] = "any user + row scope",
        ["Equipment"] = "any user",
        ["EquipmentTag"] = "any user",
        ["HelpArticle"] = "any user + row scope",
        ["Letter"] = "any user + row scope",
        ["MOC"] = "any user",
        ["MaterialRequisition"] = "MaterialRequisition/View",
        ["Notification"] = "any user + row scope",
        ["PartCatalogueRequest"] = "any user + row scope",
        ["Project"] = "any user",
        ["SparePart"] = "any user",
        ["TechnicalQuery"] = "any user",
        ["VDRItem"] = "any user",
        ["Vendor"] = "Vendors/View",
        ["WorkOrder"] = "WorkOrder/View",
    };

    private static IEnumerable<Type> ProviderTypes() =>
        typeof(GlobalSearchService).Assembly.GetTypes()
            .Where(t => typeof(ISearchableEntityProvider).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

    // Access and EntityType are expression-bodied, so they can be read without the provider's services.
    private static ISearchableEntityProvider Bare(Type t) => (ISearchableEntityProvider)RuntimeHelpers.GetUninitializedObject(t);

    private static string Describe(SearchAccessRule r) =>
        Regex.Replace(r.ToString(), @" \+ row scope: [^(]*", " + row scope").Trim();

    [Fact]
    public void EveryProvider_DeclaresTheRuleOfItsPage()
    {
        var actual = ProviderTypes().Select(Bare).ToDictionary(p => p.EntityType, p => Describe(p.Access));
        Assert.Equal(ExpectedRules.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }

    [Fact]
    public void EveryProvider_IsRegisteredInProgram()
    {
        var program = File.ReadAllText(RepoFile("PEMS.Web", "Program.cs"));
        foreach (var t in ProviderTypes())
            Assert.Contains($"AddScoped<ISearchableEntityProvider, PEMS.Persistence.Services.SearchProviders.{t.Name}>", program);
    }

    [Fact]
    public void GlobalSearchPage_NoLongerQueriesTablesDirectly()
    {
        var operation = File.ReadAllText(RepoFile("PEMS.Web", "Services", "GlobalSearchOperations.cs"));
        Assert.DoesNotContain("PEMSDbContext", operation);
        Assert.Contains("CommandPaletteService", operation);
    }

    [Fact]
    public void Layout_LoadsThePaletteFromPemsUi_WithTextsFromResources()
    {
        var layout = File.ReadAllText(RepoFile("PEMS.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("~/_content/PEMS.UI/pems-command-palette.js", layout);
        Assert.Contains("~/_content/PEMS.UI/pems-command-palette.css", layout);
        Assert.Contains("data-pems-command-palette-trigger", layout);

        var js = File.ReadAllText(RepoFile("PEMS.UI", "wwwroot", "pems-command-palette.js"));
        Assert.DoesNotMatch(@"https?://", js);   // offline: no external call
        foreach (var key in Regex.Matches(layout, @"Localizer\[""(Palette_\w+)""\]").Select(m => m.Groups[1].Value).Distinct())
        {
            Assert.Contains($"name=\"{key}\"", File.ReadAllText(RepoFile("PEMS.Web", "Resources", "SharedResource.resx")));
            Assert.Contains($"name=\"{key}\"", File.ReadAllText(RepoFile("PEMS.Web", "Resources", "SharedResource.fa.resx")));
        }
    }

    // ---------------------------------------------------------------- (2) the central check

    private sealed class SpyProvider : ISearchableEntityProvider
    {
        public SpyProvider(string type, SearchAccessRule access, params GlobalSearchResultItemDto[] items)
        {
            EntityType = type;
            Access = access;
            Items = items.ToList();
        }

        public string EntityType { get; }
        public string DisplayName => EntityType;
        public SearchAccessRule Access { get; }
        public List<GlobalSearchResultItemDto> Items { get; }
        public int Calls { get; private set; }
        public Exception? Throw { get; init; }

        public Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(string query, int page, int pageSize, int? projectId)
        {
            Calls++;
            if (Throw != null) throw Throw;
            return Task.FromResult((Items.Take(pageSize).ToList(), Items.Count));
        }
    }

    private static GlobalSearchResultItemDto Item(string label, string? url, int score = 50) =>
        new() { Label = label, Url = url, Score = score };

    private static FakeCurrentUserContext User(params string[] roles) =>
        new() { UserId = 7, EmployeeId = 7, Roles = roles.ToList() };

    [Fact]
    public async Task DeniedPermission_ProviderIsNotRun()
    {
        var vendors = new SpyProvider("Vendor", SearchAccessRule.Permission("Vendors"), Item("ACME", "/Vendor/Details/1"));
        var perms = new FakePermissionService();
        perms.Denied.Add(("Vendors", "View"));
        var service = new GlobalSearchService(new[] { vendors }, perms, User("Viewer"));

        var groups = await service.SearchEverywhereAsync("ACME");

        Assert.Empty(groups);
        Assert.Equal(0, vendors.Calls);   // the module is not even queried
    }

    [Fact]
    public async Task GrantedPermission_ResultsAreReturned()
    {
        var vendors = new SpyProvider("Vendor", SearchAccessRule.Permission("Vendors"), Item("ACME", "/Vendor/Details/1"));
        var service = new GlobalSearchService(new[] { vendors }, new FakePermissionService(), User("Viewer"));

        var group = Assert.Single(await service.SearchEverywhereAsync("ACME"));
        Assert.Equal("Vendor", group.EntityType);
        Assert.Equal("/Vendor/Details/1", Assert.Single(group.Items).Url);
    }

    [Fact]
    public async Task RoleRule_NeedsOneOfTheRoles()
    {
        var rule = SearchAccessRule.Roles(SystemRoles.SystemAdmin, SystemRoles.EngineeringManager);
        var p = new SpyProvider("Staff", rule, Item("Ali", "/Staff/1"));

        Assert.Empty(await new GlobalSearchService(new[] { p }, new FakePermissionService(), User(SystemRoles.Viewer)).SearchEverywhereAsync("Ali"));
        Assert.Single(await new GlobalSearchService(new[] { p }, new FakePermissionService(), User(SystemRoles.EngineeringManager)).SearchEverywhereAsync("Ali"));
    }

    [Fact]
    public async Task AnonymousUser_GetsNothing_EvenForAnyUserRules()
    {
        var p = new SpyProvider("Equipment", SearchAccessRule.AnyUser(), Item("P-101", "/Equipment/Details/1"));
        var service = new GlobalSearchService(new[] { p }, new FakePermissionService(), new FakeCurrentUserContext());

        Assert.Empty(await service.SearchEverywhereAsync("P-101"));
        Assert.Equal(0, p.Calls);
    }

    [Fact]
    public async Task LookupOnlyProviders_AreNotInThePalette()
    {
        var p = new SpyProvider("Employee", SearchAccessRule.AnyUser().LookupOnly(), Item("Sara", "/Employee/1"));
        var service = new GlobalSearchService(new[] { p }, new FakePermissionService(), User("Viewer"));

        Assert.Empty(await service.SearchEverywhereAsync("Sara"));
        Assert.Equal(0, p.Calls);
    }

    [Fact]
    public async Task ResultsWithoutALocalLink_AreDropped()
    {
        var p = new SpyProvider("Notification", SearchAccessRule.AnyUser(),
            Item("no link", null), Item("external", "https://example.org/x"), Item("protocol relative", "//evil/x"),
            Item("backslash", "/\\evil"), Item("local", "/Notification/Index"));
        var service = new GlobalSearchService(new[] { p }, new FakePermissionService(), User("Viewer"));

        var item = Assert.Single(Assert.Single(await service.SearchEverywhereAsync("link")).Items);
        Assert.Equal("local", item.Label);
    }

    [Fact]
    public async Task ShortQuery_RunsNothing()
    {
        var p = new SpyProvider("Equipment", SearchAccessRule.AnyUser(), Item("P", "/e/1"));
        var service = new GlobalSearchService(new[] { p }, new FakePermissionService(), User("Viewer"));

        Assert.Empty(await service.SearchEverywhereAsync(" p "));
        Assert.Equal(0, p.Calls);
    }

    [Fact]
    public async Task OneFailingProvider_DoesNotHideTheOthers()
    {
        var broken = new SpyProvider("Broken", SearchAccessRule.AnyUser()) { Throw = new InvalidOperationException("db down") };
        var ok = new SpyProvider("Equipment", SearchAccessRule.AnyUser(), Item("P-101", "/Equipment/Details/1"));
        var service = new GlobalSearchService(new ISearchableEntityProvider[] { broken, ok }, new FakePermissionService(), User("Viewer"));

        Assert.Equal("Equipment", Assert.Single(await service.SearchEverywhereAsync("P-101")).EntityType);
    }

    // ---------------------------------------------------------------- (3) real providers, real permission matrix

    private static async Task<(PEMSDbContext ctx, PermissionService perms)> SeedAsync(string db)
    {
        var ctx = PemsDbContextFactory.CreateInMemory(db);
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var perms = new PermissionService(ctx, new AccessControlService(ctx));
        await perms.SeedDefaultsAsync();
        var services = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(services);
        await AccessControlSeeder.SeedPhase3PermissionsAsync(services);
        return (ctx, perms);
    }

    private static async Task<string> RoleWithoutAsync(IPermissionService perms, string module, string action)
    {
        foreach (var r in SystemRoles.All.Where(r => r != SystemRoles.SystemAdmin))
            if (!await perms.HasPermissionAsync(new[] { r }, module, action)) return r;
        throw new InvalidOperationException($"Every seeded role has {module}/{action}; pick another fixture.");
    }

    [Fact]
    public async Task SeededMatrix_VendorHiddenWithoutVendorsView_ShownToAdmin()
    {
        var (ctx, perms) = await SeedAsync(nameof(SeededMatrix_VendorHiddenWithoutVendorsView_ShownToAdmin));
        ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "PALVEND Industrial", IsActive = true });
        await ctx.SaveChangesAsync();
        var role = await RoleWithoutAsync(perms, "Vendors", "View");

        var denied = new GlobalSearchService(new ISearchableEntityProvider[] { new VendorSearchProvider(ctx) }, perms, User(role));
        Assert.DoesNotContain(await denied.SearchEverywhereAsync("PALVEND"), g => g.EntityType == "Vendor");

        var admin = new GlobalSearchService(new ISearchableEntityProvider[] { new VendorSearchProvider(ctx) }, perms, User(SystemRoles.SystemAdmin));
        Assert.Contains(await admin.SearchEverywhereAsync("PALVEND"), g => g.EntityType == "Vendor");
    }

    [Fact]
    public async Task SeededMatrix_WorkOrderHiddenWithoutWorkOrderView()
    {
        var (ctx, perms) = await SeedAsync(nameof(SeededMatrix_WorkOrderHiddenWithoutWorkOrderView));
        ctx.Equipments.Add(new Equipment { EquipmentId = 1, TagNumber = "EQ-1", Name = "Pump", EquipmentType = "Pump", IsActive = true });
        ctx.WorkOrders.Add(new WorkOrder { WorkOrderId = 1, WorkOrderNumber = "WO-PAL-0001", Description = "x", Status = "Open", Priority = "Medium", EquipmentId = 1, IsActive = true });
        await ctx.SaveChangesAsync();
        var role = await RoleWithoutAsync(perms, "WorkOrder", "View");
        var user = User(role);

        var service = new GlobalSearchService(new ISearchableEntityProvider[] { new WorkOrderSearchProvider(ctx, perms, user) }, perms, user);

        Assert.Empty(await service.SearchEverywhereAsync("WO-PAL-0001"));
    }

    [Fact]
    public async Task SeededMatrix_ConfidentialLetterNotFound_PublicLetterFound()
    {
        var (ctx, perms) = await SeedAsync(nameof(SeededMatrix_ConfidentialLetterNotFound_PublicLetterFound));
        ctx.Letters.Add(new Letter { LetterId = 1, LetterType = "Outgoing", LetterNumber = "PAL-CONF-1", Subject = "Secret", ConfidentialityLevel = "Confidential", RegisteredById = 999, Status = "Draft", IsActive = true });
        ctx.Letters.Add(new Letter { LetterId = 2, LetterType = "Outgoing", LetterNumber = "PAL-OPEN-1", Subject = "Hello", ConfidentialityLevel = "Internal", RegisteredById = 999, Status = "Draft", IsActive = true });
        await ctx.SaveChangesAsync();
        var user = new FakeCurrentUserContext { UserId = 1, EmployeeId = 1, Roles = new List<string> { SystemRoles.DisciplineEngineer } };
        var service = new GlobalSearchService(
            new ISearchableEntityProvider[] { new LetterSearchProvider(ctx, new FakeKnowledgeBaseService(), perms, user) }, perms, user);

        var labels = (await service.SearchEverywhereAsync("PAL-")).SelectMany(g => g.Items).Select(i => i.Label).ToList();

        Assert.DoesNotContain("PAL-CONF-1", labels);
        Assert.Contains("PAL-OPEN-1", labels);
    }

    // ---------------------------------------------------------------- (4) actions

    [Fact]
    public async Task Actions_FollowThePermissionOfTheirPage()
    {
        var perms = new FakePermissionService();
        perms.Denied.Add(("WorkOrder", "Create"));
        perms.Denied.Add(("Contracts", "Create"));
        var search = new GlobalSearchService(Array.Empty<ISearchableEntityProvider>(), perms, User("Viewer"));
        var palette = new CommandPaletteService(search, new CommandPaletteActions(), new KeyLocalizer());

        var urls = (await palette.ActionsAsync(new FakeUrl(), "/Home", "light")).Select(a => a.Url).ToList();

        Assert.DoesNotContain("/WorkOrder/Create", urls);
        Assert.DoesNotContain("/Contract/Create", urls);
        Assert.Contains("/MaterialRequisition/Create", urls);
        Assert.Contains("/Letter/Create", urls);
        Assert.DoesNotContain(urls, u => u.Contains("theme=light"));   // the current theme is not offered again
    }

    [Fact]
    public async Task Actions_ReturnUrlMustBeLocal()
    {
        var search = new GlobalSearchService(Array.Empty<ISearchableEntityProvider>(), new FakePermissionService(), User("Viewer"));
        var palette = new CommandPaletteService(search, new CommandPaletteActions(), new KeyLocalizer());

        var urls = (await palette.ActionsAsync(new FakeUrl(), "https://evil.example/x", "light")).Select(a => a.Url).ToList();

        Assert.DoesNotContain(urls, u => u.Contains("evil"));
    }

    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name + ":" + string.Join(",", arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private sealed class FakeUrl : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();

        public string? Action(UrlActionContext a)
        {
            var path = "/" + a.Controller + "/" + a.Action;
            var query = a.Values == null ? "" : string.Join("&", a.Values.GetType().GetProperties()
                .Where(p => p.Name != "area" && p.GetValue(a.Values) != null)
                .Select(p => p.Name + "=" + Uri.EscapeDataString(p.GetValue(a.Values)!.ToString()!)));
            return query.Length == 0 ? path : path + "?" + query;
        }

        public string? Content(string? contentPath) => contentPath?.TrimStart('~');
        public bool IsLocalUrl(string? url) => url != null && url.StartsWith('/') && !url.StartsWith("//");
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
