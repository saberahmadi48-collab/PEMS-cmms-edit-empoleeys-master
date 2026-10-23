using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>CMMS audit B8, B9, B11, B13, B14 روی گردش‌کار واقعی Work Order (InMemory).</summary>
public class WorkOrderAuditFixTests : WorkOrderGovernanceTestBase
{
    // ---------------------------------------------------------------- B8
    [Fact]
    public async Task B8_labor_without_an_effective_rate_is_refused_instead_of_silently_costing_zero()
    {
        var id = await CreateAsync(); await RunToInProgressAsync(id);
        var labor = new WorkOrderLaborService(Ctx, AccessControl);
        var emp = Executor.EmployeeId!.Value;

        var ex = await Assert.ThrowsAsync<LaborRateMissingException>(() => labor.AddLaborEntryAsync(id, emp, null, DateTime.Today, 4, 0, null, Executor));
        Assert.Equal(emp, ex.EmployeeId); Assert.Equal("Normal", ex.RateType);
        Assert.Contains("rate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Ctx.WorkOrderLabors);

        Ctx.EmployeeLaborRates.Add(new EmployeeLaborRate { EmployeeId = emp, RateType = "Normal", Rate = 100, EffectiveFrom = DateTime.Today.AddYears(-1), IsActive = true });
        await Ctx.SaveChangesAsync();
        var ot = await Assert.ThrowsAsync<LaborRateMissingException>(() => labor.AddLaborEntryAsync(id, emp, null, DateTime.Today, 4, 2, null, Executor));
        Assert.Equal("Overtime", ot.RateType);                        // ساعت اضافه‌کاری بدون نرخ اضافه‌کاری هم رد می‌شود

        var okId = await labor.AddLaborEntryAsync(id, emp, null, DateTime.Today, 4, 0, null, Executor);          // فقط عادی → مجاز
        var row = await Ctx.WorkOrderLabors.AsNoTracking().SingleAsync(l => l.WorkOrderLaborId == okId);
        Assert.Equal(100, row.Rate); Assert.Equal(400, row.LaborCost);                                           // Snapshot نرخ
    }

    [Fact]
    public async Task B8_controller_shows_a_specific_message_and_a_link_target_for_the_missing_rate()
    {
        var id = await CreateAsync(); await RunToInProgressAsync(id);
        Ctx.Users.Add(new PEMS.Persistence.Identity.ApplicationUser { Id = 77, UserName = "ex", NormalizedUserName = "EX", EmployeeId = Executor.EmployeeId, Email = "ex@t.local" });
        await Ctx.SaveChangesAsync();
        var user = new FakeCurrentUserService { UserId = 77, Roles = Executor.Roles.ToList() };
        var ctl = new WorkOrderController(Ctx, null!, user, new PermissionService(Ctx, AccessControl), new WorkOrderLaborService(Ctx, AccessControl), Svc, new CrewService(Ctx), null!, null!,
            new WorkOrderSectionGuard(Ctx, AccessControl)).WithTempData();

        await ctl.AddLaborEntry(id, Executor.EmployeeId!.Value, null, DateTime.Today, 2, 0, null);

        Assert.Contains("rate", (string)ctl.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Executor.EmployeeId, ctl.TempData["LaborRateEmployeeId"]);
        Assert.Empty(Ctx.WorkOrderLabors);
    }

    // ---------------------------------------------------------------- B9
    private async Task SeedCatalogAsync()
    {
        Ctx.FailureModes.Add(new FailureMode { Code = "LEAK", Name = "Leakage", IsActive = true });
        Ctx.FailureModes.Add(new FailureMode { Code = "VIB", Name = "Vibration", IsActive = true });
        Ctx.FailureCauses.Add(new FailureCause { Code = "WEAR", Name = "Wear", IsActive = true });
        await Ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task B9_completion_requires_catalog_failure_codes_when_a_catalog_exists_and_stores_the_references()
    {
        await SeedCatalogAsync();
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);

        var bad = await Fail(id, "Approve", Executor, new() { FailureModeCode = "Leakish", FailureCauseCode = "Wear", DowntimeHours = 1 });
        Assert.Contains("catalog", bad, StringComparison.OrdinalIgnoreCase);
        var badCause = await Fail(id, "Approve", Executor, new() { FailureModeCode = "LEAK", FailureCauseCode = "Gremlins" });
        Assert.Contains("catalog", badCause, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InProgress", (await Wo(id)).Status);

        // نام یا کد با هر حروفی پذیرفته و به کد استاندارد کاتالوگ نرمال می‌شود
        await Do(id, "Approve", Executor, null, new() { FailureModeCode = "leakage", FailureCauseCode = "wear", RootCause = "seal", DowntimeHours = 3, FailureEffect = "flush lost" });
        var wo = await Wo(id);
        Assert.Equal("LEAK", wo.FailureModeCode); Assert.Equal("WEAR", wo.FailureCauseCode);
        Assert.NotNull(wo.FailureModeId); Assert.NotNull(wo.FailureCauseId); Assert.Equal("flush lost", wo.FailureEffect);
    }

    [Fact]
    public async Task B9_with_an_empty_catalog_free_text_is_still_accepted_so_unseeded_environments_do_not_block_completion()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);
        await Do(id, "Approve", Executor, null, Data("Leakage"));
        Assert.Equal("Leakage", (await Wo(id)).FailureModeCode);
        Assert.Null((await Wo(id)).FailureModeId);
    }

