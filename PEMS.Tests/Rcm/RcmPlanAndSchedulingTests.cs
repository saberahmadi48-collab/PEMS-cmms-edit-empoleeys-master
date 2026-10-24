using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Rcm;

/// <summary>
/// ساخت Plan از تخصیص (تجهیز مشتق از سرور، نه کلاینت)، اعتبارسنجی Plan، و تولید WO: اولویت/رشته از استراتژی،
/// سیاست زمان‌بندی Fixed/Floating، عدم «طوفان جبران»، و رد Planهای بدون کار زمان‌بندی‌شده.
/// </summary>
public class RcmPlanAndSchedulingTests
{
    private static Task<RcmEnv> Env(string name) => RcmTestFactory.CreateEnvAsync($"Plan_{name}");

    private sealed record Seed(RcmEnv Env, int ClassId, int ModeId, int StrategyId, Equipment Tag, int AssignmentId, int JobPlanId);

    private static async Task<Seed> SeedAsync(string name, string type = "Preventive", string consequence = "Operational",
        string? criticality = null, int? ownerDisciplineId = null)
    {
        var env = await Env(name);
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var input = RcmTestFactory.ValidInput(classId, modeId, type, consequence);
        input.OwnerDisciplineId = ownerDisciplineId;
        input.RequiredSkills = "Vibration analyst";
        var created = await env.Commands.CreateAsync(input, RcmTestFactory.Actor(1));
        Assert.True(created.Success, string.Join(",", created.Errors.Select(e => e.Code)));
        await env.Commands.SubmitForReviewAsync(created.Id!.Value, RcmTestFactory.Actor(1));
        await env.Commands.ReviewAsync(created.Id.Value, true, null, RcmTestFactory.Actor(2));
        await env.Commands.ActivateAsync(created.Id.Value, true, RcmTestFactory.Actor(2));

        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "80-P-101", classId);
        if (criticality != null) await RcmTestFactory.AddCriticalityAsync(env.Ctx, tag.EquipmentId, criticality);
        var assigned = await env.Assignments.AssignAsync(created.Id.Value, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        Assert.True(assigned.Success && assigned.Assigned == 1, string.Join(",", assigned.Errors.Select(e => e.Code)));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        var jp = await RcmTestFactory.AddJobPlanAsync(env.Ctx);
        return new Seed(env, classId, modeId, created.Id.Value, tag, assignmentId, jp.JobPlanId);
    }

    private static PlanInputDto Input(Seed s, Action<PlanInputDto>? tweak = null)
    {
        var i = new PlanInputDto
        {
            MaintenanceStrategyAssignmentId = s.AssignmentId, Title = "Monthly lubrication 80-P-101", TriggerType = "Calendar",
            IntervalDays = 30, NextDueDate = DateTime.Today.AddDays(7), JobPlanId = s.JobPlanId
        };
        tweak?.Invoke(i);
        return i;
    }

    private static bool Has(OpResult r, string code) => r.Errors.Any(e => e.Code == code);

    // ---------- D15: equipment derived from the assignment ----------

    [Fact]
    public async Task Plan_IsBuiltFromTheAssignment_EquipmentAndStrategyAreDerivedServerSide()
    {
        var s = await SeedAsync(nameof(Plan_IsBuiltFromTheAssignment_EquipmentAndStrategyAreDerivedServerSide));

        var result = await s.Env.Plans().CreateAsync(Input(s), RcmTestFactory.Actor(5));

        Assert.True(result.Success);
        var plan = await s.Env.Ctx.MaintenancePlans.AsNoTracking().SingleAsync();
        Assert.Equal(s.Tag.EquipmentId, plan.EquipmentId);
        Assert.Equal(s.StrategyId, plan.MaintenanceStrategyId);
        Assert.Equal(s.AssignmentId, plan.MaintenanceStrategyAssignmentId);
        Assert.StartsWith("MaintenancePlan-", plan.PlanNumber);
    }

