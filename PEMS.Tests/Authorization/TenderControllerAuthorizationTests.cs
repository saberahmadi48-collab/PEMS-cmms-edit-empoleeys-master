using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§H/§J، Phase 7.2/9): پوشش دروازه‌ی مجوز
/// برای TenderController تازه‌ساخته - منطق عمیق کسب‌وکار در TenderServiceTests پوشش داده شده.
/// </summary>
public class TenderControllerAuthorizationTests
{
    private static async Task<(TenderController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateController(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TenderCtrlAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { UserId = 1, Roles = role != null ? new List<string> { role } : new List<string>() };
        var service = new TenderService(ctx, new FakeNumberingService());
        var packageService = new ProcurementPackageService(ctx);
        var controller = new TenderController(service, packageService, permissionService, currentUser, ctx, accessControl).WithTempData();

        return (controller, ctx);
    }

    [Fact]
    public async Task Index_WithoutViewPermission_ReturnsForbid()
    {
        var (controller, _) = await CreateController(nameof(Index_WithoutViewPermission_ReturnsForbid), role: null);

        var result = await controller.Index(null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbid_DoesNotCreateTender()
    {
        var (controller, ctx) = await CreateController(nameof(Create_WithoutCreatePermission_ReturnsForbid_DoesNotCreateTender), role: SystemRoles.Viewer);
        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = 1, Title = "T", IsActive = true };
        ctx.ProcurementPackages.Add(package);
        await ctx.SaveChangesAsync();

        var result = await controller.Create(package.ProcurementPackageId, "Pump Tender", null, null, true, null, null, null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(ctx.Tenders);
    }

    [Fact]
    public async Task Create_WithCreatePermission_CreatesTender()
    {
        var (controller, ctx) = await CreateController(nameof(Create_WithCreatePermission_CreatesTender), role: SystemRoles.ContractManager);
        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = 1, Title = "T", IsActive = true };
        ctx.ProcurementPackages.Add(package);
        await ctx.SaveChangesAsync();

        var result = await controller.Create(package.ProcurementPackageId, "Pump Tender", null, null, true, null, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(ctx.Tenders);
    }

    [Fact]
    public async Task SelectWinner_WithoutApprovePermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(SelectWinner_WithoutApprovePermission_ReturnsForbid), role: SystemRoles.DisciplineEngineer);
        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = 1, Title = "T", IsActive = true };
        ctx.ProcurementPackages.Add(package);
        await ctx.SaveChangesAsync();
        var tender = new Tender { TenderNumber = "TND-0001", ProcurementPackageId = package.ProcurementPackageId, Title = "T", Status = "Closed" };
        ctx.Tenders.Add(tender);
        await ctx.SaveChangesAsync();

        var result = await controller.SelectWinner(tender.TenderId, 1);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Closed", ctx.Tenders.Single().Status);
    }
}