    // ---------------------------------------------------------------- B11
    [Fact]
    public async Task B11_planning_assignment_and_scheduling_never_throw_even_when_the_workflow_record_is_inconsistent()
    {
        var id = await CreateAsync(); await PlanAsync(id);               // Preparation
        // رکورد ناسازگار: دومین Instance چرخه‌عمر برای همین Work Order (مثلاً ثبت هم‌زمان/دوبل‌کلیک قدیمی)
        var first = await Ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.EntityKey == WorkOrderWorkflow.LifecycleKey(id));
        Ctx.WorkflowInstances.Add(new PEMS.WorkflowPlatform.Entities.WorkflowInstance
        {
            WorkflowVersionId = first.WorkflowVersionId, BusinessObjectType = first.BusinessObjectType, BusinessObjectId = first.BusinessObjectId, EntityKey = first.EntityKey,
            Status = "Cancelled", Module = first.Module, StartDate = DateTime.Now
        });
        await Ctx.SaveChangesAsync();

        var assign = await Svc.AssignExecutorAsync(id, null, Executor.EmployeeId, DisciplineHead);
        Assert.False(assign.Success); Assert.False(string.IsNullOrWhiteSpace(assign.Error));
        var plan = await Svc.SetPlanningAsync(id, Mech, Planner);
        Assert.False(plan.Success); Assert.False(string.IsNullOrWhiteSpace(plan.Error));
        var sched = await Svc.ScheduleWorkOrderAsync(id, DateTime.Today, DateTime.Today.AddDays(1), DisciplineHead);
        Assert.False(sched.Success); Assert.False(string.IsNullOrWhiteSpace(sched.Error));
    }

    [Fact]
    public async Task B11_assign_executor_posts_always_end_in_a_toast_for_every_input_combination()
    {
        var id = await CreateAsync(); await PlanAsync(id);
        var teamId = AddTeam(Mech);
        var ctl = new WorkOrderController(Ctx, null!, new FakeCurrentUserService { Roles = DisciplineHead.Roles.ToList() }, new PermissionService(Ctx, AccessControl),
            null!, Svc, new CrewService(Ctx), null!, null!, new WorkOrderSectionGuard(Ctx, AccessControl)).WithTempData();

        foreach (var (team, emp) in new (int?, int?)[] { (null, null), (teamId, null), (null, Executor.EmployeeId), (teamId, Executor.EmployeeId), (999999, null), (null, 999999) })
        {
            ctl.TempData.Clear();
            var result = await ctl.AssignExecutor(id, team, emp);
            Assert.IsType<RedirectToActionResult>(result);                                         // هرگز استثنا/صفحه‌ی خطا
            Assert.True(ctl.TempData.ContainsKey("Message") || ctl.TempData.ContainsKey("Error"), $"no toast for team={team} emp={emp}");
        }
    }

    // ---------------------------------------------------------------- B13 (EN 13460)
    [Fact]
    public async Task B13_completion_is_blocked_while_a_discipline_referral_is_pending_and_allowed_after_all_respond()
    {
        var id = await CreateAsync("Preventive"); await RunToInProgressAsync(id);
        var hook = new WorkOrderAuthorizationHook(Ctx, AccessControl);
        var runtime = new PEMS.WorkflowPlatform.Services.WorkflowRuntimeService(Ctx, new PEMS.WorkflowPlatform.Services.WorkflowBindingService(Ctx), null, hook, null, WorkOrderWorkflow.CreateRegistry());
        var reviews = new WorkOrderReviewService(Ctx, runtime);
        await reviews.RequestDisciplineReviewAsync(id, new[] { Elec, Instr }, Planner.EmployeeId);

        Assert.Matches("review|بازبینی", await Fail(id, "Approve", Executor, Data(null)));
        var elec = Actor(DeptMaint, Elec, "MNT.Expert"); var instr = Actor(DeptMaint, Instr, "MNT.Expert");
        await reviews.RespondAsync(id, "Approve", "ok", null, null, elec.EmployeeId!.Value, Elec);
        Assert.Matches("review|بازبینی", await Fail(id, "Approve", Executor, Data(null)));           // هنوز یک ارجاع باز است
        await reviews.RespondAsync(id, "Comment", "see note", null, null, instr.EmployeeId!.Value, Instr);
        await Do(id, "Approve", Executor, null, Data(null));
        Assert.Equal("Completed", (await Wo(id)).Status);
    }

    // ---------------------------------------------------------------- B14
    [Fact]
    public async Task B14_hold_cancel_return_and_reject_need_a_reason_which_is_stored_in_both_histories()
    {
        var id = await CreateAsync(); await RunToInProgressAsync(id);
        foreach (var blank in new string?[] { null, "", "  " })
        {
            var (ok, err) = await Svc.ExecuteWorkflowActionAsync(id, "Hold", blank, DisciplineHead);
            Assert.False(ok); Assert.Contains("reason", err!, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal("InProgress", (await Wo(id)).Status);
        Assert.True((await Svc.ExecuteWorkflowActionAsync(id, "Hold", " waiting for spare parts ", DisciplineHead)).Success);
        Assert.Contains(await Ctx.WorkOrderStatusHistories.Where(h => h.WorkOrderId == id).ToListAsync(), h => h.ToStatus == "OnHold" && h.Reason == "waiting for spare parts");
        await Do(id, "Approve", DisciplineHead);                                                     // Resume

        // لغو بعد از آزادسازی (در اجرا)
        Assert.False((await Svc.ExecuteWorkflowActionAsync(id, "Cancel", null, Manager)).Success);
        Assert.True((await Svc.ExecuteWorkflowActionAsync(id, "Cancel", "client withdrew", Manager)).Success);
        Assert.Equal("Cancelled", (await Wo(id)).Status);
        Assert.Contains(await Ctx.WorkflowHistories.ToListAsync(), h => h.ActionType == "Cancel" && h.Comment == "client withdrew");

        // بازگشت برای اصلاح (Operations Decline) و رد در برنامه‌ریزی
        var id2 = await CreateAsync(); await PlanAsync(id2);
        Assert.True((await Svc.AssignExecutorAsync(id2, null, Executor.EmployeeId, DisciplineHead)).Success);
        await Do(id2, "Approve", Planner);
        Assert.False((await Svc.ExecuteWorkflowActionAsync(id2, "Return", null, Ops)).Success);
        Assert.True((await Svc.ExecuteWorkflowActionAsync(id2, "Return", "permit not ready", Ops)).Success);

        var id3 = await CreateAsync(); await Do(id3, "Approve", Creator);
        Assert.False((await Svc.ExecuteWorkflowActionAsync(id3, "Reject", "", Planner)).Success);
        Assert.True((await Svc.ExecuteWorkflowActionAsync(id3, "Reject", "out of scope", Planner)).Success);
    }

    [Fact]
    public async Task B14_legacy_status_path_requires_a_reason_for_hold_cancel_reject()
    {
        // Work Order بدون Instance (Legacy)
        var cutoff = await Ctx.WorkflowBindings.Where(b => b.EntityType == WorkOrderWorkflow.LifecycleEntityType).MinAsync(b => b.CreatedDate);
        var w = new WorkOrder { EquipmentId = EquipmentId, WorkOrderNumber = "WO-LEG1", WorkOrderType = "Preventive", Priority = "Medium", Status = "InProgress", IsActive = true, CreatedDate = cutoff.AddDays(-10) };
        Ctx.WorkOrders.Add(w); await Ctx.SaveChangesAsync();
        w.CreatedDate = cutoff.AddDays(-10); await Ctx.SaveChangesAsync();          // SaveChanges تاریخ ایجاد را هنگام Insert می‌گذارد
        var (ok, err) = await Svc.AdvanceStatusAsync(w.WorkOrderId, "OnHold", " ", null, null, null, null, null, null, null);
        Assert.False(ok); Assert.Contains("reason", err!, StringComparison.OrdinalIgnoreCase);
        Assert.True((await Svc.AdvanceStatusAsync(w.WorkOrderId, "OnHold", "inspection", null, null, null, null, null, null, null)).Success);
    }

    // ---------------------------------------------------------------- B15 (single rule source)
    [Fact]
    public void B15_reason_rule_is_defined_once_and_used_by_both_services()
    {
        foreach (var a in new[] { "Reject", "Cancel", "Hold", "Return" }) Assert.True(WorkflowActionRules.ActionRequiresReason(a));
        foreach (var a in new[] { "Approve", "Complete", "Resume", "Start" }) Assert.False(WorkflowActionRules.ActionRequiresReason(a));
        foreach (var s in new[] { "Rejected", "Cancelled", "OnHold" }) Assert.NotNull(WorkflowActionRules.ValidateStatus(s, " "));
        Assert.Null(WorkflowActionRules.ValidateStatus("Completed", null));
    }
}
