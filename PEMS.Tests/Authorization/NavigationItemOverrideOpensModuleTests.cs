using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// باگ گزارش‌شده: در «Effective Navigation Visibility» مدیر آیتم‌های CMMS را برای یک نقش «نمایان» می‌کرد و ذخیره می‌شد،
/// ولی کاربر آن نقش هیچ‌چیز در سایدبار نمی‌دید. علت: آیتم‌ها فقط وقتی محاسبه می‌شدند که ماژول CmmsIntegration نمایان
/// باشد و دروازه‌ی ماژول فهرست ثابت MenuAccess بود (نقش‌های MNT.* در آن نیستند). اکنون Override صریح «نمایان» روی آیتم،
/// ماژول را هم باز می‌کند؛ Hide صریح ماژول، و پیش‌فرض نقش‌های بدون Override دست‌نخورده می‌مانند.
/// </summary>
public class NavigationItemOverrideOpensModuleTests
{
    private const string CustomRole = "MNT.PlanningHead";   // مثل نقش‌های واقعی دیتابیس: خارج از MenuAccess.CmmsIntegration

    private static async Task<(PEMS.Persistence.Context.PEMSDbContext ctx, NavigationVisibilityResolver resolver, NavigationVisibilityOverrideService overrides, int roleId)> SeedAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("NavItemOpens_" + dbName);
        foreach (var roleName in SystemRoles.All.Append(CustomRole))
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var access = new AccessControlService(ctx);
        await new PermissionService(ctx, access).SeedDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase3PermissionsAsync(provider);

        var overrides = new NavigationVisibilityOverrideService(ctx);
        var roleId = await ctx.Roles.Where(r => r.Name == CustomRole).Select(r => r.Id).FirstAsync();
        return (ctx, new NavigationVisibilityResolver(overrides, access), overrides, roleId);
    }

    [Fact]
    public async Task ReportedBug_ItemShowOverride_ForARoleOutsideMenuAccess_NowRendersTheModuleAndTheItem()
    {
        var (_, resolver, overrides, roleId) = await SeedAsync(nameof(ReportedBug_ItemShowOverride_ForARoleOutsideMenuAccess_NowRendersTheModuleAndTheItem));
        Assert.False((await resolver.ResolveAsync(new[] { CustomRole })).IsModuleVisible("CmmsIntegration")); // پیش‌فرض بدون Override: مخفی

        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Permit", isVisible: true, reason: "test", actingUser: "admin");
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Equipment", isVisible: true, reason: "test", actingUser: "admin");

        var snapshot = await resolver.ResolveAsync(new[] { CustomRole });
        Assert.True(snapshot.IsModuleVisible("CmmsIntegration"));
        Assert.True(snapshot.IsCmmsItemVisible("Equipment"));
        Assert.True(snapshot.IsCmmsItemVisible("Permit"));
        Assert.False(snapshot.IsCmmsItemVisible("Instrument"));   // بدون سیگنال مجوزی و بدون Override: همچنان مخفی
        Assert.False(snapshot.IsCmmsItemVisible("SparePart"));
    }

    [Fact]
    public async Task ExplicitModuleHide_StillWins_OverAnItemShow_ForTheSameRole()
    {
        var (_, resolver, overrides, roleId) = await SeedAsync(nameof(ExplicitModuleHide_StillWins_OverAnItemShow_ForTheSameRole));
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Equipment", isVisible: true, reason: "test", actingUser: "admin");
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", null, isVisible: false, reason: "test", actingUser: "admin");

        var snapshot = await resolver.ResolveAsync(new[] { CustomRole });

        Assert.False(snapshot.IsModuleVisible("CmmsIntegration"));
        Assert.False(snapshot.IsCmmsItemVisible("Equipment"));
    }

    [Fact]
    public async Task ItemShowOnOneRole_DoesNotOpenTheModuleForAUserWhoOnlyHasAnotherRole()
    {
        var (ctx, resolver, overrides, roleId) = await SeedAsync(nameof(ItemShowOnOneRole_DoesNotOpenTheModuleForAUserWhoOnlyHasAnotherRole));
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Equipment", isVisible: true, reason: "test", actingUser: "admin");

        var otherUser = await resolver.ResolveAsync(new[] { SystemRoles.QAManager });
        var bothRoles = await resolver.ResolveAsync(new[] { SystemRoles.QAManager, CustomRole });

        Assert.False(otherUser.IsModuleVisible("CmmsIntegration"));
        Assert.True(bothRoles.IsModuleVisible("CmmsIntegration"));
        Assert.True(bothRoles.IsCmmsItemVisible("Equipment"));
        _ = ctx;
    }

    [Fact]
    public async Task ItemHideOverride_DoesNotOpenTheModule()
    {
        var (_, resolver, overrides, roleId) = await SeedAsync(nameof(ItemHideOverride_DoesNotOpenTheModule));
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Equipment", isVisible: false, reason: "test", actingUser: "admin");

        Assert.False((await resolver.ResolveAsync(new[] { CustomRole })).IsModuleVisible("CmmsIntegration"));
    }

    [Fact]
    public async Task SystemRolesWithoutOverrides_KeepTheirExactDefaults()
    {
        var (_, resolver, _, _) = await SeedAsync(nameof(SystemRolesWithoutOverrides_KeepTheirExactDefaults));

        Assert.True((await resolver.ResolveAsync(new[] { SystemRoles.MaintenanceEngineer })).IsModuleVisible("CmmsIntegration"));
        Assert.False((await resolver.ResolveAsync(new[] { SystemRoles.QAManager })).IsModuleVisible("CmmsIntegration"));
        Assert.False((await resolver.ResolveAsync(new[] { SystemRoles.Viewer })).IsModuleVisible("CmmsIntegration"));
    }

    [Fact]
    public async Task AdminDescribePage_AgreesWithTheRealSidebar_ForTheModuleRow()
    {
        var (_, resolver, overrides, roleId) = await SeedAsync(nameof(AdminDescribePage_AgreesWithTheRealSidebar_ForTheModuleRow));
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "Permit", isVisible: true, reason: "test", actingUser: "admin");

        var rows = await resolver.DescribeForRoleAsync(CustomRole);
        var module = rows.Single(r => r.ModuleKey == "CmmsIntegration" && r.ItemKey == null);

        Assert.False(module.DefaultVisible);
        Assert.True(module.EffectiveVisible);                      // قبلاً false بود، درحالی‌که آیتم‌ها Effective=true نشان داده می‌شدند
        Assert.Contains("item-level override", module.DefaultSource);
        Assert.True((await resolver.ResolveAsync(new[] { CustomRole })).IsModuleVisible("CmmsIntegration"));
    }

    [Fact]
    public async Task ShowingAnItem_NeverGrantsTheUnderlyingPermission()
    {
        var (ctx, resolver, overrides, roleId) = await SeedAsync(nameof(ShowingAnItem_NeverGrantsTheUnderlyingPermission));
        await overrides.SetOverrideAsync(roleId, "CmmsIntegration", "WorkOrder", isVisible: true, reason: "test", actingUser: "admin");

        Assert.True((await resolver.ResolveAsync(new[] { CustomRole })).IsCmmsItemVisible("WorkOrder"));
        var decision = await new AccessControlService(ctx).AuthorizeForRolesAsync(new[] { CustomRole }, "WorkOrder", "View");
        Assert.False(decision.Allowed);   // Visible ≠ Authorized
    }
}
