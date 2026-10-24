using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// ممیزی مجوز (مسئله ۲۶): هر Endpoint استراتژی/Plan/کاتالوگ/Import سمت سرور مجوز می‌گیرد، همه‌ی POST ها
/// Antiforgery دارند، و کاربر بدون مجوز نمی‌تواند Endpoint را مستقیم صدا بزند. نقش‌ها: Viewer فقط View،
/// MaintenanceEngineer = Create+Edit (بدون Approve)، EngineeringSupervisor/Manager = + Approve.
/// ماژول MaintenanceStrategy: Create=ساخت الگو، Edit=ویرایش/ارسال/درخواست Override، Approve=تایید/فعال‌سازی/
/// بایگانی/تخصیص/تصمیم Override.
/// </summary>
public class MaintenanceStrategyControllerAuthorizationTests
{
    private static Task<RcmEnv> Env(string name, string? role = SystemRoles.MaintenanceEngineer)
        => RcmTestFactory.CreateEnvAsync($"StratAuth_{name}", role);

    [Fact]
    public async Task Index_WithoutViewPermission_ReturnsForbid()
    {
        var env = await Env(nameof(Index_WithoutViewPermission_ReturnsForbid), role: null);
        Assert.IsType<ForbidResult>(await env.StrategyController().Index(null));
    }

    [Fact]
    public async Task Index_ViewerRole_CanView()
    {
        var env = await Env(nameof(Index_ViewerRole_CanView), SystemRoles.Viewer);
        Assert.IsType<ViewResult>(await env.StrategyController().Index(null));
    }

    [Fact]
    public async Task Create_ViewerRole_ReturnsForbid_NoStrategyCreated()
    {
        var env = await Env(nameof(Create_ViewerRole_ReturnsForbid_NoStrategyCreated), SystemRoles.Viewer);
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);

