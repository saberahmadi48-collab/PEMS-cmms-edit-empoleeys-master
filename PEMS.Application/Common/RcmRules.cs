using PEMS.Domain.Common;

namespace PEMS.Application.Common;

/// <summary>
/// خطای اعتبارسنجی مستقل از زبان. Code همان کلید Resource است (Rcm_Err_*)؛ لایه‌ی وب آن را
/// با Localizer ترجمه می‌کند. Field نام فیلد فرم برای نمایش کنار ورودی است.
/// </summary>
public sealed record RuleError(string Code, string? Field = null, params string[] Args);

/// <summary>ورودی خالص قوانین استراتژی (بدون وابستگی به EF) - برای تست واحد و اشتراک بین UI/Import/Bridge.</summary>
public sealed class StrategyRuleInput
{
    public string? StrategyType { get; init; }
    public string? ConsequenceCategory { get; init; }
    public bool IsHiddenFailure { get; init; }
    public int? FailureModeId { get; init; }
    public int? EquipmentClassId { get; init; }
    public string? Justification { get; init; }
    public string? Frequency { get; init; }
    public int? DefaultIntervalDays { get; init; }
    public string? RecommendedTriggerType { get; init; }
    public DateTime? StartDate { get; init; }
    public string? TaskType { get; init; }
    public string? Source { get; init; }

    // CBM / P-F
    public string? CbmParameter { get; init; }
    public int? PfIntervalDays { get; init; }
    public int? InspectionIntervalDays { get; init; }

    public int? OwnerDisciplineId { get; init; }
    public decimal? EstimatedDurationHours { get; init; }
}

/// <summary>
/// منطق تصمیم RCM (SAE JA1011) روی سمت سرور. هیچ‌چیز این‌جا به UI تکیه نمی‌کند:
/// UI فقط راهنمای کاربر است و هر مسیر (فرم، Import، Bridge) از همین موتور می‌گذرد.
/// </summary>
public static class StrategyRules
{
    public const int MinJustificationLength = 20;

    /// <summary>انواعی که پیامد ایمنی/محیط‌زیستی/پنهان را کنترل نمی‌کنند (JA1011: «بدون کار زمان‌بندی‌شده»).</summary>
    public static readonly string[] ReactiveTypes = { RcmCodes.StrategyType.RunToFailure, RcmCodes.StrategyType.Corrective };

    public static bool RequiresProactiveTask(string? consequence, bool isHiddenFailure) =>
        isHiddenFailure || consequence is RcmCodes.Consequence.Safety or RcmCodes.Consequence.Environmental or RcmCodes.Consequence.Hidden;

