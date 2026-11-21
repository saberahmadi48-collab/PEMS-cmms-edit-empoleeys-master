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
    private sealed class KeyLocalizer : Microsoft.Extensions.Localization.IStringLocalizer<PEMS.Web.Resources.SharedResource>
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => new(name, name);
        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>SCRUM-755: the create and status commands are operations; the permission floor is the operation catalog's, the object-level rules are the task manager's.</summary>
    private static async Task<(PEMS.Web.Services.TaskItemOperations Ops, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateOps(string dbName, string? role = SystemRoles.EngineeringManager)
    {
        var (controller, ctx) = await CreateController(dbName, role);
        var accessControl = new AccessControlService(ctx);
        var manager = new PEMS.Persistence.Services.TaskManager.TaskManagerService(ctx, new PermissionService(ctx, accessControl), new FakeNumberingService());
        var user = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>(), UserId = 1 };
        return (new PEMS.Web.Services.TaskItemOperations(manager, user, new KeyLocalizer()), ctx);
    }

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
    public async Task Create_Operation_CreatesTask()
    {
        var (ops, ctx) = await CreateOps(nameof(Create_Operation_CreatesTask));

        var result = await ops.CreateAsync("Test task", null, null, null, "Normal");

        Assert.True(result.Success);
        Assert.Single(ctx.TaskItems);
    }

    [Fact]
    public async Task UpdateStatus_Operation_UpdatesStatus()
    {
        var (ops, ctx) = await CreateOps(nameof(UpdateStatus_Operation_UpdatesStatus));
        // the role permission is not enough: the user must be involved in the task (here the assigner)
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 1 });
        await ctx.SaveChangesAsync();

        var result = await ops.UpdateStatusAsync(1, "Completed");

        Assert.True(result.Success);
        Assert.Equal("Completed", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task UpdateStatus_Operation_ButNotInvolved_Fails_AndKeepsStatus()
    {
        var (ops, ctx) = await CreateOps(nameof(UpdateStatus_Operation_ButNotInvolved_Fails_AndKeepsStatus));
        // any user with Task.Edit used to change any id (IDOR)
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Open", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 42 });
        await ctx.SaveChangesAsync();

        Exception? error = null;
        (bool Success, string? Error) result = default;
        try { result = await ops.UpdateStatusAsync(1, "Completed"); } catch (UnauthorizedAccessException ex) { error = ex; }

        Assert.True(error != null || !result.Success);
        Assert.Equal("Open", ctx.TaskItems.Single().Status);
    }

    [Fact]
    public async Task UpdateStatus_Operation_WithInvalidTransition_KeepsStatus()
    {
        var (ops, ctx) = await CreateOps(nameof(UpdateStatus_Operation_WithInvalidTransition_KeepsStatus));
        ctx.TaskItems.Add(new PEMS.Domain.Entities.TaskItem { TaskItemId = 1, Title = "T", Status = "Completed", CreatedDate = DateTime.Now, AssignedByApplicationUserId = 1 });
        await ctx.SaveChangesAsync();

        Assert.False((await ops.UpdateStatusAsync(1, "Whatever")).Success);
        Assert.False((await ops.UpdateStatusAsync(1, "InProgress")).Success);
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
