using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Rcm;

/// <summary>
/// چرخه‌عمر استراتژی RCM روی سرویس واقعی (InMemory): قوانین سمت سرور، ورودی نامعتبر بدون استثنا (نه 500)،
/// تکراری‌ها، ویرایش با کنترل RowVersion، نسخه‌بندی، تفکیک وظایف از طریق موتور گردش‌کار، و تاریخچه‌ی Append-only.
/// </summary>
public class RcmStrategyLifecycleTests
{
    private static Task<RcmEnv> Env(string name) => RcmTestFactory.CreateEnvAsync($"Life_{name}");

    private static bool Has(OpResult r, string code) => r.Errors.Any(e => e.Code == code);

    // ---------- A: server-side enforcement ----------

    [Fact]
    public async Task Create_ReportedDefect_RunToFailureSafetyWithoutModeOrJustification_IsRejected_AndNothingSaved()
    {
        var env = await Env(nameof(Create_ReportedDefect_RunToFailureSafetyWithoutModeOrJustification_IsRejected_AndNothingSaved));
        var (classId, _) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);

        var result = await env.Commands.CreateAsync(new StrategyInputDto
        {
            StrategyType = "RunToFailure", ConsequenceCategory = "Safety", Frequency = "Daily", EquipmentClassId = classId
        }, RcmTestFactory.Actor(1));