    public static List<RuleError> Validate(StrategyRuleInput i, DateTime? today = null)
    {
        var errors = new List<RuleError>();
        var type = i.StrategyType;
        var consequence = i.ConsequenceCategory;

        if (!RcmCodes.StrategyType.IsValid(type))
            errors.Add(new RuleError("Rcm_Err_UnknownStrategyType", "strategyType"));

        if (!RcmCodes.Consequence.IsValid(consequence))
            errors.Add(new RuleError("Rcm_Err_UnknownConsequence", "consequenceCategory"));

        if (!i.FailureModeId.HasValue)
            errors.Add(new RuleError("Rcm_Err_FailureModeRequired", "failureModeId"));

        var justification = (i.Justification ?? "").Trim();
        if (justification.Length < MinJustificationLength)
            errors.Add(new RuleError("Rcm_Err_JustificationTooShort", "justification", MinJustificationLength.ToString()));

        if (!string.IsNullOrWhiteSpace(i.Frequency) && !RcmCodes.Frequency.IsValid(i.Frequency))
            errors.Add(new RuleError("Rcm_Err_UnknownFrequency", "frequency"));

        if (!string.IsNullOrWhiteSpace(i.RecommendedTriggerType) && !RcmCodes.Trigger.IsValid(i.RecommendedTriggerType))
            errors.Add(new RuleError("Rcm_Err_UnknownTrigger", "recommendedTriggerType"));

        if (!string.IsNullOrWhiteSpace(i.TaskType) && !RcmCodes.TaskType.IsValid(i.TaskType))
            errors.Add(new RuleError("Rcm_Err_UnknownTaskType", "taskType"));

        if (!string.IsNullOrWhiteSpace(i.Source) && !RcmCodes.Source.IsValid(i.Source))
            errors.Add(new RuleError("Rcm_Err_UnknownSource", "source"));

        if (i.StartDate.HasValue && i.StartDate.Value.Date < (today ?? DateTime.Today).Date)
            errors.Add(new RuleError("Rcm_Err_StartDateInPast", "startDate"));

        if (i.DefaultIntervalDays is <= 0)
            errors.Add(new RuleError("Rcm_Err_IntervalMustBePositive", "defaultIntervalDays"));
        if (i.EstimatedDurationHours is < 0)
            errors.Add(new RuleError("Rcm_Err_NegativeValue", "estimatedDurationHours"));

        // ============ قاعده‌ی اصلی JA1011 ============
        // Safety / Environmental / Hidden => کار پیشگیرانه (یا بازطراحی) الزامی است؛ RTF و Corrective مجاز نیست.
        if (RcmCodes.StrategyType.IsValid(type) && RcmCodes.Consequence.IsValid(consequence)
            && RequiresProactiveTask(consequence, i.IsHiddenFailure) && ReactiveTypes.Contains(type!))
        {
            errors.Add(type == RcmCodes.StrategyType.RunToFailure
                ? new RuleError("Rcm_Err_RtfNotAllowedForConsequence", "strategyType", consequence!)
                : new RuleError("Rcm_Err_ProactiveTaskRequired", "strategyType", consequence!));
        }

        // انواع دارای کار زمان‌بندی‌شده به تناوب نیاز دارند
        if (type is RcmCodes.StrategyType.Preventive or RcmCodes.StrategyType.FailureFinding
            && string.IsNullOrWhiteSpace(i.Frequency) && !i.DefaultIntervalDays.HasValue)
            errors.Add(new RuleError("Rcm_Err_IntervalRequired", "frequency"));

        // ============ CBM / P-F ============
        if (type is RcmCodes.StrategyType.Cbm or RcmCodes.StrategyType.Predictive)
        {
            if (string.IsNullOrWhiteSpace(i.CbmParameter))
                errors.Add(new RuleError("Rcm_Err_CbmParameterRequired", "cbmParameter"));
            if (!i.InspectionIntervalDays.HasValue && string.IsNullOrWhiteSpace(i.Frequency) && !i.DefaultIntervalDays.HasValue)
                errors.Add(new RuleError("Rcm_Err_InspectionIntervalRequired", "inspectionIntervalDays"));
        }

        if (i.PfIntervalDays is <= 0)
            errors.Add(new RuleError("Rcm_Err_IntervalMustBePositive", "pfIntervalDays"));
        if (i.InspectionIntervalDays is <= 0)
            errors.Add(new RuleError("Rcm_Err_IntervalMustBePositive", "inspectionIntervalDays"));
        if (i.PfIntervalDays.HasValue && i.InspectionIntervalDays.HasValue
            && i.PfIntervalDays > 0 && i.InspectionIntervalDays > i.PfIntervalDays)
            errors.Add(new RuleError("Rcm_Err_InspectionExceedsPf", "inspectionIntervalDays"));

        return errors;
    }

    /// <summary>
    /// تاثیر بحرانی‌بودن تجهیز (A/B/C/Unrated) بر انواع مجاز استراتژی هنگام تخصیص به تگ.
    /// کلاس A: RTF و Corrective مجاز نیست (تجهیز بحرانی هرگز بدون کار پیشگیرانه نمی‌ماند).
    /// </summary>
    public static RuleError? CheckCriticalityAllowsType(string? criticalityClass, string? strategyType)
    {
        if (criticalityClass == RcmCodes.Criticality.A && strategyType != null && ReactiveTypes.Contains(strategyType))
            return new RuleError("Rcm_Err_CriticalityADisallowsReactive", "strategyType", strategyType);
        return null;
    }

    /// <summary>ضریب تناوب پیش‌فرض بر اساس بحرانی‌بودن: A دو برابر پرتکرارتر، C یک‌ونیم برابر کم‌تکرارتر.</summary>
    public static double IntervalFactor(string? criticalityClass) => criticalityClass switch
    {
        RcmCodes.Criticality.A => 0.5,
        RcmCodes.Criticality.C => 1.5,
        _ => 1.0
    };

    public static int? SuggestedIntervalDays(int? baseIntervalDays, string? criticalityClass)
    {
        if (!baseIntervalDays.HasValue || baseIntervalDays <= 0) return baseIntervalDays;
        return Math.Max(1, (int)Math.Round(baseIntervalDays.Value * IntervalFactor(criticalityClass)));
    }