    [Fact]
    public async Task Plan_ForUnknownAssignment_ReturnsErrorCode_NotAnException()
    {
        var env = await Env(nameof(Plan_ForUnknownAssignment_ReturnsErrorCode_NotAnException));
        var result = await env.Plans().CreateAsync(new PlanInputDto { MaintenanceStrategyAssignmentId = 987654, Title = "Whatever title" }, RcmTestFactory.Actor(1));
        Assert.True(Has(result, "Rcm_Err_AssignmentNotFound"));
        Assert.Empty(env.Ctx.MaintenancePlans);
    }

    [Fact]
    public async Task FormContext_PrefillsTagIdentity_SuggestedTitleAndCriticalityScaledInterval()
    {
        var s = await SeedAsync(nameof(FormContext_PrefillsTagIdentity_SuggestedTitleAndCriticalityScaledInterval), criticality: "A");

        var ctx = await s.Env.Plans().GetFormContextAsync(s.AssignmentId);

        Assert.NotNull(ctx);
        Assert.Equal(("80-P-101", "A", "Test PUMP_T"), (ctx!.Equipment.Tag, ctx.Equipment.CriticalityClass, ctx.Equipment.EquipmentClassName));
        Assert.Equal("80-P-101 - Leakage PUMP_T - Preventive", ctx.SuggestedTitle);
        Assert.Equal(15, ctx.SuggestedIntervalDays); // 30 روز × ضریب بحرانی A (۰٫۵)
        Assert.Equal("Calendar", ctx.RecommendedTriggerType);
    }

    // ---------- D14: RTF / Corrective ----------

    [Fact]
    public async Task Plan_OnRunToFailureStrategy_IsBlocked()
    {
        var s = await SeedAsync(nameof(Plan_OnRunToFailureStrategy_IsBlocked), type: "RunToFailure");
        var result = await s.Env.Plans().CreateAsync(Input(s), RcmTestFactory.Actor(5));
        Assert.True(Has(result, "Rcm_Err_PlanNotAllowedForType"));
        Assert.Empty(s.Env.Ctx.MaintenancePlans);
    }

