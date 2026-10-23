using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// H26: HTML نهایی صفحه‌ی جزئیات Work Order. «هرگز فرمی رندر نشود که سرور رد می‌کند» و «کنترل بدون مجوز پنهان باشد». فرم‌ها با نام فیلدهای منحصربه‌فردشان
/// تشخیص داده می‌شوند (آدرس‌سازی مسیر در این هارنس خروجی ندارد). View واقعی کامپایل‌شده‌ی PEMS.Web با داده‌ی واقعی InMemory و کنترلر واقعی رندر می‌شود.
/// </summary>
public class WorkOrderDetailsRenderTests : WorkOrderGovernanceTestBase
{
    private readonly WorkOrderSectionGuard _guard;

    public WorkOrderDetailsRenderTests()
    {
        WorkOrderReopenPermissionSeeder.EnsureAsync(Ctx).GetAwaiter().GetResult();
        _guard = new WorkOrderSectionGuard(Ctx, AccessControl);
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Ctx.MaterialItems.Add(new PEMS.Domain.Entities.MaterialItem { MaterialCode = "MM-1", Description = "Seal", IsStockItem = true, IsActive = true });
        Ctx.Skills.Add(new PEMS.Domain.Entities.Skill { Name = "Welding", IsActive = true });
        Ctx.SaveChanges();
    }

    [Fact]
    public async Task B12_legacy_hold_and_cancel_buttons_are_shown_only_to_users_holding_the_permission_the_controller_enforces()
    {
        var cutoff = await Ctx.WorkflowBindings.Where(b => b.EntityType == PEMS.Persistence.Workflow.WorkOrderWorkflow.LifecycleEntityType).MinAsync(b => b.CreatedDate);
        var w = new PEMS.Domain.Entities.WorkOrder { EquipmentId = EquipmentId, WorkOrderNumber = "WO-LEGB12", WorkOrderType = "Preventive", Priority = "Medium", Status = "InProgress", IsActive = true, CreatedDate = cutoff.AddDays(-10) };
        Ctx.WorkOrders.Add(w); await Ctx.SaveChangesAsync();
        w.CreatedDate = cutoff.AddDays(-10); await Ctx.SaveChangesAsync();

        var editor = await RenderAsync(w.WorkOrderId, DisciplineHead);          // WorkOrder.Edit دارد، Approve ندارد
        Assert.Contains("data-legacy=\"hold\"", editor);
        Assert.DoesNotContain("data-legacy=\"cancel\"", editor);

        var manager = await RenderAsync(w.WorkOrderId, Manager);                 // Approve دارد، Edit ندارد
        Assert.DoesNotContain("data-legacy=\"hold\"", manager);
        Assert.Contains("data-legacy=\"cancel\"", manager);

        var none = await RenderAsync(w.WorkOrderId, Creator);
        Assert.DoesNotContain("data-legacy=", none);
    }

    [Fact]
    public async Task Empty_material_master_shows_a_guided_message_instead_of_an_unusable_form()
    {
        Ctx.MaterialItems.RemoveRange(Ctx.MaterialItems);
        await Ctx.SaveChangesAsync();
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);

        var html = await RenderAsync(id, DisciplineHead);