    /// <summary>
    /// اولویت پیش‌فرض Work Order از ماتریس پیامد × بحرانی‌بودن:
    /// Safety/Environmental/Hidden → High، Operational → Medium، NonOperational → Low؛
    /// کلاس A یک پله بالاتر (حداکثر Critical).
    /// </summary>
    public static string DerivePriority(string? consequence, string? criticalityClass, bool isHiddenFailure = false)
    {
        var level = consequence switch
        {
            RcmCodes.Consequence.Safety or RcmCodes.Consequence.Environmental or RcmCodes.Consequence.Hidden => 3,
            RcmCodes.Consequence.Operational => 2,
            RcmCodes.Consequence.NonOperational => 1,
            _ => 2
        };
        if (isHiddenFailure) level = Math.Max(level, 3);
        if (criticalityClass == RcmCodes.Criticality.A) level++;
        return Math.Min(level, 4) switch
        {
            1 => RcmCodes.Priority.Low,
            2 => RcmCodes.Priority.Medium,
            3 => RcmCodes.Priority.High,
            _ => RcmCodes.Priority.Critical
        };
    }

    /// <summary>تبدیل مقدار قدیمی (متن فارسی/انگلیسی آزاد) به کد پایدار؛ Null اگر ناشناخته.</summary>
    public static string? NormalizeLegacyStrategyType(string? raw) => LegacyMaps.StrategyType(raw);
    public static string? NormalizeLegacyConsequence(string? raw) => LegacyMaps.Consequence(raw);
    public static string? NormalizeLegacyFrequency(string? raw) => LegacyMaps.Frequency(raw);
}