    [Fact]
    public async Task Plan_OnApprovedButNotActiveStrategy_IsAllowed_OnRetiredOrDraft_IsBlocked()
    {
        var s = await SeedAsync(nameof(Plan_OnApprovedButNotActiveStrategy_IsAllowed_OnRetiredOrDraft_IsBlocked));
        var strategy = await s.Env.Ctx.MaintenanceStrategies.SingleAsync();
        strategy.Status = "Approved";
        await s.Env.Ctx.SaveChangesAsync();
        Assert.True((await s.Env.Plans().CreateAsync(Input(s), RcmTestFactory.Actor(5))).Success);

        strategy.Status = "Draft";
        await s.Env.Ctx.SaveChangesAsync();
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.Title = "A different title"), RcmTestFactory.Actor(5)), "Rcm_Err_StrategyNotApproved"));

        strategy.Status = "Active";
        var assignment = await s.Env.Ctx.MaintenanceStrategyAssignments.SingleAsync();
        assignment.Status = "Retired";
        assignment.IsActive = false;
        await s.Env.Ctx.SaveChangesAsync();
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.Title = "Yet another title"), RcmTestFactory.Actor(5)), "Rcm_Err_AssignmentNotActive"));
    }

    // ---------- D16–D18 ----------

    [Fact]
    public async Task ReportedDefect_NegativeIntervalAnd2020DueDate_AreRejectedByTheService()
    {
        var s = await SeedAsync(nameof(ReportedDefect_NegativeIntervalAnd2020DueDate_AreRejectedByTheService));
        var result = await s.Env.Plans().CreateAsync(Input(s, i => { i.IntervalDays = -2; i.NextDueDate = new DateTime(2020, 1, 1); }), RcmTestFactory.Actor(5));
        Assert.True(Has(result, "Rcm_Err_IntervalMustBePositive"));
        Assert.True(Has(result, "Rcm_Err_NextDueInPast"));
        Assert.Empty(s.Env.Ctx.MaintenancePlans);
    }

    [Fact]
    public async Task PastDueDate_IsAcceptedOnlyWithExplicitConfirmation()
    {
        var s = await SeedAsync(nameof(PastDueDate_IsAcceptedOnlyWithExplicitConfirmation));
        var past = DateTime.Today.AddDays(-10);
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.NextDueDate = past), RcmTestFactory.Actor(5)), "Rcm_Err_NextDueInPast"));
        Assert.True((await s.Env.Plans().CreateAsync(Input(s, i => { i.NextDueDate = past; i.AllowPastDueDate = true; }), RcmTestFactory.Actor(5))).Success);
    }

    [Fact]
    public async Task CalendarPlan_WithoutDueDate_DefaultsToTodayPlusInterval()
    {
        var s = await SeedAsync(nameof(CalendarPlan_WithoutDueDate_DefaultsToTodayPlusInterval));
        var now = new DateTime(2026, 10, 13, 8, 0, 0);
        var result = await s.Env.Plans().CreateAsync(Input(s, i => i.NextDueDate = null), RcmTestFactory.Actor(5), now);
        Assert.True(result.Success);
        Assert.Equal(now.AddDays(30), s.Env.Ctx.MaintenancePlans.Single().NextDueDate);
    }

    [Fact]
    public async Task HiddenFormFields_AreNormalizedAway_ByTriggerType()
    {
        var s = await SeedAsync(nameof(HiddenFormFields_AreNormalizedAway_ByTriggerType));
        var result = await s.Env.Plans().CreateAsync(Input(s, i => { i.MeterType = "Cycles"; i.MeterInterval = 500; }), RcmTestFactory.Actor(5));
        Assert.True(result.Success);
        var plan = s.Env.Ctx.MaintenancePlans.Single();
        Assert.Null(plan.MeterType);
        Assert.Null(plan.MeterInterval);
    }

    [Fact]
    public async Task CbmPlan_WithMeterTrigger_IsRejected_CalendarInspectionIsAccepted()
    {
        var s = await SeedAsync(nameof(CbmPlan_WithMeterTrigger_IsRejected_CalendarInspectionIsAccepted), type: "CBM");
        var meter = await s.Env.Plans().CreateAsync(Input(s, i => { i.TriggerType = "Both"; i.MeterType = "RunningHours"; i.MeterInterval = 5000; }), RcmTestFactory.Actor(5));
        Assert.True(Has(meter, "Rcm_Err_ConditionBasedCalendarOnly"));
        var tooLong = await s.Env.Plans().CreateAsync(Input(s, i => i.IntervalDays = 90), RcmTestFactory.Actor(5)); // بازرسی استراتژی: ۳۰ روز
        Assert.True(Has(tooLong, "Rcm_Err_PlanIntervalExceedsInspection"));
        Assert.True((await s.Env.Plans().CreateAsync(Input(s), RcmTestFactory.Actor(5))).Success);
    }

    [Fact]
    public async Task Title_MustBeMeaningful_AndUniquePerEquipment()
    {
        var s = await SeedAsync(nameof(Title_MustBeMeaningful_AndUniquePerEquipment));
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.Title = "fvv"), RcmTestFactory.Actor(5)), "Rcm_Err_PlanTitleInvalid"));
        Assert.True((await s.Env.Plans().CreateAsync(Input(s), RcmTestFactory.Actor(5))).Success);
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.Title = "  MONTHLY LUBRICATION 80-p-101 "), RcmTestFactory.Actor(5)), "Rcm_Err_PlanTitleDuplicate"));
    }

    [Fact]
    public async Task JobPlan_IsRequired_AndMustExist()
    {
        var s = await SeedAsync(nameof(JobPlan_IsRequired_AndMustExist));
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.JobPlanId = null), RcmTestFactory.Actor(5)), "Rcm_Err_JobPlanRequired"));
        Assert.True(Has(await s.Env.Plans().CreateAsync(Input(s, i => i.JobPlanId = 4242), RcmTestFactory.Actor(5)), "Rcm_Err_JobPlanNotFound"));
    }

    [Fact]
    public async Task MeterPlan_NeedsTypeAndInterval_AndCarriesNoCalendarFields()
    {
        var s = await SeedAsync(nameof(MeterPlan_NeedsTypeAndInterval_AndCarriesNoCalendarFields));
        var bad = await s.Env.Plans().CreateAsync(Input(s, i => { i.TriggerType = "Meter"; i.IntervalDays = null; i.NextDueDate = null; }), RcmTestFactory.Actor(5));
        Assert.True(Has(bad, "Rcm_Err_MeterTypeRequired"));
        Assert.True(Has(bad, "Rcm_Err_MeterIntervalMustBePositive"));

        var ok = await s.Env.Plans().CreateAsync(Input(s, i => { i.TriggerType = "Meter"; i.MeterType = "RunningHours"; i.MeterInterval = 2000; }), RcmTestFactory.Actor(5));
        Assert.True(ok.Success);
        var plan = s.Env.Ctx.MaintenancePlans.Single();
        Assert.Null(plan.IntervalDays);
        Assert.Null(plan.NextDueDate);
    }

    // ---------- D19: scheduling ----------

    private static async Task<MaintenancePlan> OverduePlanAsync(Seed s, string mode = "Fixed", int interval = 30)
    {
        var plan = new MaintenancePlan
        {
            EquipmentId = s.Tag.EquipmentId, MaintenanceStrategyId = s.StrategyId, MaintenanceStrategyAssignmentId = s.AssignmentId,
            PlanNumber = "MP-" + Guid.NewGuid().ToString("N")[..5], Title = "Overdue plan " + Guid.NewGuid().ToString("N")[..4],
            TriggerType = "Calendar", IntervalDays = interval, NextDueDate = new DateTime(2020, 1, 1), ScheduleMode = mode,
            JobPlanId = s.JobPlanId, IsActive = true
        };
        s.Env.Ctx.MaintenancePlans.Add(plan);
        await s.Env.Ctx.SaveChangesAsync();
        return plan;
    }

    private static PMGenerationService Pm(RcmEnv env) => new(env.Ctx, new FakeNumberingService());

    [Fact]
    public async Task FixedSchedule_OverduePlan_GeneratesOneWorkOrder_AndJumpsToTheNextFutureDue_WithoutCatchUp()
    {
        var s = await SeedAsync(nameof(FixedSchedule_OverduePlan_GeneratesOneWorkOrder_AndJumpsToTheNextFutureDue_WithoutCatchUp));
        var plan = await OverduePlanAsync(s);

        var first = await Pm(s.Env).GenerateDueWorkOrdersAsync();
        var second = await Pm(s.Env).GenerateDueWorkOrdersAsync();

        Assert.Equal(1, first.WorkOrdersGenerated);
        Assert.Equal(0, second.WorkOrdersGenerated); // نه طوفان جبران و نه WO تکراری
        Assert.Single(s.Env.Ctx.WorkOrders);
        var refreshed = await s.Env.Ctx.MaintenancePlans.AsNoTracking().SingleAsync(p => p.MaintenancePlanId == plan.MaintenancePlanId);
        Assert.True(refreshed.NextDueDate > DateTime.Now);
        Assert.True(refreshed.NextDueDate <= DateTime.Now.AddDays(31));
        Assert.NotNull(refreshed.LastExecutionDate);
        Assert.Equal(new DateTime(2020, 1, 1), s.Env.Ctx.WorkOrders.Single().DueDate); // سررسید پوشش‌داده‌شده ثبت شد
    }

    [Fact]
    public async Task PlanWithoutInterval_KeepsItsDueDate_ThisWasTheReportedNonAdvancingCase()
    {
        // علت ریشه‌ای مسئله ۱۹: Plan تقویمی بدون IntervalDays هرگز جلو نمی‌رفت؛ اکنون چنین Planی ساخته نمی‌شود (قاعده‌ی ۱۶)،
        // ولی Plan قدیمی موجود نباید خراب شود: WO ساخته می‌شود و سررسید دست‌نخورده می‌ماند.
        var s = await SeedAsync(nameof(PlanWithoutInterval_KeepsItsDueDate_ThisWasTheReportedNonAdvancingCase));
        var plan = await OverduePlanAsync(s);
        plan.IntervalDays = null;
        await s.Env.Ctx.SaveChangesAsync();

        var number = await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);

        Assert.NotNull(number);
        Assert.Equal(new DateTime(2020, 1, 1), s.Env.Ctx.MaintenancePlans.AsNoTracking().Single().NextDueDate);
    }

    [Fact]
    public async Task FloatingSchedule_DoesNotAdvanceAtGeneration_AndFollowsActualCompletion()
    {
        var s = await SeedAsync(nameof(FloatingSchedule_DoesNotAdvanceAtGeneration_AndFollowsActualCompletion));
        var plan = await OverduePlanAsync(s, "Floating", 30);

        await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);
        Assert.Equal(new DateTime(2020, 1, 1), s.Env.Ctx.MaintenancePlans.AsNoTracking().Single().NextDueDate);
        Assert.Equal(0, await Pm(s.Env).ReconcileFloatingSchedulesAsync()); // WO هنوز تکمیل نشده

        var wo = s.Env.Ctx.WorkOrders.Single();
        wo.Status = "Completed";
        wo.CompletionDate = new DateTime(2026, 10, 1);
        await s.Env.Ctx.SaveChangesAsync();

        Assert.Equal(1, await Pm(s.Env).ReconcileFloatingSchedulesAsync());
        var refreshed = s.Env.Ctx.MaintenancePlans.AsNoTracking().Single();
        Assert.Equal(new DateTime(2026, 10, 31), refreshed.NextDueDate);
        Assert.Equal(new DateTime(2026, 10, 1), refreshed.LastCompletionDate);
        Assert.Equal(0, await Pm(s.Env).ReconcileFloatingSchedulesAsync()); // Idempotent
    }

    [Fact]
    public async Task ApprovedOverrideInterval_IsUsedWhenAdvancingTheSchedule()
    {
        var s = await SeedAsync(nameof(ApprovedOverrideInterval_IsUsedWhenAdvancingTheSchedule));
        await s.Env.Assignments.RequestOverrideAsync(s.AssignmentId, 10, null, RcmTestFactory.GoodJustification, RcmTestFactory.Actor(5));
        await s.Env.Assignments.DecideOverrideAsync(s.AssignmentId, true, null, RcmTestFactory.Actor(6));
        var plan = await OverduePlanAsync(s, interval: 30);
        plan.NextDueDate = DateTime.Now.AddDays(-1);
        await s.Env.Ctx.SaveChangesAsync();

        await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);

        var next = s.Env.Ctx.MaintenancePlans.AsNoTracking().Single().NextDueDate!.Value;
        Assert.InRange((next - DateTime.Now).TotalDays, 8.5, 10.1); // ۱۰ روز (Override)، نه ۳۰
    }

    // ---------- D20: WO derivation ----------

    [Fact]
    public async Task GeneratedWorkOrder_DerivesPriorityFromConsequenceAndCriticality_AndCopiesDiscipline()
    {
        var env = await Env(nameof(GeneratedWorkOrder_DerivesPriorityFromConsequenceAndCriticality_AndCopiesDiscipline));
        var discipline = new Discipline { Code = "MECH", Name = "Mechanical", IsActive = true };
        env.Ctx.Disciplines.Add(discipline);
        await env.Ctx.SaveChangesAsync();
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var input = RcmTestFactory.ValidInput(classId, modeId, "Preventive", "Safety");
        input.OwnerDisciplineId = discipline.DisciplineId;
        input.RequiredSkills = "Rotating equipment technician";
        var id = (await env.Commands.CreateAsync(input, RcmTestFactory.Actor(1))).Id!.Value;
        await env.Commands.SubmitForReviewAsync(id, RcmTestFactory.Actor(1));
        await env.Commands.ReviewAsync(id, true, null, RcmTestFactory.Actor(2));
        await env.Commands.ActivateAsync(id, true, RcmTestFactory.Actor(2));
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "SAFE-1", classId);
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, tag.EquipmentId, "A");
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var assignmentId = env.Ctx.MaintenanceStrategyAssignments.Single().MaintenanceStrategyAssignmentId;
        var jp = await RcmTestFactory.AddJobPlanAsync(env.Ctx);
        await env.Plans().CreateAsync(new PlanInputDto
        {
            MaintenanceStrategyAssignmentId = assignmentId, Title = "Safety valve proof test", TriggerType = "Calendar",
            IntervalDays = 30, NextDueDate = DateTime.Today.AddDays(1), JobPlanId = jp.JobPlanId
        }, RcmTestFactory.Actor(5));
        var planId = env.Ctx.MaintenancePlans.Single().MaintenancePlanId;

        var number = await Pm(env).GenerateForPlanAsync(planId);

        Assert.NotNull(number);
        var wo = env.Ctx.WorkOrders.Single();
        Assert.Equal("Critical", wo.Priority); // Safety + کلاس A (نه Medium ثابت)
        Assert.Equal(discipline.DisciplineId, wo.ResponsibleDisciplineId);
        Assert.Equal("Maintenance", wo.ResponsibleOrganization);
        Assert.Contains("Rotating equipment technician", wo.Description);
        Assert.Equal("Preventive", wo.WorkOrderType);
    }

    [Fact]
    public async Task GeneratedWorkOrder_PriorityFollowsConsequence_WhenEquipmentIsUnrated()
    {
        var s = await SeedAsync(nameof(GeneratedWorkOrder_PriorityFollowsConsequence_WhenEquipmentIsUnrated), consequence: "Environmental");
        var plan = await OverduePlanAsync(s);
        await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);
        Assert.Equal("High", s.Env.Ctx.WorkOrders.Single().Priority);
    }

    [Fact]
    public async Task Generation_SkipsPlansOfRunToFailureStrategies_AndRetiredAssignments()
    {
        var s = await SeedAsync(nameof(Generation_SkipsPlansOfRunToFailureStrategies_AndRetiredAssignments));
        var plan = await OverduePlanAsync(s);

        // نوع استراتژی تغییر می‌کند (داده‌ی قدیمی نامعتبر مثل Plan شماره ۵ دیتابیس)
        var strategy = await s.Env.Ctx.MaintenanceStrategies.SingleAsync();
        strategy.StrategyType = "RunToFailure";
        await s.Env.Ctx.SaveChangesAsync();
        Assert.Null(await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId));

        strategy.StrategyType = "Preventive";
        var assignment = await s.Env.Ctx.MaintenanceStrategyAssignments.SingleAsync();
        assignment.IsActive = false;
        assignment.Status = "Retired";
        await s.Env.Ctx.SaveChangesAsync();
        Assert.Null(await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId));

        Assert.Empty(s.Env.Ctx.WorkOrders);
    }

    [Fact]
    public async Task DuplicateGuard_StillBlocksASecondOpenWorkOrderForTheSamePlan()
    {
        var s = await SeedAsync(nameof(DuplicateGuard_StillBlocksASecondOpenWorkOrderForTheSamePlan));
        var plan = await OverduePlanAsync(s);

        var first = await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);
        var second = await Pm(s.Env).GenerateForPlanAsync(plan.MaintenancePlanId);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(s.Env.Ctx.WorkOrders);
    }
}
