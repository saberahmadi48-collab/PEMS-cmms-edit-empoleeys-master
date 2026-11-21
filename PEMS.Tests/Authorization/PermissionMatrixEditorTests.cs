using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Authorization;

/// <summary>
/// ذخیره‌ی خودکار Permission Matrix (Admin/PermissionMatrix/SetCells): تغییر خانه‌ها باید روی سطر
/// RolePermission و Grant ماژول اثر بگذارد (AccessControlService همان را می‌بیند)، فقط خانه‌های درخواست
/// را عوض کند، و ورودی نامعتبر کل درخواست را بدون هیچ تغییر نسبی رد کند.
/// </summary>
public class PermissionMatrixEditorTests
{
    private static readonly string[] Modules = { "WorkOrder", "StoreRequisition", "ReplenishmentRecommendation" };

    private static async Task<(PEMSDbContext ctx, PermissionMatrixEditor editor, AccessControlService access)> SeedAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory(dbName);

        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var access = new AccessControlService(ctx);
        await new PermissionService(ctx, access).SeedDefaultsAsync();

        var provider = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);

        return (ctx, new PermissionMatrixEditor(ctx), access);
    }

    private static PermissionCellChange Cell(string module, string action, bool enabled) =>
        new() { ModuleName = module, Action = action, Enabled = enabled };

    [Fact]
    public async Task SingleCell_Enable_UpdatesRowGrantAndDecision()
    {
        var (ctx, editor, access) = await SeedAsync(nameof(SingleCell_Enable_UpdatesRowGrantAndDecision));
        var roles = new[] { SystemRoles.MaintenanceEngineer };
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);

        var result = await editor.ApplyAsync(SystemRoles.MaintenanceEngineer, new[] { Cell("WorkOrder", "Delete", true) }, Modules, "tester");

        Assert.True(result.Success);
        Assert.True(result.Modules.Single().Flags["Delete"]);
        Assert.True((await ctx.RolePermissions.SingleAsync(x => x.RoleName == SystemRoles.MaintenanceEngineer && x.ModuleName == "WorkOrder")).CanDelete);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Delete")).Allowed);
    }

    [Fact]
    public async Task SingleCell_Disable_OnlyChangesThatCell()
    {
        var (_, editor, access) = await SeedAsync(nameof(SingleCell_Disable_OnlyChangesThatCell));
        var roles = new[] { SystemRoles.MaintenanceEngineer };

        var result = await editor.ApplyAsync(SystemRoles.MaintenanceEngineer, new[] { Cell("WorkOrder", "Create", false) }, Modules, "tester");

        Assert.True(result.Success);
        Assert.False((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "View")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Edit")).Allowed);
        // another role is untouched
        Assert.True((await access.AuthorizeForRolesAsync(new[] { SystemRoles.EngineeringManager }, "WorkOrder", "Create")).Allowed);
    }

    [Fact]
    public async Task RowBatch_AppliesManyCellsAcrossModules_InOneRequest()
    {
        var (_, editor, access) = await SeedAsync(nameof(RowBatch_AppliesManyCellsAcrossModules_InOneRequest));
        var roles = new[] { SystemRoles.Viewer };

        var result = await editor.ApplyAsync(SystemRoles.Viewer, new[]
        {
            Cell("WorkOrder", "Create", true), Cell("WorkOrder", "Edit", true), Cell("StoreRequisition", "Approve", true)
        }, Modules, "tester");

        Assert.True(result.Success);
        Assert.Equal(2, result.Modules.Count);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Create")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "WorkOrder", "Edit")).Allowed);
        Assert.True((await access.AuthorizeForRolesAsync(roles, "StoreRequisition", "Approve")).Allowed);
    }

    [Fact]
    public async Task LastValueWins_ForTheSameCell()
    {
        var (_, editor, access) = await SeedAsync(nameof(LastValueWins_ForTheSameCell));

        await editor.ApplyAsync(SystemRoles.Viewer, new[] { Cell("WorkOrder", "Create", true), Cell("WorkOrder", "Create", false) }, Modules, "tester");

        Assert.False((await access.AuthorizeForRolesAsync(new[] { SystemRoles.Viewer }, "WorkOrder", "Create")).Allowed);
    }

    [Fact]
    public async Task ModuleWithoutAnyRow_GetsARowAndPermissions()
    {
        var (ctx, editor, access) = await SeedAsync(nameof(ModuleWithoutAnyRow_GetsARowAndPermissions));
        Assert.False(await ctx.RolePermissions.AnyAsync(x => x.ModuleName == "ReplenishmentRecommendation" && x.RoleName == SystemRoles.Viewer));

        var result = await editor.ApplyAsync(SystemRoles.Viewer, new[] { Cell("ReplenishmentRecommendation", "View", true) }, Modules, "tester");

        Assert.True(result.Success);
        Assert.True((await access.AuthorizeForRolesAsync(new[] { SystemRoles.Viewer }, "ReplenishmentRecommendation", "View")).Allowed);
        Assert.False((await access.AuthorizeForRolesAsync(new[] { SystemRoles.Viewer }, "ReplenishmentRecommendation", "Create")).Allowed);
    }

    [Fact]
    public async Task ConfiguredModules_CountsModulesWithAnyPermission()
    {
        var (ctx, editor, _) = await SeedAsync(nameof(ConfiguredModules_CountsModulesWithAnyPermission));
        var before = await ctx.RolePermissions.CountAsync(x => x.RoleName == SystemRoles.Viewer
            && (x.CanView || x.CanCreate || x.CanEdit || x.CanDelete || x.CanApprove || x.CanManualNumber || x.CanFreeze || x.CanUnfreeze || x.CanHold || x.CanReleaseHold));

        var result = await editor.ApplyAsync(SystemRoles.Viewer, new[] { Cell("ReplenishmentRecommendation", "View", true) }, Modules, "tester");

        Assert.Equal(before + 1, result.ConfiguredModules);
    }

    [Fact]
    public async Task Disable_SurvivesARestartSync()
    {
        var (ctx, editor, access) = await SeedAsync(nameof(Disable_SurvivesARestartSync));

        await editor.ApplyAsync(SystemRoles.MaintenanceEngineer, new[] { Cell("WorkOrder", "Create", false) }, Modules, "tester");
        var provider = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);

        Assert.False((await access.AuthorizeForRolesAsync(new[] { SystemRoles.MaintenanceEngineer }, "WorkOrder", "Create")).Allowed);
    }

    [Theory]
    [InlineData("SystemAdmin", "WorkOrder", "View", "SystemAdminExcluded")]
    [InlineData("No.Such.Role", "WorkOrder", "View", "UnknownRole")]
    [InlineData("Viewer", "NotInTheMatrix", "View", "UnknownModule")]
    [InlineData("Viewer", "WorkOrder", "Explode", "InvalidCell")]
    public async Task InvalidInput_IsRejected_WithoutAnyChange(string role, string module, string action, string expectedError)
    {
        var (ctx, editor, _) = await SeedAsync("invalid_" + expectedError);
        var rowsBefore = await ctx.RolePermissions.CountAsync();
        var grantsBefore = await ctx.RolePermissionGrants.CountAsync(g => g.IsActive);

        var result = await editor.ApplyAsync(role, new[] { Cell(module, action, true) }, Modules, "tester");

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(rowsBefore, await ctx.RolePermissions.CountAsync());
        Assert.Equal(grantsBefore, await ctx.RolePermissionGrants.CountAsync(g => g.IsActive));
    }

    [Fact]
    public async Task OneInvalidCell_RejectsTheWholeRequest_NoPartialApply()
    {
        var (_, editor, access) = await SeedAsync(nameof(OneInvalidCell_RejectsTheWholeRequest_NoPartialApply));

        var result = await editor.ApplyAsync(SystemRoles.Viewer,
            new[] { Cell("WorkOrder", "Create", true), Cell("Bogus", "View", true) }, Modules, "tester");

        Assert.False(result.Success);
        Assert.False((await access.AuthorizeForRolesAsync(new[] { SystemRoles.Viewer }, "WorkOrder", "Create")).Allowed);
    }
}