/// <summary>
/// نگاشت مقادیر ذخیره‌شده‌ی قدیمی (برچسب‌های فارسی/انگلیسی فرم‌های قبلی) به کدهای پایدار.
/// همین جدول در Data Migration (SQL) و در Import هم بازتولید می‌شود؛ منبع حقیقت یکی است.
/// </summary>
public static class LegacyMaps
{
    private static string Key(string? raw) =>
        new string((raw ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_' && c != '‌').ToArray()).ToLowerInvariant();

    private static readonly Dictionary<string, string> StrategyTypes = new()
    {
        ["preventive"] = RcmCodes.StrategyType.Preventive, ["پیشگیرانه"] = RcmCodes.StrategyType.Preventive, ["pm"] = RcmCodes.StrategyType.Preventive,
        ["predictive"] = RcmCodes.StrategyType.Predictive, ["پیشبینانه"] = RcmCodes.StrategyType.Predictive, ["پیشبینی"] = RcmCodes.StrategyType.Predictive, ["pdm"] = RcmCodes.StrategyType.Predictive,
        ["cbm"] = RcmCodes.StrategyType.Cbm, ["مبتنیبروضعیت"] = RcmCodes.StrategyType.Cbm, ["پایشوضعیت"] = RcmCodes.StrategyType.Cbm,
        ["corrective"] = RcmCodes.StrategyType.Corrective, ["اصلاحی"] = RcmCodes.StrategyType.Corrective, ["cm"] = RcmCodes.StrategyType.Corrective,
        ["runtofailure"] = RcmCodes.StrategyType.RunToFailure, ["rtf"] = RcmCodes.StrategyType.RunToFailure, ["اجراتاخرابی"] = RcmCodes.StrategyType.RunToFailure,
        ["failurefinding"] = RcmCodes.StrategyType.FailureFinding, ["redesign"] = RcmCodes.StrategyType.Redesign, ["بازطراحی"] = RcmCodes.StrategyType.Redesign
    };

    private static readonly Dictionary<string, string> Consequences = new()
    {
        ["safety"] = RcmCodes.Consequence.Safety, ["ایمنی"] = RcmCodes.Consequence.Safety,
        ["environmental"] = RcmCodes.Consequence.Environmental, ["environment"] = RcmCodes.Consequence.Environmental, ["زیستمحیطی"] = RcmCodes.Consequence.Environmental, ["محیطزیست"] = RcmCodes.Consequence.Environmental,
        ["operational"] = RcmCodes.Consequence.Operational, ["production"] = RcmCodes.Consequence.Operational, ["عملیاتی"] = RcmCodes.Consequence.Operational, ["تولیدی"] = RcmCodes.Consequence.Operational,
        ["economic"] = RcmCodes.Consequence.NonOperational, ["nonoperational"] = RcmCodes.Consequence.NonOperational, ["اقتصادی"] = RcmCodes.Consequence.NonOperational, ["غیرعملیاتی"] = RcmCodes.Consequence.NonOperational,
        ["hidden"] = RcmCodes.Consequence.Hidden, ["پنهان"] = RcmCodes.Consequence.Hidden
    };

    private static readonly Dictionary<string, string> Frequencies = new()
    {
        ["daily"] = RcmCodes.Frequency.Daily, ["روزانه"] = RcmCodes.Frequency.Daily,
        ["weekly"] = RcmCodes.Frequency.Weekly, ["هفتگی"] = RcmCodes.Frequency.Weekly,
        ["monthly"] = RcmCodes.Frequency.Monthly, ["ماهانه"] = RcmCodes.Frequency.Monthly, ["ماهیانه"] = RcmCodes.Frequency.Monthly,
        ["quarterly"] = RcmCodes.Frequency.Quarterly, ["سهماهه"] = RcmCodes.Frequency.Quarterly, ["فصلی"] = RcmCodes.Frequency.Quarterly,
        ["semiannual"] = RcmCodes.Frequency.SemiAnnual, ["ششماهه"] = RcmCodes.Frequency.SemiAnnual,
        ["yearly"] = RcmCodes.Frequency.Yearly, ["annual"] = RcmCodes.Frequency.Yearly, ["سالانه"] = RcmCodes.Frequency.Yearly, ["سالیانه"] = RcmCodes.Frequency.Yearly
    };

    public static string? StrategyType(string? raw) => StrategyTypes.TryGetValue(Key(raw), out var v) ? v : null;
    public static string? Consequence(string? raw) => Consequences.TryGetValue(Key(raw), out var v) ? v : null;
    public static string? Frequency(string? raw) => Frequencies.TryGetValue(Key(raw), out var v) ? v : null;

    public static IReadOnlyDictionary<string, string> StrategyTypeMap => StrategyTypes;
    public static IReadOnlyDictionary<string, string> ConsequenceMap => Consequences;
    public static IReadOnlyDictionary<string, string> FrequencyMap => Frequencies;
}

/// <summary>ورودی خالص قوانین Plan.</summary>
public sealed class PlanRuleInput
{
    public string? StrategyType { get; init; }
    public string? StrategyStatus { get; init; }
    public bool AssignmentActive { get; init; } = true;
    public string? Title { get; init; }
    public string? TriggerType { get; init; }
    public int? IntervalDays { get; init; }
    public string? MeterType { get; init; }
    public decimal? MeterInterval { get; init; }
    public DateTime? NextDueDate { get; init; }
    public int? JobPlanId { get; init; }
    public string? ScheduleMode { get; init; }
    public int? StrategyInspectionIntervalDays { get; init; }
    public bool AllowPastDueDate { get; init; }
}

public static class PlanRules
{
    public const int MinTitleLength = 5;

    public static List<RuleError> Validate(PlanRuleInput i, DateTime? now = null)
    {
        var errors = new List<RuleError>();
        var today = (now ?? DateTime.Now).Date;

        if (!RcmCodes.StrategyType.IsValid(i.StrategyType))
        {
            errors.Add(new RuleError("Rcm_Err_UnknownStrategyType", "strategyId"));
        }
        else if (!RcmCodes.StrategyType.PlanGenerating.Contains(i.StrategyType!))
        {
            // RTF / Corrective / Redesign: «بدون کار زمان‌بندی‌شده» - Plan معنا ندارد
            errors.Add(new RuleError("Rcm_Err_PlanNotAllowedForType", "strategyId", i.StrategyType!));
        }

        if (i.StrategyStatus == null || !RcmCodes.Status.Usable.Contains(i.StrategyStatus))
            errors.Add(new RuleError("Rcm_Err_StrategyNotApproved", "strategyId"));

        if (!i.AssignmentActive)
            errors.Add(new RuleError("Rcm_Err_AssignmentNotActive", "assignmentId"));

        var title = (i.Title ?? "").Trim();
        if (title.Length < MinTitleLength || !title.Any(char.IsLetter))
            errors.Add(new RuleError("Rcm_Err_PlanTitleInvalid", "title", MinTitleLength.ToString()));

        if (!RcmCodes.Trigger.IsValid(i.TriggerType))
        {
            errors.Add(new RuleError("Rcm_Err_UnknownTrigger", "triggerType"));
        }
        else
        {
            var usesCalendar = i.TriggerType is RcmCodes.Trigger.Calendar or RcmCodes.Trigger.Both;
            var usesMeter = i.TriggerType is RcmCodes.Trigger.Meter or RcmCodes.Trigger.Both;

            if (usesCalendar)
            {
                if (!i.IntervalDays.HasValue || i.IntervalDays <= 0)
                    errors.Add(new RuleError("Rcm_Err_IntervalMustBePositive", "intervalDays"));
                if (i.NextDueDate.HasValue && i.NextDueDate.Value.Date < today && !i.AllowPastDueDate)
                    errors.Add(new RuleError("Rcm_Err_NextDueInPast", "nextDueDate"));
            }

            if (usesMeter)
            {
                if (!RcmCodes.MeterType.IsValid(i.MeterType))
                    errors.Add(new RuleError("Rcm_Err_MeterTypeRequired", "meterType"));
                if (!i.MeterInterval.HasValue || i.MeterInterval <= 0)
                    errors.Add(new RuleError("Rcm_Err_MeterIntervalMustBePositive", "meterInterval"));
            }

            // قاعده‌ی هر نوع: CBM/Predictive بر پایه‌ی وضعیت‌اند، نه کارکرد - فقط بازه‌ی بازرسی تقویمی مجاز است.
            if (RcmCodes.StrategyType.ConditionBased.Contains(i.StrategyType ?? "") && i.TriggerType != RcmCodes.Trigger.Calendar)
                errors.Add(new RuleError("Rcm_Err_ConditionBasedCalendarOnly", "triggerType"));

            if (RcmCodes.StrategyType.ConditionBased.Contains(i.StrategyType ?? "")
                && i.StrategyInspectionIntervalDays.HasValue && i.IntervalDays.HasValue
                && i.IntervalDays > i.StrategyInspectionIntervalDays)
                errors.Add(new RuleError("Rcm_Err_PlanIntervalExceedsInspection", "intervalDays", i.StrategyInspectionIntervalDays.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(i.ScheduleMode) && !RcmCodes.ScheduleMode.IsValid(i.ScheduleMode))
            errors.Add(new RuleError("Rcm_Err_UnknownScheduleMode", "scheduleMode"));

        // Preventive/CBM/Predictive/FailureFinding باید دستورالعمل اجرا (Job Plan) داشته باشند.
        if (RcmCodes.StrategyType.PlanGenerating.Contains(i.StrategyType ?? "") && !i.JobPlanId.HasValue)
            errors.Add(new RuleError("Rcm_Err_JobPlanRequired", "jobPlanId"));

        return errors;
    }

    /// <summary>
    /// مقادیر غیرقابل‌اعمال برای نوع تریگر پاک می‌شوند (فیلدهای مخفی UI هنوز Post می‌شوند):
    /// Calendar → بدون Meter؛ Meter → بدون Interval/NextDue.
    /// </summary>
    public static (int? IntervalDays, DateTime? NextDueDate, string? MeterType, decimal? MeterInterval) Normalize(
        string? trigger, int? intervalDays, DateTime? nextDueDate, string? meterType, decimal? meterInterval)
    {
        var calendar = trigger is RcmCodes.Trigger.Calendar or RcmCodes.Trigger.Both;
        var meter = trigger is RcmCodes.Trigger.Meter or RcmCodes.Trigger.Both;
        return (calendar ? intervalDays : null, calendar ? nextDueDate : null, meter ? meterType : null, meter ? meterInterval : null);
    }

    public static string SuggestTitle(string tag, string? failureModeName, string strategyType)
        => string.Join(" - ", new[] { tag, failureModeName, strategyType }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// سیاست زمان‌بندی NextDueDate (خالص و قابل تست):
/// Fixed   — لنگر روی سررسید قبلی، بدون «طوفان جبران» (فقط یک WO و پرش به اولین سررسید آینده).
/// Floating— از تاریخ تکمیل واقعی WO.
/// </summary>
public static class ScheduleCalculator
{
    public static DateTime NextFixedDue(DateTime? previousDue, int intervalDays, DateTime now)
    {
        var next = (previousDue ?? now).AddDays(intervalDays);
        if (next > now) return next;
        // عقب‌افتاده: پرش به اولین سررسید آینده روی همان شبکه‌ی تناوب (بدون ساخت WO برای هر دوره‌ی از دست‌رفته)
        var missed = (int)Math.Floor((now - next).TotalDays / intervalDays) + 1;
        return next.AddDays((double)missed * intervalDays);
    }

    public static DateTime NextFloatingDue(DateTime completionDate, int intervalDays) => completionDate.AddDays(intervalDays);
}
