using PEMS.Application.Common;
using PEMS.Domain.Common;

namespace PEMS.Tests.Rcm;

/// <summary>
/// موتور قوانین خالص RCM (SAE JA1011) و Plan: بدون دیتابیس، پس سریع و قطعی. هر مسئله‌ی ممیزی که قاعده‌محور است
/// (۱–۵، ۱۴، ۱۶–۱۸، ۱۹) این‌جا مستقیم اثبات می‌شود؛ سرویس‌ها فقط همین موتور را صدا می‌زنند.
/// </summary>
public class RcmRulesTests
{
    private static StrategyRuleInput Valid(Func<StrategyRuleInput, StrategyRuleInput>? tweak = null)
    {
        var i = new StrategyRuleInput
        {
            StrategyType = RcmCodes.StrategyType.Preventive,
            ConsequenceCategory = RcmCodes.Consequence.Operational,
            FailureModeId = 1,
            EquipmentClassId = 1,
            Justification = "OEM manual section 7 requires periodic inspection.",
            Frequency = RcmCodes.Frequency.Monthly
        };
        return tweak?.Invoke(i) ?? i;
    }

    private static StrategyRuleInput With(StrategyRuleInput b, string? type = null, string? consequence = null, bool? hidden = null,
        int? failureModeId = -1, string? justification = null, string? frequency = null, DateTime? start = null,
        string? cbm = null, int? pf = null, int? inspection = null, int? interval = null, string? taskType = null)
        => new()
        {
            StrategyType = type ?? b.StrategyType,
            ConsequenceCategory = consequence ?? b.ConsequenceCategory,
            IsHiddenFailure = hidden ?? b.IsHiddenFailure,
            FailureModeId = failureModeId == -1 ? b.FailureModeId : failureModeId,
            EquipmentClassId = b.EquipmentClassId,
            Justification = justification ?? b.Justification,
            Frequency = frequency ?? b.Frequency,
            DefaultIntervalDays = interval ?? b.DefaultIntervalDays,
            StartDate = start ?? b.StartDate,
            TaskType = taskType ?? b.TaskType,
            CbmParameter = cbm ?? b.CbmParameter,
            PfIntervalDays = pf ?? b.PfIntervalDays,
            InspectionIntervalDays = inspection ?? b.InspectionIntervalDays
        };

    private static bool Has(List<RuleError> errors, string code) => errors.Any(e => e.Code == code);

    // ---------- A1: JA1011 decision logic ----------