        var result = await env.StrategyController().Create(RcmTestFactory.ValidInput(classId, modeId));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(env.Ctx.MaintenanceStrategies);
    }

    [Fact]
    public async Task Create_MaintenanceEngineer_CanCreate_AsDraft()
    {
        var env = await Env(nameof(Create_MaintenanceEngineer_CanCreate_AsDraft));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);

        var result = await env.StrategyController().Create(RcmTestFactory.ValidInput(classId, modeId));

        Assert.IsType<RedirectToActionResult>(result);
        var strategy = Assert.Single(env.Ctx.MaintenanceStrategies);
        Assert.Equal("Draft", strategy.Status);
        Assert.Equal("PUMP_T_LEAK", strategy.FailureModeCode);
    }

    [Fact]
    public async Task Create_InvalidInput_ReturnsFormWith400_NotA500_AndSavesNothing()
    {
        var env = await Env(nameof(Create_InvalidInput_ReturnsFormWith400_NotA500_AndSavesNothing));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var bad = RcmTestFactory.ValidInput(classId, modeId, "RunToFailure", "Safety");
        bad.Justification = null;
        var controller = env.StrategyController();

        var result = await controller.Create(bad);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Edit", view.ViewName);
        Assert.Equal(StatusCodes.Status400BadRequest, controller.Response.StatusCode);
        Assert.Empty(env.Ctx.MaintenanceStrategies);
    }

    [Fact]
    public async Task Create_DuplicateClassFailureModeAndType_RejectedWithoutCreatingSecondRow()
    {
        var env = await Env(nameof(Create_DuplicateClassFailureModeAndType_RejectedWithoutCreatingSecondRow));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var controller = env.StrategyController();

        var first = await controller.Create(RcmTestFactory.ValidInput(classId, modeId));
        var second = await controller.Create(RcmTestFactory.ValidInput(classId, modeId));

        Assert.IsType<RedirectToActionResult>(first);
        var view = Assert.IsType<ViewResult>(second); // پیام قابل‌مشاهده، نه رد بی‌صدا
        var form = Assert.IsType<PEMS.Web.ViewModels.StrategyFormViewModel>(view.Model);
        Assert.Contains(form.Errors, e => e.Contains("Rcm_Err_DuplicateStrategy"));
        Assert.Single(env.Ctx.MaintenanceStrategies);
    }

    [Fact]
    public async Task BulkCreate_Get_MaintenanceEngineer_ReturnsForbid()
    {
        var env = await Env(nameof(BulkCreate_Get_MaintenanceEngineer_ReturnsForbid));
        Assert.IsType<ForbidResult>(await env.StrategyController().BulkCreate(null, null, null, null));
    }

    [Fact]
    public async Task BulkCreate_Get_EngineeringSupervisor_CanView()
    {
        var env = await Env(nameof(BulkCreate_Get_EngineeringSupervisor_CanView), SystemRoles.EngineeringSupervisor);
        Assert.IsType<ViewResult>(await env.StrategyController().BulkCreate(null, null, null, null));
    }

    private static async Task<(int StrategyId, int ClassId, int ModeId)> ActiveStrategyAsync(RcmEnv env, string code = "PUMP_T")
    {
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, code);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        return (id, classId, modeId);
    }

    [Fact]
    public async Task BulkCreate_Post_MaintenanceEngineer_ReturnsForbid_NoAssignmentsCreated()
    {
        var env = await Env(nameof(BulkCreate_Post_MaintenanceEngineer_ReturnsForbid_NoAssignmentsCreated));
        var (strategyId, classId, _) = await ActiveStrategyAsync(env);
        var e1 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EQ-4", classId);

        var result = await env.StrategyController().BulkCreate(new List<int> { e1.EquipmentId }, strategyId);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(env.Ctx.MaintenanceStrategyAssignments);
    }

    [Fact]
    public async Task BulkCreate_Post_EngineeringSupervisor_AssignsAllSelectedEquipment()
    {
        var env = await Env(nameof(BulkCreate_Post_EngineeringSupervisor_AssignsAllSelectedEquipment), SystemRoles.EngineeringSupervisor);
        var (strategyId, classId, _) = await ActiveStrategyAsync(env);
        var e1 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EQ-6", classId);
        var e2 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EQ-7", classId);

        var result = await env.StrategyController().BulkCreate(new List<int> { e1.EquipmentId, e2.EquipmentId }, strategyId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await env.Ctx.MaintenanceStrategyAssignments.CountAsync());
        Assert.Single(env.Ctx.MaintenanceStrategies); // الگو یک بار تعریف شد، نه یک استراتژی برای هر تگ
    }

    [Fact]
    public async Task BulkCreate_Post_SkipsEquipmentThatAlreadyHasConflictingStrategyForSameFailureMode()
    {
        var env = await Env(nameof(BulkCreate_Post_SkipsEquipmentThatAlreadyHasConflictingStrategyForSameFailureMode), SystemRoles.EngineeringSupervisor);
        var (firstId, classId, modeId) = await ActiveStrategyAsync(env);
        var secondId = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId, "Predictive");
        var e1 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EQ-8", classId);
        var e2 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EQ-9", classId);
        await env.Assignments.AssignAsync(firstId, new[] { e1.EquipmentId }, RcmTestFactory.Actor(9));

        await env.StrategyController().BulkCreate(new List<int> { e1.EquipmentId, e2.EquipmentId }, secondId);

        var forSecond = await env.Ctx.MaintenanceStrategyAssignments.Where(a => a.MaintenanceStrategyId == secondId).ToListAsync();
        Assert.Single(forSecond); // فقط e2؛ e1 به‌خاطر تعارض (همان حالت خرابی) رد شد
        Assert.Equal(e2.EquipmentId, forSecond[0].EquipmentId);
    }

    [Fact]
    public async Task Deactivate_ViewerRole_ReturnsForbid()
    {
        var env = await Env(nameof(Deactivate_ViewerRole_ReturnsForbid), SystemRoles.Viewer);
        var strategy = new MaintenanceStrategy { StrategyType = "Preventive", Status = "Active", IsActive = true };
        env.Ctx.MaintenanceStrategies.Add(strategy);
        await env.Ctx.SaveChangesAsync();

        var result = await env.StrategyController().Deactivate(strategy.MaintenanceStrategyId);

        Assert.IsType<ForbidResult>(result);
        Assert.True(env.Ctx.MaintenanceStrategies.Single().IsActive);
    }

    // ==================== مجوز هر Endpoint (فراخوانی مستقیم بدون مجوز) ====================

    [Fact]
    public async Task EveryMutatingEndpoint_ForViewerRole_ReturnsForbid()
    {
        var env = await Env(nameof(EveryMutatingEndpoint_ForViewerRole_ReturnsForbid), SystemRoles.Viewer);
        var c = env.StrategyController();

        Assert.IsType<ForbidResult>(await c.Create(new StrategyInputDto()));
        Assert.IsType<ForbidResult>(await c.Edit(1, new StrategyInputDto()));
        Assert.IsType<ForbidResult>(await c.Submit(1));
        Assert.IsType<ForbidResult>(await c.Review(1, true, null));
        Assert.IsType<ForbidResult>(await c.Activate(1));
        Assert.IsType<ForbidResult>(await c.Retire(1, null));
        Assert.IsType<ForbidResult>(await c.Assign(1, new List<int> { 1 }));
        Assert.IsType<ForbidResult>(await c.Unassign(1, 1, null));
        Assert.IsType<ForbidResult>(await c.RequestOverride(1, 1, 10, null, "x"));
        Assert.IsType<ForbidResult>(await c.DecideOverride(1, 1, true, null));
        Assert.IsType<ForbidResult>(await c.BulkCreate(new List<int> { 1 }, 1));
    }

    [Fact]
    public async Task ApprovalEndpoints_ForMaintenanceEngineer_ReturnForbid_ButEditEndpointsAreAllowed()
    {
        var env = await Env(nameof(ApprovalEndpoints_ForMaintenanceEngineer_ReturnForbid_ButEditEndpointsAreAllowed));
        var c = env.StrategyController();

        Assert.IsType<ForbidResult>(await c.Review(1, true, null));
        Assert.IsType<ForbidResult>(await c.Activate(1));
        Assert.IsType<ForbidResult>(await c.Retire(1, null));
        Assert.IsType<ForbidResult>(await c.Assign(1, new List<int> { 1 }));
        Assert.IsType<ForbidResult>(await c.Unassign(1, 1, null));
        Assert.IsType<ForbidResult>(await c.DecideOverride(1, 1, true, null));

        // Edit-level endpoints pass the permission gate (they then answer 404 for a missing record, not Forbid)
        Assert.IsNotType<ForbidResult>(await c.Submit(999));
        Assert.IsNotType<ForbidResult>(await c.RequestOverride(999, 1, 10, null, "x"));
    }

    [Fact]
    public async Task ReadEndpoints_WithoutViewPermission_ReturnForbid()
    {
        var env = await Env(nameof(ReadEndpoints_WithoutViewPermission_ReturnForbid), role: null);
        var c = env.StrategyController();

        Assert.IsType<ForbidResult>(await c.Details(1));
        Assert.IsType<ForbidResult>(await c.Tag(1));
        Assert.IsType<ForbidResult>(await c.Coverage());
        Assert.IsType<ForbidResult>(await c.ExportUncovered());
        Assert.IsType<ForbidResult>(await c.FailureModes(null, null));
    }

    [Fact]
    public async Task Details_ForUnknownId_ReturnsNotFound_NotA500()
    {
        var env = await Env(nameof(Details_ForUnknownId_ReturnsNotFound_NotA500), SystemRoles.Viewer);
        Assert.IsType<NotFoundResult>(await env.StrategyController().Details(424242));
        Assert.IsType<NotFoundResult>(await env.StrategyController().Tag(424242));
    }

    [Fact]
    public void EveryPostAction_OnRcmControllers_HasAntiforgeryValidation()
    {
        var controllers = new[]
        {
            typeof(MaintenanceStrategyController), typeof(MaintenancePlanController),
            typeof(FailureModeCatalogController), typeof(StrategyImportController)
        };

        var missing = new List<string>();
        foreach (var type in controllers)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttribute<HttpPostAttribute>() == null) continue;
                if (m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() == null)
                    missing.Add($"{type.Name}.{m.Name}");
            }
        }

        Assert.True(missing.Count == 0, "POST actions without [ValidateAntiForgeryToken]: " + string.Join(", ", missing));
    }
}