        Assert.Contains("No materials exist in the Material Master yet", html);
        Assert.DoesNotContain("name=\"unitCost\"", html);                  // فرم قطعات مصرفی
        Assert.DoesNotContain("name=\"requiredQuantity\"", html);          // فرم متریال موردنیاز
        Assert.Contains("name=\"regularHours\"", html);                     // بخش‌های نامرتبط با Material Master همچنان کار می‌کنند
    }

    // نام فیلد یکتای فرم هر بخش
    private static readonly (WoSection Section, string Marker)[] Markers =
    {
        (WoSection.Labor, "name=\"regularHours\""),
        (WoSection.SpareParts, "name=\"unitCost\""),
        (WoSection.RequiredSkills, "name=\"skillId\""),
        (WoSection.RequiredMaterials, "name=\"requiredQuantity\""),
        (WoSection.Crew, "name=\"code\""),
        (WoSection.DisciplineReview, "name=\"disciplineIds\""),
        (WoSection.ResponsibleDiscipline, "name=\"responsibleDisciplineId\""),
        (WoSection.ExecutorAssignment, "name=\"teamId\" class=\"form-select form-select-sm\"><option value=\"\">--"),
        (WoSection.Schedule, "name=\"plannedStart\""),
    };

    private async Task<string> RenderAsync(int id, WorkOrderActor viewer)
    {
        var permissions = new PermissionService(Ctx, AccessControl);
        var user = new FakeCurrentUserService { UserName = "viewer", Roles = viewer.Roles.ToList() };
        var controller = new WorkOrderController(Ctx, new WorkOrderReviewService(Ctx, new PEMS.WorkflowPlatform.Services.WorkflowRuntimeService(Ctx)), user, permissions,
            new WorkOrderLaborService(Ctx, AccessControl), Svc, new CrewService(Ctx), null!, null!, _guard).WithTempData();

        var action = await controller.Details(id);
        var view = Assert.IsType<ViewResult>(action);
        return await RazorViewRenderer.RenderAsync(controller, view, "~/Views/WorkOrder/Details.cshtml");
    }

    private static void AssertForms(string html, WoSection? only, params WoSection[] expectedPresent)
    {
        foreach (var (section, marker) in Markers)
        {
            if (only.HasValue && section != only.Value) continue;
            var present = html.Contains(marker, StringComparison.Ordinal);
            Assert.True(present == expectedPresent.Contains(section), $"{section}: form {(present ? "rendered" : "missing")} but expected {(expectedPresent.Contains(section) ? "present" : "absent")}");
        }
    }

    [Fact]
    public async Task Created_work_order_shows_only_the_editable_planning_forms_and_hides_actual_labor_and_spare_forms()
    {
        var id = await CreateAsync();
        var html = await RenderAsync(id, DisciplineHead);

        AssertForms(html, null, WoSection.RequiredSkills, WoSection.RequiredMaterials, WoSection.Crew, WoSection.DisciplineReview);
        Assert.Contains("not available until execution starts", html);                  // راهنمای کوتاه به‌جای فرم
        Assert.DoesNotContain("Reopen Work Order", html);
    }

    [Fact]
    public async Task Execution_shows_labor_and_spare_forms_to_an_authorized_executor_and_hides_them_from_a_user_without_edit()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);

        var asDisciplineHead = await RenderAsync(id, DisciplineHead);
        AssertForms(html: asDisciplineHead, only: null, WoSection.Labor, WoSection.SpareParts, WoSection.DisciplineReview);
        Assert.DoesNotContain("name=\"correctionReason\"", asDisciplineHead);          // اجرا = Editable، نه Correctable

        var asCreator = await RenderAsync(id, Creator);                                // بدون WorkOrder.Edit → هیچ فرم جهشی
        AssertForms(asCreator, null);
        Assert.Contains("read-only", asCreator);
    }

    [Fact]
    public async Task Completion_stage_offers_correction_forms_with_a_mandatory_reason_field_only_to_holders_of_the_correction_permission()
    {
        var id = await CreateAsync("Corrective"); await RunToVerificationAsync(id);

        var corrector = await RenderAsync(id, DisciplineHead);
        AssertForms(corrector, null, WoSection.Labor, WoSection.SpareParts);
        Assert.Contains("name=\"correctionReason\"", corrector);

        var executor = await RenderAsync(id, Executor);                                // Edit دارد، CorrectActuals ندارد
        AssertForms(executor, null);
        Assert.DoesNotContain("name=\"correctionReason\"", executor);
    }

    [Fact]
    public async Task Closed_work_order_renders_no_mutation_forms_at_all_and_offers_reopen_only_to_the_reopen_permission()
    {
        var id = await CreateAsync("Corrective"); await RunToVerificationAsync(id); await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);

        foreach (var viewer in new[] { DisciplineHead, Executor, Creator, Planner })
        {
            var html = await RenderAsync(id, viewer);
            AssertForms(html, null);
            Assert.Contains("immutable", html);
            Assert.DoesNotContain("Reopen Work Order", html);
        }

        var manager = await RenderAsync(id, Manager);
        AssertForms(manager, null);
        Assert.Contains("Reopen Work Order", manager);
        Assert.Contains("name=\"reason\"", manager);
    }

    [Fact]
    public async Task Rendered_forms_agree_with_the_server_guard_for_every_phase_and_viewer()
    {
        // «هرگز فرمی که سرور رد می‌کند»: اگر یک فرم رندر شده، همان POST برای همان کاربر باید از دروازه عبور کند (با دلیل نمونه)
        var ids = new List<(WoPhase Phase, int Id)>();
        var created = await CreateAsync(); ids.Add((WoPhase.Created, created));
        var prep = await CreateAsync(); await PlanAsync(prep); ids.Add((WoPhase.Preparation, prep));
        var exec = await CreateAsync("Corrective"); await RunToInProgressAsync(exec); ids.Add((WoPhase.Execution, exec));
        var verification = await CreateAsync("Corrective"); await RunToVerificationAsync(verification); ids.Add((WoPhase.Verification, verification));
        var closed = await CreateAsync("Corrective"); await RunToVerificationAsync(closed); await Do(closed, "Approve", Verifier); await Do(closed, "Approve", Approver); ids.Add((WoPhase.Closed, closed));

        foreach (var (phase, id) in ids)
            foreach (var viewer in new[] { DisciplineHead, Planner, Executor, Creator })
            {
                var html = await RenderAsync(id, viewer);
                foreach (var (section, marker) in Markers)
                {
                    var rendered = html.Contains(marker, StringComparison.Ordinal);
                    var decision = await _guard.AuthorizeAsync(id, section, WoSectionAction.Add, new WoCaller(null, "v", viewer.EmployeeId, viewer.Roles), "reason");
                    Assert.True(rendered == decision.Allowed, $"{phase}/{section}/{viewer.Roles.First()}: rendered={rendered} but server allowed={decision.Allowed} ({decision.Code})");
                }
            }
    }
}
