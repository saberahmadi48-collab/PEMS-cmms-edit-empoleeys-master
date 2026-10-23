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
/// طبق «PEMS Phase 8» بند ۴/۲۸ (ممیزی مستقل): دو مسیر بدون هیچ بررسی مجوزی پیدا شدند -
/// WorkOrderController.CreateCrewForWorkOrder (که یک Crew واقعی و متصل به هر WorkOrder ای
/// می‌ساخت) و KpiController.Index (که هزینه/Maintenance KPI را بدون هیچ Gate ای نشان می‌داد).
/// این‌ها همان الگوی شکاف قبلی MaintenanceStrategyController (فاز ۷.۵) بودند - همین‌جا در فاز ۸
/// رفع و تست شدند (بند ۷: «Do not create another phase for this»).
/// </summary>
public class Phase8AuthorizationGapTests
{
    private static async Task<(WorkOrderController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateWorkOrderController(
        string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"P8AuthWO_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>() };
        var crewService = new CrewService(ctx);

        var controller = new WorkOrderController(ctx, null!, currentUser, permissionService, null!, null!, crewService, null!, null!, new WorkOrderSectionGuard(ctx, accessControl)).WithTempData();
        return (controller, ctx);
    }

    [Fact]
    public async Task CreateCrewForWorkOrder_WithoutEditPermission_ReturnsForbid_AndCreatesNoCrew()
    {
        var (controller, ctx) = await CreateWorkOrderController(nameof(CreateCrewForWorkOrder_WithoutEditPermission_ReturnsForbid_AndCreatesNoCrew), role: null);
        var workOrder = new WorkOrder { WorkOrderId = 1, EquipmentId = 1, WorkOrderNumber = "WO-1", WorkOrderType = "Corrective", Status = "Open", IsActive = true };
        ctx.WorkOrders.Add(workOrder);
        await ctx.SaveChangesAsync();

        var result = await controller.CreateCrewForWorkOrder(workOrder.WorkOrderId, "CR-1", "Test Crew");

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(ctx.Crews);
    }

    [Fact]
    public async Task CreateCrewForWorkOrder_WithEditPermission_CreatesCrew()
    {
        var (controller, ctx) = await CreateWorkOrderController(nameof(CreateCrewForWorkOrder_WithEditPermission_CreatesCrew), role: SystemRoles.MaintenanceEngineer);
        var workOrder = new WorkOrder { WorkOrderId = 1, EquipmentId = 1, WorkOrderNumber = "WO-1", WorkOrderType = "Corrective", Status = "Open", IsActive = true };
        ctx.WorkOrders.Add(workOrder);
        await ctx.SaveChangesAsync();

        var result = await controller.CreateCrewForWorkOrder(workOrder.WorkOrderId, "CR-1", "Test Crew");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(ctx.Crews);
    }

    private static async Task<(KpiController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateKpiController(
        string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"P8AuthKpi_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>() };
        var kpiService = new KpiService(ctx);
        var reliabilityService = new ReliabilityService(ctx);

        var controller = new KpiController(kpiService, reliabilityService, currentUser, permissionService);
        return (controller, ctx);
    }

    [Fact]
    public async Task Index_WithoutReliabilityViewPermission_ReturnsForbid()
    {
        var (controller, _) = await CreateKpiController(nameof(Index_WithoutReliabilityViewPermission_ReturnsForbid), role: null);

        var result = await controller.Index(null, null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Index_WithReliabilityViewPermission_ReturnsView()
    {
        var (controller, _) = await CreateKpiController(nameof(Index_WithReliabilityViewPermission_ReturnsView), role: SystemRoles.MaintenanceEngineer);

        var result = await controller.Index(null, null);

        Assert.IsType<ViewResult>(result);
    }
}
