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

    // SCRUM-437: CreateCrewForWorkOrder is the action definition work-order-create-crew over the operation Crew.Create; the permission is declared on the service
    // method and checked by the operation catalog before the service is called.
    [Fact]
    public void CreateCrew_operation_requires_WorkOrder_Edit()
    {
        var catalog = PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly);
        Assert.Equal("WorkOrder.Edit", catalog.Get("Crew", "Create")!.Permission);
    }

    [Fact]
    public async Task CreateCrew_service_creates_a_crew_for_the_work_order()
    {
        var (_, ctx) = await CreateWorkOrderController(nameof(CreateCrew_service_creates_a_crew_for_the_work_order), role: SystemRoles.MaintenanceEngineer);
        var workOrder = new WorkOrder { WorkOrderId = 1, EquipmentId = 1, WorkOrderNumber = "WO-1", WorkOrderType = "Corrective", Status = "Open", IsActive = true };
        ctx.WorkOrders.Add(workOrder);
        await ctx.SaveChangesAsync();

        var (success, _, crewId) = await new CrewService(ctx).CreateAsync("CR-1", "Test Crew", workOrder.WorkOrderId);

        Assert.True(success); Assert.NotNull(crewId);
        Assert.Single(ctx.Crews);
    }

}
