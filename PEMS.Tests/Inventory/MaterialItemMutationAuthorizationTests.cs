using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Warehouse.Controllers;

namespace PEMS.Tests.Inventory;

/// <summary>
/// P0 / F-04 - هر اقدام تغییردهنده‌ی MaterialItemController باید در سمت سرور مجوز داشته باشد (نه فقط دکمه‌ی مخفی):
/// Create ← MaterialItem.Create ؛ Edit/ToggleActive/AdjustStock/TransferStock ← MaterialItem.Edit ؛ (ReceiveStock از قبل MaterialItem.Create).
/// </summary>
public class MaterialItemMutationAuthorizationTests
{
    private static async Task<(MaterialItemController Controller, PEMSDbContext Ctx, FakePermissionService Perm)> CreateAsync(string db, bool allow = true)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"MatP0_{db}");
        ctx.Warehouses.Add(new Warehouse { WarehouseId = 1, Code = "WH-01", Name = "Main", IsActive = true });
        ctx.Warehouses.Add(new Warehouse { WarehouseId = 2, Code = "WH-02", Name = "Second", IsActive = true });
        ctx.MaterialItems.Add(new MaterialItem { MaterialItemId = 1, MaterialCode = "MAT-001", Description = "Widget", IsActive = true, IsStockItem = true });
        ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = 1, WarehouseId = 1, OnHandQuantity = 10, ReservedQuantity = 0, IsActive = true });
        await ctx.SaveChangesAsync();

        var perm = new FakePermissionService { AllowAll = allow };
        var user = new FakeCurrentUserService { UserId = 1, Roles = new[] { "MaintenanceEngineer" } };
        var controller = new MaterialItemController(ctx, new MaterialItemService(ctx), new MaterialStockService(ctx), user, perm, null!).WithTempData();
        return (controller, ctx, perm);
    }

    private static async Task AssertStockUntouchedAsync(PEMSDbContext ctx)
    {
        Assert.Equal(10, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
        Assert.Equal(0, await ctx.InventoryTransactions.CountAsync());
    }

    // --------------------------------------------------------------- منفی: بدون مجوز
    [Fact]
    public async Task AdjustStock_WithoutPermission_IsForbidden_AndStockIsUntouched()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(AdjustStock_WithoutPermission_IsForbidden_AndStockIsUntouched), allow: false);

        var result = await controller.AdjustStock(1, 1, 9999, "forged adjustment");

        Assert.IsType<ForbidResult>(result);
        await AssertStockUntouchedAsync(ctx);
    }

    [Fact]
    public async Task TransferStock_WithoutPermission_IsForbidden_AndStockIsUntouched()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(TransferStock_WithoutPermission_IsForbidden_AndStockIsUntouched), allow: false);

        var result = await controller.TransferStock(1, 1, 2, 5, "forged transfer");

        Assert.IsType<ForbidResult>(result);
        await AssertStockUntouchedAsync(ctx);
        Assert.Equal(1, await ctx.MaterialStocks.CountAsync());
    }

    [Fact]
    public async Task Create_WithoutPermission_IsForbidden_AndCreatesNoMaterial()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(Create_WithoutPermission_IsForbidden_AndCreatesNoMaterial), allow: false);

        var result = await controller.Create("MAT-NEW", "New", null, null, null, null, null, null, null, null, null, null, null, null, true, false);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(1, await ctx.MaterialItems.CountAsync());
    }

    [Fact]
    public async Task Edit_WithoutPermission_IsForbidden_AndChangesNothing()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(Edit_WithoutPermission_IsForbidden_AndChangesNothing), allow: false);

        var result = await controller.Edit(1, "MAT-001", "Hijacked", null, null, null, null, null, null, null, null, null, null, null, null, true, false);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Widget", (await ctx.MaterialItems.SingleAsync()).Description);
    }

    [Fact]
    public async Task ToggleActive_WithoutPermission_IsForbidden_AndChangesNothing()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(ToggleActive_WithoutPermission_IsForbidden_AndChangesNothing), allow: false);

        var result = await controller.ToggleActive(1);

        Assert.IsType<ForbidResult>(result);
        Assert.True((await ctx.MaterialItems.SingleAsync()).IsActive);
    }

    // --------------------------------------------------------------- هر اقدام دقیقاً مجوز درست را می‌خواهد
    [Fact]
    public async Task StockMutations_RequireMaterialItemEdit_NotJustAnyPermission()
    {
        var (controller, ctx, perm) = await CreateAsync(nameof(StockMutations_RequireMaterialItemEdit_NotJustAnyPermission));
        perm.Denied.Add(("MaterialItem", "Edit"));        // Create هنوز مجاز است

        Assert.IsType<ForbidResult>(await controller.AdjustStock(1, 1, 99, null));
        Assert.IsType<ForbidResult>(await controller.TransferStock(1, 1, 2, 1, null));
        Assert.IsType<ForbidResult>(await controller.ToggleActive(1));
        Assert.IsType<ForbidResult>(await controller.Edit(1, "MAT-001", "x", null, null, null, null, null, null, null, null, null, null, null, null, true, false));
        await AssertStockUntouchedAsync(ctx);
    }

    [Fact]
    public async Task Create_RequiresMaterialItemCreate()
    {
        var (controller, ctx, perm) = await CreateAsync(nameof(Create_RequiresMaterialItemCreate));
        perm.Denied.Add(("MaterialItem", "Create"));

        Assert.IsType<ForbidResult>(await controller.Create("MAT-NEW", "New", null, null, null, null, null, null, null, null, null, null, null, null, true, false));
        Assert.Equal(1, await ctx.MaterialItems.CountAsync());
    }

    // --------------------------------------------------------------- مثبت: با مجوز رفتار قبلی حفظ می‌شود
    [Fact]
    public async Task AdjustStock_WithPermission_StillAdjustsAndWritesTheLedger()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(AdjustStock_WithPermission_StillAdjustsAndWritesTheLedger));

        var result = await controller.AdjustStock(1, 1, 25, "cycle count");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(25, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
        Assert.Equal(1, await ctx.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task TransferStock_WithPermission_StillMovesTheQuantity()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(TransferStock_WithPermission_StillMovesTheQuantity));

        var result = await controller.TransferStock(1, 1, 2, 4, "rebalance");

        Assert.IsType<RedirectToActionResult>(result);
        var stocks = await ctx.MaterialStocks.ToListAsync();
        Assert.Equal(6, stocks.Single(s => s.WarehouseId == 1).OnHandQuantity);
        Assert.Equal(4, stocks.Single(s => s.WarehouseId == 2).OnHandQuantity);
    }

    [Fact]
    public async Task ToggleActive_WithPermission_StillToggles()
    {
        var (controller, ctx, _) = await CreateAsync(nameof(ToggleActive_WithPermission_StillToggles));

        var result = await controller.ToggleActive(1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False((await ctx.MaterialItems.SingleAsync()).IsActive);
    }

    // --------------------------------------------------------------- موتور مجوز واقعی (نه Fake): نقش‌های واقعی PEMS
    [Theory]
    [InlineData(SystemRoles.Viewer, false)]
    [InlineData(SystemRoles.DisciplineEngineer, false)]
    [InlineData(SystemRoles.MaintenanceEngineer, true)]
    [InlineData(SystemRoles.EngineeringManager, true)]
    public async Task AdjustStock_WithTheRealPermissionEngine_FollowsTheSeededMaterialItemEditGrant(string role, bool allowed)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"MatP0Real_{role}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var access = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, access);
        await permissions.SeedDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());
        ctx.Warehouses.Add(new Warehouse { WarehouseId = 1, Code = "WH-01", Name = "Main", IsActive = true });
        ctx.MaterialItems.Add(new MaterialItem { MaterialItemId = 1, MaterialCode = "MAT-001", Description = "Widget", IsActive = true, IsStockItem = true });
        ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = 1, WarehouseId = 1, OnHandQuantity = 10, IsActive = true });
        await ctx.SaveChangesAsync();

        var user = new FakeCurrentUserService { UserId = 1, Roles = new[] { role } };
        var controller = new MaterialItemController(ctx, new MaterialItemService(ctx), new MaterialStockService(ctx), user, permissions, null!).WithTempData();

        var result = await controller.AdjustStock(1, 1, 77, "real engine check");

        if (allowed) Assert.IsType<RedirectToActionResult>(result);
        else Assert.IsType<ForbidResult>(result);
        Assert.Equal(allowed ? 77 : 10, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
    }
}
