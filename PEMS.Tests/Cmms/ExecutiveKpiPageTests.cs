using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;

namespace PEMS.Tests.Cmms;

/// <summary>
/// صفحه‌ی /Kpi/Executive: رندر واقعی View (بدون HTTP) و قواعد مجوز Controller - مشاهده با Reliability.View، ذخیره‌ی هدف با
/// Reliability.Edit، و این‌که بدون داده هیچ عدد جعلی نمایش داده نمی‌شود.
/// </summary>
public class ExecutiveKpiPageTests
{
    private static readonly DateTime Today = DateTime.Today;

    private static (KpiController controller, PEMSDbContext ctx, FakePermissionService perms) NewController(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("kpiPage_" + name);
        var targets = new KpiTargetService(ctx);
        var executive = new ExecutiveKpiService(ctx, new KpiService(ctx), new ReliabilityService(ctx), new ReliabilityIntelligenceService(ctx), targets);
        var perms = new FakePermissionService();
        var user = new FakeCurrentUserService { Roles = new List<string> { "Tester" } };
        var controller = new KpiController(new KpiService(ctx), new ReliabilityService(ctx), user, perms, executive, targets).WithTempData();
        return (controller, ctx, perms);
    }

    private static Task<string> RenderAsync(KpiController host, ExecutiveKpiPageViewModel model)
    {
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(host.ViewData) { Model = model } };
        return RazorViewRenderer.RenderAsync(host, result, "~/Views/Kpi/Executive.cshtml");
    }

    private static async Task<ExecutiveKpiPageViewModel> PageModelAsync(KpiController controller)
    {
        var view = Assert.IsType<ViewResult>(await controller.Executive(Today.AddDays(-29), Today));
        return Assert.IsType<ExecutiveKpiPageViewModel>(view.Model);
    }

    // ------------------------------------------------------------------ authorization

    [Fact]
    public async Task Executive_WithoutViewPermission_IsForbidden()
    {
        var (controller, _, perms) = NewController(nameof(Executive_WithoutViewPermission_IsForbidden));
        perms.Denied.Add(("Reliability", "View"));

        Assert.IsType<ForbidResult>(await controller.Executive(null, null));
    }

    [Fact]
    public async Task SaveTarget_WithoutEditPermission_IsForbidden_AndStoresNothing()
    {
        var (controller, ctx, perms) = NewController(nameof(SaveTarget_WithoutEditPermission_IsForbidden_AndStoresNothing));
        perms.Denied.Add(("Reliability", "Edit"));

        Assert.IsType<ForbidResult>(await controller.SaveTarget("pm_compliance", "80", "70", null, null));
        Assert.Empty(ctx.KpiTargets);
    }

    [Fact]
    public async Task SaveTarget_WithEditPermission_StoresTheTarget_AndRedirectsBackToTheSamePeriod()
    {
        var (controller, ctx, _) = NewController(nameof(SaveTarget_WithEditPermission_StoresTheTarget_AndRedirectsBackToTheSamePeriod));

        var result = Assert.IsType<RedirectToActionResult>(await controller.SaveTarget("pm_compliance", "80.5", "70", Today.AddDays(-10), Today));

        Assert.Equal("Executive", result.ActionName);
        Assert.Equal(Today.AddDays(-10).ToString("yyyy-MM-dd"), result.RouteValues!["fromDate"]);
        Assert.Equal("Kpi_Msg_TargetSaved", controller.TempData["KpiMessage"]);
        var row = Assert.Single(ctx.KpiTargets);
        Assert.Equal((80.5m, 70m), (row.TargetValue, row.WarnValue));
    }

    [Theory]
    [InlineData("abc", null)]
    [InlineData("80", "x1")]
    public async Task SaveTarget_NonNumericInput_IsRejected_AndStoresNothing(string target, string? warn)
    {
        var (controller, ctx, _) = NewController("nan_" + target + warn);

        await controller.SaveTarget("pm_compliance", target, warn, null, null);

        Assert.Equal("Kpi_Err_NotANumber", controller.TempData["KpiError"]);
        Assert.Empty(ctx.KpiTargets);
    }

    [Fact]
    public async Task SaveTarget_ServiceValidationError_IsSurfacedAsAKey_AndStoresNothing()
    {
        var (controller, ctx, _) = NewController(nameof(SaveTarget_ServiceValidationError_IsSurfacedAsAKey_AndStoresNothing));

        await controller.SaveTarget("pm_compliance", "150", null, null, null);

        Assert.Equal("Kpi_Err_TargetRange", controller.TempData["KpiError"]);
        Assert.Empty(ctx.KpiTargets);
    }

    [Fact]
    public async Task Executive_ClampsAnAbsurdPeriod_AndNormalisesSwappedDates()
    {
        var (controller, _, _) = NewController(nameof(Executive_ClampsAnAbsurdPeriod_AndNormalisesSwappedDates));

        // the controller's ViewData is shared between results, so each model is read right after its own call
        var hugeModel = (ExecutiveKpiPageViewModel)Assert.IsType<ViewResult>(await controller.Executive(new DateTime(2000, 1, 1), Today)).Model!;
        Assert.Equal(366, (hugeModel.To - hugeModel.From).TotalDays);

        var swappedModel = (ExecutiveKpiPageViewModel)Assert.IsType<ViewResult>(await controller.Executive(Today, Today.AddDays(-5))).Model!;
        Assert.True(swappedModel.From < swappedModel.To);
        Assert.Equal(5, (swappedModel.To - swappedModel.From).TotalDays);
    }

    // ------------------------------------------------------------------ rendering

    [Fact]
    public async Task EmptyDatabase_RendersNoDataTiles_NotTrackedList_AndNoZeroFabrication()
    {
        var (controller, _, _) = NewController(nameof(EmptyDatabase_RendersNoDataTiles_NotTrackedList_AndNoZeroFabrication));
        var model = await PageModelAsync(controller);

        var html = await RenderAsync(controller, model);

        // one tile per catalogue KPI, grouped in sections
        Assert.Equal(model.Dashboard.Tiles.Count, Regex.Matches(html, "class=\"pems-kpi-tile ").Count);
        Assert.Contains("data-kpi=\"pm_compliance\" data-status=\"NoData\"", html);
        Assert.Contains("data-kpi=\"cost_total\" data-status=\"NoData\"", html);
        Assert.Contains("data-kpi=\"backlog_count\" data-status=\"NoTarget\"", html);   // a genuine zero, shown as such
        // categories without any data source are listed, with no values
        Assert.Contains("id=\"kpi-nottracked\"", html);
        Assert.Equal(9, Regex.Matches(html, "<li><strong>").Count);
        // the sample numbers of the framework document must never appear
        foreach (var fake in new[] { "88.5", "21,625", "2,450,000", "97.1%" })
            Assert.DoesNotContain(fake, html);
    }

    [Fact]
    public async Task TargetEditor_IsRenderedOnlyForUsersWhoCanEdit()
    {
        var (controller, _, perms) = NewController(nameof(TargetEditor_IsRenderedOnlyForUsersWhoCanEdit));

        var editorModel = await PageModelAsync(controller);
        var editorHtml = await RenderAsync(controller, editorModel);
        Assert.True(editorModel.CanEditTargets);
        Assert.Contains("id=\"kpi-targets\"", editorHtml);
        Assert.Equal(editorModel.Targets.Count, Regex.Matches(editorHtml, "data-kpi-target=").Count);
        Assert.Matches(new Regex("name=\"target\"[^>]*value=\"95\""), editorHtml);   // pm_compliance default shown for editing

        perms.Denied.Add(("Reliability", "Edit"));
        var viewerModel = await PageModelAsync(controller);
        var viewerHtml = await RenderAsync(controller, viewerModel);
        Assert.False(viewerModel.CanEditTargets);
        Assert.DoesNotContain("id=\"kpi-targets\"", viewerHtml);
        Assert.DoesNotContain("SaveTarget", viewerHtml);
    }

    [Fact]
    public async Task Tile_ShowsValueTargetAndStatusAsTextAndIcon()
    {
        var (controller, ctx, _) = NewController(nameof(Tile_ShowsValueTargetAndStatusAsTextAndIcon));
        for (var i = 1; i <= 4; i++)
            ctx.MaintenancePlans.Add(new PEMS.Domain.Entities.MaintenancePlan { MaintenancePlanId = i, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "P" + i, Title = "p", IsActive = true,
                NextDueDate = i == 1 ? Today.AddDays(-3) : Today.AddDays(10) });
        await ctx.SaveChangesAsync();
        var model = await PageModelAsync(controller);

        // Razor HTML-encodes non-ASCII characters such as the ≥ sign; compare what the browser sees
        var html = System.Net.WebUtility.HtmlDecode(await RenderAsync(controller, model));

        var tile = Regex.Match(html, "<div class=\"pems-kpi-tile [^\"]*\" data-kpi=\"pm_compliance\".*?</div>\\s*</div>", RegexOptions.Singleline).Value;
        Assert.Contains("75.0%", tile);
        Assert.Contains("≥ 95.0%", tile);
        Assert.Contains("data-status=\"Missed\"", tile);
        Assert.Contains("bi-x-circle", tile);   // the status is never colour-only: icon + label accompany it
        Assert.Equal(KpiStatuses.Missed, model.Dashboard.Tiles.Single(t => t.Key == "pm_compliance").Status);
    }
}
