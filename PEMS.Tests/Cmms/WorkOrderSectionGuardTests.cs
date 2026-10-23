using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// H25-H31 روی گردش‌کار واقعی (Seederهای واقعی، بدون Mock): فاز از مرحله‌ی واقعی گردش‌کار خوانده می‌شود، دروازه‌ی مشترک تصمیم می‌گیرد، و POSTهای مستقیم کنترلر
/// (همان مسیری که یک مهاجم/کاربر بدون UI می‌زند) برای هر عمل × فاز × (مجاز/غیرمجاز) سنجیده می‌شوند. همه چیز InMemory است؛ هیچ دیتایی در دیتابیس مشترک نمی‌ماند.
/// </summary>
public class WorkOrderSectionGuardTests : WorkOrderGovernanceTestBase
{
    private readonly WorkOrderSectionGuard _guard;

    public WorkOrderSectionGuardTests()
    {
        WorkOrderReopenPermissionSeeder.EnsureAsync(Ctx).GetAwaiter().GetResult();
        _guard = new WorkOrderSectionGuard(Ctx, AccessControl);
    }

    private static WoCaller Caller(WorkOrderActor a) => new(null, "tester", a.EmployeeId, a.Roles, "10.0.0.9");

    // مجاز = داشتن مجوز لازم همان بخش؛ سرگروه دیسیپلین: Edit + Assign + CorrectActuals؛ برنامه‌ریز: Plan
    private WorkOrderActor Authorized(WoSection s) => s == WoSection.ResponsibleDiscipline ? Planner : DisciplineHead;
    // غیرمجاز = نقشی که مجوز لازم آن بخش را ندارد (Creator: Create/Plan/Prepare بدون Edit/Assign؛ Executor: Edit/Execute بدون Plan)
    private WorkOrderActor Unauthorized(WoSection s) => s == WoSection.ResponsibleDiscipline ? Executor : Creator;

    // ---- ساخت Work Order در هر فاز واقعی ----------------------------------------------------------------------------------
    private async Task<(WoPhase Phase, int Id)[]> BuildAllPhasesAsync()
    {
        var list = new List<(WoPhase, int)>();

        var created = await CreateAsync(); list.Add((WoPhase.Created, created));

        var planning = await CreateAsync(); await Do(planning, "Approve", Creator); list.Add((WoPhase.Planning, planning));

        var prep = await CreateAsync(); await PlanAsync(prep); list.Add((WoPhase.Preparation, prep));

        var issue = await CreateAsync(); await PlanAsync(issue);
        Assert.True((await Svc.AssignExecutorAsync(issue, null, Executor.EmployeeId, DisciplineHead)).Success);
        await Do(issue, "Approve", Planner); list.Add((WoPhase.OperationsIssue, issue));

        var released = await CreateAsync(); await PlanAsync(released);
        Assert.True((await Svc.AssignExecutorAsync(released, null, Executor.EmployeeId, DisciplineHead)).Success);
        await Do(released, "Approve", Planner); await Do(released, "Approve", Ops); list.Add((WoPhase.Released, released));

        var exec = await CreateAsync("Corrective"); await RunToInProgressAsync(exec); list.Add((WoPhase.Execution, exec));

        var hold = await CreateAsync(); await RunToInProgressAsync(hold); await Do(hold, "Hold", DisciplineHead); list.Add((WoPhase.OnHold, hold));

        var completion = await CreateAsync("Corrective"); await RunToInProgressAsync(completion);
        await Do(completion, "Approve", Executor, null, Data()); list.Add((WoPhase.Completion, completion));

        var verification = await CreateAsync("Corrective"); await RunToVerificationAsync(verification); list.Add((WoPhase.Verification, verification));

        var finalApproval = await CreateAsync("Corrective"); await RunToVerificationAsync(finalApproval); await Do(finalApproval, "Approve", Verifier);
        list.Add((WoPhase.FinalApproval, finalApproval));

        var closed = await CreateAsync("Corrective"); await RunToVerificationAsync(closed); await Do(closed, "Approve", Verifier); await Do(closed, "Approve", Approver);
        list.Add((WoPhase.Closed, closed));

        var cancelled = await CreateAsync(); await RunToInProgressAsync(cancelled); await Do(cancelled, "Cancel", Manager); list.Add((WoPhase.Cancelled, cancelled));

        var rejected = await CreateAsync(); await Do(rejected, "Approve", Creator); await Do(rejected, "Reject", Planner); list.Add((WoPhase.Rejected, rejected));

        return list.ToArray();
    }

    [Fact]
    public async Task Phase_is_read_from_the_real_workflow_stage_for_every_lifecycle_step()
    {
        foreach (var (phase, id) in await BuildAllPhasesAsync())
        {
            var page = await _guard.GetPageAsync(id, Caller(Manager));
            Assert.NotNull(page);
            Assert.Equal(phase, page!.Phase);
        }
    }

