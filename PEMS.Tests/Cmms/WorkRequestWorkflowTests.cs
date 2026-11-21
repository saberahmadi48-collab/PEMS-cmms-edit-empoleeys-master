using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Phase 2 - Work Request روی پلتفرم مرکزی گردش‌کار. با InMemory و سرویس‌های واقعی (WorkRequestService، Runtime، Binding،
/// Read Model، AccessControlService)؛ هیچ Mock ای برای مجوز یا گردش‌کار استفاده نشده است.
/// </summary>
public class WorkRequestWorkflowTests : IDisposable
{
    private sealed class FakeNumbering : INumberingService
    {
        private int _n;
        public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null) => Task.FromResult($"{documentType}-T{++_n:D4}");
    }

    private readonly PEMSDbContext _ctx = PemsDbContextFactory.CreateInMemory("wr-wf-" + Guid.NewGuid());
    private readonly WorkRequestService _svc;
    private readonly WorkflowDefinitionService _defs;
    private readonly WorkflowBindingService _bindings;
    private readonly WorkflowActor _admin = new(99, "admin");

    // نقش‌ها: Requester = فقط Create، Reviewer = Create + Approve، Outsider = هیچ‌کدام
    private static readonly string[] Requester = { "WrRequester" };
    private static readonly string[] Reviewer = { "WrReviewer" };
    private static readonly string[] Outsider = { "WrOutsider" };
    private static WorkRequestActor Actor(int emp, string[] roles) => new(emp, roles);

    public WorkRequestWorkflowTests()
    {
        _ctx.Departments.Add(new Department { DepartmentId = 1, Name = "Maintenance" });
        foreach (var id in new[] { 1, 2, 3 })
            _ctx.Employees.Add(new Employee { EmployeeId = id, FirstName = "E" + id, LastName = "T", DepartmentId = 1, IsActive = true });
        _ctx.Equipments.Add(new Equipment { EquipmentId = 1, TagNumber = "P-101", Name = "Pump", IsActive = true });

        var create = new Permission { Name = "WorkRequest.Create", ResourceName = "WorkRequest", ActionName = "Create", IsActive = true };
        var approve = new Permission { Name = "WorkRequest.Approve", ResourceName = "WorkRequest", ActionName = "Approve", IsActive = true };
        _ctx.Permissions.AddRange(create, approve);
        _ctx.SaveChanges();

        void Role(string name, params Permission[] grants)
        {
            var r = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() };
            _ctx.Roles.Add(r);
            _ctx.SaveChanges();
            foreach (var g in grants) _ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = r.Id, PermissionId = g.PermissionId, IsActive = true });
            _ctx.SaveChanges();
        }
        Role("WrRequester", create);
        Role("WrReviewer", create, approve);
        Role("WrOutsider");

        _svc = new WorkRequestService(_ctx, new FakeNumbering(), null!);
        _bindings = new WorkflowBindingService(_ctx);
        _defs = new WorkflowDefinitionService(_ctx, new WorkflowVersionValidator(_ctx, new WorkflowActionRegistry(), new WorkflowConditionEvaluator(), new PemsWorkflowReferenceCatalog(_ctx)));
    }

    public void Dispose() => _ctx.Dispose();

    private async Task SeedWorkflowAsync()
    {
        var sp = new ServiceCollection().AddSingleton(_ctx).BuildServiceProvider();
        await WorkRequestWorkflowSeeder.SeedAsync(sp);
    }

    private async Task<int> NewDraftAsync(string title = "Pump vibration")
    {
        var (ok, err, id) = await _svc.CreateAsync(title, "desc", 1, 1, null, null, "vibration", "Medium", null);
        Assert.True(ok, err);
        return id!.Value;
    }

    private async Task<int> NewSubmittedAsync(string title = "Pump vibration")
    {
        var id = await NewDraftAsync(title);
        var (ok, err) = await _svc.SubmitAsync(id, Actor(1, Requester));
        Assert.True(ok, err);
        return id;
    }

    private async Task<WorkRequest> WrAsync(int id) => await _ctx.WorkRequests.AsNoTracking().FirstAsync(x => x.WorkRequestId == id);
    private async Task<WorkflowInstance> InstanceAsync(int id) =>
        await _ctx.WorkflowInstances.AsNoTracking().Where(i => i.EntityKey == $"WorkRequest|WorkRequest|{id}").OrderByDescending(i => i.WorkflowInstanceId).FirstAsync();

    // ------------------------------------------------------------------ binding / runtime
    [Fact]
    public async Task Seeder_creates_one_published_definition_and_one_explicit_pinned_binding_and_is_idempotent()
    {
        await SeedWorkflowAsync();
        await SeedWorkflowAsync();

        Assert.Equal(1, await _ctx.WorkflowDefinitions.CountAsync(d => d.Module == "WorkRequest"));
        var v = await _ctx.WorkflowVersions.SingleAsync();
        Assert.Equal("Published", v.Status);
        var b = await _ctx.WorkflowBindings.SingleAsync();
        Assert.Equal(("WorkRequest", "WorkRequest", "Standard"), (b.Module, b.EntityType, b.Context));
        Assert.Equal(v.WorkflowVersionId, b.PinnedVersionId);
        Assert.True(b.IsActive);

        var stages = await _ctx.WorkflowStages.OrderBy(s => s.StageOrder).Select(s => s.Name).ToListAsync();
        Assert.Equal(new[] { "Submitted", "Under Review", "Approved", "Planned" }, stages);
        // ساخته‌شده از چرخه‌ی Phase 1 (با ممیزی پیکربندی)، نه درج مستقیم
        Assert.Contains(await _ctx.WorkflowConfigAudits.Select(a => a.Action).ToListAsync(), a => a == "Published");
    }

    [Fact]
    public async Task Seeder_does_not_recreate_a_binding_an_administrator_deactivated()
    {
        await SeedWorkflowAsync();
        var b = await _ctx.WorkflowBindings.SingleAsync();
        await _bindings.DeactivateAsync(b.WorkflowBindingId, _admin);
        await SeedWorkflowAsync();
        Assert.Equal(1, await _ctx.WorkflowBindings.CountAsync());
        Assert.False((await _ctx.WorkflowBindings.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Submit_starts_the_central_runtime_via_the_explicit_binding_and_projects_the_business_status()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();

        var wr = await WrAsync(id);
        Assert.Equal("Submitted", wr.Status);
        var inst = await InstanceAsync(id);
        var binding = await _ctx.WorkflowBindings.SingleAsync();
        Assert.Equal(binding.WorkflowBindingId, inst.WorkflowBindingId);
        Assert.Equal(binding.PinnedVersionId, inst.WorkflowVersionId);
        Assert.Equal("Running", inst.Status);
        Assert.Equal(1, await _ctx.WorkflowHistories.CountAsync(h => h.WorkflowInstanceId == inst.WorkflowInstanceId && h.ActionType == "Start"));
        // ممیزی کسب‌وکاری همچنان نوشته می‌شود (جدا از تاریخچه‌ی گردش‌کار)
        Assert.Contains(await _ctx.WorkRequestStatusHistories.Where(h => h.WorkRequestId == id).ToListAsync(), h => h.FromStatus == "Draft" && h.ToStatus == "Submitted");
    }

    [Fact]
    public async Task Submit_without_any_binding_fails_clearly_and_does_not_change_the_work_request()
    {
        var id = await NewDraftAsync();
        var (ok, err) = await _svc.SubmitAsync(id, Actor(1, Requester));
        Assert.False(ok);
        Assert.Matches("پیکربندی|not configured", err);
        Assert.Equal("Draft", (await WrAsync(id)).Status);
        Assert.Equal(0, await _ctx.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task Inactive_binding_does_not_resolve_and_submit_fails()
    {
        await SeedWorkflowAsync();
        await _bindings.DeactivateAsync((await _ctx.WorkflowBindings.SingleAsync()).WorkflowBindingId, _admin);
        var id = await NewDraftAsync();
        var (ok, _) = await _svc.SubmitAsync(id, Actor(1, Requester));
        Assert.False(ok);
        Assert.Equal("Draft", (await WrAsync(id)).Status);
    }

    [Fact]
    public async Task Ambiguous_bindings_fail_deterministically_instead_of_picking_one()
    {
        await SeedWorkflowAsync();
        var def = await _ctx.WorkflowDefinitions.SingleAsync();
        _ctx.WorkflowBindings.Add(new WorkflowBinding { Module = "WorkRequest", EntityType = "WorkRequest", Context = "Standard", WorkflowDefinitionId = def.WorkflowDefinitionId });
        await _ctx.SaveChangesAsync();

        var id = await NewDraftAsync();
        await Assert.ThrowsAsync<WorkflowBindingAmbiguousException>(() => _bindings.ResolveAsync("WorkRequest", "WorkRequest", "Standard"));
        var (ok, err) = await _svc.SubmitAsync(id, Actor(1, Requester));
        Assert.False(ok);
        Assert.Matches("Binding|binding", err);
        Assert.Equal("Draft", (await WrAsync(id)).Status);
    }

    // ------------------------------------------------------------------ business flow through the platform
    [Fact]
    public async Task Full_lifecycle_runs_through_the_central_runtime_and_converts_to_a_work_order_atomically()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();

        Assert.True((await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester))).Success);          // Start Review
        var afterReview = await WrAsync(id);
        Assert.Equal("UnderReview", afterReview.Status);
        Assert.Equal(3, afterReview.ReviewedById);

        Assert.True((await _svc.ExecuteWorkflowActionAsync(id, "Approve", "ok", Actor(2, Reviewer))).Success);          // Approve
        var approved = await WrAsync(id);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(2, approved.ApprovedById);

        Assert.True((await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer))).Success);          // Plan
        Assert.Equal("Planned", (await WrAsync(id)).Status);

        var (ok, err, woId) = await _svc.ConvertToWorkOrderAsync(id, "Corrective", "tester", Actor(2, Reviewer));
        Assert.True(ok, err);
        var final = await WrAsync(id);
        Assert.Equal("Converted", final.Status);
        Assert.Equal(woId, final.LinkedWorkOrderId);
        Assert.Equal("Completed", (await InstanceAsync(id)).Status);

        var hist = await _ctx.WorkflowHistories.Where(h => h.WorkflowInstanceId == (InstanceAsync(id).Result.WorkflowInstanceId)).OrderBy(h => h.WorkflowHistoryId).Select(h => h.ActionType).ToListAsync();
        Assert.Equal(new[] { "Start", "Approve", "Approve", "Approve", "Complete" }, hist);
    }

    [Fact]
    public async Task Reject_and_cancel_end_the_instance_with_the_matching_status()
    {
        await SeedWorkflowAsync();
        var rejected = await NewSubmittedAsync("r");
        await _svc.ExecuteWorkflowActionAsync(rejected, "Approve", null, Actor(3, Requester));
        var (ok, err) = await _svc.ExecuteWorkflowActionAsync(rejected, "Reject", "not needed", Actor(2, Reviewer));
        Assert.True(ok, err);
        Assert.Equal("Rejected", (await WrAsync(rejected)).Status);
        Assert.Equal("not needed", (await WrAsync(rejected)).RejectionReason);
        Assert.Equal("Rejected", (await InstanceAsync(rejected)).Status);

        var cancelled = await NewSubmittedAsync("c");
        (ok, err) = await _svc.ExecuteWorkflowActionAsync(cancelled, "Cancel", "dup", Actor(1, Requester));
        Assert.True(ok, err);
        Assert.Equal("Cancelled", (await WrAsync(cancelled)).Status);
        Assert.Equal("Cancelled", (await InstanceAsync(cancelled)).Status);

        // خواندنی: مرحله‌ی لغو «Cancelled» است (نه Completed)، مراحل بعدی Upcoming، و هیچ اکشنی باقی نمی‌ماند
        var vm = (await _svc.GetWorkflowViewAsync(cancelled, Actor(2, Reviewer)))!;
        Assert.Equal("Cancelled", vm.Status);
        Assert.Equal(new[] { "Cancelled", "Upcoming", "Upcoming", "Upcoming" }, vm.Stages.Select(s => s.Status).ToArray());
        Assert.Empty(vm.AvailableActions);
    }

    [Fact]
    public async Task Complete_cannot_be_executed_as_a_generic_action_only_through_the_business_conversion()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        var (ok, err) = await _svc.ExecuteWorkflowActionAsync(id, "Complete", null, Actor(2, Reviewer));
        Assert.False(ok);
        Assert.Matches("تبدیل|Convert to Work Order", err);
        Assert.Equal("Submitted", (await WrAsync(id)).Status);
    }

    [Fact]
    public async Task Conversion_business_rules_still_apply_when_workflow_allows_it()
    {
        await SeedWorkflowAsync();
        // A2: بدون تجهیز/Asset/Functional Location ثبت نمی‌شود؛ درخواستِ فقط-Functional-Location ثبت می‌شود ولی تبدیل آن به Work Order تجهیز می‌خواهد
        _ctx.FunctionalLocations.Add(new FunctionalLocation { FunctionalLocationId = 7, Code = "FL-7", Name = "Area 7", IsActive = true });
        await _ctx.SaveChangesAsync();
        var (_, _, noEquipment) = await _svc.CreateAsync("No equipment", null, 1, null, null, 7, null, "Medium", null);
        await _svc.SubmitAsync(noEquipment!.Value, Actor(1, Requester));
        await _svc.ExecuteWorkflowActionAsync(noEquipment.Value, "Approve", null, Actor(3, Requester));
        await _svc.ExecuteWorkflowActionAsync(noEquipment.Value, "Approve", null, Actor(2, Reviewer));
        await _svc.ExecuteWorkflowActionAsync(noEquipment.Value, "Approve", null, Actor(2, Reviewer));

        var (ok, err, _) = await _svc.ConvertToWorkOrderAsync(noEquipment.Value, "Corrective", "t", Actor(2, Reviewer));
        Assert.False(ok);
        Assert.Matches("تجهیز|equipment", err);
        Assert.Equal("Planned", (await WrAsync(noEquipment.Value)).Status);
        Assert.Equal("Running", (await InstanceAsync(noEquipment.Value)).Status);
        Assert.Empty(_ctx.WorkOrders);
    }

    // ------------------------------------------------------------------ security (server-side)
    [Fact]
    public async Task Unauthorized_action_is_rejected_server_side_and_changes_nothing()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester)); // → UnderReview

        // Requester فقط Create دارد؛ تأیید نیازمند WorkRequest.Approve است
        var (ok, err) = await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester));
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Equal("UnderReview", (await WrAsync(id)).Status);
        Assert.Null((await WrAsync(id)).ApprovedById);
        Assert.Equal("Running", (await InstanceAsync(id)).Status);

        // کاربر بدون هیچ مجوزی
        (ok, _) = await _svc.ExecuteWorkflowActionAsync(id, "Reject", null, Actor(3, Outsider));
        Assert.False(ok);
        Assert.Equal("UnderReview", (await WrAsync(id)).Status);
    }

    [Fact]
    public async Task Runtime_hook_itself_rejects_the_transition_even_when_the_service_layer_is_bypassed()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester)); // → UnderReview
        var inst = await InstanceAsync(id);

        // فراخوانی مستقیم Runtime با نقش-مجازی جعلی «Permission:WorkRequest.Approve» برای کاربری که مجوز ندارد
        var hook = new AccessControlWorkflowAuthorizationHook(new AccessControlService(_ctx));
        var runtime = new WorkflowRuntimeService(_ctx, _bindings, null, hook);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            runtime.ExecuteActionAsync(inst.WorkflowInstanceId, "Approve", 1, new[] { "WrRequester", "Permission:WorkRequest.Approve" }, null, null, null));

        _ctx.ChangeTracker.Clear();
        Assert.Equal("Running", (await InstanceAsync(id)).Status);
        Assert.Equal((await InstanceAsync(id)).CurrentStageId, (await _ctx.WorkflowStages.AsNoTracking().FirstAsync(s => s.Name == "Under Review")).WorkflowStageId);
    }

    [Fact]
    public async Task Legacy_status_path_cannot_be_used_to_bypass_a_workflow_governed_request()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        var (ok, err) = await _svc.AdvanceStatusAsync(id, "UnderReview", null, 1);
        Assert.False(ok);
        Assert.Matches("گردش‌کار مرکزی|central workflow", err);
        Assert.Equal("Submitted", (await WrAsync(id)).Status);

        // پیش‌نویس هم نمی‌تواند با تغییر مستقیم وضعیت بدون Instance «ارسال» شود
        var draft = await NewDraftAsync("d");
        (ok, err) = await _svc.AdvanceStatusAsync(draft, "Submitted", null, 1);
        Assert.False(ok);
        Assert.Equal("Draft", (await WrAsync(draft)).Status);
    }

    [Fact]
    public async Task Submit_requires_the_create_permission()
    {
        await SeedWorkflowAsync();
        var id = await NewDraftAsync();
        var (ok, _) = await _svc.SubmitAsync(id, Actor(3, Outsider));
        Assert.False(ok);
        Assert.Equal("Draft", (await WrAsync(id)).Status);
        Assert.Equal(0, await _ctx.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task Convert_requires_the_approve_permission_through_the_workflow_not_the_button()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester));
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer));
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer)); // Planned

        var (ok, err, wo) = await _svc.ConvertToWorkOrderAsync(id, "Corrective", "t", Actor(1, Requester));
        Assert.False(ok);
        Assert.Null(wo);
        Assert.Equal("Planned", (await WrAsync(id)).Status);
        Assert.Empty(_ctx.WorkOrders);
        (ok, err, _) = await _svc.ConvertToWorkOrderAsync(id, "Corrective", "t", null);   // بدون actor برای رکورد تحت گردش‌کار: رد
        Assert.False(ok);
    }

    // ------------------------------------------------------------------ read model / UI data
    [Fact]
    public async Task Workflow_view_stages_current_stage_actions_and_history_come_from_the_runtime()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester)); // UnderReview

        var vm = (await _svc.GetWorkflowViewAsync(id, Actor(2, Reviewer)))!;
        Assert.Equal(new[] { "Submitted", "Under Review", "Approved", "Planned" }, vm.Stages.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { "Completed", "Current", "Upcoming", "Upcoming" }, vm.Stages.Select(s => s.Status).ToArray());
        Assert.Equal("Under Review", vm.CurrentStageName);
        Assert.Equal(new[] { "ثبت‌شده", "در حال بررسی", "تأییدشده", "برنامه‌ریزی‌شده" }, vm.Stages.Select(s => s.NameFa).ToArray());
        Assert.Equal(new[] { "Approve", "Cancel", "Reject" }, vm.AvailableActions.Select(a => a.ActionType).OrderBy(x => x).ToArray());
        Assert.All(vm.AvailableActions, a => Assert.True(a.IsAllowed));
        Assert.Equal("Approved", vm.AvailableActions.Single(a => a.ActionType == "Approve").ToStageName);
        Assert.Equal("تأیید", vm.AvailableActions.Single(a => a.ActionType == "Approve").LabelFa);
        Assert.Equal(2, vm.HistorySummary.Count);
        Assert.Contains(vm.Stages[1].Participants, p => p.Status == "Pending");

        // همان اکشن‌ها برای Requester: نمایش داده می‌شود ولی غیرمجاز (سرور مرجع است)
        var asRequester = (await _svc.GetWorkflowViewAsync(id, Actor(1, Requester)))!;
        Assert.False(asRequester.AvailableActions.Single(a => a.ActionType == "Approve").IsAllowed);
        Assert.False(asRequester.AvailableActions.Single(a => a.ActionType == "Reject").IsAllowed);
        Assert.True(asRequester.AvailableActions.Single(a => a.ActionType == "Cancel").IsAllowed);

        // بدون مجوز اصلاً شرکت‌کننده نیست → هیچ اکشنی
        Assert.Empty((await _svc.GetWorkflowViewAsync(id, Actor(3, Outsider)))!.AvailableActions);
    }

    [Fact]
    public async Task Draft_and_legacy_records_without_an_instance_have_no_workflow_view()
    {
        await SeedWorkflowAsync();
        var draft = await NewDraftAsync();
        Assert.Null(await _svc.GetWorkflowViewAsync(draft, Actor(2, Reviewer)));

        // رکورد Legacy قبل از مهاجرت: وضعیت میانی بدون Instance؛ مسیر قدیمی همچنان کار می‌کند (Binding فعال است)
        _ctx.WorkRequests.Add(new WorkRequest { RequestNumber = "WR-OLD", Title = "old", RequestedById = 1, EquipmentId = 1, Priority = "Medium", Status = "Submitted", IsActive = true });
        await _ctx.SaveChangesAsync();
        var oldId = (await _ctx.WorkRequests.SingleAsync(x => x.RequestNumber == "WR-OLD")).WorkRequestId;
        Assert.Null(await _svc.GetWorkflowViewAsync(oldId, Actor(2, Reviewer)));
        var (ok, err) = await _svc.AdvanceStatusAsync(oldId, "UnderReview", null, 1);
        Assert.True(ok, err);
        Assert.Equal(0, await _ctx.WorkflowInstances.CountAsync(i => i.BusinessObjectId == oldId));   // Instance ساخته/بازنویسی نشد
    }

    [Fact]
    public async Task More_than_one_instance_for_a_work_request_is_an_explicit_error_not_a_guess_of_the_newest()
    {
        await SeedWorkflowAsync();
        var id = await NewSubmittedAsync();
        var first = await InstanceAsync(id);
        // ناوردای شکسته (مثلاً دست‌کاری داده): Instance دوم برای همان Work Request
        _ctx.WorkflowInstances.Add(new WorkflowInstance
        {
            WorkflowVersionId = first.WorkflowVersionId, Module = "WorkRequest", BusinessObjectType = "WorkRequest", BusinessObjectId = id,
            EntityKey = first.EntityKey, Status = "Running", CurrentStageId = first.CurrentStageId
        });
        await _ctx.SaveChangesAsync();

        var (ok, err) = await _svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester));
        Assert.False(ok);
        Assert.Matches("بیش از یک Instance|more than one workflow instance", err);
        (ok, err) = await _svc.AdvanceStatusAsync(id, "UnderReview", null, 1);
        Assert.False(ok);
        Assert.Equal("Submitted", (await WrAsync(id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.GetWorkflowViewAsync(id, Actor(2, Reviewer)));
    }

    [Fact]
    public void Legacy_next_status_map_lives_only_in_the_service_and_has_no_terminal_successors()
    {
        Assert.Equal("Submitted", _svc.GetLegacyNextStatus("Draft"));
        Assert.Equal("UnderReview", _svc.GetLegacyNextStatus("Submitted"));
        Assert.Equal("Approved", _svc.GetLegacyNextStatus("UnderReview"));
        Assert.Equal("Planned", _svc.GetLegacyNextStatus("Approved"));
        foreach (var terminal in new[] { "Planned", "Converted", "Rejected", "Cancelled" })
            Assert.Null(_svc.GetLegacyNextStatus(terminal));
    }

    [Fact]
    public void Controller_has_no_private_status_map()
    {
        string? path = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "PEMS.Web", "Controllers", "WorkRequestController.cs");
            if (File.Exists(c)) { path = c; break; }
        }
        Assert.NotNull(path);
        var code = File.ReadAllText(path!);
        Assert.DoesNotContain("NextStatusMap", code);
        Assert.DoesNotContain("Dictionary<string, string>", code);
    }

    [Fact]
    public void Details_view_contains_no_hard_coded_workflow_stage_names()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "PEMS.Web", "Views", "WorkRequest", "Details.cshtml");
            if (File.Exists(candidate)) { path = candidate; break; }
        }
        Assert.NotNull(path);
        var html = File.ReadAllText(path!);
        foreach (var stageName in new[] { "Reliability Review", "Technical Review", "Under Review", "Supervisor Approval", "Reliability" })
            Assert.DoesNotContain(stageName, html);
        // لیست هم نباید زنجیره‌ی ثابت گردش‌کار را متن‌نویسی کند (با نسخه‌ی جدید کهنه می‌شد)
        var indexPath = Path.Combine(Path.GetDirectoryName(path!)!, "Index.cshtml");
        var indexHtml = File.ReadAllText(indexPath);
        Assert.DoesNotContain("Submitted → UnderReview", indexHtml);
        Assert.DoesNotContain("→ Planned", indexHtml);
        Assert.Contains("wf.Stages", html);
        Assert.Contains("wf?.AvailableActions", html);
    }

    // ------------------------------------------------------------------ versioning + Reliability Review proof
    private async Task<WorkflowVersion> PublishVersion2WithReliabilityReviewAsync(bool pin)
    {
        var def = await _ctx.WorkflowDefinitions.SingleAsync();
        var v1 = await _ctx.WorkflowVersions.SingleAsync(v => v.WorkflowDefinitionId == def.WorkflowDefinitionId && v.VersionNumber == 1);
        var v2 = await _defs.CloneVersionAsync(v1.WorkflowVersionId, _admin, "Add Reliability Review");

        var review = await _ctx.WorkflowStages.SingleAsync(s => s.WorkflowVersionId == v2.WorkflowVersionId && s.Name == "Under Review");
        var approved = await _ctx.WorkflowStages.SingleAsync(s => s.WorkflowVersionId == v2.WorkflowVersionId && s.Name == "Approved");
        var rr = await _defs.AddStageAsync(v2.WorkflowVersionId, new WorkflowStageInput
        {
            Name = "Reliability Review", NameFa = "بازبینی قابلیت اطمینان", StageOrder = 25, ResponsibleRole = "Permission:WorkRequest.Create,Permission:WorkRequest.Approve",
            BusinessStatus = "UnderReview"
        }, _admin);

        // «Approve» از Under Review به‌جای Approved به Reliability Review می‌رود
        var oldEdge = await _ctx.WorkflowTransitions.SingleAsync(t => t.WorkflowVersionId == v2.WorkflowVersionId && t.FromStageId == review.WorkflowStageId && t.ActionType == "Approve");
        await _defs.RemoveTransitionAsync(oldEdge.WorkflowTransitionId, _admin);
        await _defs.AddTransitionAsync(v2.WorkflowVersionId, new WorkflowTransitionInput
        { FromStageId = review.WorkflowStageId, ToStageId = rr.WorkflowStageId, ActionType = "Approve", Priority = 1, RequiredPermission = "WorkRequest.Approve", Label = "Send to Reliability", LabelFa = "ارسال برای قابلیت اطمینان" }, _admin);
        await _defs.AddTransitionAsync(v2.WorkflowVersionId, new WorkflowTransitionInput
        { FromStageId = rr.WorkflowStageId, ToStageId = approved.WorkflowStageId, ActionType = "Approve", Priority = 1, RequiredPermission = "WorkRequest.Approve", Label = "Approve Reliability", LabelFa = "تأیید قابلیت اطمینان" }, _admin);
        await _defs.AddTransitionAsync(v2.WorkflowVersionId, new WorkflowTransitionInput
        { FromStageId = rr.WorkflowStageId, ToStageId = null, ActionType = "Reject", Priority = 1, RequiredPermission = "WorkRequest.Approve", Label = "Reject", LabelFa = "رد" }, _admin);
        await _defs.AddTransitionAsync(v2.WorkflowVersionId, new WorkflowTransitionInput
        { FromStageId = rr.WorkflowStageId, ToStageId = null, ActionType = "Cancel", Priority = 1, RequiredPermission = "WorkRequest.Create", Label = "Cancel Request", LabelFa = "لغو درخواست" }, _admin);

        var r = await _defs.ValidateVersionAsync(v2.WorkflowVersionId, _admin);
        Assert.True(r.IsValid, string.Join("; ", r.Errors.Select(e => e.Code + " " + e.Message)));
        var published = await _defs.PublishVersionAsync(v2.WorkflowVersionId, _admin);

        if (pin)
        {
            var b = await _ctx.WorkflowBindings.SingleAsync();
            await _bindings.ChangeAsync(b.WorkflowBindingId, published.WorkflowVersionId, null, null, _admin);
        }
        return published;
    }

    [Fact]
    public async Task Publishing_a_new_version_without_changing_the_binding_does_not_affect_new_work_requests()
    {
        await SeedWorkflowAsync();
        var v1 = await _ctx.WorkflowVersions.SingleAsync();
        await PublishVersion2WithReliabilityReviewAsync(pin: false);
        var id = await NewSubmittedAsync();
        Assert.Equal(v1.WorkflowVersionId, (await InstanceAsync(id)).WorkflowVersionId);   // Binding پین‌شده به v1
    }

    [Fact]
    public async Task Reliability_Review_added_by_configuration_appears_for_new_requests_only_without_any_UI_or_service_change()
    {
        await SeedWorkflowAsync();
        var v1 = await _ctx.WorkflowVersions.SingleAsync();

        // Work Request A روی v1
        var a = await NewSubmittedAsync("A");
        Assert.Equal(v1.WorkflowVersionId, (await InstanceAsync(a)).WorkflowVersionId);
        await _svc.ExecuteWorkflowActionAsync(a, "Approve", null, Actor(3, Requester));   // A → UnderReview (v1)

        // نسخه‌ی 2 با مرحله‌ی جدید، منتشر و برای درخواست‌های جدید Bind می‌شود
        var v2 = await PublishVersion2WithReliabilityReviewAsync(pin: true);

        // Work Request B روی v2
        var b = await NewSubmittedAsync("B");
        Assert.Equal(v2.WorkflowVersionId, (await InstanceAsync(b)).WorkflowVersionId);
        Assert.Equal(v1.WorkflowVersionId, (await InstanceAsync(a)).WorkflowVersionId);    // A همچنان به v1 پین است

        // تایم‌لاین A: مرحله‌ی جدید ندارد. تایم‌لاین B: دارد، به ترتیب درست
        var vmA = (await _svc.GetWorkflowViewAsync(a, Actor(2, Reviewer)))!;
        var vmB = (await _svc.GetWorkflowViewAsync(b, Actor(2, Reviewer)))!;
        Assert.DoesNotContain("Reliability Review", vmA.Stages.Select(s => s.Name));
        Assert.Equal(1, vmA.VersionNumber);
        Assert.Equal(new[] { "Submitted", "Under Review", "Reliability Review", "Approved", "Planned" }, vmB.Stages.Select(s => s.Name).ToArray());
        Assert.Equal(2, vmB.VersionNumber);
        Assert.Equal("بازبینی قابلیت اطمینان", vmB.Stages[2].NameFa);

        // B: Submitted → Under Review → Reliability Review
        Assert.True((await _svc.ExecuteWorkflowActionAsync(b, "Approve", null, Actor(3, Requester))).Success);
        var underReviewActions = (await _svc.GetWorkflowViewAsync(b, Actor(2, Reviewer)))!.AvailableActions;
        Assert.Equal("Reliability Review", underReviewActions.Single(x => x.ActionType == "Approve").ToStageName);
        Assert.Equal("Send to Reliability", underReviewActions.Single(x => x.ActionType == "Approve").Label);

        Assert.True((await _svc.ExecuteWorkflowActionAsync(b, "Approve", "to reliability", Actor(2, Reviewer))).Success);
        vmB = (await _svc.GetWorkflowViewAsync(b, Actor(2, Reviewer)))!;
        Assert.Equal("Reliability Review", vmB.CurrentStageName);
        Assert.Equal("Current", vmB.Stages.Single(s => s.Name == "Reliability Review").Status);
        Assert.Equal("Completed", vmB.Stages.Single(s => s.Name == "Under Review").Status);
        Assert.Equal("UnderReview", (await WrAsync(b)).Status);                              // نگاشت وضعیت از داده‌ی پیکربندی
        Assert.Contains(vmB.AvailableActions, x => x.ActionType == "Approve" && x.Label == "Approve Reliability" && x.IsAllowed);
        Assert.Contains(vmB.HistorySummary, h => h.ToStageName == "Reliability Review");
        Assert.Contains(vmB.Stages.Single(s => s.Name == "Reliability Review").Participants, p => p.Status == "Pending");
        // Requester در مرحله‌ی جدید هم مجاز به تأیید نیست
        Assert.False((await _svc.GetWorkflowViewAsync(b, Actor(1, Requester)))!.AvailableActions.Single(x => x.ActionType == "Approve").IsAllowed);

        // A روی v1 ادامه می‌دهد: Approve مستقیم به Approved می‌رود (Reliability Review در مسیرش نیست)
        Assert.True((await _svc.ExecuteWorkflowActionAsync(a, "Approve", null, Actor(2, Reviewer))).Success);
        Assert.Equal("Approved", (await WrAsync(a)).Status);
        var vmA2 = (await _svc.GetWorkflowViewAsync(a, Actor(2, Reviewer)))!;
        Assert.Equal("Approved", vmA2.CurrentStageName);
        Assert.DoesNotContain("Reliability Review", vmA2.Stages.Select(s => s.Name));

        // B ادامه: Reliability → Approved
        Assert.True((await _svc.ExecuteWorkflowActionAsync(b, "Approve", null, Actor(2, Reviewer))).Success);
        Assert.Equal("Approved", (await WrAsync(b)).Status);

        // نسخه‌ی 1 هرگز تغییر نکرده (تغییرناپذیر): ۴ مرحله و ۱۱ گذار
        Assert.Equal(4, await _ctx.WorkflowStages.CountAsync(s => s.WorkflowVersionId == v1.WorkflowVersionId));
        Assert.Equal(11, await _ctx.WorkflowTransitions.CountAsync(t => t.WorkflowVersionId == v1.WorkflowVersionId));
    }

    [Fact]
    public async Task Published_work_request_workflow_version_cannot_be_modified()
    {
        await SeedWorkflowAsync();
        var v1 = await _ctx.WorkflowVersions.SingleAsync();
        await Assert.ThrowsAsync<WorkflowImmutableVersionException>(() =>
            _defs.AddStageAsync(v1.WorkflowVersionId, new WorkflowStageInput { Name = "Sneaky", StageOrder = 5, ResponsibleRole = "Permission:WorkRequest.Approve" }, _admin));
    }
}
