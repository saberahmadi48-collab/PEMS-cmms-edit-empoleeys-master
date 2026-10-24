using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.ViewModels;

namespace PEMS.Tests.Rcm;

/// <summary>
/// HTML واقعی صفحه‌های RCM (View کامپایل‌شده‌ی PEMS.Web + داده‌ی InMemory): برچسب‌ها از Resource ترجمه می‌شوند (نه کد خام)،
/// هویت تجهیز فقط‌خواندنی است، فرم Plan هرگز equipmentId نمی‌پذیرد، و هیچ View در زمان اجرا خطا نمی‌دهد.
/// </summary>
public class RcmViewRenderTests
{
    public RcmViewRenderTests() => CultureInfo.CurrentUICulture = new CultureInfo("en");

    private static async Task<string> Render(Controller c, IActionResult result, string view)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en"); // فرهنگ جریان async همین تست (کلاس‌های موازی فرهنگ را عوض می‌کنند)
        // Razor کاراکترهای غیر ASCII (مثل «–») را Encode می‌کند؛ مقایسه روی متن Decode‌شده انجام می‌شود
        return System.Net.WebUtility.HtmlDecode(await RazorViewRenderer.RenderAsync(c, (ViewResult)result, view));
    }

    private sealed record Fixture(RcmEnv Env, int StrategyId, int ClassId, int ModeId, int TagId, int AssignmentId);

    private static async Task<Fixture> BuildAsync(string name)
    {
        var env = await RcmTestFactory.CreateEnvAsync("View_" + name);
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var strategyId = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "80-FT-6001", classId);
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, tag.EquipmentId, "A");
        await env.Assignments.AssignAsync(strategyId, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        return new Fixture(env, strategyId, classId, modeId, tag.EquipmentId, assignmentId);
    }

    [Fact]
    public async Task StrategyIndex_ShowsLocalizedLabels_NotRawCodes()
    {
        var f = await BuildAsync(nameof(StrategyIndex_ShowsLocalizedLabels_NotRawCodes));
        var c = f.Env.StrategyController();

        var html = await Render(c, await c.Index(null), "/Views/MaintenanceStrategy/Index.cshtml");

        Assert.Contains("Leakage PUMP_T", html);                       // نام حالت خرابی کاتالوگ
        Assert.Contains("Preventive (scheduled restoration/discard)", html); // برچسب ترجمه‌شده‌ی نوع، نه "Preventive"
        Assert.Contains("Operational", html);
        Assert.Contains("Active", html);
        Assert.DoesNotContain(">RunToFailure<", html);                 // کد خام هرگز به‌عنوان متن نمایش داده نمی‌شود
        Assert.Contains("Test PUMP_T", html);                          // کلاس تجهیز
    }

    [Fact]
    public async Task StrategyDetails_ShowsTagIdentityVersionsHistoryAndAssignmentActions()
    {
        var f = await BuildAsync(nameof(StrategyDetails_ShowsTagIdentityVersionsHistoryAndAssignmentActions));
        var c = f.Env.StrategyController();

        var html = await Render(c, await c.Details(f.StrategyId), "/Views/MaintenanceStrategy/Details.cshtml");

        Assert.Contains("80-FT-6001", html);
        Assert.Contains("A – critical", html);
        Assert.Contains("Change history", html);
        Assert.Contains("Activated", html);
        Assert.Contains("name=\"overrideJustification\"", html);
    }

    [Fact]
    public async Task StrategyForm_HasMandatoryRcmFields_AndNoTagFreeTextField()
    {
        var f = await BuildAsync(nameof(StrategyForm_HasMandatoryRcmFields_AndNoTagFreeTextField));
        var c = f.Env.StrategyController();

        var html = await Render(c, await c.Create(equipmentId: f.TagId), "/Views/MaintenanceStrategy/Edit.cshtml");

        Assert.Contains("name=\"FailureModeId\"", html);
        Assert.Contains("name=\"Justification\"", html);
        Assert.Contains("name=\"ConsequenceCategory\"", html);
        Assert.Contains("data-equipment-identity", html);              // هویت تجهیز فقط‌خواندنی (تگ/نام/کلاس/بحرانی‌بودن)
        Assert.Contains("80-FT-6001", html);
        Assert.DoesNotContain("name=\"equipmentId\"", html);           // تگ هرگز فیلد متنی/ورودی نیست
        Assert.DoesNotContain("<option>Leakage</option>", html);       // دیگر فهرست ثابت ۷تایی نیست
    }

    [Fact]
    public async Task StrategyForm_AfterRejectedSave_ShowsTheLocalizedBusinessErrors()
    {
        var f = await BuildAsync(nameof(StrategyForm_AfterRejectedSave_ShowsTheLocalizedBusinessErrors));
        var c = f.Env.StrategyController();
        var bad = RcmTestFactory.ValidInput(f.ClassId, f.ModeId, "RunToFailure", "Safety");
        bad.Justification = "";

        var result = await c.Create(bad);
        var html = await Render(c, result, "/Views/MaintenanceStrategy/Edit.cshtml");

        Assert.Contains("alert-danger", html);
        Assert.Contains("name=\"Justification\"", html);
    }

    [Fact]
    public async Task BulkAssign_ShowsCriticalityAndConflicts_ForTheSelectedStrategy()
    {
        var f = await BuildAsync(nameof(BulkAssign_ShowsCriticalityAndConflicts_ForTheSelectedStrategy));
        var second = await RcmTestFactory.CreateActiveStrategyAsync(f.Env, f.ClassId, f.ModeId, "Predictive");
        var c = f.Env.StrategyController();

        var html = await Render(c, await c.BulkCreate(null, null, null, "80-FT", strategyId: second, classId: f.ClassId),
            "/Views/MaintenanceStrategy/BulkCreate.cshtml");

        Assert.Contains("A – critical", html);
        Assert.Contains("Conflicting strategy for the same failure mode", html);
        Assert.Contains("name=\"strategyId\"", html);
    }

    [Fact]
    public async Task PlanForm_PrefillsTagAsReadOnlyIdentity_AndNeverPostsEquipmentId()
    {
        var f = await BuildAsync(nameof(PlanForm_PrefillsTagAsReadOnlyIdentity_AndNeverPostsEquipmentId));
        await RcmTestFactory.AddJobPlanAsync(f.Env.Ctx);
        var c = f.Env.PlanController();

        var html = await Render(c, await c.Create(f.AssignmentId, null, null), "/Views/MaintenancePlan/Create.cshtml");

        Assert.Contains("80-FT-6001", html);
        Assert.Contains("data-equipment-identity", html);
        Assert.Contains($"name=\"MaintenanceStrategyAssignmentId\" value=\"{f.AssignmentId}\"", html);
        Assert.DoesNotContain("name=\"equipmentId\"", html);
        Assert.DoesNotContain("name=\"maintenanceStrategyId\"", html);
        Assert.Contains("80-FT-6001 - Leakage PUMP_T - Preventive", html); // عنوان پیشنهادی
        Assert.Contains("Suggested: 15 days", html);                         // ضریب بحرانی A
    }

    [Fact]
    public async Task PlanForm_WithoutAssignment_ListsDistinctChoices()
    {
        var f = await BuildAsync(nameof(PlanForm_WithoutAssignment_ListsDistinctChoices));
        var c = f.Env.PlanController();

        var html = await Render(c, await c.Create(null, null, f.TagId), "/Views/MaintenancePlan/Create.cshtml");

        Assert.Contains("Select the tag and strategy for the new plan", html);
        Assert.Contains("80-FT-6001", html);
        Assert.Contains("v1", html);
    }

    [Fact]
    public async Task PlanForm_UnknownAssignment_RendersAMessage_NotAnException()
    {
        var env = await RcmTestFactory.CreateEnvAsync("View_PlanUnknown");
        var c = env.PlanController();

        var result = await c.Create(424242, null, null);
        var html = await Render(c, result, "/Views/MaintenancePlan/Create.cshtml");

        Assert.Equal(404, c.Response.StatusCode);
        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task PlanIndex_ShowsEquipmentIdentity_Criticality_AndStrategyVersion()
    {
        var f = await BuildAsync(nameof(PlanIndex_ShowsEquipmentIdentity_Criticality_AndStrategyVersion));
        var jp = await RcmTestFactory.AddJobPlanAsync(f.Env.Ctx);
        await f.Env.Plans().CreateAsync(new PlanInputDto
        {
            MaintenanceStrategyAssignmentId = f.AssignmentId, Title = "Quarterly lubrication", TriggerType = "Calendar",
            IntervalDays = 30, NextDueDate = DateTime.Today.AddDays(2), JobPlanId = jp.JobPlanId
        }, RcmTestFactory.Actor(5));
        var c = f.Env.PlanController();

        var html = await Render(c, await c.Index(), "/Views/MaintenancePlan/Index.cshtml");

        Assert.Contains("80-FT-6001", html);
        Assert.Contains("Test PUMP_T", html);
        Assert.Contains("A – critical", html);
        Assert.Contains("Fixed schedule", html);
    }

    [Fact]
    public async Task TagCoverageAndCatalogPages_RenderWithoutErrors()
    {
        var f = await BuildAsync(nameof(TagCoverageAndCatalogPages_RenderWithoutErrors));
        var c = f.Env.StrategyController();

        var tag = await Render(c, await c.Tag(f.TagId), "/Views/MaintenanceStrategy/Tag.cshtml");
        var coverage = await Render(c, await c.Coverage(), "/Views/MaintenanceStrategy/Coverage.cshtml");

        Assert.Contains("Maintenance of tag", tag);
        Assert.Contains("Strategy coverage", coverage);
        Assert.Contains("Coverage by criticality", coverage);

        var catalog = new FailureModeCatalogController(f.Env.Ctx, new PEMS.Persistence.Services.FailureModeCatalogService(f.Env.Ctx),
            f.Env.User, f.Env.Permissions).WithTempData();
        var index = await Render(catalog, await catalog.Index(null, null), "/Views/FailureModeCatalog/Index.cshtml");
        var edit = await Render(catalog, await catalog.Edit((int?)null), "/Views/FailureModeCatalog/Edit.cshtml");
        Assert.Contains("Failure mode catalogue", index);
        Assert.Contains("name=\"Code\"", edit);
    }

    [Fact]
    public async Task ImportPage_RendersUploadFormsAndResults()
    {
        var env = await RcmTestFactory.CreateEnvAsync("View_Import");
        var controller = new StrategyImportController(env.Ctx, new PEMS.Persistence.Services.StrategyImportService(env.Ctx, env.Commands, env.Assignments),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), env.User, env.Permissions).WithTempData();

        var html = await Render(controller, await controller.Index(), "/Views/StrategyImport/Index.cshtml");

        Assert.Contains("name=\"file\"", html);
        Assert.Contains("Download template", html);
        _ = typeof(StrategyImportViewModel);
    }
}
