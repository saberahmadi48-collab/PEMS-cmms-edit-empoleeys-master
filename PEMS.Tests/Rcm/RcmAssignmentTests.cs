using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Rcm;

/// <summary>
/// تخصیص نسخه‌ی استراتژی به تگ‌ها (RCM کلاس‌محور): تکی و دسته‌جمعی، تعارض، قاعده‌ی بحرانی‌بودن، اتمیک‌بودن
/// (Rollback)، Override مستند با تایید مستقل، و نمای دوطرفه‌ی تگ/استراتژی.
/// </summary>
public class RcmAssignmentTests
{
    private static Task<RcmEnv> Env(string name) => RcmTestFactory.CreateEnvAsync($"Assign_{name}");

    private sealed class FailingContext : PEMSDbContext
    {
        public bool FailNextSave { get; set; }
        public FailingContext(DbContextOptions<PEMSDbContext> options) : base(options) { }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => FailNextSave ? throw new DbUpdateException("simulated failure") : base.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task OnlyApprovedOrActiveStrategies_CanBeAssigned()
    {
        var env = await Env(nameof(OnlyApprovedOrActiveStrategies_CanBeAssigned));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var draft = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), RcmTestFactory.Actor(1))).Id!.Value;
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "T-1", classId);

        var result = await env.Assignments.AssignAsync(draft, new[] { tag.EquipmentId }, RcmTestFactory.Actor(2));

        Assert.Contains(result.Errors, e => e.Code == "Rcm_Err_StrategyNotApproved");
        Assert.Empty(env.Ctx.MaintenanceStrategyAssignments);
    }

    [Fact]
    public async Task Assign_CreatesActiveAssignments_SkipsInactiveAndMissingTags_AndReportsCounts()
    {
        var env = await Env(nameof(Assign_CreatesActiveAssignments_SkipsInactiveAndMissingTags_AndReportsCounts));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var a = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "A-1", classId);
        var b = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "A-2", classId);
        var inactive = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "A-3", classId, active: false);

        var result = await env.Assignments.AssignAsync(id, new[] { a.EquipmentId, b.EquipmentId, inactive.EquipmentId, 9999 }, RcmTestFactory.Actor(5));

        Assert.True(result.Success);
        Assert.Equal(2, result.Assigned);
        Assert.Equal(2, result.SkippedNotFound);
        Assert.All(env.Ctx.MaintenanceStrategyAssignments, x => Assert.Equal("Active", x.Status));
        Assert.Equal(1, await env.Ctx.MaintenanceStrategyHistories.CountAsync(h => h.Action == "Assigned"));
    }

    [Fact]
    public async Task Assign_SameTagTwice_IsSkipped_NotDuplicated()
    {
        var env = await Env(nameof(Assign_SameTagTwice_IsSkipped_NotDuplicated));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "S-1", classId);

        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId, tag.EquipmentId }, RcmTestFactory.Actor(5));
        var second = await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.Equal(0, second.Assigned);
        Assert.Equal(1, second.SkippedAlreadyAssigned);
        Assert.Single(env.Ctx.MaintenanceStrategyAssignments);
    }

    [Fact]
    public async Task Assign_TagOutsideTheStrategyClassTree_IsRejected_AllOrNothing()
    {
        var env = await Env(nameof(Assign_TagOutsideTheStrategyClassTree_IsRejected_AllOrNothing));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var (otherClass, _) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "VALVE_T");
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var ok = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "OK-1", classId);
        var wrong = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "BAD-1", otherClass);
        var unclassified = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "BAD-2");

        var result = await env.Assignments.AssignAsync(id, new[] { ok.EquipmentId, wrong.EquipmentId, unclassified.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.Equal(2, result.Errors.Count(e => e.Code == "Rcm_Err_TagClassMismatch"));
        Assert.Equal(0, result.Assigned);
        Assert.Empty(env.Ctx.MaintenanceStrategyAssignments); // حتی تگ سالم هم ثبت نشد
    }

    [Fact]
    public async Task Assign_SubclassTags_AreAcceptedForParentClassStrategy()
    {
        var env = await Env(nameof(Assign_SubclassTags_AreAcceptedForParentClassStrategy));
        var (parentClass, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "PUMP_PARENT");
        var child = new EquipmentClass { Code = "PUMP_CENT_T", Name = "Centrifugal", ParentEquipmentClassId = parentClass, IsActive = true };
        env.Ctx.EquipmentClasses.Add(child);
        await env.Ctx.SaveChangesAsync();
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, parentClass, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "C-1", child.EquipmentClassId);

        var result = await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.True(result.Success);
        Assert.Equal(1, result.Assigned);
    }

    [Fact]
    public async Task Conflict_AnotherStrategyForTheSameFailureMode_IsSkippedAndPreviewed()
    {
        var env = await Env(nameof(Conflict_AnotherStrategyForTheSameFailureMode_IsSkippedAndPreviewed));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var first = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var second = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId, "Predictive");
        var taken = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "X-1", classId);
        var free = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "X-2", classId);
        await env.Assignments.AssignAsync(first, new[] { taken.EquipmentId }, RcmTestFactory.Actor(5));

        var preview = await env.Assignments.PreviewAsync(second, new BulkAssignFilter { EquipmentClassId = classId });
        var result = await env.Assignments.AssignAsync(second, new[] { taken.EquipmentId, free.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.Equal(2, preview.TotalMatching);
        Assert.Equal(1, preview.ConflictCount);
        Assert.Equal(BulkAssignConflict.ConflictingStrategy, preview.Candidates.Single(c => c.Equipment.Tag == "X-1").Conflict);
        Assert.Equal(first, preview.Candidates.Single(c => c.Equipment.Tag == "X-1").ConflictingStrategyId);
        Assert.Equal((1, 1), (result.Assigned, result.SkippedConflict));
    }

    [Fact]
    public async Task ClassA_Equipment_CannotReceiveRunToFailure_ButUnratedAndBCan()
    {
        var env = await Env(nameof(ClassA_Equipment_CannotReceiveRunToFailure_ButUnratedAndBCan));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var rtf = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId, "RunToFailure");
        var a = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "CR-A", classId);
        var b = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "CR-B", classId);
        var unrated = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "CR-U", classId);
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, a.EquipmentId, "A");
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, b.EquipmentId, "B");

        var result = await env.Assignments.AssignAsync(rtf, new[] { a.EquipmentId, b.EquipmentId, unrated.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.Equal((2, 1), (result.Assigned, result.SkippedCriticality));
        Assert.DoesNotContain(env.Ctx.MaintenanceStrategyAssignments, x => x.EquipmentId == a.EquipmentId);
    }

    [Fact]
    public async Task BulkAssign_RollsBackEverything_WhenSavingFails()
    {
        var name = "Rcm_Assign_Rollback";
        var options = new DbContextOptionsBuilder<PEMSDbContext>().UseInMemoryDatabase(name).Options;
        using var ctx = new FailingContext(options);
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(ctx);
        var strategy = new MaintenanceStrategy
        {
            EquipmentClassId = classId, FailureModeId = modeId, FailureModeCode = "PUMP_T_LEAK", StrategyType = "Preventive",
            ConsequenceCategory = "Operational", Status = "Active", IsActive = true
        };
        ctx.MaintenanceStrategies.Add(strategy);
        var t1 = await RcmTestFactory.AddEquipmentAsync(ctx, "RB-1", classId);
        var t2 = await RcmTestFactory.AddEquipmentAsync(ctx, "RB-2", classId);
        await ctx.SaveChangesAsync();

        ctx.FailNextSave = true;
        var result = await new StrategyAssignmentService(ctx).AssignAsync(strategy.MaintenanceStrategyId, new[] { t1.EquipmentId, t2.EquipmentId }, RcmTestFactory.Actor(5));

        Assert.True(result.RolledBack);
        Assert.Equal(0, result.Assigned);
        Assert.Contains(result.Errors, e => e.Code == "Rcm_Err_AssignRolledBack");
        ctx.FailNextSave = false;
        using var fresh = new PEMSDbContext(options);
        Assert.Empty(fresh.MaintenanceStrategyAssignments);
        Assert.Empty(fresh.MaintenanceStrategyHistories);
    }

    [Fact]
    public async Task Preview_FiltersByClassAreaAndTagSearch_AndShowsCriticality()
    {
        var env = await Env(nameof(Preview_FiltersByClassAreaAndTagSearch_AndShowsCriticality));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var north = new Equipment { TagNumber = "N-100", Name = "N", EquipmentType = "Pump", EquipmentClassId = classId, Area = "North", IsActive = true };
        var south = new Equipment { TagNumber = "S-100", Name = "S", EquipmentType = "Pump", EquipmentClassId = classId, Area = "South", IsActive = true };
        env.Ctx.Equipments.AddRange(north, south);
        await env.Ctx.SaveChangesAsync();
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, north.EquipmentId, "B");

        var byArea = await env.Assignments.PreviewAsync(id, new BulkAssignFilter { Areas = { "North" } });
        var byTag = await env.Assignments.PreviewAsync(id, new BulkAssignFilter { TagSearch = "S-1" });

        var n = Assert.Single(byArea.Candidates);
        Assert.Equal(("N-100", "B", "North"), (n.Equipment.Tag, n.Equipment.CriticalityClass, n.Equipment.Area));
        Assert.Equal("S-100", Assert.Single(byTag.Candidates).Equipment.Tag);
        Assert.Equal("Unrated", byTag.Candidates[0].Equipment.CriticalityClass);
    }

    // ---------- Override ----------

    [Fact]
    public async Task Override_RequiresJustification_AndIndependentApproval_ThenDrivesThePlanInterval()
    {
        var env = await Env(nameof(Override_RequiresJustification_AndIndependentApproval_ThenDrivesThePlanInterval));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "OV-1", classId);
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;

        var weak = await env.Assignments.RequestOverrideAsync(assignmentId, 14, null, "too short", RcmTestFactory.Actor(5));
        Assert.Contains(weak.Errors, e => e.Code == "Rcm_Err_JustificationTooShort");
        var empty = await env.Assignments.RequestOverrideAsync(assignmentId, null, null, RcmTestFactory.GoodJustification, RcmTestFactory.Actor(5));
        Assert.Contains(empty.Errors, e => e.Code == "Rcm_Err_OverrideEmpty");

        var requested = await env.Assignments.RequestOverrideAsync(assignmentId, 14, "Severe dust: inspect fortnightly", RcmTestFactory.GoodJustification, RcmTestFactory.Actor(5));
        Assert.True(requested.Success);

        // درخواست‌دهنده نمی‌تواند تاییدکننده‌ی خودش باشد
        var self = await env.Assignments.DecideOverrideAsync(assignmentId, true, null, RcmTestFactory.Actor(5));
        Assert.Contains(self.Errors, e => e.Code == "Rcm_Err_SeparationOfDuties");

        // پیش از تایید، Plan از Override استفاده نمی‌کند
        var before = await env.Plans().GetFormContextAsync(assignmentId);
        Assert.Equal(30, before!.SuggestedIntervalDays);

        Assert.True((await env.Assignments.DecideOverrideAsync(assignmentId, true, null, RcmTestFactory.Actor(6))).Success);
        var after = await env.Plans().GetFormContextAsync(assignmentId);
        Assert.Equal(14, after!.SuggestedIntervalDays);
        Assert.Contains(await env.Queries.GetHistoryAsync(id), h => h.Action == "OverrideApproved");
    }

    [Fact]
    public async Task RejectedOverride_RequiresComment_AndDoesNotAffectPlanning()
    {
        var env = await Env(nameof(RejectedOverride_RequiresComment_AndDoesNotAffectPlanning));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "OV-2", classId);
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        await env.Assignments.RequestOverrideAsync(assignmentId, 7, null, RcmTestFactory.GoodJustification, RcmTestFactory.Actor(5));

        Assert.Contains((await env.Assignments.DecideOverrideAsync(assignmentId, false, "", RcmTestFactory.Actor(6))).Errors, e => e.Code == "Rcm_Err_RejectCommentRequired");
        Assert.True((await env.Assignments.DecideOverrideAsync(assignmentId, false, "Not justified by history.", RcmTestFactory.Actor(6))).Success);

        Assert.Equal(30, (await env.Plans().GetFormContextAsync(assignmentId))!.SuggestedIntervalDays);
    }

    // ---------- Unassign / tag view ----------

    [Fact]
    public async Task Unassign_ClosesAssignment_AndAllowsTheTagToBeAssignedAgain()
    {
        var env = await Env(nameof(Unassign_ClosesAssignment_AndAllowsTheTagToBeAssignedAgain));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "U-1", classId);
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;

        Assert.True((await env.Assignments.UnassignAsync(assignmentId, "moved", RcmTestFactory.Actor(5))).Success);
        Assert.Contains((await env.Assignments.UnassignAsync(assignmentId, null, RcmTestFactory.Actor(5))).Errors, e => e.Code == "Rcm_Err_AssignmentNotActive");
        Assert.Equal(1, (await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5))).Assigned);
    }

    [Fact]
    public async Task TagView_ShowsAssignedStrategiesPlansAndOpenWorkOrders_AndStrategyShowsItsTags()
    {
        var env = await Env(nameof(TagView_ShowsAssignedStrategiesPlansAndOpenWorkOrders_AndStrategyShowsItsTags));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "BI-1", classId);
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, tag.EquipmentId, "B");
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        var jp = await RcmTestFactory.AddJobPlanAsync(env.Ctx);
        await env.Plans().CreateAsync(new PlanInputDto
        {
            MaintenanceStrategyAssignmentId = assignmentId, Title = "Monthly check BI-1", TriggerType = "Calendar", IntervalDays = 30,
            NextDueDate = DateTime.Today.AddDays(3), JobPlanId = jp.JobPlanId
        }, RcmTestFactory.Actor(5));
        var planId = env.Ctx.MaintenancePlans.Single().MaintenancePlanId;
        env.Ctx.WorkOrders.Add(new WorkOrder { EquipmentId = tag.EquipmentId, MaintenancePlanId = planId, WorkOrderNumber = "WO-1", WorkOrderType = "Preventive", Status = "Open", IsActive = true });
        await env.Ctx.SaveChangesAsync();

        var tagView = await env.Assignments.GetTagViewAsync(tag.EquipmentId);
        var strategyView = await env.Queries.GetDetailAsync(id);

        Assert.NotNull(tagView);
        Assert.Equal(("BI-1", "B", "Test PUMP_T"), (tagView!.Equipment.Tag, tagView.Equipment.CriticalityClass, tagView.Equipment.EquipmentClassName));
        Assert.Single(tagView.Assignments);
        Assert.Single(tagView.Plans);
        Assert.Equal("WO-1", Assert.Single(tagView.OpenWorkOrders).WorkOrderNumber);
        Assert.Equal("BI-1", Assert.Single(strategyView!.Assignments).Equipment.Tag);
        Assert.Equal(1, strategyView.Assignments[0].PlanCount);
    }

    [Fact]
    public async Task PlanEligibleAssignments_HaveDistinctLabels_AndExcludeUnscheduledStrategies()
    {
        var env = await Env(nameof(PlanEligibleAssignments_HaveDistinctLabels_AndExcludeUnscheduledStrategies));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var mode2 = await RcmTestFactory.AddModeAsync(env.Ctx, classId, "PUMP_T_VIB");
        var pm = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var cbm = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, mode2.FailureModeId, "CBM");
        var rtfMode = await RcmTestFactory.AddModeAsync(env.Ctx, classId, "PUMP_T_NOISE");
        var rtf = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, rtfMode.FailureModeId, "RunToFailure");
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "EL-1", classId);
        foreach (var s in new[] { pm, cbm, rtf })
            await env.Assignments.AssignAsync(s, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));

        var choices = await env.Assignments.GetPlanEligibleAssignmentsAsync(tag.EquipmentId);

        Assert.Equal(2, choices.Count); // RTF خارج است
        Assert.Equal(2, choices.Select(c => c.Label).Distinct().Count()); // برچسب‌ها متمایزند (نه «Preventive (Quarterly)» ×۵)
        Assert.All(choices, c => Assert.Contains("EL-1", c.Label));
    }
}
