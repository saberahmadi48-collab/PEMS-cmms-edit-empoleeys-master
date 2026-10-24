using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Authorization;

/// <summary>
/// تب Effective در Admin/Users/Details مجوز را «به‌ازای کاربر» ذخیره نمی‌کند؛ مجوز مؤثر از Grant نقش‌ها
/// می‌آید. این تست‌ها ثابت می‌کنند تغییر روی Grant نقش(ها) اعمال می‌شود، AccessControlService همان را
/// می‌بیند، سطر Legacy (RolePermission) هم‌گام می‌ماند، و مجوزهای غیرقابل‌ویرایش (SystemAdmin، خارج
/// از Matrix، RoleModuleAction) دست‌نخورده می‌مانند.
/// </summary>
public class RolePermissionAdminServiceTests
{
    private static async Task<(PEMSDbContext ctx, RolePermissionAdminService admin, AccessControlService access)> SeedAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory(dbName);

        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var access = new AccessControlService(ctx);
        await new PermissionService(ctx, access).SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase3PermissionsAsync(provider);

        return (ctx, new RolePermissionAdminService(ctx), access);
    }

    private static PermissionToggleRequest Disable(string name) => new() { PermissionName = name, Enable = false };
    private static PermissionToggleRequest Enable(string name) => new() { PermissionName = name, Enable = true };

    [Fact]
    public async Task Disable_RemovesAccessFromTheRole_AndKeepsOtherRolesIntact()
    {
        var (ctx, admin, access) = await SeedAsync(nameof(Disable_RemovesAccessFromTheRole_AndKeepsOtherRolesIntact));
        var roles = new[] { SystemRoles.MaintenanceEngineer };

        var result = await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Create") }, "tester");

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(new[] { SystemRoles.MaintenanceEngineer }, result.AffectedRoles);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(new[] { SystemRoles.EngineeringManager }, "WorkOrder", "Create")).Allowed);

        var legacy = await ctx.RolePermissions.SingleAsync(x => x.RoleName == SystemRoles.MaintenanceEngineer && x.ModuleName == "WorkOrder");
        Assert.False(legacy.CanCreate);
        Assert.True(legacy.CanView); // untouched sibling
    }

    [Fact]
    public async Task Enable_GrantsToTheOnlyRole_AndMirrorsLegacyRow()
    {
        var (ctx, admin, access) = await SeedAsync(nameof(Enable_GrantsToTheOnlyRole_AndMirrorsLegacyRow));
        var roles = new[] { SystemRoles.MaintenanceEngineer };
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);

        var result = await admin.ApplyAsync(roles, null, new[] { Enable("WorkOrder.Delete") }, "tester");

        Assert.Equal(1, result.AppliedCount);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);
        Assert.True((await ctx.RolePermissions.SingleAsync(x => x.RoleName == SystemRoles.MaintenanceEngineer && x.ModuleName == "WorkOrder")).CanDelete);
    }

    [Fact]
    public async Task Disable_WithSeveralProvidingRoles_RemovesFromAllOfThem()
    {
        var (_, admin, access) = await SeedAsync(nameof(Disable_WithSeveralProvidingRoles_RemovesFromAllOfThem));
        var roles = new[] { SystemRoles.MaintenanceEngineer, SystemRoles.EngineeringManager };
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);

        var result = await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Create") }, "tester");

        Assert.Equal(2, result.AffectedRoles.Count);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
    }

    [Fact]
    public async Task Enable_WithSeveralRoles_RequiresTargetRole_ThenGrantsOnlyThatRole()
    {
        var (_, admin, access) = await SeedAsync(nameof(Enable_WithSeveralRoles_RequiresTargetRole_ThenGrantsOnlyThatRole));
        var roles = new[] { SystemRoles.MaintenanceEngineer, SystemRoles.Viewer };
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);

        var withoutTarget = await admin.ApplyAsync(roles, null, new[] { Enable("WorkOrder.Delete") }, "tester");
        Assert.Equal(0, withoutTarget.AppliedCount);
        Assert.Equal(PermissionChangeReasons.TargetRoleRequired, withoutTarget.Items.Single().Reason);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);

        var withTarget = await admin.ApplyAsync(roles, SystemRoles.Viewer, new[] { Enable("WorkOrder.Delete") }, "tester");
        Assert.Equal(new[] { SystemRoles.Viewer }, withTarget.AffectedRoles);
        Assert.True((await access.AuthorizeForRolesAsync(new[] { SystemRoles.Viewer }, "WorkOrder", "Delete")).Allowed);
        Assert.False((await access.AuthorizeForRolesAsync(new[] { SystemRoles.MaintenanceEngineer }, "WorkOrder", "Delete")).Allowed);
    }

    [Fact]
    public async Task AlreadyInRequestedState_ReportsNoChange_AndAffectsNoRole()
    {
        var (_, admin, _) = await SeedAsync(nameof(AlreadyInRequestedState_ReportsNoChange_AndAffectsNoRole));
        var roles = new[] { SystemRoles.MaintenanceEngineer };

        var enableAllowed = await admin.ApplyAsync(roles, null, new[] { Enable("WorkOrder.Create") }, "tester");
        var disableDenied = await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Delete") }, "tester");

        Assert.All(new[] { enableAllowed, disableDenied }, r =>
        {
            Assert.Equal(0, r.AppliedCount);
            Assert.Empty(r.AffectedRoles);
            Assert.Equal(PermissionChangeReasons.NoChange, r.Items.Single().Reason);
        });
    }

    [Fact]
    public async Task PermissionOutsideTheMatrix_IsNotEditable_AndStaysGranted()
    {
        var (_, admin, access) = await SeedAsync(nameof(PermissionOutsideTheMatrix_IsNotEditable_AndStaysGranted));
        var roles = new[] { SystemRoles.EngineeringManager };
        Assert.True((await access.AuthorizeForRolesAsync(roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed);

        var notEditable = await admin.GetNonEditableAsync(roles);
        var result = await admin.ApplyAsync(roles, null, new[] { Disable("EngineeringDocuments.ViewAllDisciplines") }, "tester");

        Assert.Equal(PermissionChangeReasons.OutsideMatrix, notEditable["EngineeringDocuments.ViewAllDisciplines"]);
        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(PermissionChangeReasons.OutsideMatrix, result.Items.Single().Reason);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed);
    }

    [Fact]
    public async Task PermissionFromRoleModuleAction_IsNotEditable_BecauseTheSeederWouldRestoreIt()
    {
        var (ctx, admin, access) = await SeedAsync(nameof(PermissionFromRoleModuleAction_IsNotEditable_BecauseTheSeederWouldRestoreIt));
        var roles = new[] { SystemRoles.MaintenanceEngineer };
        ctx.RoleModuleActions.Add(new RoleModuleAction { RoleName = SystemRoles.MaintenanceEngineer, Module = "WorkOrder", Action = "Hold", CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();
        var provider = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Hold")).Allowed);

        var notEditable = await admin.GetNonEditableAsync(roles);
        var result = await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Hold") }, "tester");

        Assert.Equal(PermissionChangeReasons.RoleModuleAction, notEditable["WorkOrder.Hold"]);
        Assert.Equal(0, result.AppliedCount);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Hold")).Allowed);
    }

    [Fact]
    public async Task SystemAdmin_IsEntirelyReadOnly()
    {
        var (_, admin, _) = await SeedAsync(nameof(SystemAdmin_IsEntirelyReadOnly));
        var roles = new[] { SystemRoles.SystemAdmin };

        var notEditable = await admin.GetNonEditableAsync(roles);
        var result = await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Create") }, "tester");

        Assert.NotEmpty(notEditable);
        Assert.All(notEditable.Values, v => Assert.Equal(PermissionChangeReasons.SystemAdmin, v));
        Assert.Equal(0, result.AppliedCount);
    }

    [Fact]
    public async Task UnknownPermission_IsRejected_WithoutCreatingAnything()
    {
        var (ctx, admin, _) = await SeedAsync(nameof(UnknownPermission_IsRejected_WithoutCreatingAnything));
        var before = await ctx.RolePermissionGrants.CountAsync();

        var result = await admin.ApplyAsync(new[] { SystemRoles.MaintenanceEngineer }, null, new[] { Enable("Nope.Create") }, "tester");

        Assert.Equal(PermissionChangeReasons.Unknown, result.Items.Single().Reason);
        Assert.Equal(before, await ctx.RolePermissionGrants.CountAsync());
        Assert.False(await ctx.Permissions.AnyAsync(p => p.Name == "Nope.Create"));
    }

    [Fact]
    public async Task BatchChange_AppliesEveryRow_InOneRequest()
    {
        var (_, admin, access) = await SeedAsync(nameof(BatchChange_AppliesEveryRow_InOneRequest));
        var roles = new[] { SystemRoles.MaintenanceEngineer };

        var result = await admin.ApplyAsync(roles, null,
            new[] { Disable("WorkOrder.Create"), Disable("WorkOrder.Edit"), Enable("WorkOrder.Delete") }, "tester");

        Assert.Equal(3, result.AppliedCount);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Edit")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);
    }

    [Fact]
    public async Task NewLegacyRow_PreservesExistingGrants_SoARestartSyncDoesNotRevokeThem()
    {
        var (ctx, admin, access) = await SeedAsync(nameof(NewLegacyRow_PreservesExistingGrants_SoARestartSyncDoesNotRevokeThem));

        // a role/module that has grants but no RolePermission row (e.g. granted by another seeder)
        var role = new ApplicationRole("Test.CustomRole") { NormalizedName = "TEST.CUSTOMROLE" };
        ctx.Roles.Add(role);
        var permissions = new Dictionary<string, Permission>();
        foreach (var action in new[] { "View", "Create", "Edit", "Delete", "Approve", "ManualNumber", "Freeze", "Unfreeze", "Hold", "ReleaseHold" })
        {
            var p = new Permission { Name = $"ZModule.{action}", ResourceName = "ZModule", ActionName = action };
            permissions[action] = p;
            ctx.Permissions.Add(p);
        }
        await ctx.SaveChangesAsync();
        ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = role.Id, PermissionId = permissions["View"].PermissionId, IsActive = true });
        await ctx.SaveChangesAsync();
        Assert.False(await ctx.RolePermissions.AnyAsync(x => x.RoleName == "Test.CustomRole"));

        var roles = new[] { "Test.CustomRole" };
        var result = await admin.ApplyAsync(roles, null, new[] { Enable("ZModule.Create") }, "tester");
        Assert.Equal(1, result.AppliedCount);

        var legacy = await ctx.RolePermissions.SingleAsync(x => x.RoleName == "Test.CustomRole" && x.ModuleName == "ZModule");
        Assert.True(legacy.CanView);
        Assert.True(legacy.CanCreate);
        Assert.False(legacy.CanEdit);

        // what a Permission Matrix save or the next Startup does must keep both grants
        await AccessControlSeeder.SyncRoleFromRolePermissionsAsync(ctx, "Test.CustomRole");
        Assert.True((await access.AuthorizeForRolesAsync(roles, "ZModule", "View")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "ZModule", "Create")).Allowed);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "ZModule", "Edit")).Allowed);
    }

    [Fact]
    public async Task Disable_SurvivesARestartSync()
    {
        var (ctx, admin, access) = await SeedAsync(nameof(Disable_SurvivesARestartSync));
        var roles = new[] { SystemRoles.MaintenanceEngineer };

        await admin.ApplyAsync(roles, null, new[] { Disable("WorkOrder.Create") }, "tester");
        var provider = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);

        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
    }
}
