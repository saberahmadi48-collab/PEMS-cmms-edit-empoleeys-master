using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// Role Catalog (docs/audits/role-catalog-audit.md): Seeding Idempotent و فقط-افزودنی، حفظ داده‌ی موجود،
/// نبود ارتقای دسترسی بین‌دامنه‌ای، و ردیابی Role → Permission → Navigation برای نقش‌های تازه.
/// الگوی تست هم‌سو با NavigationVisibilityTests: Seed واقعی روی PEMSDbContext درون‌حافظه‌ای.
/// </summary>
public class RoleCatalogTests
{
    private static int BaselineCount => RoleCatalog.Definitions.Length;

    private static IServiceProvider Provider(PEMSDbContext ctx)
    {
        var s = new ServiceCollection();
        s.AddSingleton(ctx);
        return s.BuildServiceProvider();
    }

    /// ترتیب دقیقاً مثل Program.cs: SeedDefaults → Role Catalog → SeedFromLegacy
    private static async Task<(PEMSDbContext Ctx, PermissionService Perm, AccessControlService Access)> StartupLikeAsync(string db)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"RoleCatalog_{db}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var access = new AccessControlService(ctx);
        var perm = new PermissionService(ctx, access);
        await perm.SeedDefaultsAsync();
        await RoleCatalogSeeder.SeedAsync(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(Provider(ctx));
        return (ctx, perm, access);
    }

    // ---------------- کاتالوگ ----------------

