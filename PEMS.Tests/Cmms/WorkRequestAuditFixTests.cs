using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// CMMS audit A1-A6 روی سرویس واقعی Work Request (InMemory، گردش‌کار و مجوز واقعی): دلیل الزامی رد/لغو، محل الزامی، تفکیک وظایف، ریسک/HSE/SLA،
/// انتقال اطلاعات خرابی و دیسیپلین پیشنهادی به Work Order.
/// </summary>
public class WorkRequestAuditFixTests : IDisposable
{
    private sealed class FakeNumbering : INumberingService
    {
        private int _n;
        public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null) => Task.FromResult($"{documentType}-T{++_n:D4}");
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(int UserId, string Title, string? Link)> Sent { get; } = new();
        public Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null)
        { Sent.Add((userId, title, linkUrl)); return Task.CompletedTask; }
        public Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false) => throw new NotImplementedException();
        public Task<PagedResult<NotificationDto>> GetInboxAsync(int userId, string? filter, int page, int pageSize) => throw new NotImplementedException();
        public Task<int> GetUnreadCountAsync(int userId) => throw new NotImplementedException();
        public Task<bool> MarkAsReadAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task MarkAllAsReadAsync(int userId) => throw new NotImplementedException();
        public Task<string?> OpenAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task<bool> DismissAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task DismissAllAsync(int userId) => throw new NotImplementedException();
        public Task MarkRelatedReadAsync(int userId, string relatedType, int relatedId) => throw new NotImplementedException();
        public Task ResolveRelatedAsync(int? userId, string relatedType, int relatedId) => throw new NotImplementedException();
    }

    private readonly PEMSDbContext _ctx = PemsDbContextFactory.CreateInMemory("wr-audit-" + Guid.NewGuid());
    private readonly RecordingNotifications _notes = new();
    private static readonly string[] Requester = { "WrRequester" };
    private static readonly string[] Reviewer = { "WrReviewer" };
    private static readonly string[] Admin = { SystemRoles.SystemAdmin };
    private static WorkRequestActor Actor(int emp, string[] roles) => new(emp, roles);

    public WorkRequestAuditFixTests()
    {
        _ctx.Departments.AddRange(new Department { DepartmentId = 1, Name = "Maintenance" }, new Department { DepartmentId = 2, Name = "HSE" });
        _ctx.Disciplines.Add(new Discipline { DisciplineId = 5, Name = "Mechanical", Code = "MECH", IsActive = true });
        foreach (var id in new[] { 1, 2, 3, 4 })
            _ctx.Employees.Add(new Employee { EmployeeId = id, FirstName = "E" + id, LastName = "T", DepartmentId = id == 4 ? 2 : 1, DisciplineId = 5, IsActive = true });
        _ctx.Equipments.Add(new Equipment { EquipmentId = 1, TagNumber = "P-101", Name = "Pump", IsActive = true });
        _ctx.FunctionalLocations.Add(new FunctionalLocation { FunctionalLocationId = 7, Code = "FL-7", Name = "Area 7", IsActive = true });
        _ctx.FailureModes.Add(new FailureMode { FailureModeId = 9, Code = "LEAK", Name = "Leakage", IsActive = true });
        _ctx.Users.Add(new ApplicationUser { Id = 40, UserName = "hse", NormalizedUserName = "HSE", EmployeeId = 4, Email = "hse@t.local" });
        var create = new Permission { Name = "WorkRequest.Create", ResourceName = "WorkRequest", ActionName = "Create", IsActive = true };
        var approve = new Permission { Name = "WorkRequest.Approve", ResourceName = "WorkRequest", ActionName = "Approve", IsActive = true };
        _ctx.Permissions.AddRange(create, approve);
        _ctx.SaveChanges();
        void Role(string name, params Permission[] grants)
        {
            var r = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() };
            _ctx.Roles.Add(r); _ctx.SaveChanges();
            foreach (var g in grants) _ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = r.Id, PermissionId = g.PermissionId, IsActive = true });
            _ctx.SaveChanges();
        }
        Role("WrRequester", create);
        Role("WrReviewer", create, approve);
        Role(SystemRoles.SystemAdmin, create, approve);
        var sp = new ServiceCollection().AddSingleton(_ctx).BuildServiceProvider();
        WorkRequestWorkflowSeeder.SeedAsync(sp).GetAwaiter().GetResult();
    }

    public void Dispose() => _ctx.Dispose();

    private WorkRequestService Svc(bool sod = true) => new(_ctx, new FakeNumbering(), null!, notifications: _notes, policy: new WorkRequestPolicyOptions { EnforceSegregationOfDuties = sod });

    private async Task<int> SubmittedAsync(WorkRequestService svc, int requester = 1, Action<WorkRequestCreateRequest>? tweak = null)
    {
        var req = new WorkRequestCreateRequest { Title = "Pump vibration", RequestedById = requester, EquipmentId = 1, Priority = "Medium" };
        tweak?.Invoke(req);
        var (ok, err, id) = await svc.CreateAsync(req);
        Assert.True(ok, err);
        Assert.True((await svc.SubmitAsync(id!.Value, Actor(requester, Requester))).Success);
        return id!.Value;
    }

    private Task<WorkRequest> Wr(int id) => _ctx.WorkRequests.AsNoTracking().FirstAsync(x => x.WorkRequestId == id);

    // ---------------------------------------------------------------- A1
    [Theory]
    [InlineData("Reject")]
    [InlineData("Cancel")]
    public async Task A1_reject_and_cancel_require_a_reason_and_store_it_in_both_histories(string action)
    {
        var svc = Svc();
        var id = await SubmittedAsync(svc);
        await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester));       // → Under Review

        foreach (var blank in new string?[] { null, "", "   " })
        {
            var (ok, err) = await svc.ExecuteWorkflowActionAsync(id, action, blank, Actor(2, Reviewer));
            Assert.False(ok); Assert.Contains("reason", err!, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal("UnderReview", (await Wr(id)).Status);

        var done = await svc.ExecuteWorkflowActionAsync(id, action, "  duplicate of WR-12  ", Actor(2, Reviewer));
        Assert.True(done.Success, done.Error);
        var target = action == "Reject" ? "Rejected" : "Cancelled";
        Assert.Equal(target, (await Wr(id)).Status);
        Assert.Contains(await _ctx.WorkRequestStatusHistories.Where(h => h.WorkRequestId == id).ToListAsync(), h => h.ToStatus == target && h.Reason == "duplicate of WR-12");
        Assert.Contains(await _ctx.WorkflowHistories.ToListAsync(), h => h.ActionType == action && h.Comment == "duplicate of WR-12");
        if (action == "Reject") Assert.Equal("duplicate of WR-12", (await Wr(id)).RejectionReason);
    }

    [Fact]
    public async Task A1_legacy_status_path_also_requires_a_reason_for_cancel_and_reject()
    {
        _ctx.WorkflowBindings.RemoveRange(_ctx.WorkflowBindings); await _ctx.SaveChangesAsync();      // رکورد Legacy: بدون Binding
        var svc = Svc();
        var (_, _, id) = await svc.CreateAsync("legacy", null, 1, 1, null, null, null, "Medium", null);
        var (ok, err) = await svc.AdvanceStatusAsync(id!.Value, "Cancelled", " ", 1);
        Assert.False(ok); Assert.Contains("reason", err!, StringComparison.OrdinalIgnoreCase);
        Assert.True((await svc.AdvanceStatusAsync(id.Value, "Cancelled", "no longer needed", 1)).Success);
    }

    // ---------------------------------------------------------------- A2
    [Fact]
    public async Task A2_a_request_needs_an_equipment_asset_or_functional_location()
    {
        var svc = Svc();
        var none = await svc.CreateAsync(new WorkRequestCreateRequest { Title = "x", RequestedById = 1 });
        Assert.False(none.Success); Assert.Contains("location", none.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_ctx.WorkRequests);

        Assert.True((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "fl", RequestedById = 1, FunctionalLocationId = 7 })).Success);
        Assert.True((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "eq", RequestedById = 1, EquipmentId = 1 })).Success);

        Assert.False((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "ghost", RequestedById = 1, EquipmentId = 999 })).Success);
        Assert.False((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "ghost fl", RequestedById = 1, FunctionalLocationId = 999 })).Success);
    }

    [Fact]
    public async Task A2_asset_only_request_derives_the_equipment()
    {
        _ctx.Assets.Add(new Asset { AssetId = 3, EquipmentId = 1, AssetNumber = "A-1" });
        await _ctx.SaveChangesAsync();
        var (ok, err, id) = await Svc().CreateAsync(new WorkRequestCreateRequest { Title = "asset", RequestedById = 1, AssetId = 3 });
        Assert.True(ok, err);
        Assert.Equal(1, (await Wr(id!.Value)).EquipmentId);
    }

    [Fact]
    public async Task A2_a_legacy_draft_without_location_cannot_be_submitted()
    {
        _ctx.WorkRequests.Add(new WorkRequest { RequestNumber = "WR-OLD", Title = "old", RequestedById = 1, Status = "Draft", IsActive = true });
        await _ctx.SaveChangesAsync();
        var (ok, err) = await Svc().SubmitAsync(_ctx.WorkRequests.Single().WorkRequestId, Actor(1, Requester));
        Assert.False(ok); Assert.Contains("location", err!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- A3
    [Fact]
    public async Task A3_requester_cannot_review_approve_or_reject_their_own_request()
    {
        var svc = Svc();
        var id = await SubmittedAsync(svc, requester: 1);
        var (ok, err) = await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(1, Requester));       // Start Review by the requester
        Assert.False(ok); Assert.Contains("requester", err!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Submitted", (await Wr(id)).Status);

        Assert.True((await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester))).Success);  // another employee reviews
        Assert.False((await svc.ExecuteWorkflowActionAsync(id, "Reject", "mine", Actor(1, Reviewer))).Success);  // requester with approve rights still blocked
        Assert.Equal("UnderReview", (await Wr(id)).Status);
    }

    [Fact]
    public async Task A3_approver_cannot_be_the_reviewer()
    {
        var svc = Svc();
        var id = await SubmittedAsync(svc, requester: 1);
        Assert.True((await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer))).Success);   // employee 2 reviews
        var (ok, err) = await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer));          // and tries to approve
        Assert.False(ok); Assert.Contains("reviewer", err!, StringComparison.OrdinalIgnoreCase);
        Assert.True((await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Reviewer))).Success);
    }

    [Fact]
    public async Task A3_system_admin_may_override_only_with_an_audit_record_and_the_policy_can_be_switched_off()
    {
        var svc = Svc();
        var id = await SubmittedAsync(svc, requester: 1);
        var r = await svc.ExecuteWorkflowActionAsync(id, "Approve", "urgent", Actor(1, Admin));
        Assert.True(r.Success, r.Error);
        var audit = await _ctx.AuthorizationAuditLogs.SingleAsync(a => a.Decision == "SoDOverride");
        Assert.Equal(id.ToString(), audit.ResourceId); Assert.Contains("urgent", audit.Reason);

        var relaxed = Svc(sod: false);
        var id2 = await SubmittedAsync(relaxed, requester: 1);
        Assert.True((await relaxed.ExecuteWorkflowActionAsync(id2, "Approve", null, Actor(1, Requester))).Success);       // پیکربندی خاموش → مجاز
        Assert.Single(_ctx.AuthorizationAuditLogs.Where(a => a.Decision == "SoDOverride"));
    }

    // ---------------------------------------------------------------- A4
    [Theory]
    [InlineData(1, 1, false, false, "Low", "Low")]
    [InlineData(2, 3, false, false, "Low", "Medium")]
    [InlineData(2, 5, false, false, "Low", "High")]
    [InlineData(4, 4, false, false, "Medium", "Critical")]
    [InlineData(1, 1, true, false, "Low", "High")]            // HSE → حداقل High
    [InlineData(1, 1, false, true, "Low", "Critical")]        // اضطراری → Critical
    [InlineData(1, 1, false, false, "Critical", "Critical")]  // هرگز پایین‌تر از اولویت اعلامی نمی‌شود
    public void A4_priority_is_derived_from_the_risk_matrix_hse_and_emergency_but_never_lowered(int l, int s, bool hse, bool emergency, string requested, string expected)
    {
        var r = WorkRequestRiskAssessor.Assess(l, s, hse, emergency, requested, new DateTime(2026, 1, 1, 8, 0, 0));
        Assert.Equal(expected, r.Priority);
        var (response, resolution) = WorkRequestRiskAssessor.SlaHours(expected);
        Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0).AddHours(response), r.ResponseDue);
        Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0).AddHours(resolution), r.ResolutionDue);
        Assert.True(Array.IndexOf(WorkRequestRiskAssessor.Priorities, r.Priority) >= Array.IndexOf(WorkRequestRiskAssessor.Priorities, requested));
    }

    [Fact]
    public async Task A4_creation_stores_risk_priority_sla_and_notifies_hse_users_for_hse_or_emergency_requests()
    {
        var svc = Svc();
        var (ok, err, id) = await svc.CreateAsync(new WorkRequestCreateRequest
        { Title = "H2S alarm", RequestedById = 1, EquipmentId = 1, Priority = "Low", RiskLikelihood = 4, RiskSeverity = 5, IsHseRelated = true, IsEmergency = true });
        Assert.True(ok, err);
        var wr = await Wr(id!.Value);
        Assert.Equal("Critical", wr.Priority); Assert.Equal("Low", wr.RequestedPriority); Assert.Equal(20, wr.RiskScore); Assert.Equal("Critical", wr.RiskLevel);
        Assert.True(wr.IsEmergency && wr.IsHseRelated);
        Assert.Equal(wr.RequestedDate.AddHours(1).Date, wr.ResponseDueDate!.Value.Date);
        Assert.True(wr.ResponseDueDate < wr.ResolutionDueDate);
        var note = Assert.Single(_notes.Sent);
        Assert.Equal(40, note.UserId); Assert.StartsWith("EMERGENCY", note.Title); Assert.Equal($"/WorkRequest/Details/{id}", note.Link);

        _notes.Sent.Clear();
        await svc.CreateAsync(new WorkRequestCreateRequest { Title = "routine", RequestedById = 1, EquipmentId = 1, Priority = "Low" });
        Assert.Empty(_notes.Sent);                                      // بدون HSE/اضطراری اعلانی نیست
    }

    [Fact]
    public async Task A4_invalid_ratings_are_rejected()
    {
        var svc = Svc();
        Assert.False((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "x", RequestedById = 1, EquipmentId = 1, RiskLikelihood = 6, RiskSeverity = 2 })).Success);
        Assert.False((await svc.CreateAsync(new WorkRequestCreateRequest { Title = "x", RequestedById = 1, EquipmentId = 1, RiskLikelihood = 3 })).Success);
        Assert.Empty(_ctx.WorkRequests);
    }

    // ---------------------------------------------------------------- A5 / A6 (+ FL-only conversion)
    private async Task<int> PlannedAsync(WorkRequestService svc, Action<WorkRequestCreateRequest>? tweak = null)
    {
        var id = await SubmittedAsync(svc, 1, tweak);
        await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(3, Requester));
        await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer));
        await svc.ExecuteWorkflowActionAsync(id, "Approve", null, Actor(2, Reviewer));
        Assert.Equal("Planned", (await Wr(id)).Status);
        return id;
    }

    [Fact]
    public async Task A5_A6_failure_information_hse_flag_and_suggested_discipline_reach_the_work_order()
    {
        var svc = Svc();
        var id = await PlannedAsync(svc, r => { r.FailureModeId = 9; r.FailureEffect = "loss of seal flush"; r.ReportedDowntimeHours = 6.5m; r.IsHseRelated = true; r.SuggestedDisciplineId = 5; });
        var (ok, err, woId) = await svc.ConvertToWorkOrderAsync(id, "Corrective", "tester", Actor(2, Reviewer));
        Assert.True(ok, err);
        var wo = await _ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == woId);
        Assert.Equal("LEAK", wo.FailureModeCode); Assert.Equal(9, wo.FailureModeId);
        Assert.Equal("loss of seal flush", wo.FailureEffect); Assert.Equal(6.5m, wo.DowntimeHours); Assert.True(wo.IsHseRelated);
        Assert.Equal(5, wo.ResponsibleDisciplineId);                    // A6: پیشنهادی → مسئول (قابل تغییر در Planning)
        Assert.Equal(OrganizationDomainsMaintenance, wo.ResponsibleOrganization);
        Assert.Equal("High", wo.Priority);                              // HSE → حداقل High
    }

    private const string OrganizationDomainsMaintenance = PEMS.Domain.Common.OrganizationDomains.Maintenance;

    [Fact]
    public async Task A6_without_a_suggestion_the_responsible_discipline_stays_for_planning_to_decide()
    {
        var svc = Svc();
        var id = await PlannedAsync(svc);
        var (ok, err, woId) = await svc.ConvertToWorkOrderAsync(id, "Corrective", "tester", Actor(2, Reviewer));
        Assert.True(ok, err);
        Assert.Null((await _ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == woId)).ResponsibleDisciplineId);
    }

    [Fact]
    public async Task A2_functional_location_only_request_converts_once_the_equipment_is_chosen_at_conversion_and_a_failed_conversion_burns_no_work_order_number()
    {
        var svc = Svc();
        var id = await PlannedAsync(svc, r => { r.EquipmentId = null; r.FunctionalLocationId = 7; });

        var (ok, err, _) = await svc.ConvertToWorkOrderAsync(id, "Corrective", "t", Actor(2, Reviewer));
        Assert.False(ok); Assert.Contains("equipment", err!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_ctx.WorkOrders);

        (ok, err, var woId) = await svc.ConvertToWorkOrderAsync(id, "Corrective", "t", Actor(2, Reviewer), equipmentId: 1);
        Assert.True(ok, err);
        Assert.Equal(1, (await _ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == woId)).EquipmentId);
        Assert.Equal("WorkOrder-T0002", (await _ctx.WorkOrders.AsNoTracking().SingleAsync(w => w.WorkOrderId == woId)).WorkOrderNumber);   // ۰۰۰۱ = شماره‌ی WR؛ تلاش ردشده شماره نسوزاند
    }
}