        Assert.False(result.Success);
        Assert.True(Has(result, "Rcm_Err_RtfNotAllowedForConsequence"));
        Assert.True(Has(result, "Rcm_Err_FailureModeRequired"));
        Assert.True(Has(result, "Rcm_Err_JustificationTooShort"));
        Assert.Empty(env.Ctx.MaintenanceStrategies);
    }

    [Fact]
    public async Task Create_BogusReferences_ReturnErrorCodes_NotExceptions()
    {
        var env = await Env(nameof(Create_BogusReferences_ReturnErrorCodes_NotExceptions));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);

        var input = RcmTestFactory.ValidInput(999, 998);
        input.OwnerDisciplineId = 997;
        input.FailureCauseId = 996;
        var result = await env.Commands.CreateAsync(input, RcmTestFactory.Actor(1));

        Assert.True(Has(result, "Rcm_Err_ClassNotFound"));
        Assert.True(Has(result, "Rcm_Err_FailureModeNotFound"));
        Assert.True(Has(result, "Rcm_Err_DisciplineNotFound"));
        Assert.True(Has(result, "Rcm_Err_FailureCauseNotFound"));
        Assert.Empty(env.Ctx.MaintenanceStrategies);
        _ = (classId, modeId);
    }

    [Fact]
    public async Task Create_UnknownTypeConsequenceFrequencyAndPastStartDate_AreAllReported()
    {
        var env = await Env(nameof(Create_UnknownTypeConsequenceFrequencyAndPastStartDate_AreAllReported));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var input = RcmTestFactory.ValidInput(classId, modeId);
        input.StrategyType = "Nonsense";
        input.ConsequenceCategory = "Nonsense";
        input.Frequency = "Nonsense";
        input.StartDate = DateTime.Today.AddDays(-5);

        var result = await env.Commands.CreateAsync(input, RcmTestFactory.Actor(1));

        Assert.True(Has(result, "Rcm_Err_UnknownStrategyType"));
        Assert.True(Has(result, "Rcm_Err_UnknownConsequence"));
        Assert.True(Has(result, "Rcm_Err_UnknownFrequency"));
        Assert.True(Has(result, "Rcm_Err_StartDateInPast"));
    }

    [Fact]
    public async Task Create_FailureModeOfAnotherClass_IsRejected_ModeOfAncestorClass_IsAccepted()
    {
        var env = await Env(nameof(Create_FailureModeOfAnotherClass_IsRejected_ModeOfAncestorClass_IsAccepted));
        var (parentClass, parentMode) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "PARENT");
        var (otherClass, otherMode) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "OTHER");
        var child = new EquipmentClass { Code = "CHILD", Name = "Child", ParentEquipmentClassId = parentClass, IsActive = true };
        env.Ctx.EquipmentClasses.Add(child);
        await env.Ctx.SaveChangesAsync();

        var mismatch = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(child.EquipmentClassId, otherMode), RcmTestFactory.Actor(1));
        var inherited = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(child.EquipmentClassId, parentMode), RcmTestFactory.Actor(1));

        Assert.True(Has(mismatch, "Rcm_Err_FailureModeClassMismatch"));
        Assert.True(inherited.Success);
        _ = otherClass;
    }

    [Fact]
    public async Task Create_LegacyPersianLabels_AreAcceptedAndStoredAsStableCodes()
    {
        var env = await Env(nameof(Create_LegacyPersianLabels_AreAcceptedAndStoredAsStableCodes));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var input = RcmTestFactory.ValidInput(classId, modeId);
        input.StrategyType = "پیشگیرانه";
        input.ConsequenceCategory = "عملیاتی";
        input.Frequency = "ماهانه";

        var result = await env.Commands.CreateAsync(input, RcmTestFactory.Actor(1));

        Assert.True(result.Success);
        var s = env.Ctx.MaintenanceStrategies.Single();
        Assert.Equal(("Preventive", "Operational", "Monthly"), (s.StrategyType, s.ConsequenceCategory, s.Frequency));
        Assert.Equal(30, s.DefaultIntervalDays);
    }

    [Fact]
    public async Task Create_Duplicate_IsRejectedWithVisibleError_AndAllowedAgainAfterRetirement()
    {
        var env = await Env(nameof(Create_Duplicate_IsRejectedWithVisibleError_AndAllowedAgainAfterRetirement));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var actor = RcmTestFactory.Actor(1);

        var first = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor);
        var second = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor);
        var differentType = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId, "Predictive"), actor);

        Assert.True(first.Success);
        Assert.True(Has(second, "Rcm_Err_DuplicateStrategy"));
        Assert.True(differentType.Success);

        await env.Commands.RetireAsync(first.Id!.Value, "obsolete", actor);
        var again = await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor);
        Assert.True(again.Success);
    }

    // ---------- C10: edit with change history and concurrency ----------

    [Fact]
    public async Task EditDraft_UpdatesInPlace_AndRecordsBeforeAfterInHistory()
    {
        var env = await Env(nameof(EditDraft_UpdatesInPlace_AndRecordsBeforeAfterInHistory));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var actor = RcmTestFactory.Actor(1, "alice");
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor)).Id!.Value;

        var edit = RcmTestFactory.ValidInput(classId, modeId);
        edit.Justification = "Updated after reviewing the vendor service bulletin 2026-04.";
        var result = await env.Commands.UpdateAsync(id, edit, actor);

        Assert.True(result.Success);
        Assert.Equal(id, result.Id);
        Assert.Equal(1, await env.Ctx.MaintenanceStrategies.CountAsync()); // درجا، نه ردیف جدید
        var history = await env.Queries.GetHistoryAsync(id);
        var edited = history.Single(h => h.Action == "Edited");
        Assert.Equal("alice", edited.ActorName);
        Assert.Contains("Justification:", edited.ChangesSummary);
        Assert.Contains("->", edited.ChangesSummary);
        Assert.Contains(history, h => h.Action == "Created");
    }

    [Fact]
    public async Task EditDraft_WithStaleRowVersion_ReturnsConcurrencyConflict_AndKeepsOtherUsersChange()
    {
        var env = await Env(nameof(EditDraft_WithStaleRowVersion_ReturnsConcurrencyConflict_AndKeepsOtherUsersChange));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var actor = RcmTestFactory.Actor(1);
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor)).Id!.Value;

        var row = env.Ctx.MaintenanceStrategies.Single();
        row.RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 }; // نسخه‌ی فعلی ردیف (پس از تغییر کاربر دوم)
        await env.Ctx.SaveChangesAsync();

        var stale = RcmTestFactory.ValidInput(classId, modeId);
        stale.Justification = "This edit was prepared from an outdated copy of the form.";
        stale.RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 3 };
        var result = await env.Commands.UpdateAsync(id, stale, actor);

        Assert.True(Has(result, "Rcm_Err_ConcurrencyConflict"));
        Assert.Equal(RcmTestFactory.GoodJustification, env.Ctx.MaintenanceStrategies.AsNoTracking().Single().Justification);
    }

    [Fact]
    public async Task EditApprovedOrActive_CreatesNewDraftVersion_AndKeepsTheApprovedOneUnchanged()
    {
        var env = await Env(nameof(EditApprovedOrActive_CreatesNewDraftVersion_AndKeepsTheApprovedOneUnchanged));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var v1 = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);

        var edit = RcmTestFactory.ValidInput(classId, modeId);
        edit.Frequency = "Weekly";
        var result = await env.Commands.UpdateAsync(v1, edit, RcmTestFactory.Actor(1));

        Assert.True(result.Success);
        Assert.NotEqual(v1, result.Id);
        Assert.Equal("Rcm_Info_NewVersionCreated", result.InfoCode);
        var old = await env.Ctx.MaintenanceStrategies.AsNoTracking().SingleAsync(x => x.MaintenanceStrategyId == v1);
        var next = await env.Ctx.MaintenanceStrategies.AsNoTracking().SingleAsync(x => x.MaintenanceStrategyId == result.Id);
        Assert.Equal(("Active", "Monthly", 1), (old.Status, old.Frequency, old.VersionNumber));
        Assert.Equal(("Draft", "Weekly", 2), (next.Status, next.Frequency, next.VersionNumber));
        Assert.Equal(old.FamilyId, next.FamilyId);
        Assert.Equal(v1, next.SupersedesStrategyId);

        var again = await env.Commands.UpdateAsync(v1, edit, RcmTestFactory.Actor(1)); // دومین پیش‌نویس هم‌خانواده
        Assert.True(Has(again, "Rcm_Err_DraftVersionExists"));
    }

    [Fact]
    public async Task InReviewAndRetiredStrategies_CannotBeEdited()
    {
        var env = await Env(nameof(InReviewAndRetiredStrategies_CannotBeEdited));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var actor = RcmTestFactory.Actor(1);
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), actor)).Id!.Value;
        await env.Commands.SubmitForReviewAsync(id, actor);

        Assert.True(Has(await env.Commands.UpdateAsync(id, RcmTestFactory.ValidInput(classId, modeId), actor), "Rcm_Err_LockedInReview"));

        await env.Commands.ReviewAsync(id, false, "needs rework", RcmTestFactory.Actor(2));
        await env.Commands.RetireAsync(id, null, actor);
        Assert.True(Has(await env.Commands.UpdateAsync(id, RcmTestFactory.ValidInput(classId, modeId), actor), "Rcm_Err_NotEditableStatus"));
    }

    [Fact]
    public async Task Update_OfUnknownStrategy_ReturnsNotFoundCode()
    {
        var env = await Env(nameof(Update_OfUnknownStrategy_ReturnsNotFoundCode));
        var result = await env.Commands.UpdateAsync(424242, new StrategyInputDto(), RcmTestFactory.Actor(1));
        Assert.True(Has(result, "Rcm_Err_StrategyNotFound"));
    }

    // ---------- C11: workflow + separation of duties ----------

    [Fact]
    public async Task Workflow_DraftToActive_WithIndependentReviewer_Succeeds()
    {
        var env = await Env(nameof(Workflow_DraftToActive_WithIndependentReviewer_Succeeds));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);

        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);

        var s = await env.Ctx.MaintenanceStrategies.AsNoTracking().SingleAsync(x => x.MaintenanceStrategyId == id);
        Assert.Equal("Active", s.Status);
        Assert.Equal(2, s.ApprovedByEmployeeId);
        Assert.NotNull(s.ApprovedAt);
        var actions = (await env.Queries.GetHistoryAsync(id)).Select(h => h.Action).ToList();
        Assert.Equal(new[] { "Created", "Submitted", "Approved", "Activated" }, actions.AsEnumerable().Reverse());
    }

    [Fact]
    public async Task Author_CannotApproveOwnStrategy_AndSubmitterCannotEither()
    {
        var env = await Env(nameof(Author_CannotApproveOwnStrategy_AndSubmitterCannotEither));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var author = RcmTestFactory.Actor(1, "author");
        var submitter = RcmTestFactory.Actor(3, "submitter");
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), author)).Id!.Value;
        await env.Commands.SubmitForReviewAsync(id, submitter);

        Assert.True(Has(await env.Commands.ReviewAsync(id, true, null, author), "Rcm_Err_SeparationOfDuties"));
        Assert.True(Has(await env.Commands.ReviewAsync(id, true, null, submitter), "Rcm_Err_SeparationOfDuties"));
        Assert.Equal("InReview", env.Ctx.MaintenanceStrategies.AsNoTracking().Single().Status);

        Assert.True((await env.Commands.ReviewAsync(id, true, null, RcmTestFactory.Actor(2, "reviewer"))).Success);
    }

    [Fact]
    public async Task Reject_RequiresComment_AndReturnsStrategyToDraft()
    {
        var env = await Env(nameof(Reject_RequiresComment_AndReturnsStrategyToDraft));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var author = RcmTestFactory.Actor(1);
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), author)).Id!.Value;
        await env.Commands.SubmitForReviewAsync(id, author);

        Assert.True(Has(await env.Commands.ReviewAsync(id, false, "  ", RcmTestFactory.Actor(2)), "Rcm_Err_RejectCommentRequired"));
        var rejected = await env.Commands.ReviewAsync(id, false, "Justification is not convincing.", RcmTestFactory.Actor(2));

        Assert.True(rejected.Success);
        Assert.Equal("Draft", env.Ctx.MaintenanceStrategies.AsNoTracking().Single().Status);
        Assert.Contains((await env.Queries.GetHistoryAsync(id)), h => h.Action == "Rejected" && h.Comment == "Justification is not convincing.");
    }

    [Fact]
    public async Task WorkflowEngineRefusal_IsReportedAsError_AndStatusDoesNotAdvance()
    {
        var env = await Env(nameof(WorkflowEngineRefusal_IsReportedAsError_AndStatusDoesNotAdvance));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var author = RcmTestFactory.Actor(1);
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), author)).Id!.Value;

        env.Runtime.ThrowOnStart = true;
        Assert.True(Has(await env.Commands.SubmitForReviewAsync(id, author), "Rcm_Err_WorkflowUnavailable"));
        Assert.Equal("Draft", env.Ctx.MaintenanceStrategies.AsNoTracking().Single().Status);

        env.Runtime.ThrowOnStart = false;
        await env.Commands.SubmitForReviewAsync(id, author);
        env.Runtime.ThrowOnExecute = true;
        Assert.True(Has(await env.Commands.ReviewAsync(id, true, null, RcmTestFactory.Actor(2)), "Rcm_Err_WorkflowActorNotAllowed"));
        Assert.Equal("InReview", env.Ctx.MaintenanceStrategies.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task InvalidTransitions_AreRejected()
    {
        var env = await Env(nameof(InvalidTransitions_AreRejected));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var author = RcmTestFactory.Actor(1);
        var id = (await env.Commands.CreateAsync(RcmTestFactory.ValidInput(classId, modeId), author)).Id!.Value;

        Assert.True(Has(await env.Commands.ReviewAsync(id, true, null, RcmTestFactory.Actor(2)), "Rcm_Err_InvalidTransition")); // Draft → Approved
        Assert.True(Has(await env.Commands.ActivateAsync(id, true, RcmTestFactory.Actor(2)), "Rcm_Err_InvalidTransition"));      // Draft → Active
    }

    // ---------- C12: versioning ----------

    [Fact]
    public async Task ActivatingNewVersion_SupersedesOld_MovesAssignments_AndFlagsPlansOnTheOldVersion()
    {
        var env = await Env(nameof(ActivatingNewVersion_SupersedesOld_MovesAssignments_AndFlagsPlansOnTheOldVersion));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var v1 = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "P-1", classId);
        await env.Assignments.AssignAsync(v1, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        var jp = await RcmTestFactory.AddJobPlanAsync(env.Ctx);
        var plan = await env.Plans().CreateAsync(new PlanInputDto
        {
            MaintenanceStrategyAssignmentId = assignmentId, Title = "Monthly lubrication P-1", TriggerType = "Calendar",
            IntervalDays = 30, NextDueDate = DateTime.Today.AddDays(5), JobPlanId = jp.JobPlanId
        }, RcmTestFactory.Actor(5));
        Assert.True(plan.Success);

        var edit = RcmTestFactory.ValidInput(classId, modeId);
        edit.Frequency = "Weekly";
        var v2 = (await env.Commands.UpdateAsync(v1, edit, RcmTestFactory.Actor(1))).Id!.Value;
        await env.Commands.SubmitForReviewAsync(v2, RcmTestFactory.Actor(1));
        await env.Commands.ReviewAsync(v2, true, null, RcmTestFactory.Actor(2));
        var activated = await env.Commands.ActivateAsync(v2, moveAssignmentsFromPreviousVersion: true, RcmTestFactory.Actor(2));

        Assert.True(activated.Success);
        Assert.Equal("Superseded", env.Ctx.MaintenanceStrategies.AsNoTracking().Single(x => x.MaintenanceStrategyId == v1).Status);
        Assert.Equal(v2, env.Ctx.MaintenanceStrategyAssignments.AsNoTracking().Single().MaintenanceStrategyId);

        var detail = await env.Queries.GetDetailAsync(v2);
        Assert.NotNull(detail);
        Assert.Equal(new[] { 1, 2 }, detail!.Versions.Select(v => v.VersionNumber).OrderBy(x => x));
        var usage = Assert.Single(detail.Plans);
        Assert.Equal(1, usage.StrategyVersion); // Plan هنوز روی نسخه‌ی ۱ است
        Assert.True(usage.OutOfSync);
    }

    [Fact]
    public async Task Activation_IsBlocked_WhenNewVersionTypeIsForbiddenForACriticalTagAlreadyAssigned()
    {
        var env = await Env(nameof(Activation_IsBlocked_WhenNewVersionTypeIsForbiddenForACriticalTagAlreadyAssigned));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var v1 = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "P-A", classId);
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, tag.EquipmentId, "A");
        await env.Assignments.AssignAsync(v1, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));

        var edit = RcmTestFactory.ValidInput(classId, modeId, "RunToFailure");
        edit.Frequency = null;
        var v2 = (await env.Commands.UpdateAsync(v1, edit, RcmTestFactory.Actor(1))).Id!.Value;
        await env.Commands.SubmitForReviewAsync(v2, RcmTestFactory.Actor(1));
        await env.Commands.ReviewAsync(v2, true, null, RcmTestFactory.Actor(2));

        var activation = await env.Commands.ActivateAsync(v2, true, RcmTestFactory.Actor(2));

        Assert.True(Has(activation, "Rcm_Err_CriticalityADisallowsReactive"));
        Assert.Equal("Active", env.Ctx.MaintenanceStrategies.AsNoTracking().Single(x => x.MaintenanceStrategyId == v1).Status);
    }

    // ---------- C13: retirement / audit ----------

    [Fact]
    public async Task Retire_ClosesAssignments_HidesStrategy_AndKeepsHistory()
    {
        var env = await Env(nameof(Retire_ClosesAssignments_HidesStrategy_AndKeepsHistory));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "P-R", classId);
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));

        var result = await env.Commands.RetireAsync(id, "plant decommissioned", RcmTestFactory.Actor(2, "manager"));

        Assert.True(result.Success);
        var s = env.Ctx.MaintenanceStrategies.AsNoTracking().Single();
        Assert.Equal(("Retired", false), (s.Status, s.IsActive));
        var a = env.Ctx.MaintenanceStrategyAssignments.AsNoTracking().Single();
        Assert.Equal(("Retired", false), (a.Status, a.IsActive));
        var retired = (await env.Queries.GetHistoryAsync(id)).First();
        Assert.Equal(("Retired", "manager", "plant decommissioned"), (retired.Action, retired.ActorName, retired.Comment));
        Assert.True(Has(await env.Commands.RetireAsync(id, null, RcmTestFactory.Actor(2)), "Rcm_Err_AlreadyRetired"));
    }

    [Fact]
    public async Task EveryLifecycleStep_IsWrittenToHistory_WithActorAndTimestamp()
    {
        var env = await Env(nameof(EveryLifecycleStep_IsWrittenToHistory_WithActorAndTimestamp));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);

        var history = await env.Queries.GetHistoryAsync(id);

        Assert.Equal(4, history.Count);
        Assert.All(history, h => { Assert.False(string.IsNullOrEmpty(h.ActorName)); Assert.True(h.Timestamp > DateTime.Now.AddMinutes(-5)); });
    }

    [Fact]
    public async Task GlobalAuditLog_RecordsCreateEditApproveAndRetire_WithBeforeAfterAndUser()
    {
        var env = await Env(nameof(GlobalAuditLog_RecordsCreateEditApproveAndRetire_WithBeforeAfterAndUser));
        env.Ctx.CurrentUserName = "auditor";
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        await env.Commands.RetireAsync(id, null, RcmTestFactory.Actor(2));

        var audit = await env.Ctx.AuditLogs.Where(a => a.EntityName == "MaintenanceStrategy").OrderBy(a => a.AuditLogId).ToListAsync();

        Assert.Contains(audit, a => a.Action == "Insert");
        var updates = audit.Where(a => a.Action == "Update").Select(a => a.ChangedFieldsSummary ?? "").ToList();
        Assert.Contains(updates, s => s.Contains("Status: Draft -> InReview"));
        Assert.Contains(updates, s => s.Contains("Status: InReview -> Approved"));
        Assert.Contains(updates, s => s.Contains("Status: Approved -> Active"));
        Assert.Contains(updates, s => s.Contains("Status: Active -> Retired") && s.Contains("IsActive: True -> False"));
        Assert.All(audit, a => Assert.Equal("auditor", a.UserName));
    }

    // ---------- list ----------

    [Fact]
    public async Task List_ShowsClassFailureModeStatusVersionAndAssignedTagCount()
    {
        var env = await Env(nameof(List_ShowsClassFailureModeStatusVersionAndAssignedTagCount));
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var t1 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "L-1", classId);
        var t2 = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "L-2", classId);
        await env.Assignments.AssignAsync(id, new[] { t1.EquipmentId, t2.EquipmentId }, RcmTestFactory.Actor(5));

        var page = await env.Queries.GetListAsync(new StrategyListFilter { EquipmentClassId = classId });

        var row = Assert.Single(page.Items);
        Assert.Equal(("Preventive", "Active", 1, 2), (row.StrategyType, row.Status, row.VersionNumber, row.AssignedTagCount));
        Assert.Equal("PUMP_T_LEAK", row.FailureModeCode);
        Assert.Equal("Test PUMP_T", row.EquipmentClassName);

        var byTag = await env.Queries.GetListAsync(new StrategyListFilter { EquipmentId = t1.EquipmentId });
        Assert.Single(byTag.Items);
    }
}