    [Fact]
    public void Catalog_Invariants_UniqueNamesAndValidMetadata()
    {
        var defs = RoleCatalog.Definitions;
        Assert.Equal(defs.Length, defs.Select(d => d.Name).Distinct().Count());
        Assert.Equal(defs.Length, defs.Select(d => d.SortOrder).Distinct().Count());
        Assert.All(defs, d =>
        {
            Assert.Contains(d.Domain, RoleCatalog.Domains.All);
            Assert.Contains(d.Category, RoleCatalog.Categories.All);
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));
            Assert.DoesNotContain(d.Name, new[] { "", " " });
        });
        // همه‌ی نقش‌های SystemRoles و MNT/OPS در کاتالوگ هستند
        foreach (var n in SystemRoles.All.Concat(WorkOrderGovernanceSeeder.MaintenanceRoles).Append(WorkOrderGovernanceSeeder.OperationsRole))
            Assert.NotNull(RoleCatalog.Find(n));
    }

    [Fact]
    public void NewRoles_NeverCombineCreateAndApproveOnOneModule_SeparationByDesign()
    {
        foreach (var d in RoleCatalog.Definitions.Where(x => x.DefaultPermissions != null))
            Assert.DoesNotContain(d.DefaultPermissions!, g => g.Create && g.Approve);
    }

    [Fact]
    public void NewRoles_ContainNoAdministrativeOrWorkflowManagementModules()
    {
        var forbidden = new[] { "WorkflowManagement", "Users", "Roles", "PermissionMatrix", "Setup", "WorkOrder", "Permit" };
        foreach (var d in RoleCatalog.Definitions.Where(x => x.DefaultPermissions != null))
            Assert.DoesNotContain(d.DefaultPermissions!, g => forbidden.Contains(g.Module));
    }

    // ---------------- Seeding: تازه / دوباره / موجود ----------------

    [Fact]
    public async Task FreshDatabase_CreatesAllBaselineRoles_WithMetadata()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_fresh");

        var result = await RoleCatalogSeeder.SeedAsync(ctx);

        Assert.Equal(BaselineCount, result.CreatedRoles.Count);
        var roles = await ctx.Roles.ToListAsync();
        Assert.Equal(BaselineCount, roles.Count);
        Assert.All(roles, r =>
        {
            Assert.True(r.IsSystemRole);
            Assert.False(string.IsNullOrWhiteSpace(r.Domain));
            Assert.False(string.IsNullOrWhiteSpace(r.Category));
            Assert.False(string.IsNullOrWhiteSpace(r.DisplayName));
            Assert.True(r.SortOrder > 0);
        });
        Assert.Contains(roles, r => r.Name == RoleCatalog.WarehouseStorekeeper);
        Assert.Contains(roles, r => r.Name == RoleCatalog.WarehouseManager);
    }

    [Fact]
    public async Task SecondRun_IsIdempotent_NoDuplicatesNoChanges()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_idem");
        await RoleCatalogSeeder.SeedAsync(ctx);
        var before = await ctx.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Name, r.DisplayName, r.Description, r.Domain, r.Category, r.SortOrder }).ToListAsync();
        var rpBefore = await ctx.RolePermissions.CountAsync();
        var navBefore = await ctx.NavigationVisibilityOverrides.CountAsync();

        var second = await RoleCatalogSeeder.SeedAsync(ctx);

        Assert.Empty(second.CreatedRoles);
        Assert.Empty(second.BackfilledRoles);
        Assert.Equal(before, await ctx.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Name, r.DisplayName, r.Description, r.Domain, r.Category, r.SortOrder }).ToListAsync());
        Assert.Equal(rpBefore, await ctx.RolePermissions.CountAsync());
        Assert.Equal(navBefore, await ctx.NavigationVisibilityOverrides.CountAsync());
        Assert.Equal(BaselineCount, await ctx.Roles.CountAsync());
    }

    [Fact]
    public async Task ExistingDatabase_AddsMissingRoles_BackfillsOnlyEmptyMetadata_PreservesAdminEdits()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_existing");
        ctx.Roles.Add(new ApplicationRole(SystemRoles.QAManager) { NormalizedName = "QAMANAGER", DisplayName = "مدیر کیفیت (ویرایش مدیر)", Description = "admin wrote this" });
        ctx.Roles.Add(new ApplicationRole("MNT.Manager") { NormalizedName = "MNT.MANAGER" }); // مثل DB زنده: DisplayName/Description خالی
        await ctx.SaveChangesAsync();

        var result = await RoleCatalogSeeder.SeedAsync(ctx);

        Assert.DoesNotContain("QAManager", result.CreatedRoles);
        Assert.Equal(BaselineCount - 2, result.CreatedRoles.Count);

        var qa = await ctx.Roles.SingleAsync(r => r.Name == SystemRoles.QAManager);
        Assert.Equal("مدیر کیفیت (ویرایش مدیر)", qa.DisplayName);     // ویرایش مدیر بازنویسی نشد
        Assert.Equal("admin wrote this", qa.Description);
        Assert.Equal(RoleCatalog.Domains.Quality, qa.Domain);          // فقط خالی‌ها پر شد

        var mnt = await ctx.Roles.SingleAsync(r => r.Name == "MNT.Manager");
        Assert.Equal("Maintenance Manager", mnt.DisplayName);          // قبلاً null بود
        Assert.True(mnt.IsSystemRole);
    }

    [Fact]
    public async Task CustomRole_IsUntouched_AndReportedWhenLookingLikeBaseline()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_custom");
        ctx.Roles.Add(new ApplicationRole("MyPlantRole") { NormalizedName = "MYPLANTROLE", DisplayName = "My Plant Role" });
        ctx.Roles.Add(new ApplicationRole("Maintenance Manager") { NormalizedName = "MAINTENANCE MANAGER" });
        ctx.Roles.Add(new ApplicationRole("ENGINEERING_MANAGER") { NormalizedName = "ENGINEERING_MANAGER" });
        await ctx.SaveChangesAsync();

        var result = await RoleCatalogSeeder.SeedAsync(ctx);

        var plant = await ctx.Roles.SingleAsync(r => r.Name == "MyPlantRole");
        Assert.False(plant.IsSystemRole);
        Assert.Null(plant.Domain);
        Assert.Null(plant.Description);
        Assert.Equal(3, await ctx.Roles.CountAsync(r => !r.IsSystemRole));   // هیچ نقش سفارشی تبدیل/ادغام نشد

        Assert.Contains(result.PossibleDuplicates, d => d.Custom == "Maintenance Manager" && d.Baseline == "MNT.Manager");
        Assert.Contains(result.PossibleDuplicates, d => d.Custom == "ENGINEERING_MANAGER" && d.Baseline == SystemRoles.EngineeringManager);
        Assert.DoesNotContain(result.PossibleDuplicates, d => d.Custom == "MyPlantRole");
    }

    [Theory]
    [InlineData("wh.manager", "WH.MANAGER", "WH.Manager")]
    [InlineData("Mnt.Expert", "MNT.EXPERT", "MNT.Expert")]
    [InlineData("ops.issueauthority", "OPS.ISSUEAUTHORITY", "OPS.IssueAuthority")]
    public async Task CaseVariantCustomRole_IsAdoptedNotDuplicated_AndGetsNoDefaultGrants(string existingName, string normalized, string baseline)
    {
        // AspNetRoles دارای ایندکس یکتا روی NormalizedName است؛ INSERT تکراری باعث شکست Startup می‌شد
        var ctx = PemsDbContextFactory.CreateInMemory($"RoleCatalog_case_{existingName}");
        ctx.Roles.Add(new ApplicationRole(existingName) { NormalizedName = normalized });
        await ctx.SaveChangesAsync();

        var result = await RoleCatalogSeeder.SeedAsync(ctx);
        var again = await RoleCatalogSeeder.SeedAsync(ctx);

        Assert.DoesNotContain(baseline, result.CreatedRoles);
        Assert.Empty(again.CreatedRoles);
        Assert.Equal(1, await ctx.Roles.CountAsync(r => r.NormalizedName == normalized));      // نقش تکراری ساخته نشد
        Assert.Equal(BaselineCount, await ctx.Roles.CountAsync());
        Assert.Empty(await ctx.RolePermissions.Where(r => r.RoleName == baseline).ToListAsync()); // نقش موجود Grant پیش‌فرض نمی‌گیرد
        var adoptedId = (await ctx.Roles.SingleAsync(r => r.NormalizedName == normalized)).Id;
        Assert.Empty(await ctx.NavigationVisibilityOverrides.Where(o => o.RoleId == adoptedId).ToListAsync());
        Assert.True((await ctx.Roles.SingleAsync(r => r.Id == adoptedId)).IsSystemRole);          // نقش پایه محسوب و محافظت می‌شود
    }

    [Fact]
    public async Task ExistingUsersAndAssignments_AreNotTouched_AndNoUserIsAddedToAnyNewRole()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_users");
        var admin = new ApplicationRole(SystemRoles.SystemAdmin) { NormalizedName = "SYSTEMADMIN" };
        ctx.Roles.Add(admin);
        ctx.Users.Add(new ApplicationUser { Id = 1, UserName = "admin" });
        await ctx.SaveChangesAsync();
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = 1, RoleId = admin.Id });
        await ctx.SaveChangesAsync();

        await RoleCatalogSeeder.SeedAsync(ctx);
        await RoleCatalogSeeder.SeedAsync(ctx);

        var assignments = await ctx.UserRoles.ToListAsync();
        Assert.Single(assignments);
        Assert.Equal(admin.Id, assignments[0].RoleId);
        Assert.Empty(await ctx.RolePermissions.Where(r => r.RoleName == SystemRoles.SystemAdmin).ToListAsync()); // SystemAdmin بدون ردیف ماتریس (طراحی موجود)
    }

    [Fact]
    public async Task ExistingPermissions_AreNotChanged_ByCatalogSeeding()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_perms");
        var access = new AccessControlService(ctx);
        await new PermissionService(ctx, access).SeedDefaultsAsync();
        var before = await ctx.RolePermissions.AsNoTracking().OrderBy(r => r.RolePermissionId)
            .Select(r => new { r.RolePermissionId, r.RoleName, r.ModuleName, r.CanView, r.CanCreate, r.CanEdit, r.CanDelete, r.CanApprove }).ToListAsync();

        await RoleCatalogSeeder.SeedAsync(ctx);

        var after = await ctx.RolePermissions.AsNoTracking().Where(r => before.Select(b => b.RolePermissionId).Contains(r.RolePermissionId))
            .OrderBy(r => r.RolePermissionId)
            .Select(r => new { r.RolePermissionId, r.RoleName, r.ModuleName, r.CanView, r.CanCreate, r.CanEdit, r.CanDelete, r.CanApprove }).ToListAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task NewRolePermissions_AreSeededOnlyOnCreation_AdminCustomizationSurvivesReseed()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RoleCatalog_customize");
        await RoleCatalogSeeder.SeedAsync(ctx);

        var store = RoleCatalog.WarehouseStorekeeper;
        var rows = await ctx.RolePermissions.Where(r => r.RoleName == store).ToListAsync();
        Assert.Equal(RoleCatalog.Find(store)!.DefaultPermissions!.Length, rows.Count);

        // مدیر یک ردیف را حذف و یکی را تغییر می‌دهد
        ctx.RolePermissions.Remove(rows.Single(r => r.ModuleName == "Barcode"));
        rows.Single(r => r.ModuleName == "StoreRequisition").CanEdit = false;
        await ctx.SaveChangesAsync();

        await RoleCatalogSeeder.SeedAsync(ctx);

        var after = await ctx.RolePermissions.Where(r => r.RoleName == store).ToListAsync();
        Assert.DoesNotContain(after, r => r.ModuleName == "Barcode");                  // حذفِ مدیر زنده نشد
        Assert.False(after.Single(r => r.ModuleName == "StoreRequisition").CanEdit);   // تغییر مدیر بازنویسی نشد
    }

    // ---------------- Authorization واقعی (Allow/Deny) ----------------

    [Fact]
    public async Task SCRUM547_The_warehouse_manager_resolves_holds_and_the_buyer_opens_the_IRN_but_neither_approves_QA()
    {
        var (_, perm, _) = await StartupLikeAsync(nameof(SCRUM547_The_warehouse_manager_resolves_holds_and_the_buyer_opens_the_IRN_but_neither_approves_QA));
        var manager = new[] { RoleCatalog.WarehouseManager };
        var buyer = new[] { RoleCatalog.ProcurementBuyer };

        Assert.True(await perm.HasPermissionAsync(manager, "IRN", "Edit"));     // Hold resolution
        Assert.False(await perm.HasPermissionAsync(manager, "IRN", "Create"));
        Assert.True(await perm.HasPermissionAsync(buyer, "IRN", "View"));       // close short / replacement decision
        Assert.False(await perm.HasPermissionAsync(buyer, "IRN", "Edit"));
        foreach (var role in new[] { manager, buyer })
            Assert.False(await perm.HasPermissionAsync(role, "IRN", "Approve"));   // QA approval stays with QAManager
    }

    [Fact]
    public void SCRUM547_Existing_databases_get_the_two_IRN_grants_once_through_a_data_migration()
    {
        var file = Directory.GetFiles(Path.Combine(PEMS.Tests.TestSupport.SupportedReleases.RepoRoot(), "PEMS.Migrations", "Migrations"), "*_Scrum547IrnDefaultGrants.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var sql = File.ReadAllText(file).Replace("\r\n", "\n");

        Assert.Contains("SET [CanView] = 1, [CanEdit] = 1\nWHERE [RoleName] = N'WH.Manager' AND [ModuleName] = N'IRN'", sql);
        Assert.Contains("SELECT N'PRC.Buyer', N'IRN', 1, 0, 0, 0, 0", sql);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM [RolePermissions] WHERE [RoleName] = N'PRC.Buyer' AND [ModuleName] = N'IRN')", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Storekeeper_And_WarehouseManager_HaveSeparatedAuthority_AndNothingElse()
    {
        var (_, perm, _) = await StartupLikeAsync(nameof(Storekeeper_And_WarehouseManager_HaveSeparatedAuthority_AndNothingElse));
        var keeper = new[] { RoleCatalog.WarehouseStorekeeper };
        var manager = new[] { RoleCatalog.WarehouseManager };

        // Storekeeper: ایجاد/ویرایش بله، تأیید خیر
        Assert.True(await perm.HasPermissionAsync(keeper, "StoreRequisition", "Create"));
        Assert.True(await perm.HasPermissionAsync(keeper, "MaterialItem", "Create"));
        Assert.True(await perm.HasPermissionAsync(keeper, "IRN", "Create"));
        Assert.False(await perm.HasPermissionAsync(keeper, "StoreRequisition", "Approve"));
        Assert.False(await perm.HasPermissionAsync(keeper, "IRN", "Approve"));   // QC جداست

        // Manager: تأیید بله، ایجاد خیر
        Assert.True(await perm.HasPermissionAsync(manager, "StoreRequisition", "Approve"));
        Assert.False(await perm.HasPermissionAsync(manager, "StoreRequisition", "Create"));
        Assert.False(await perm.HasPermissionAsync(manager, "IRN", "Approve"));

        // هیچ‌کدام: Work Order، Permit، مدیریت گردش‌کار، Setup، Contracts
        foreach (var role in new[] { keeper, manager })
        {
            Assert.False(await perm.HasPermissionAsync(role, "WorkOrder", "FinalApprove"));
            Assert.False(await perm.HasPermissionAsync(role, "WorkOrder", "Approve"));
            Assert.False(await perm.HasPermissionAsync(role, "WorkflowManagement", "Publish"));
            Assert.False(await perm.HasPermissionAsync(role, "Setup", "View"));
            Assert.False(await perm.HasPermissionAsync(role, "Contracts", "Approve"));
        }
    }

    [Fact]
    public async Task NoNonAdminRole_HoldsWorkflowManagementGrants_ManagerCannotBecomeSystemAdmin()
    {
        var (ctx, perm, _) = await StartupLikeAsync(nameof(NoNonAdminRole_HoldsWorkflowManagementGrants_ManagerCannotBecomeSystemAdmin));

        var wfHolders = await (from g in ctx.RolePermissionGrants
                               join p in ctx.Permissions on g.PermissionId equals p.PermissionId
                               join r in ctx.Roles on g.RoleId equals r.Id
                               where g.IsActive && p.ResourceName == "WorkflowManagement"
                               select r.Name).Distinct().ToListAsync();
        Assert.DoesNotContain(wfHolders, n => n != SystemRoles.SystemAdmin);

        // مدیران دامنه هرگز نقش SystemAdmin نیستند و بایپس ندارند
        foreach (var manager in new[] { "MNT.Manager", SystemRoles.EngineeringManager, SystemRoles.ContractManager, RoleCatalog.WarehouseManager })
            Assert.False(await perm.HasPermissionAsync(new[] { manager }, "WorkflowManagement", "Publish"));
    }

    [Fact]
    public async Task NewRoles_AreWiredToNavigation_InventoryVisible_CmmsAndAdminHidden()
    {
        var (ctx, _, access) = await StartupLikeAsync(nameof(NewRoles_AreWiredToNavigation_InventoryVisible_CmmsAndAdminHidden));
        var resolver = new NavigationVisibilityResolver(new NavigationVisibilityOverrideService(ctx), access);

        foreach (var role in new[] { RoleCatalog.WarehouseStorekeeper, RoleCatalog.WarehouseManager })
        {
            var snap = await resolver.ResolveAsync(new[] { role });
            Assert.True(snap.IsModuleVisible("Inventory"));
            Assert.False(snap.IsModuleVisible("CmmsIntegration"));
            Assert.False(snap.IsModuleVisible("Administration"));
            Assert.False(snap.IsModuleVisible("ContractManagement"));
        }
    }
}