    [Theory]
    [InlineData("Safety")]
    [InlineData("Environmental")]
    [InlineData("Hidden")]
    public void RunToFailure_IsRejected_ForSafetyEnvironmentalAndHiddenConsequences(string consequence)
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "RunToFailure", consequence: consequence));
        Assert.True(Has(errors, "Rcm_Err_RtfNotAllowedForConsequence"));
    }

    [Fact]
    public void ReportedDefect_RunToFailure_Safety_NoFailureMode_NoJustification_IsRejectedOnEveryCount()
    {
        // دقیقاً همان رکوردی که در دیتابیس ذخیره شده بود (استراتژی ۹)
        var errors = StrategyRules.Validate(new StrategyRuleInput
        {
            StrategyType = "RunToFailure", ConsequenceCategory = "Safety", Frequency = "Daily"
        });
        Assert.True(Has(errors, "Rcm_Err_RtfNotAllowedForConsequence"));
        Assert.True(Has(errors, "Rcm_Err_FailureModeRequired"));
        Assert.True(Has(errors, "Rcm_Err_JustificationTooShort"));
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Environmental")]
    public void Corrective_IsRejected_WhenConsequenceRequiresProactiveTask(string consequence)
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "Corrective", consequence: consequence));
        Assert.True(Has(errors, "Rcm_Err_ProactiveTaskRequired"));
    }

    [Fact]
    public void HiddenFailureFlag_RequiresProactiveTask_EvenWhenConsequenceIsOperational()
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "RunToFailure", hidden: true));
        Assert.True(Has(errors, "Rcm_Err_RtfNotAllowedForConsequence"));
    }

    [Theory]
    [InlineData("Operational")]
    [InlineData("NonOperational")]
    public void RunToFailure_IsAccepted_ForOperationalOrNonOperationalConsequences(string consequence)
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "RunToFailure", consequence: consequence));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Preventive")]
    [InlineData("FailureFinding")]
    [InlineData("Redesign")]
    public void ProactiveOrRedesign_IsAccepted_ForSafetyConsequence(string type)
    {
        var errors = StrategyRules.Validate(With(Valid(), type: type, consequence: "Safety"));
        Assert.DoesNotContain(errors, e => e.Code.StartsWith("Rcm_Err_Rtf") || e.Code == "Rcm_Err_ProactiveTaskRequired");
    }

    // ---------- A2/A3: mandatory failure mode and justification ----------

    [Fact]
    public void FailureMode_IsMandatory_ForEveryStrategyType()
    {
        foreach (var type in RcmCodes.StrategyType.All)
        {
            var errors = StrategyRules.Validate(With(Valid(), type: type, failureModeId: null, cbm: "Vib", inspection: 30, frequency: "Monthly"));
            Assert.True(Has(errors, "Rcm_Err_FailureModeRequired"), type);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too short")]
    public void Justification_IsMandatory_WithMinimumLength(string? text)
    {
        var errors = StrategyRules.Validate(new StrategyRuleInput
        {
            StrategyType = "Preventive", ConsequenceCategory = "Operational", FailureModeId = 1, Frequency = "Monthly", Justification = text
        });
        Assert.True(Has(errors, "Rcm_Err_JustificationTooShort"));
    }

    // ---------- A5: bogus values ----------

    [Fact]
    public void UnknownCodes_AreRejected()
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "پیشگیرانه‌ی ناشناخته", consequence: "Whatever", frequency: "EveryBlueMoon"));
        Assert.True(Has(errors, "Rcm_Err_UnknownStrategyType"));
        Assert.True(Has(errors, "Rcm_Err_UnknownConsequence"));
        Assert.True(Has(errors, "Rcm_Err_UnknownFrequency"));
    }

    [Fact]
    public void StartDate_InThePast_IsRejected_TodayIsAccepted()
    {
        var today = new DateTime(2026, 10, 13);
        Assert.True(Has(StrategyRules.Validate(With(Valid(), start: today.AddDays(-1)), today), "Rcm_Err_StartDateInPast"));
        Assert.False(Has(StrategyRules.Validate(With(Valid(), start: today), today), "Rcm_Err_StartDateInPast"));
    }

    [Fact]
    public void NonPositiveIntervals_AreRejected()
    {
        var errors = StrategyRules.Validate(With(Valid(), interval: -5));
        Assert.True(Has(errors, "Rcm_Err_IntervalMustBePositive"));
    }

    // ---------- B8: CBM / P-F ----------

    [Fact]
    public void Cbm_RequiresMeasuredParameterAndInspectionInterval()
    {
        var errors = StrategyRules.Validate(new StrategyRuleInput
        {
            StrategyType = "CBM", ConsequenceCategory = "Operational", FailureModeId = 1,
            Justification = "Vibration trend allows detection weeks before failure."
        });
        Assert.True(Has(errors, "Rcm_Err_CbmParameterRequired"));
        Assert.True(Has(errors, "Rcm_Err_InspectionIntervalRequired"));
    }

    [Fact]
    public void InspectionInterval_CannotExceedPfInterval()
    {
        var errors = StrategyRules.Validate(With(Valid(), type: "CBM", cbm: "Vib", pf: 30, inspection: 60));
        Assert.True(Has(errors, "Rcm_Err_InspectionExceedsPf"));
        Assert.False(Has(StrategyRules.Validate(With(Valid(), type: "CBM", cbm: "Vib", pf: 60, inspection: 30)), "Rcm_Err_InspectionExceedsPf"));
    }

    // ---------- B9: criticality drives allowed types, priority and intervals ----------

    [Theory]
    [InlineData("A", "RunToFailure", true)]
    [InlineData("A", "Corrective", true)]
    [InlineData("A", "Preventive", false)]
    [InlineData("B", "RunToFailure", false)]
    [InlineData("C", "RunToFailure", false)]
    [InlineData("Unrated", "RunToFailure", false)]
    public void ClassACriticality_DisallowsReactiveTypes(string criticality, string type, bool blocked)
        => Assert.Equal(blocked, StrategyRules.CheckCriticalityAllowsType(criticality, type) != null);

    [Theory]
    [InlineData("Safety", "A", "Critical")]
    [InlineData("Safety", "B", "High")]
    [InlineData("Safety", "Unrated", "High")]
    [InlineData("Environmental", "C", "High")]
    [InlineData("Hidden", "B", "High")]
    [InlineData("Operational", "A", "High")]
    [InlineData("Operational", "B", "Medium")]
    [InlineData("NonOperational", "C", "Low")]
    [InlineData("NonOperational", "A", "Medium")]
    public void WorkOrderPriority_IsDerivedFromConsequenceAndCriticality(string consequence, string criticality, string expected)
        => Assert.Equal(expected, StrategyRules.DerivePriority(consequence, criticality));

    [Fact]
    public void HiddenFailureFlag_RaisesPriorityToAtLeastHigh()
        => Assert.Equal("High", StrategyRules.DerivePriority("Operational", "B", isHiddenFailure: true));

    [Theory]
    [InlineData(90, "A", 45)]
    [InlineData(90, "B", 90)]
    [InlineData(90, "C", 135)]
    [InlineData(90, "Unrated", 90)]
    public void SuggestedInterval_ScalesWithCriticality(int baseDays, string criticality, int expected)
        => Assert.Equal(expected, StrategyRules.SuggestedIntervalDays(baseDays, criticality));

    // ---------- B6: language-neutral codes / legacy conversion ----------

    [Theory]
    [InlineData("پیشگیرانه", "Preventive")]
    [InlineData("Preventive", "Preventive")]
    [InlineData("RunToFailure", "RunToFailure")]
    [InlineData("run-to-failure", "RunToFailure")]
    [InlineData("مبتنی بر وضعیت", "CBM")]
    [InlineData("اصلاحی", "Corrective")]
    [InlineData("PM", "Preventive")]
    public void LegacyStrategyTypeLabels_MapToStableCodes(string raw, string code)
        => Assert.Equal(code, LegacyMaps.StrategyType(raw));

    [Theory]
    [InlineData("روزانه", "Daily")]
    [InlineData("Daily", "Daily")]
    [InlineData("ماهانه", "Monthly")]
    [InlineData("Quarterly", "Quarterly")]
    [InlineData("سالانه", "Yearly")]
    public void LegacyFrequencyLabels_MapToStableCodes(string raw, string code)
        => Assert.Equal(code, LegacyMaps.Frequency(raw));

    [Theory]
    [InlineData("Economic", "NonOperational")]
    [InlineData("ایمنی", "Safety")]
    [InlineData("Safety", "Safety")]
    [InlineData("Operational", "Operational")]
    public void LegacyConsequenceLabels_MapToStableCodes(string raw, string code)
        => Assert.Equal(code, LegacyMaps.Consequence(raw));

    [Fact]
    public void UnknownLegacyValue_IsNotGuessed()
        => Assert.Null(LegacyMaps.StrategyType("fvvsd"));

    // ---------- D14–D18: plan rules ----------

    private static PlanRuleInput Plan(string type = "Preventive", string trigger = "Calendar", int? interval = 30, DateTime? due = null,
        string? title = "Quarterly lubrication", int? jobPlan = 1, string? meterType = null, decimal? meterInterval = null,
        string status = "Active", bool assignmentActive = true, int? inspection = null, bool allowPast = false)
        => new()
        {
            StrategyType = type, StrategyStatus = status, AssignmentActive = assignmentActive, Title = title, TriggerType = trigger,
            IntervalDays = interval, NextDueDate = due, JobPlanId = jobPlan, MeterType = meterType, MeterInterval = meterInterval,
            StrategyInspectionIntervalDays = inspection, AllowPastDueDate = allowPast
        };

    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0);

    [Fact]
    public void ValidCalendarPlan_HasNoErrors()
        => Assert.Empty(PlanRules.Validate(Plan(due: Now.AddDays(10)), Now));

    [Theory]
    [InlineData("RunToFailure")]
    [InlineData("Corrective")]
    [InlineData("Redesign")]
    public void PlanCreation_IsBlocked_ForStrategiesWithoutScheduledTask(string type)
        => Assert.True(Has(PlanRules.Validate(Plan(type: type), Now), "Rcm_Err_PlanNotAllowedForType"));

    [Theory]
    [InlineData("Draft")]
    [InlineData("InReview")]
    [InlineData("Retired")]
    [InlineData("Superseded")]
    public void PlanCreation_RequiresApprovedOrActiveStrategy(string status)
        => Assert.True(Has(PlanRules.Validate(Plan(status: status), Now), "Rcm_Err_StrategyNotApproved"));

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    public void CalendarTrigger_RequiresPositiveInterval(int interval)
        => Assert.True(Has(PlanRules.Validate(Plan(interval: interval), Now), "Rcm_Err_IntervalMustBePositive"));

    [Fact]
    public void ReportedDefect_NegativeInterval_And2020DueDate_AreRejected()
    {
        var errors = PlanRules.Validate(Plan(interval: -2, due: new DateTime(2020, 1, 1)), Now);
        Assert.True(Has(errors, "Rcm_Err_IntervalMustBePositive"));
        Assert.True(Has(errors, "Rcm_Err_NextDueInPast"));
    }

    [Fact]
    public void PastDueDate_IsAllowed_OnlyWithExplicitConfirmation()
    {
        Assert.True(Has(PlanRules.Validate(Plan(due: Now.AddDays(-3)), Now), "Rcm_Err_NextDueInPast"));
        Assert.False(Has(PlanRules.Validate(Plan(due: Now.AddDays(-3), allowPast: true), Now), "Rcm_Err_NextDueInPast"));
    }

    [Fact]
    public void MeterTrigger_RequiresMeterTypeAndPositiveInterval()
    {
        var errors = PlanRules.Validate(Plan(trigger: "Meter", interval: null, meterType: null, meterInterval: 0), Now);
        Assert.True(Has(errors, "Rcm_Err_MeterTypeRequired"));
        Assert.True(Has(errors, "Rcm_Err_MeterIntervalMustBePositive"));
        Assert.Empty(PlanRules.Validate(Plan(trigger: "Meter", interval: null, meterType: "RunningHours", meterInterval: 2000), Now));
    }

    [Theory]
    [InlineData("CBM")]
    [InlineData("Predictive")]
    public void ConditionBasedPlans_AreCalendarInspectionOnly_NotMeter(string type)
    {
        var meter = PlanRules.Validate(Plan(type: type, trigger: "Meter", interval: null, meterType: "RunningHours", meterInterval: 100), Now);
        var both = PlanRules.Validate(Plan(type: type, trigger: "Both", meterType: "RunningHours", meterInterval: 100, due: Now.AddDays(5)), Now);
        Assert.True(Has(meter, "Rcm_Err_ConditionBasedCalendarOnly"));
        Assert.True(Has(both, "Rcm_Err_ConditionBasedCalendarOnly")); // مورد «CBM با ۳۰ روز + Meter» گزارش‌شده
        Assert.Empty(PlanRules.Validate(Plan(type: type, due: Now.AddDays(5)), Now));
    }

    [Fact]
    public void CbmPlanInterval_CannotBeLongerThanStrategyInspectionInterval()
        => Assert.True(Has(PlanRules.Validate(Plan(type: "CBM", interval: 60, inspection: 30, due: Now.AddDays(5)), Now), "Rcm_Err_PlanIntervalExceedsInspection"));

    [Fact]
    public void ScheduledStrategies_RequireAJobPlan()
        => Assert.True(Has(PlanRules.Validate(Plan(jobPlan: null), Now), "Rcm_Err_JobPlanRequired"));

    [Theory]
    [InlineData("fvv")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData(null)]
    public void JunkOrMissingTitles_AreRejected(string? title)
        => Assert.True(Has(PlanRules.Validate(Plan(title: title), Now), "Rcm_Err_PlanTitleInvalid"));

    [Fact]
    public void NonApplicableFields_AreNormalizedAwayByTriggerType()
    {
        var cal = PlanRules.Normalize("Calendar", 30, Now, "Cycles", 100);
        Assert.Equal(30, cal.IntervalDays);
        Assert.Equal(Now, cal.NextDueDate);
        Assert.Null(cal.MeterType);
        Assert.Null(cal.MeterInterval);

        var meter = PlanRules.Normalize("Meter", 30, Now, "Cycles", 100);
        Assert.Null(meter.IntervalDays);
        Assert.Null(meter.NextDueDate);
        Assert.Equal("Cycles", meter.MeterType);
        Assert.Equal(100m, meter.MeterInterval);
    }

    [Fact]
    public void SuggestedTitle_CombinesTagFailureModeAndType()
        => Assert.Equal("80-P-101 - Seal leakage - CBM", PlanRules.SuggestTitle("80-P-101", "Seal leakage", "CBM"));

    // ---------- D19: schedule policy ----------

    [Fact]
    public void FixedSchedule_AnchorsOnPreviousDue_WhenNotOverdue()
        => Assert.Equal(Now.AddDays(10).AddDays(30), ScheduleCalculator.NextFixedDue(Now.AddDays(10), 30, Now));

    [Fact]
    public void FixedSchedule_JumpsToNextFutureOccurrence_WithoutCatchUpStorm()
    {
        // سررسید ۲۰۲۰ با تناوب ۳۰ روز: فقط یک گام به اولین سررسید آینده روی شبکه‌ی تناوب (نه ۷۰ سررسید جبرانی)
        var next = ScheduleCalculator.NextFixedDue(new DateTime(2020, 1, 1), 30, Now);
        Assert.True(next > Now);
        Assert.True(next <= Now.AddDays(30));
        Assert.Equal(0, (int)(next - new DateTime(2020, 1, 1)).TotalDays % 30);
    }

    [Fact]
    public void FloatingSchedule_IsMeasuredFromActualCompletion()
        => Assert.Equal(new DateTime(2026, 11, 20), ScheduleCalculator.NextFloatingDue(new DateTime(2026, 10, 21), 30));
}
