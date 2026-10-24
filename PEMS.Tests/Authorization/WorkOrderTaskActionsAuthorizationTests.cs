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
/// طبق «PEMS Phase 6B Wave A — ممیزی مستقل» بند P0: WorkOrderController.CompleteTask/
/// ReopenTask هیچ بررسی مجوزی نداشتند - برخلاف بقیه‌ی جهش‌های همین کنترلر (Hold/Resume/
/// AddLaborEntry/...) که همه به "WorkOrder"/"Edit" گیت شده‌اند.
/// </summary>
public class WorkOrderTaskActionsAuthorizationTests
{
    private static async Task<(WorkOrderController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateController(
        string dbName, string? role = SystemRoles.MaintenanceEngineer)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"WOTaskAuth_{dbName}");
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

        // H25-H27: جهش‌های فرزند از دروازه‌ی ماتریس «بخش × فاز × مجوز» عبور می‌کنند؛ Work Orderهای این تست‌ها در فاز اجرا (InProgress) هستند
        var controller = new WorkOrderController(ctx, null!, currentUser, permissionService, null!, null!, null!, null!, null!, new WorkOrderSectionGuard(ctx, accessControl)).WithTempData();
        foreach (var id in new[] { 1, 100, 200, 300 })
            ctx.WorkOrders.Add(new WorkOrder { WorkOrderId = id, EquipmentId = 1, WorkOrderNumber = "WO-" + id, WorkOrderType = "Corrective", Status = "InProgress", IsActive = true });
        await ctx.SaveChangesAsync();
        return (controller, ctx);
    }

    [Fact]
    public async Task CompleteTask_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(CompleteTask_WithoutEditPermission_ReturnsForbid), role: null);
        var task = new WorkOrderTask { WorkOrderTaskId = 1, WorkOrderId = 1, TaskNumber = 1, Title = "T", IsCompleted = false };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.CompleteTask(task.WorkOrderTaskId, task.WorkOrderId);

        Assert.IsType<ForbidResult>(result);
        Assert.False(ctx.WorkOrderTasks.Single().IsCompleted);
    }

    [Fact]
    public async Task CompleteTask_WithEditPermission_CompletesTask()
    {
        var (controller, ctx) = await CreateController(nameof(CompleteTask_WithEditPermission_CompletesTask));
        var task = new WorkOrderTask { WorkOrderTaskId = 1, WorkOrderId = 1, TaskNumber = 1, Title = "T", IsCompleted = false };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.CompleteTask(task.WorkOrderTaskId, task.WorkOrderId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(ctx.WorkOrderTasks.Single().IsCompleted);
    }

    [Fact]
    public async Task ReopenTask_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(ReopenTask_WithoutEditPermission_ReturnsForbid), role: null);
        var task = new WorkOrderTask { WorkOrderTaskId = 1, WorkOrderId = 1, TaskNumber = 1, Title = "T", IsCompleted = true, CompletedDate = DateTime.Now };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.ReopenTask(task.WorkOrderTaskId, task.WorkOrderId);

        Assert.IsType<ForbidResult>(result);
        Assert.True(ctx.WorkOrderTasks.Single().IsCompleted);
    }

    [Fact]
    public async Task ReopenTask_WithEditPermission_ReopensTask()
    {
        var (controller, ctx) = await CreateController(nameof(ReopenTask_WithEditPermission_ReopensTask));
        var task = new WorkOrderTask { WorkOrderTaskId = 1, WorkOrderId = 1, TaskNumber = 1, Title = "T", IsCompleted = true, CompletedDate = DateTime.Now };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.ReopenTask(task.WorkOrderTaskId, task.WorkOrderId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(ctx.WorkOrderTasks.Single().IsCompleted);
    }

    // ==============================================================================
    // طبق ممیزی مستقل فاز ۶B Wave A (نهایی) بند P1-01: CompleteTask/ReopenTask قبلاً فقط با
    // WorkOrderTaskId پیدا می‌شدند - بدون اثبات اینکه واقعاً به workOrderId درخواستی تعلق دارند.
    // این یعنی درخواست‌کننده می‌توانست Task یک WorkOrder دیگر را با فرستادن workOrderId دلخواه
    // در فرم تغییر دهد.
    // ==============================================================================

    [Fact]
    public async Task CompleteTask_MismatchedWorkOrderId_ReturnsNotFound_DoesNotModifyTask()
    {
        var (controller, ctx) = await CreateController(nameof(CompleteTask_MismatchedWorkOrderId_ReturnsNotFound_DoesNotModifyTask));
        var task = new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "T", IsCompleted = false };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        // درخواست ادعا می‌کند Task 500 متعلق به WorkOrder 100 است، اما واقعاً متعلق به 200 است.
        var result = await controller.CompleteTask(workOrderTaskId: 500, workOrderId: 100);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(ctx.WorkOrderTasks.Single(x => x.WorkOrderTaskId == 500).IsCompleted);
    }

    [Fact]
    public async Task ReopenTask_MismatchedWorkOrderId_ReturnsNotFound_DoesNotModifyTask()
    {
        var (controller, ctx) = await CreateController(nameof(ReopenTask_MismatchedWorkOrderId_ReturnsNotFound_DoesNotModifyTask));
        var task = new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "T", IsCompleted = true, CompletedDate = DateTime.Now, CompletedBy = "someone" };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.ReopenTask(workOrderTaskId: 500, workOrderId: 100);

        Assert.IsType<NotFoundResult>(result);
        var unchanged = ctx.WorkOrderTasks.Single(x => x.WorkOrderTaskId == 500);
        Assert.True(unchanged.IsCompleted);
        Assert.Equal("someone", unchanged.CompletedBy);
    }

    [Fact]
    public async Task CompleteTask_MismatchedWorkOrderId_DoesNotModifyAnyUnrelatedTask()
    {
        var (controller, ctx) = await CreateController(nameof(CompleteTask_MismatchedWorkOrderId_DoesNotModifyAnyUnrelatedTask));
        var targetTask = new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "Target", IsCompleted = false };
        var unrelatedTask1 = new WorkOrderTask { WorkOrderTaskId = 501, WorkOrderId = 100, TaskNumber = 1, Title = "Unrelated in claimed WorkOrder", IsCompleted = false };
        var unrelatedTask2 = new WorkOrderTask { WorkOrderTaskId = 502, WorkOrderId = 300, TaskNumber = 1, Title = "Unrelated elsewhere", IsCompleted = false };
        ctx.WorkOrderTasks.AddRange(targetTask, unrelatedTask1, unrelatedTask2);
        await ctx.SaveChangesAsync();

        var result = await controller.CompleteTask(workOrderTaskId: 500, workOrderId: 100);

        Assert.IsType<NotFoundResult>(result);
        Assert.All(ctx.WorkOrderTasks, t => Assert.False(t.IsCompleted));
    }

    [Fact]
    public async Task CompleteTask_MatchingWorkOrderId_Succeeds()
    {
        var (controller, ctx) = await CreateController(nameof(CompleteTask_MatchingWorkOrderId_Succeeds));
        var task = new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 100, TaskNumber = 1, Title = "T", IsCompleted = false };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.CompleteTask(workOrderTaskId: 500, workOrderId: 100);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(ctx.WorkOrderTasks.Single().IsCompleted);
    }

    [Fact]
    public async Task CompleteTask_UnauthorizedUser_MismatchedWorkOrderId_ReturnsForbid_NotNotFound()
    {
        // ترتیب کنترل‌ها مهم است: مجوز باید قبل از هر Query ای بررسی شود - حتی وقتی ownership هم
        // نامعتبر است، پیام باید Forbid باشد (نه NotFound که اطلاعات وجود/عدم‌وجود Task را افشا کند).
        var (controller, ctx) = await CreateController(nameof(CompleteTask_UnauthorizedUser_MismatchedWorkOrderId_ReturnsForbid_NotNotFound), role: null);
        var task = new WorkOrderTask { WorkOrderTaskId = 500, WorkOrderId = 200, TaskNumber = 1, Title = "T", IsCompleted = false };
        ctx.WorkOrderTasks.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.CompleteTask(workOrderTaskId: 500, workOrderId: 100);

        Assert.IsType<ForbidResult>(result);
    }
}