    [Fact]
    public async Task Guard_decisions_for_every_section_and_phase_match_the_matrix_for_authorized_and_unauthorized_users()
    {
        var phases = await BuildAllPhasesAsync();
        var checkedCells = 0;

        foreach (var (phase, id) in phases)
            foreach (var section in Enum.GetValues<WoSection>())
            {
                var access = WorkOrderSectionPolicy.AccessFor(section, phase);

                // مجاز + دلیل
                var ok = await _guard.AuthorizeAsync(id, section, WoSectionAction.Add, Caller(Authorized(section)), "documented reason");
                var shouldAllow = access is SectionAccess.Editable or SectionAccess.Correctable;
                Assert.True(shouldAllow == ok.Allowed, $"{section} @ {phase}: expected allowed={shouldAllow}, got {ok.Allowed} ({ok.Code})");

                if (!shouldAllow)
                {
                    Assert.Contains(ok.Code, new[] { "SectionNotAvailable", "SectionReadOnly", "WorkOrderImmutable" });
                    Assert.Equal(400, ok.HttpStatus);
                    Assert.False(string.IsNullOrWhiteSpace(ok.MessageFa)); Assert.False(string.IsNullOrWhiteSpace(ok.MessageEn));
                }

                // Correctable بدون دلیل → رد با ReasonRequired
                if (access == SectionAccess.Correctable)
                {
                    var noReason = await _guard.AuthorizeAsync(id, section, WoSectionAction.Add, Caller(Authorized(section)), null);
                    Assert.False(noReason.Allowed); Assert.Equal("ReasonRequired", noReason.Code); Assert.Equal(400, noReason.HttpStatus);
                }

                // غیرمجاز: هرگز مجاز نیست؛ در بخش‌های قابل‌ویرایش دلیلش «مجوز» و HTTP 403 است
                var denied = await _guard.AuthorizeAsync(id, section, WoSectionAction.Add, Caller(Unauthorized(section)), "documented reason");
                Assert.False(denied.Allowed, $"{section} @ {phase}: unauthorized user was allowed");
                if (access is SectionAccess.Editable or SectionAccess.Correctable)
                {
                    Assert.Equal("MissingPermission", denied.Code); Assert.Equal(403, denied.HttpStatus);
                    Assert.Contains(WorkOrderSectionPolicy.RequiredPermission(section), denied.MessageEn);
                }
                checkedCells++;
            }

        Assert.Equal(phases.Length * Enum.GetValues<WoSection>().Length, checkedCells);
    }

