using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Phase 6B Wave B1 — ممیزی مستقل نهایی» بند P0: MaintenancePlanController هیچ
/// بررسی مجوزی نداشت - هر کاربر احرازهویت‌شده‌ای می‌توانست Plan بسازد، از آن WorkOrder بسازد،
/// یا آن را غیرفعال کند.
/// </summary>
public class MaintenancePlanControllerAuthorizationTests
{
    // NumberingService واقعی از UPDATE...OUTPUT خام SQL Server استفاده می‌کند (با InMemory کار
    // نمی‌کند) - این Fake ساده فقط برای اثبات Forbid/موفقیت مسیر مجوز لازم است، نه خودِ منطق شماره‌گذاری.
    private class FakeNumberingService : INumberingService
    {
        public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null) =>
            Task.FromResult($"{documentType}-0001");
    }

    // طبق «PEMS Phase 6»: این تست‌ها فقط مسیر مجوز را می‌سنجند (Forbid قبل از رسیدن به سرویس) -
    // یک Fake ساده کافی است، نه پیاده‌سازی واقعی Idempotency.
    private class FakePMGenerationService : IPMGenerationService
    {
        public Task<string?> GenerateForPlanAsync(int planId) => Task.FromResult<string?>("WO-0001");
        public Task<PEMS.Application.DTOs.PMGenerationResultDto> GenerateDueWorkOrdersAsync() =>
            Task.FromResult(new PEMS.Application.DTOs.PMGenerationResultDto());
        public Task<int> ReconcileFloatingSchedulesAsync() => Task.FromResult(0);
    }

    private static async Task<(MaintenancePlanController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateController(
        string dbName, string? role = SystemRoles.MaintenanceEngineer)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"MaintPlanAuth_{dbName}");
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

        var maintenancePlanService = new MaintenancePlanService(ctx);
        var controller = new MaintenancePlanController(ctx, maintenancePlanService,
                new MaintenancePlanCommandService(ctx, new FakeNumberingService()), new StrategyAssignmentService(ctx),
                new FakePMGenerationService(), currentUser, permissionService)
            .WithTempData();
        return (controller, ctx);
    }

    [Fact]
    public async Task Index_WithoutViewPermission_ReturnsForbid()
    {
        var (controller, _) = await CreateController(nameof(Index_WithoutViewPermission_ReturnsForbid), role: null);

        var result = await controller.Index();

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Index_WithViewPermission_ReturnsView()
    {
        var (controller, _) = await CreateController(nameof(Index_WithViewPermission_ReturnsView), role: SystemRoles.Viewer);

        var result = await controller.Index();

        Assert.IsType<ViewResult>(result);
    }

    private static async Task<int> SeedAssignmentAsync(PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        var eq = new Equipment { EquipmentId = 1, TagNumber = "EQ-001", Name = "EQ-001", EquipmentType = "Pump", IsActive = true };
        ctx.Equipments.Add(eq);
        var strategy = new MaintenanceStrategy
        {
            StrategyType = "Preventive", ConsequenceCategory = "Operational", Status = "Active", IsActive = true, FailureModeCode = "X"
        };
        ctx.MaintenanceStrategies.Add(strategy);
        await ctx.SaveChangesAsync();
        var assignment = new MaintenanceStrategyAssignment { MaintenanceStrategyId = strategy.MaintenanceStrategyId, EquipmentId = eq.EquipmentId, IsActive = true };
        ctx.MaintenanceStrategyAssignments.Add(assignment);
        ctx.JobPlans.Add(new JobPlan { JobPlanId = 1, JobPlanNumber = "JP-1", Title = "JP", IsActive = true });
        await ctx.SaveChangesAsync();
        return assignment.MaintenanceStrategyAssignmentId;
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(Create_WithoutCreatePermission_ReturnsForbid), role: SystemRoles.Viewer);
        var assignmentId = await SeedAssignmentAsync(ctx);

        var result = await controller.Create(new PEMS.Application.DTOs.PlanInputDto
        {
            MaintenanceStrategyAssignmentId = assignmentId, Title = "Test Plan", TriggerType = "Calendar", IntervalDays = 30, JobPlanId = 1
        });

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(ctx.MaintenancePlans);
    }

    [Fact]
    public async Task Create_WithCreatePermission_CreatesMaintenancePlan()
    {
        var (controller, ctx) = await CreateController(nameof(Create_WithCreatePermission_CreatesMaintenancePlan));
        var assignmentId = await SeedAssignmentAsync(ctx);

        var result = await controller.Create(new PEMS.Application.DTOs.PlanInputDto
        {
            MaintenanceStrategyAssignmentId = assignmentId, Title = "Test Plan", TriggerType = "Calendar", IntervalDays = 30, JobPlanId = 1
        });

        Assert.IsType<RedirectToActionResult>(result);
        var plan = Assert.Single(ctx.MaintenancePlans);
        Assert.Equal(1, plan.EquipmentId); // تجهیز از تخصیص مشتق شد، نه از کلاینت
    }

    [Fact]
    public async Task RecordMeterReading_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(RecordMeterReading_WithoutEditPermission_ReturnsForbid), role: SystemRoles.Viewer);
        var plan = new MaintenancePlan { MaintenancePlanId = 1, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "MP-001", Title = "T", TriggerType = "Meter", MeterInterval = 100, IsActive = true };
        ctx.MaintenancePlans.Add(plan);
        await ctx.SaveChangesAsync();

        var result = await controller.RecordMeterReading(plan.MaintenancePlanId, 500);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(ctx.MaintenancePlans.Single().LastExecutionMeterReading);
    }

    [Fact]
    public async Task RecordMeterReading_WithEditPermission_UpdatesReading()
    {
        var (controller, ctx) = await CreateController(nameof(RecordMeterReading_WithEditPermission_UpdatesReading));
        var plan = new MaintenancePlan { MaintenancePlanId = 1, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "MP-001", Title = "T", TriggerType = "Meter", MeterInterval = 100, IsActive = true };
        ctx.MaintenancePlans.Add(plan);
        await ctx.SaveChangesAsync();

        var result = await controller.RecordMeterReading(plan.MaintenancePlanId, 500);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(500, ctx.MaintenancePlans.Single().LastExecutionMeterReading);
    }

    [Fact]
    public async Task Deactivate_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(Deactivate_WithoutEditPermission_ReturnsForbid), role: SystemRoles.Viewer);
        var plan = new MaintenancePlan { MaintenancePlanId = 1, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "MP-001", Title = "T", TriggerType = "Calendar", IsActive = true };
        ctx.MaintenancePlans.Add(plan);
        await ctx.SaveChangesAsync();

        var result = await controller.Deactivate(plan.MaintenancePlanId);

        Assert.IsType<ForbidResult>(result);
        Assert.True(ctx.MaintenancePlans.Single().IsActive);
    }

    [Fact]
    public async Task Deactivate_WithEditPermission_DeactivatesPlan()
    {
        var (controller, ctx) = await CreateController(nameof(Deactivate_WithEditPermission_DeactivatesPlan));
        var plan = new MaintenancePlan { MaintenancePlanId = 1, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "MP-001", Title = "T", TriggerType = "Calendar", IsActive = true };
        ctx.MaintenancePlans.Add(plan);
        await ctx.SaveChangesAsync();

        var result = await controller.Deactivate(plan.MaintenancePlanId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(ctx.MaintenancePlans.Single().IsActive);
    }

    [Fact]
    public async Task GenerateWorkOrder_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(GenerateWorkOrder_WithoutEditPermission_ReturnsForbid), role: SystemRoles.Viewer);
        ctx.Equipments.Add(new Equipment { EquipmentId = 1, TagNumber = "EQ-001", Name = "EQ-001", EquipmentType = "Pump", IsActive = true });
        var plan = new MaintenancePlan { MaintenancePlanId = 1, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "MP-001", Title = "T", TriggerType = "Calendar", IsActive = true };
        ctx.MaintenancePlans.Add(plan);
        await ctx.SaveChangesAsync();

        var result = await controller.GenerateWorkOrder(plan.MaintenancePlanId);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(ctx.WorkOrders);
    }
}
