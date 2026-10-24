using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Phase 6B Wave A — ممیزی مستقل» بند P0: TaskController.Index/Create/UpdateStatus
/// قبلاً هیچ بررسی مجوزی نداشتند - هر کاربر احرازهویت‌شده‌ای (بدون توجه به نقش) می‌توانست از
/// آن‌ها استفاده کند. این تست‌ها ثابت می‌کنند کاربران بدون مجوز "Task"/View|Create|Edit با
/// Forbid رد می‌شوند و کاربران مجاز همچنان می‌توانند از عملکرد فعلی استفاده کنند.
/// </summary>
public class TaskControllerAuthorizationTests
{
    private static async Task<(TaskController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateController(
        string dbName, string? role = SystemRoles.EngineeringManager)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TaskAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService
        {
            Roles = role != null ? new List<string> { role } : new List<string>(),
            UserId = 1
        };

        var taskService = new TaskService(ctx, null!);
        var manager = new PEMS.Persistence.Services.TaskManager.TaskManagerService(ctx, permissionService, new FakeNumberingService());
        var controller = new TaskController(taskService, manager, currentUser, permissionService).WithTempData();
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

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<PagedResult<TaskItemDto>>(view.Model);
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbid()
    {
        var (controller, _) = await CreateController(nameof(Create_WithoutCreatePermission_ReturnsForbid), role: null);

        var result = await controller.Create("Test task", null, null, null, "Normal");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Create_WithCreatePermission_CreatesTask()
    {
        var (controller, ctx) = await CreateController(nameof(Create_WithCreatePermission_CreatesTask));

        var result = await controller.Create("Test task", null, null, null, "Normal");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(ctx.TaskItems);
    }

    [Fact]
    public async Task UpdateStatus_WithoutEditPermission_ReturnsForbid()
    {
        var (controller, ctx) = await CreateController(nameof(UpdateStatus_WithoutEditPermission_ReturnsForbid), role: null);
        var task = new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now };
        ctx.TaskItems.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.UpdateStatus(task.TaskItemId, "Completed", null, "action");

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Open", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task UpdateStatus_WithEditPermission_UpdatesStatus()
    {
        var (controller, ctx) = await CreateController(nameof(UpdateStatus_WithEditPermission_UpdatesStatus));
        // Task Manager فاز ۰: مجوز نقش کافی نیست - کاربر باید درگیر وظیفه باشد (اینجا ارجاع‌دهنده)
        var task = new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 1 };
        ctx.TaskItems.Add(task);
        await ctx.SaveChangesAsync();

        var result = await controller.UpdateStatus(task.TaskItemId, "Completed", null, "action");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Completed", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task UpdateStatus_WithEditPermission_ButNotInvolved_ReturnsNotFound_AndKeepsStatus()
    {
        var (controller, ctx) = await CreateController(nameof(UpdateStatus_WithEditPermission_ButNotInvolved_ReturnsNotFound_AndKeepsStatus));
        // قبلاً هر کاربر با Task.Edit هر شناسه‌ای را تغییر می‌داد (IDOR)
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 42 });
        await ctx.SaveChangesAsync();

        var result = await controller.UpdateStatus(1, "Completed", null, "action");

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("Open", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task UpdateStatus_WithInvalidTransition_KeepsStatus()
    {
        var (controller, ctx) = await CreateController(nameof(UpdateStatus_WithInvalidTransition_KeepsStatus));
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Completed", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 1 });
        await ctx.SaveChangesAsync();

        Assert.IsType<RedirectToActionResult>(await controller.UpdateStatus(1, "Whatever", null, "action"));
        Assert.IsType<RedirectToActionResult>(await controller.UpdateStatus(1, "InProgress", null, "action"));
        Assert.Equal("Completed", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task Details_ForATaskTheUserIsNotInvolvedIn_ReturnsNotFound()
    {
        var (controller, ctx) = await CreateController(nameof(Details_ForATaskTheUserIsNotInvolvedIn_ReturnsNotFound));
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 42 });
        await ctx.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await controller.Details(1));
    }
}