    [Fact]
    public async Task Correction_after_execution_needs_the_correction_permission_in_addition_to_edit()
    {
        var id = await CreateAsync("Corrective"); await RunToVerificationAsync(id);
        // Executor دارای Edit است ولی CorrectActuals ندارد
        var denied = await _guard.AuthorizeAsync(id, WoSection.Labor, WoSectionAction.Add, Caller(Executor), "typo");
        Assert.False(denied.Allowed); Assert.Equal("MissingCorrectionPermission", denied.Code); Assert.Equal(403, denied.HttpStatus);
        Assert.Contains("WorkOrder.CorrectActuals", denied.MessageEn);

        var allowed = await _guard.AuthorizeAsync(id, WoSection.Labor, WoSectionAction.Add, Caller(DisciplineHead), "typo");
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task Denied_attempts_are_logged_and_attempts_on_finished_work_orders_become_security_events()
    {
        var phases = await BuildAllPhasesAsync();
        var closed = phases.Single(p => p.Phase == WoPhase.Closed).Id;
        var created = phases.Single(p => p.Phase == WoPhase.Created).Id;

        await _guard.AuthorizeAsync(closed, WoSection.Crew, WoSectionAction.Add, Caller(DisciplineHead), null, "CR-1");
        await _guard.AuthorizeAsync(created, WoSection.Labor, WoSectionAction.Add, Caller(DisciplineHead), null);       // بخش در دسترس نیست (غیر پایانی)
        await _guard.AuthorizeAsync(created, WoSection.Crew, WoSectionAction.Add, Caller(Creator), null);               // مجوز ندارد

        var logs = await Ctx.WorkOrderChangeLogs.AsNoTracking().OrderBy(l => l.WorkOrderChangeLogId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, l => { Assert.Equal("Denied", l.Outcome); Assert.NotNull(l.ChangedByEmployeeId); Assert.Equal("10.0.0.9", l.IpAddress); });

        var onClosed = logs[0];
        Assert.Equal(closed, onClosed.WorkOrderId); Assert.Equal("WorkOrderImmutable", onClosed.Code); Assert.True(onClosed.IsSecurityEvent);
        Assert.Equal("Closed", onClosed.WorkOrderStatus); Assert.Equal("CR-1", onClosed.RecordRef);

        Assert.False(logs[1].IsSecurityEvent);                 // ردِ قاعده‌ی فاز روی Work Order فعال: رویداد عملیاتی، نه امنیتی
        Assert.True(logs[2].IsSecurityEvent);                  // تلاش بدون مجوز: رویداد امنیتی
        Assert.Equal("MissingPermission", logs[2].Code);

        // و برای Work Order پایان‌یافته، ردی هم در AuthorizationAuditLog (منبع حقیقت رویدادهای امنیتی) ثبت می‌شود
        Assert.Contains(await Ctx.AuthorizationAuditLogs.AsNoTracking().ToListAsync(),
            a => a.ResourceName == "WorkOrder" && a.ActionName == "ChildMutation:Crew.Add" && a.ResourceId == closed.ToString());
    }

    [Fact]
    public async Task Unknown_work_order_is_404_and_never_throws()
    {
        var r = await _guard.AuthorizeAsync(987654, WoSection.Labor, WoSectionAction.Add, Caller(DisciplineHead), "x");
        Assert.False(r.Allowed); Assert.Equal(404, r.HttpStatus);
        Assert.Null(await _guard.GetPageAsync(987654, Caller(DisciplineHead)));
    }

    [Fact]
    public async Task Page_model_hides_controls_the_user_cannot_use_and_offers_reopen_only_with_permission()
    {
        var phases = await BuildAllPhasesAsync();
        var exec = phases.Single(p => p.Phase == WoPhase.Execution).Id;
        var closed = phases.Single(p => p.Phase == WoPhase.Closed).Id;

        var asExecutor = await _guard.GetPageAsync(exec, Caller(Executor));
        Assert.True(asExecutor!.Controls(WoSection.Labor)); Assert.True(asExecutor.Controls(WoSection.SpareParts)); Assert.True(asExecutor.Controls(WoSection.JobPlanTasks));
        Assert.False(asExecutor.Controls(WoSection.Crew));                       // اجرا: Crew فقط‌خواندنی
        Assert.False(asExecutor.Controls(WoSection.ExecutorAssignment));        // و تخصیص/زمان‌بندی فقط در Preparation

        var asCreator = await _guard.GetPageAsync(exec, Caller(Creator));       // بدون Edit
        Assert.False(asCreator!.Controls(WoSection.Labor)); Assert.False(asCreator.Controls(WoSection.SpareParts));

        var closedAsManager = await _guard.GetPageAsync(closed, Caller(Manager));
        Assert.True(closedAsManager!.CanReopen);
        Assert.All(Enum.GetValues<WoSection>(), s => Assert.False(closedAsManager.Controls(s)));

        var closedAsExecutor = await _guard.GetPageAsync(closed, Caller(Executor));
        Assert.False(closedAsExecutor!.CanReopen);
        Assert.False((await _guard.GetPageAsync(exec, Caller(Manager)))!.CanReopen);         // Reopen فقط برای Closed
    }

    // ==============================================================================================================
    //  POST مستقیم کنترلر: عمل × فاز × (مجاز / غیرمجاز) - هر عمل جهشی فرزند
    // ==============================================================================================================
    private WorkOrderController BuildController(WorkOrderActor who, bool ajax = false, int? userId = null)
    {
        var accessControl = AccessControl;
        var permissionService = new PermissionService(Ctx, accessControl);
        var currentUser = new FakeCurrentUserService { UserId = userId, UserName = "tester", Roles = who.Roles.ToList() };
        var controller = new WorkOrderController(Ctx, new WorkOrderReviewService(Ctx, new PEMS.WorkflowPlatform.Services.WorkflowRuntimeService(Ctx)), currentUser, permissionService,
            new WorkOrderLaborService(Ctx, accessControl), Svc, new CrewService(Ctx), null!, null!, _guard).WithTempData();
        controller.ControllerContext.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.9");
        if (ajax) controller.ControllerContext.HttpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        return controller;
    }

    private int _seed;
    private async Task<(int SkillId, int MaterialId)> SeedCatalogAsync()
    {
        var skill = new Skill { Name = "Welding-" + (++_seed), IsActive = true };
        var material = new MaterialItem { MaterialCode = "MM-" + _seed, Description = "Seal", IsStockItem = true, IsActive = true };
        var wh = new Warehouse { Code = "WH-" + _seed, Name = "Main", IsActive = true };
        Ctx.Skills.Add(skill); Ctx.MaterialItems.Add(material); Ctx.Warehouses.Add(wh);
        await Ctx.SaveChangesAsync();
        Ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = material.MaterialItemId, WarehouseId = wh.WarehouseId, OnHandQuantity = 100, IsActive = true });
        await Ctx.SaveChangesAsync();
        return (skill.SkillId, material.MaterialItemId);
    }

    private sealed record Post(string Name, WoSection Section, Func<WorkOrderController, int, Task<IActionResult>> Run, Func<int, Task<int>> Count);

    private List<Post> Posts((int SkillId, int MaterialId) c) => new()
    {
        new("AddRequiredSkill", WoSection.RequiredSkills, (ctl, id) => ctl.AddRequiredSkill(id, c.SkillId, "Basic", 2), id => Ctx.RequiredSkills.CountAsync(x => x.WorkOrderId == id)),
        new("AddRequiredMaterial", WoSection.RequiredMaterials, (ctl, id) => ctl.AddRequiredMaterial(id, c.MaterialId, 3), id => Ctx.WorkOrderMaterials.CountAsync(x => x.WorkOrderId == id)),
        new("CreateCrewForWorkOrder", WoSection.Crew, (ctl, id) => ctl.CreateCrewForWorkOrder(id, "CR-" + id + "-" + (++_seed), "Crew"), id => Ctx.Crews.CountAsync(x => x.WorkOrderId == id)),
        new("RequestDisciplineReview", WoSection.DisciplineReview, (ctl, id) => ctl.RequestDisciplineReview(id, new List<int> { Elec }), id => Ctx.WorkOrderDisciplineReferrals.CountAsync(x => x.WorkOrderId == id)),
        new("AddSpareUsage", WoSection.SpareParts, (ctl, id) => ctl.AddSpareUsage(id, c.MaterialId, 1, 10, "reason"), id => Ctx.MaintenanceSpareUsages.CountAsync(x => x.WorkOrderId == id)),
    };

    [Fact]
    public async Task Direct_posts_are_accepted_or_rejected_exactly_as_the_matrix_says_for_every_phase_and_never_500()
    {
        var phases = await BuildAllPhasesAsync();
        var catalog = await SeedCatalogAsync();
        var cells = 0;

        foreach (var post in Posts(catalog))
            foreach (var (phase, id) in phases)
            {
                var access = WorkOrderSectionPolicy.AccessFor(post.Section, phase);
                var mayChange = access is SectionAccess.Editable or SectionAccess.Correctable;
                var before = await post.Count(id);

                // --- کاربر مجاز (Edit + CorrectActuals): فقط وقتی ماتریس اجازه می‌دهد داده تغییر می‌کند
                var result = await post.Run(BuildController(DisciplineHead), id);
                var after = await post.Count(id);
                if (mayChange)
                {
                    Assert.True(after == before + 1, $"{post.Name} @ {phase}: expected one new row, before={before} after={after}");
                    Assert.IsType<RedirectToActionResult>(result);
                }
                else
                {
                    Assert.True(after == before, $"{post.Name} @ {phase}: row was created although the section is {access}");
                    var redirect = Assert.IsType<RedirectToActionResult>(result);        // PRG + Toast، نه 500 و نه سکوت
                    Assert.Equal("Details", redirect.ActionName);
                }

                // --- همان POST به‌صورت AJAX: وضعیت واقعی HTTP 400 با بدنه‌ی JSON (در موارد رد)
                if (!mayChange)
                {
                    var ajax = await post.Run(BuildController(DisciplineHead, ajax: true), id);
                    var obj = Assert.IsType<ObjectResult>(ajax);
                    Assert.Equal(400, obj.StatusCode);
                    Assert.Equal(before, await post.Count(id));
                }

                // --- کاربر بدون مجوز: هرگز تغییری ایجاد نمی‌کند (Forbid یا 403)
                var unauthorized = BuildController(Creator);                      // Create/Plan/Prepare، بدون WorkOrder.Edit
                var denied = await post.Run(unauthorized, id);
                Assert.IsType<ForbidResult>(denied);
                Assert.Equal(after, await post.Count(id));
                cells++;
            }

        Assert.Equal(Posts(catalog).Count * phases.Length, cells);
    }

    /// <summary>H27: بقیه‌ی POSTهای فرزند (حذف مهارت/متریال، برگشت قطعه، چک‌لیست، برنامه‌ریزی، تخصیص، زمان‌بندی) روی Created / In Execution / Completion / Verification / Closed.</summary>
    [Fact]
    public async Task Remaining_child_posts_are_rejected_with_a_message_and_change_nothing_in_the_phases_the_matrix_forbids()
    {
        var catalog = await SeedCatalogAsync();
        var created = await CreateAsync();
        var execution = await CreateAsync("Corrective"); await RunToInProgressAsync(execution);
        var completion = await CreateAsync("Corrective"); await RunToInProgressAsync(completion); await Do(completion, "Approve", Executor, null, Data());
        var verification = await CreateAsync("Corrective"); await RunToVerificationAsync(verification);
        var closed = await CreateAsync("Corrective"); await RunToVerificationAsync(closed); await Do(closed, "Approve", Verifier); await Do(closed, "Approve", Approver);

        var phases = new (WoPhase Phase, int Id)[] { (WoPhase.Created, created), (WoPhase.Execution, execution), (WoPhase.Completion, completion), (WoPhase.Verification, verification), (WoPhase.Closed, closed) };
        var cells = 0;
        foreach (var (phase, id) in phases)
        {
            // داده‌ی فرزند موجود برای حذف/برگشت/چک‌لیست
            Ctx.RequiredSkills.Add(new RequiredSkill { WorkOrderId = id, SkillId = catalog.SkillId, CreatedDate = DateTime.Now });
            Ctx.WorkOrderMaterials.Add(new WorkOrderMaterial { WorkOrderId = id, MaterialItemId = catalog.MaterialId, RequiredQuantity = 1 });
            var task = new WorkOrderTask { WorkOrderId = id, TaskNumber = 1, Title = "T", IsCompleted = false };
            Ctx.WorkOrderTasks.Add(task);
            var usage = new MaintenanceSpareUsage { WorkOrderId = id, MaterialItemId = catalog.MaterialId, Quantity = 2, UnitCost = 10, TotalCost = 20, IsActive = true };
            Ctx.MaintenanceSpareUsages.Add(usage);
            await Ctx.SaveChangesAsync();
            var skillId = Ctx.RequiredSkills.Single(x => x.WorkOrderId == id).RequiredSkillId;
            var materialId = Ctx.WorkOrderMaterials.Single(x => x.WorkOrderId == id).WorkOrderMaterialId;

            var ctl = BuildController(DisciplineHead);
            var planner = BuildController(Planner);

            async Task Rejected(Func<Task<IActionResult>> post, Func<Task<bool>> unchanged, string name)
            {
                ctl.TempData.Clear(); planner.TempData.Clear();
                var r = await post();
                Assert.True(r is RedirectToActionResult, $"{name} @ {phase}: expected redirect, got {r.GetType().Name}");
                Assert.True(await unchanged(), $"{name} @ {phase}: data changed although the matrix forbids it");
                cells++;
            }

            var skillsOpen = WorkOrderSectionPolicy.AccessFor(WoSection.RequiredSkills, phase) == SectionAccess.Editable;
            if (!skillsOpen)
            {
                await Rejected(() => ctl.RemoveRequiredSkill(skillId, id), async () => await Ctx.RequiredSkills.AnyAsync(x => x.RequiredSkillId == skillId), "RemoveRequiredSkill");
                await Rejected(() => ctl.RemoveRequiredMaterial(materialId, id), async () => await Ctx.WorkOrderMaterials.AnyAsync(x => x.WorkOrderMaterialId == materialId), "RemoveRequiredMaterial");
                Assert.NotNull(ctl.TempData["Error"]);
            }
            else
            {
                await ctl.RemoveRequiredSkill(skillId, id); await ctl.RemoveRequiredMaterial(materialId, id);          // Created: مجاز
                Assert.False(await Ctx.RequiredSkills.AnyAsync(x => x.RequiredSkillId == skillId)); Assert.False(await Ctx.WorkOrderMaterials.AnyAsync(x => x.WorkOrderMaterialId == materialId));
            }

            var tasksOpen = phase == WoPhase.Execution;
            if (!tasksOpen)
            {
                await Rejected(() => ctl.CompleteTask(task.WorkOrderTaskId, id), async () => !(await Ctx.WorkOrderTasks.AsNoTracking().SingleAsync(x => x.WorkOrderTaskId == task.WorkOrderTaskId)).IsCompleted, "CompleteTask");
                task.IsCompleted = true; await Ctx.SaveChangesAsync();
                await Rejected(() => ctl.ReopenTask(task.WorkOrderTaskId, id), async () => (await Ctx.WorkOrderTasks.AsNoTracking().SingleAsync(x => x.WorkOrderTaskId == task.WorkOrderTaskId)).IsCompleted, "ReopenTask");
            }
            else
            {
                await ctl.CompleteTask(task.WorkOrderTaskId, id);
                Assert.True((await Ctx.WorkOrderTasks.AsNoTracking().SingleAsync(x => x.WorkOrderTaskId == task.WorkOrderTaskId)).IsCompleted);
            }

            // برگشت قطعه: Editable فقط در Execution؛ در Completion/Verification اصلاح (با دلیل و مجوز) و در Closed ممنوع. بدون مبدأ Issue برگشت در سرویس رد می‌شود - فقط ممنوعیت فاز سنجیده می‌شود
            if (phase is WoPhase.Created or WoPhase.Closed)
                await Rejected(() => ctl.ReturnSpareUsage(usage.MaintenanceSpareUsageId, id, 1, "reason"), async () => (await Ctx.MaintenanceSpareUsages.AsNoTracking().SingleAsync(x => x.MaintenanceSpareUsageId == usage.MaintenanceSpareUsageId)).ReturnedQuantity is null or 0, "ReturnSpareUsage");

            // برنامه‌ریزی / تخصیص / زمان‌بندی: فقط Planning/Preparation (دیسیپلین) و Preparation (تخصیص/زمان‌بندی)
            var woBefore = await Ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == id);
            await Rejected(() => planner.SetPlanning(id, Elec), async () => (await Ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == id)).ResponsibleDisciplineId == woBefore.ResponsibleDisciplineId, "SetPlanning");
            await Rejected(() => ctl.AssignExecutor(id, null, Executor.EmployeeId), async () => (await Ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == id)).AssignedEmployeeId == woBefore.AssignedEmployeeId, "AssignExecutor");
            await Rejected(() => ctl.SetSchedule(id, DateTime.Today, DateTime.Today.AddDays(1)), async () => (await Ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == id)).PlannedStart == woBefore.PlannedStart, "SetSchedule");
            Assert.NotNull(ctl.TempData["Error"]);          // آخرین رد (SetSchedule) پیام مشخص دارد
        }
        Assert.True(cells >= 25, $"only {cells} rejected cells were exercised");

        // و در Closed هر رویداد امنیتی ثبت شده است
        Assert.True(await Ctx.WorkOrderChangeLogs.CountAsync(l => l.WorkOrderId == closed && l.Outcome == "Denied" && l.IsSecurityEvent) >= 6);
    }

    [Fact]
    public async Task Controller_rejection_shows_a_specific_localized_message_and_policy_code_instead_of_failing_silently()
    {
        var closed = await CreateAsync("Corrective"); await RunToVerificationAsync(closed); await Do(closed, "Approve", Verifier); await Do(closed, "Approve", Approver);
        var ctl = BuildController(DisciplineHead);

        var result = await ctl.CreateCrewForWorkOrder(closed, "AUDITCREW", "Audit crew");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("immutable", (string)ctl.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WorkOrderImmutable", ctl.TempData["PolicyCode"]);
        Assert.Equal("WorkOrderImmutable", ctl.Response.Headers["X-PEMS-Policy"].ToString());
        Assert.Empty(Ctx.Crews.Where(x => x.Code == "AUDITCREW"));
    }

    [Fact]
    public async Task Crew_on_a_closed_work_order_fails_through_the_crew_controller_too()
    {
        var closed = await CreateAsync("Corrective"); await RunToVerificationAsync(closed); await Do(closed, "Approve", Verifier); await Do(closed, "Approve", Approver);
        var open = await CreateAsync();
        var crewOnOpen = new Crew { Code = "OPEN-CREW", Name = "c", WorkOrderId = open, IsActive = true };
        var crewOnClosed = new Crew { Code = "OLD-CREW", Name = "c", WorkOrderId = closed, IsActive = true };
        Ctx.Crews.AddRange(crewOnOpen, crewOnClosed);
        await Ctx.SaveChangesAsync();

        // SystemAdmin: بایپس سراسری مجوز ماژول Crew؛ ماتریس Work Order (فاز) برای او هم اعمال می‌شود - همین نکته‌ی تست است
        var permissions = new PermissionService(Ctx, AccessControl);
        var controller = new CrewController(Ctx, new CrewService(Ctx), new FakeCurrentUserService { Roles = new List<string> { SystemRoles.SystemAdmin } }, permissions, _guard).WithTempData();

        // ایجاد Crew جدید روی WO بسته → رد
        var created = await controller.CreateCrew("AUDITCREW", "Audit", closed);
        Assert.IsType<RedirectToActionResult>(created);
        Assert.Empty(Ctx.Crews.Where(x => x.Code == "AUDITCREW"));
        Assert.Equal("WorkOrderImmutable", controller.TempData["PolicyCode"]);

        // افزودن عضو به Crew وصل به WO بسته → رد؛ در WO باز (Created) → مجاز
        var emp = Executor.EmployeeId!.Value;
        var memberRole = new MaintenanceRole { Name = "Fitter", IsActive = true }; Ctx.MaintenanceRoles.Add(memberRole); await Ctx.SaveChangesAsync();
        await controller.AddMember(crewOnClosed.CrewId, emp, memberRole.MaintenanceRoleId);
        Assert.Empty(Ctx.CrewMembers.Where(m => m.CrewId == crewOnClosed.CrewId));

        controller.TempData.Clear();
        await controller.AddMember(crewOnOpen.CrewId, emp, memberRole.MaintenanceRoleId);
        Assert.Single(Ctx.CrewMembers.Where(m => m.CrewId == crewOnOpen.CrewId));

        // خروج عضو از Crew وصل به WO بسته → رد (و Crew مستقل بدون WO همچنان آزاد است)
        var oldMember = new CrewMember { CrewId = crewOnClosed.CrewId, EmployeeId = emp, RoleId = memberRole.MaintenanceRoleId, IsActive = true, JoinedDate = DateTime.Now };
        Ctx.CrewMembers.Add(oldMember); await Ctx.SaveChangesAsync();
        await controller.RemoveMember(oldMember.CrewMemberId);
        Assert.True(Ctx.CrewMembers.AsNoTracking().Single(m => m.CrewMemberId == oldMember.CrewMemberId).IsActive);

        var standalone = await controller.CreateCrew("STANDALONE", "no work order", null);
        Assert.IsType<RedirectToActionResult>(standalone);
        Assert.Single(Ctx.Crews.Where(x => x.Code == "STANDALONE"));
    }

    [Fact]
    public async Task Crew_service_rejects_a_work_order_that_does_not_exist()
    {
        var (ok, error, id) = await new CrewService(Ctx).CreateAsync("GHOST", "ghost", 424242);
        Assert.False(ok); Assert.Null(id); Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(Ctx.Crews);
    }

    // ==============================================================================================================
    //  H27-H31 رفتار پایان‌به‌پایان: ثبت مجاز، ممیزی قبل/بعد، اصلاح، منجمد‌شدن هزینه، Reopen
    // ==============================================================================================================
    private async Task<int> SeedRateAsync(int employeeId, decimal rate = 100, decimal overtime = 150)
    {
        Ctx.EmployeeLaborRates.AddRange(
            new EmployeeLaborRate { EmployeeId = employeeId, RateType = "Normal", Rate = rate, EffectiveFrom = DateTime.Today.AddYears(-1), IsActive = true },
            new EmployeeLaborRate { EmployeeId = employeeId, RateType = "Overtime", Rate = overtime, EffectiveFrom = DateTime.Today.AddYears(-1), IsActive = true });
        await Ctx.SaveChangesAsync();
        return employeeId;
    }

    private async Task<int> UserForAsync(WorkOrderActor actor)
    {
        var u = new ApplicationUser { UserName = "u" + actor.EmployeeId, NormalizedUserName = "U" + actor.EmployeeId, EmployeeId = actor.EmployeeId, Email = "u" + actor.EmployeeId + "@t.local" };
        Ctx.Users.Add(u);
        await Ctx.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public async Task Labor_and_spare_changes_in_execution_are_accepted_and_audited_with_who_when_state_and_values()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);
        var catalog = await SeedCatalogAsync();
        await SeedRateAsync(Executor.EmployeeId!.Value);
        var userId = await UserForAsync(Executor);
        var ctl = BuildController(Executor, userId: userId);

        await ctl.AddLaborEntry(id, Executor.EmployeeId!.Value, null, DateTime.Today, 4, 1, "weld", null);
        await ctl.AddSpareUsage(id, catalog.MaterialId, 2, 10, null);

        Assert.Single(Ctx.WorkOrderLabors.Where(l => l.WorkOrderId == id));
        var labor = Ctx.WorkOrderLabors.Single(l => l.WorkOrderId == id);
        Assert.Equal(100, labor.Rate); Assert.Equal(150, labor.OvertimeRate); Assert.Equal(4 * 100 + 1 * 150, labor.LaborCost);   // Snapshot نرخ
        Assert.Single(Ctx.MaintenanceSpareUsages.Where(s => s.WorkOrderId == id));

        var logs = await Ctx.WorkOrderChangeLogs.AsNoTracking().Where(l => l.WorkOrderId == id && l.Outcome == "Allowed").ToListAsync();
        Assert.Equal(2, logs.Count);
        var l1 = logs.Single(l => l.Section == "Labor");
        Assert.Equal("Add", l1.Action); Assert.Equal(userId, l1.ChangedByUserId); Assert.Equal(Executor.EmployeeId, l1.ChangedByEmployeeId);
        Assert.Equal("InProgress", l1.WorkOrderStatus); Assert.Equal("In Execution", l1.WorkflowStage);
        Assert.Contains("RegularHours=4", l1.AfterValue); Assert.Contains("OvertimeHours=1", l1.AfterValue);
        Assert.True(l1.ChangedDate > DateTime.Now.AddMinutes(-5));
        Assert.Contains("MaterialItemId=" + catalog.MaterialId, logs.Single(l => l.Section == "SpareParts").AfterValue);
    }

    [Fact]
    public async Task Correction_before_final_approval_keeps_the_rate_snapshot_requires_a_reason_and_is_audited()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);
        var emp = Executor.EmployeeId!.Value;
        await SeedRateAsync(emp, 100, 150);
        var userId = await UserForAsync(DisciplineHead);
        var execCtl = BuildController(Executor, userId: await UserForAsync(Executor));
        await execCtl.AddLaborEntry(id, emp, null, DateTime.Today, 2, 0, null, null);
        var originalRate = Ctx.WorkOrderLabors.Single().Rate;

        // نرخ بعداً تغییر می‌کند؛ رکورد قبلی نباید عوض شود
        foreach (var r in Ctx.EmployeeLaborRates.ToList()) r.IsActive = false;
        Ctx.EmployeeLaborRates.Add(new EmployeeLaborRate { EmployeeId = emp, RateType = "Normal", Rate = 999, EffectiveFrom = DateTime.Today.AddDays(-1), IsActive = true });
        await Ctx.SaveChangesAsync();

        await Do(id, "Approve", Executor, null, Data()); await Do(id, "Approve", Executor);     // → Verification (وضعیت Completed)
        var corrector = BuildController(DisciplineHead, userId: userId);

        // بدون دلیل → رد؛ با دلیل → ثبت
        await corrector.AddLaborEntry(id, emp, null, DateTime.Today, 1, 0, "missed hour", null);
        Assert.Single(Ctx.WorkOrderLabors);
        Assert.Contains("reason", (string)corrector.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);

        await corrector.AddLaborEntry(id, emp, null, DateTime.Today, 1, 0, "missed hour", "forgot to log the second shift");
        Assert.Equal(2, Ctx.WorkOrderLabors.Count());
        Assert.Equal(originalRate, Ctx.WorkOrderLabors.OrderBy(l => l.WorkOrderLaborId).First().Rate);          // رکورد قبلی دست‌نخورده
        Assert.Equal(999, Ctx.WorkOrderLabors.OrderBy(l => l.WorkOrderLaborId).Last().Rate);                    // نرخ جدید فقط برای ثبت جدید

        var log = await Ctx.WorkOrderChangeLogs.AsNoTracking().SingleAsync(l => l.Outcome == "Allowed" && l.Section == "Labor" && l.Reason != null);
        Assert.Equal("forgot to log the second shift", log.Reason); Assert.Equal("Completed", log.WorkOrderStatus);
        Assert.Equal("Originating-Discipline Verification", log.WorkflowStage); Assert.Equal(userId, log.ChangedByUserId);
    }

    [Fact]
    public async Task Cost_totals_are_frozen_after_close_and_all_closed_attempts_are_rejected_without_changing_money()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);
        var emp = Executor.EmployeeId!.Value; await SeedRateAsync(emp);
        var catalog = await SeedCatalogAsync();
        var execCtl = BuildController(Executor, userId: await UserForAsync(Executor));
        await execCtl.AddLaborEntry(id, emp, null, DateTime.Today, 3, 0, null, null);
        await execCtl.AddSpareUsage(id, catalog.MaterialId, 2, 25, null);
        await Do(id, "Approve", Executor, null, Data()); await Do(id, "Approve", Executor);
        await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);
        Assert.Equal("Closed", (await Wo(id)).Status);

        decimal Labor() => Ctx.WorkOrderLabors.AsNoTracking().Where(l => l.WorkOrderId == id).Sum(l => l.LaborCost);
        decimal Spares() => Ctx.MaintenanceSpareUsages.AsNoTracking().Where(s => s.WorkOrderId == id).Sum(s => s.TotalCost ?? 0);
        var history = await Ctx.MaintenanceHistories.AsNoTracking().SingleAsync(m => m.WorkOrderId == id);
        Assert.Equal(300 + 50, Labor() + Spares());
        Assert.Equal(Labor() + Spares(), history.Cost);                                                   // MaintenanceHistory = مجموع نهایی منجمد
        var frozenLabor = Labor(); var frozenSpares = Spares(); var frozenStock = Ctx.MaterialStocks.AsNoTracking().Sum(s => s.OnHandQuantity);

        // تلاش‌های متعدد روی WO بسته (کاربر مجاز و حتی دارای مجوز اصلاح)
        foreach (var who in new[] { DisciplineHead, Executor, Manager })
        {
            var ctl = BuildController(who, userId: await UserForAsync(who));
            await ctl.AddLaborEntry(id, emp, null, DateTime.Today, 5, 5, "late", "please");
            await ctl.AddSpareUsage(id, catalog.MaterialId, 1, 99, "please");
            await ctl.AddRequiredSkill(id, catalog.SkillId, null, null);
            await ctl.AddRequiredMaterial(id, catalog.MaterialId, 1);
        }
        await Assert.ThrowsAsync<Exception>(() => new WorkOrderLaborService(Ctx, AccessControl).AddLaborEntryAsync(id, emp, null, DateTime.Today, 1, 0, null, Executor, isCorrection: true));

        Assert.Equal(frozenLabor, Labor()); Assert.Equal(frozenSpares, Spares()); Assert.Equal(frozenStock, Ctx.MaterialStocks.AsNoTracking().Sum(s => s.OnHandQuantity));
        Assert.Equal(history.Cost, (await Ctx.MaintenanceHistories.AsNoTracking().SingleAsync(m => m.WorkOrderId == id)).Cost);
        Assert.Empty(Ctx.RequiredSkills.Where(x => x.WorkOrderId == id));
        Assert.True(await Ctx.WorkOrderChangeLogs.CountAsync(l => l.WorkOrderId == id && l.Outcome == "Denied" && l.IsSecurityEvent && l.WorkOrderStatus == "Closed") >= 8);   // Executor و DisciplineHead × ۴ عمل؛ Manager پیش از دروازه به‌خاطر نبود WorkOrder.Edit Forbid می‌شود
    }

    [Fact]
    public async Task Labor_and_spare_on_a_created_work_order_fail_with_a_specific_message()
    {
        var id = await CreateAsync();
        var catalog = await SeedCatalogAsync();
        var ctl = BuildController(DisciplineHead);

        await ctl.AddLaborEntry(id, Executor.EmployeeId!.Value, null, DateTime.Today, 2, 0, null, null);
        Assert.Contains("not available", (string)ctl.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);
        ctl.TempData.Clear();
        await ctl.AddSpareUsage(id, catalog.MaterialId, 1, 5, null);
        Assert.Contains("not available", (string)ctl.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Ctx.WorkOrderLabors); Assert.Empty(Ctx.MaintenanceSpareUsages); Assert.Empty(Ctx.InventoryTransactions);
        Assert.Equal(100, Ctx.MaterialStocks.Single().OnHandQuantity);
    }

    [Fact]
    public async Task Removing_a_record_of_another_work_order_is_not_possible_by_changing_the_work_order_id_in_the_form()
    {
        var a = await CreateAsync(); var b = await CreateAsync();
        var catalog = await SeedCatalogAsync();
        Ctx.RequiredSkills.Add(new RequiredSkill { WorkOrderId = b, SkillId = catalog.SkillId, CreatedDate = DateTime.Now });
        Ctx.WorkOrderMaterials.Add(new WorkOrderMaterial { WorkOrderId = b, MaterialItemId = catalog.MaterialId, RequiredQuantity = 1 });
        await Ctx.SaveChangesAsync();
        var ctl = BuildController(DisciplineHead);

        await ctl.RemoveRequiredSkill(Ctx.RequiredSkills.Single().RequiredSkillId, a);                   // رکورد WO دیگر، با workOrderId خودِ من
        await ctl.RemoveRequiredMaterial(Ctx.WorkOrderMaterials.Single().WorkOrderMaterialId, a);

        Assert.Single(Ctx.RequiredSkills); Assert.Single(Ctx.WorkOrderMaterials);
    }

    [Fact]
    public async Task Removing_required_skill_and_material_is_audited_with_before_values_when_allowed()
    {
        var id = await CreateAsync(); var catalog = await SeedCatalogAsync();
        Ctx.RequiredSkills.Add(new RequiredSkill { WorkOrderId = id, SkillId = catalog.SkillId, MinimumProficiencyLevel = "Basic", PlannedHours = 3, CreatedDate = DateTime.Now });
        Ctx.WorkOrderMaterials.Add(new WorkOrderMaterial { WorkOrderId = id, MaterialItemId = catalog.MaterialId, RequiredQuantity = 4 });
        await Ctx.SaveChangesAsync();
        var ctl = BuildController(DisciplineHead);

        await ctl.RemoveRequiredSkill(Ctx.RequiredSkills.Single().RequiredSkillId, id);
        await ctl.RemoveRequiredMaterial(Ctx.WorkOrderMaterials.Single().WorkOrderMaterialId, id);

        Assert.Empty(Ctx.RequiredSkills); Assert.Empty(Ctx.WorkOrderMaterials);
        var logs = await Ctx.WorkOrderChangeLogs.AsNoTracking().Where(l => l.Action == "Remove" && l.Outcome == "Allowed").ToListAsync();
        Assert.Contains(logs, l => l.Section == "RequiredSkills" && l.BeforeValue!.Contains("Level=Basic") && l.BeforeValue.Contains("PlannedHours=3"));
        Assert.Contains(logs, l => l.Section == "RequiredMaterials" && l.BeforeValue!.Contains("RequiredQuantity=4"));
    }

    // ---- Reopen (H28) ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Reopen_requires_permission_and_reason_returns_to_verification_and_is_audited_everywhere()
    {
        var id = await CreateAsync("Corrective"); await RunToVerificationAsync(id); await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);
        Assert.Equal("Closed", (await Wo(id)).Status);

        // بدون مجوز (Executor) → رد + رویداد امنیتی
        var noPerm = await Svc.ReopenAsync(id, "need to fix", Executor, "tester", null, "10.0.0.9");
        Assert.False(noPerm.Success); Assert.Contains("WorkOrder.Reopen", noPerm.Error);
        Assert.Equal("Closed", (await Wo(id)).Status);
        Assert.Contains(await Ctx.WorkOrderChangeLogs.AsNoTracking().ToListAsync(), l => l.Action == "Reopen" && l.Outcome == "Denied" && l.IsSecurityEvent);

        // مجوز دارد ولی دلیل ندارد → رد (حتی فاصله)
        foreach (var blank in new string?[] { null, "", "   " })
        {
            var noReason = await Svc.ReopenAsync(id, blank, Manager);
            Assert.False(noReason.Success);
        }
        Assert.Equal("Closed", (await Wo(id)).Status);

        // مجاز + دلیل
        var ok = await Svc.ReopenAsync(id, "wrong spare part quantity", Manager, "manager", null, "10.0.0.9");
        Assert.True(ok.Success, ok.Error);
        Assert.Equal("Completed", (await Wo(id)).Status);
        var inst = await Ctx.WorkflowInstances.Include(i => i.CurrentStage).SingleAsync(i => i.EntityKey == WorkOrderWorkflow.LifecycleKey(id));
        Assert.Equal("Running", inst.Status); Assert.Equal("Originating-Discipline Verification", inst.CurrentStage!.Name);
        Assert.Contains(await Ctx.WorkflowHistories.AsNoTracking().Where(h => h.WorkflowInstanceId == inst.WorkflowInstanceId).ToListAsync(),
            h => h.ActionType == "Reopen" && h.Comment == "wrong spare part quantity");
        Assert.Contains(await Ctx.WorkOrderStatusHistories.AsNoTracking().Where(h => h.WorkOrderId == id).ToListAsync(),
            h => h.FromStatus == "Closed" && h.ToStatus == "Completed" && h.Reason!.StartsWith("Reopened:"));
        var log = await Ctx.WorkOrderChangeLogs.AsNoTracking().SingleAsync(l => l.Action == "Reopen" && l.Outcome == "Allowed");
        Assert.Equal("Closed", log.BeforeValue); Assert.Contains("Completed", log.AfterValue); Assert.Equal("wrong spare part quantity", log.Reason);
        Assert.Equal(Manager.EmployeeId, log.ChangedByEmployeeId);

        // یک بار بازگشایی‌شده دوباره قابل‌بازگشایی نیست تا دوباره بسته شود
        Assert.False((await Svc.ReopenAsync(id, "again", Manager)).Success);

        // و گردش‌کار دوباره تا Closed قابل‌ادامه است؛ ردیف MaintenanceHistory تکراری نمی‌شود
        await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);
        Assert.Equal("Closed", (await Wo(id)).Status);
        Assert.Equal(1, await Ctx.MaintenanceHistories.CountAsync(m => m.WorkOrderId == id));
    }

    [Fact]
    public async Task Only_closed_work_orders_can_be_reopened()
    {
        var id = await CreateAsync(); await RunToInProgressAsync(id);
        var r = await Svc.ReopenAsync(id, "why not", Manager);
        Assert.False(r.Success); Assert.Contains("closed", r.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InProgress", (await Wo(id)).Status);
    }

    [Fact]
    public async Task Reopen_post_without_permission_is_forbidden_and_with_permission_needs_a_reason()
    {
        var id = await CreateAsync("Corrective"); await RunToVerificationAsync(id); await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);

        Assert.IsType<ForbidResult>(await BuildController(Executor).ReopenWorkOrder(id, "x"));
        Assert.Equal("Closed", (await Wo(id)).Status);

        var mgr = BuildController(Manager);
        await mgr.ReopenWorkOrder(id, " ");
        Assert.Equal("Closed", (await Wo(id)).Status);
        Assert.Contains("reason", (string)mgr.TempData["Error"]!, StringComparison.OrdinalIgnoreCase);

        await mgr.ReopenWorkOrder(id, "inspection found an error");
        Assert.Equal("Completed", (await Wo(id)).Status);
    }
}
