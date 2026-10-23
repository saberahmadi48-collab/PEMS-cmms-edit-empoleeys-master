namespace PEMS.Application.Common;

/// <summary>
/// فاز عملیاتی یک Work Order (نگاشت گردش‌کار چرخه‌عمر به مفاهیم صفحه‌ی جزئیات). منبع حقیقت واحدِ «کدام بخش در کدام فاز چه قابلیتی دارد» همین کلاس است.
/// </summary>
public enum WoPhase
{
    Created, Planning, Preparation, OperationsIssue, Released, Execution, OnHold, Completion, Verification, FinalApproval,
    Closed, Cancelled, Rejected
}

/// <summary>بخش‌های فرزند صفحه‌ی جزئیات Work Order.</summary>
public enum WoSection
{
    RequiredSkills, RequiredMaterials, Crew, Labor, SpareParts, DisciplineReview, ResponsibleDiscipline, ExecutorAssignment, Schedule, JobPlanTasks
}

public enum WoSectionAction { Add, Edit, Remove }

/// <summary>
/// Hidden: رندر نمی‌شود (حداکثر یک راهنمای کوتاه). ReadOnly: فقط جدول، بدون هیچ کنترل افزودن/ویرایش/حذف. Editable: فرم‌ها برای دارندگان مجوز ویرایش.
/// Correctable: فرم‌ها فقط برای دارندگان مجوز اصلاح (WorkOrder.CorrectActuals) با ثبت دلیل الزامی و ممیزی (اصلاح پیش از تصویب نهایی).
/// </summary>
public enum SectionAccess { Hidden, ReadOnly, Editable, Correctable }

public sealed record WoSectionDecision(bool Allowed, string Code, string MessageFa, string MessageEn)
{
    public static WoSectionDecision Ok { get; } = new(true, "Allowed", "", "");
    public string Message(bool persian) => persian ? MessageFa : MessageEn;
}

/// <summary>
/// ماتریس واحد «بخش × فاز × مجوز» برای صفحه‌ی جزئیات Work Order. هم Razor (رندر) و هم تمام POSTهای جهشی (اعمال) از همین کلاس تصمیم می‌گیرند؛ پس رابط کاربری
/// و سرور نمی‌توانند از هم جدا شوند. خالص و بدون وابستگی به دیتابیس؛ تست ماتریسی پارامتریک روی همین کلاس و روی کنترلر.
///
/// ماتریس مبنا (انحراف‌ها در توضیح هر ردیف):
///   * مهارت/متریال موردنیاز و Crew: قابل‌ویرایش در Created/Planning/Preparation؛ از Operations Issue به بعد و پس از Closed فقط‌خواندنی.
///     (انحراف پیشنهادی، اعمال‌نشده: در عمل تعویض عضو Crew حین اجرا لازم می‌شود؛ طبق دستور مبنا فقط‌خواندنی ماند.)
///   * ساعت کار واقعی و قطعات مصرفی: پیش از Released پنهان؛ Released/OnHold فقط‌خواندنی (OnHold طبق قاعده‌ی F3 سرویس کار)؛ Execution قابل‌ویرایش؛
///     Completion/Verification قابل‌اصلاح (دلیل + ممیزی + مجوز Approve)؛ FinalApproval/Closed/Cancelled فقط‌خواندنی؛ Rejected پنهان.
///   * بازبینی چنددیسیپلینه (EN 13460): قابل‌درخواست تا پیش از Completion؛ پس از آن فقط‌خواندنی. توقف Completion تا بسته‌شدن ارجاع‌های باز در سرویس چرخه‌عمر اعمال می‌شود.
///   * دیسیپلین مسئول: Planning/Preparation؛ تخصیص مجری و زمان‌بندی: فقط Preparation؛ بقیه فقط‌خواندنی (قبل از Planning/Preparation پنهان).
///   * چک‌لیست JobPlan (CompleteTask/ReopenTask): قابل‌ویرایش فقط در Execution؛ در سایر فازها فقط‌خواندنی.
/// </summary>
public static class WorkOrderSectionPolicy
{
    public const string EditPermission = "WorkOrder.Edit";
    public const string PlanPermission = "WorkOrder.Plan";
    public const string AssignPermission = "WorkOrder.Assign";
    // مجوز جدا برای اصلاح داده‌ی واقعی پس از اتمام اجرا (پیش از تصویب نهایی). عمداً WorkOrder.Approve نیست: هیچ نقش Seed‌شده‌ای هم Edit و هم Approve ندارد،
    // پس اصلاح عملاً غیرممکن می‌شد؛ مجوز مستقل قابل‌واگذاری و قابل‌ممیزی است.
    public const string CorrectionPermission = "WorkOrder.CorrectActuals";
    public const string ReopenPermission = "WorkOrder.Reopen";

    /// <summary>مجوز لازم برای جهش در هر بخش: بخش‌های برنامه‌ریزی/تخصیص همان مجوزهای وظیفه‌ای گردش‌کار (Plan/Assign) را می‌خواهند، بقیه WorkOrder.Edit.</summary>
    public static string RequiredPermission(WoSection section) => section switch
    {
        WoSection.ResponsibleDiscipline => PlanPermission,
        WoSection.ExecutorAssignment or WoSection.Schedule => AssignPermission,
        _ => EditPermission
    };

    // نام مراحل گردش‌کار چرخه‌عمر (PEMS.Persistence.Seed.WorkOrderLifecycleWorkflowSeeder / V2Seeder)
    public static WoPhase ResolvePhase(string? workOrderStatus, string? currentStageName)
    {
        // وضعیت‌های پایانی همیشه بر نام مرحله مقدم‌اند (Instance پایان‌یافته مرحله‌ی جاری ندارد)
        switch (workOrderStatus)
        {
            case "Closed": return WoPhase.Closed;
            case "Cancelled": return WoPhase.Cancelled;
            case "Rejected": return WoPhase.Rejected;
        }

        switch (currentStageName)
        {
            case "Created": return WoPhase.Created;
            case "Maintenance Planning": return WoPhase.Planning;
            case "Preparation / Assignment": return WoPhase.Preparation;
            case "Operations Issue / Release": return WoPhase.OperationsIssue;
            case "In Execution": return WoPhase.Execution;
            case "On Hold": return WoPhase.OnHold;
            case "Completion": return WoPhase.Completion;
            case "Originating-Discipline Verification": return WoPhase.Verification;
            case "Final Approval": return WoPhase.FinalApproval;
        }
        if (!string.IsNullOrEmpty(currentStageName) && currentStageName.StartsWith("Released", StringComparison.OrdinalIgnoreCase)) return WoPhase.Released;

        // بدون نام مرحله (Legacy / Instance پایان‌یافته): فقط از وضعیت کسب‌وکاری
        return workOrderStatus switch
        {
            "Open" => WoPhase.Created,
            "Assigned" => WoPhase.Preparation,
            "InProgress" => WoPhase.Execution,
            "OnHold" => WoPhase.OnHold,
            "Completed" => WoPhase.Completion,
            _ => WoPhase.Created
        };
    }

    public static SectionAccess AccessFor(WoSection section, WoPhase phase)
    {
        const SectionAccess H = SectionAccess.Hidden, R = SectionAccess.ReadOnly, E = SectionAccess.Editable, C = SectionAccess.Correctable;
        var terminal = phase is WoPhase.Closed or WoPhase.Cancelled or WoPhase.Rejected;

        switch (section)
        {
            case WoSection.RequiredSkills:
            case WoSection.RequiredMaterials:
            case WoSection.Crew:
                return phase is WoPhase.Created or WoPhase.Planning or WoPhase.Preparation ? E : R;

            case WoSection.Labor:
            case WoSection.SpareParts:
                return phase switch
                {
                    WoPhase.Created or WoPhase.Planning or WoPhase.Preparation or WoPhase.OperationsIssue => H,
                    WoPhase.Rejected => H,
                    WoPhase.Released or WoPhase.OnHold => R,
                    WoPhase.Execution => E,
                    WoPhase.Completion or WoPhase.Verification => C,
                    _ => R      // FinalApproval, Closed, Cancelled
                };

            case WoSection.DisciplineReview:
                return phase is WoPhase.Created or WoPhase.Planning or WoPhase.Preparation or WoPhase.OperationsIssue or WoPhase.Released or WoPhase.Execution or WoPhase.OnHold ? E : R;

            case WoSection.ResponsibleDiscipline:
                return phase is WoPhase.Planning or WoPhase.Preparation ? E : (phase == WoPhase.Created ? H : R);

            case WoSection.ExecutorAssignment:
            case WoSection.Schedule:
                return phase == WoPhase.Preparation ? E : (phase is WoPhase.Created or WoPhase.Planning ? H : R);

            case WoSection.JobPlanTasks:
                return phase == WoPhase.Execution ? E : R;
        }
        return terminal ? R : H;
    }

    /// <summary>تصمیم برای یک جهش مشخص. دلیل فقط برای بخش‌های Correctable الزامی است.</summary>
    public static WoSectionDecision Decide(WoSection section, WoSectionAction action, WoPhase phase, bool hasSectionPermission, bool hasCorrectionPermission, string? reason)
    {
        var access = AccessFor(section, phase);

        switch (access)
        {
            case SectionAccess.Hidden:
                return new(false, "SectionNotAvailable",
                    $"بخش «{SectionNameFa(section)}» در این مرحله (Work Order {PhaseNameFa(phase)}) در دسترس نیست.",
                    $"The \"{SectionNameEn(section)}\" section is not available at this stage ({PhaseNameEn(phase)}).");

            case SectionAccess.ReadOnly:
                return phase is WoPhase.Closed or WoPhase.Cancelled or WoPhase.Rejected
                    ? new(false, "WorkOrderImmutable",
                        $"Work Order {PhaseNameFa(phase)} است و تغییرناپذیر است؛ «{SectionNameFa(section)}» فقط‌خواندنی است. برای تغییر باید ابتدا با مجوز و دلیل «بازگشایی» شود.",
                        $"The Work Order is {PhaseNameEn(phase)} and immutable; \"{SectionNameEn(section)}\" is read-only. It must be reopened (with permission and a reason) before it can change.")
                    : new(false, "SectionReadOnly",
                        $"«{SectionNameFa(section)}» در مرحله‌ی «{PhaseNameFa(phase)}» فقط‌خواندنی است.",
                        $"\"{SectionNameEn(section)}\" is read-only at the \"{PhaseNameEn(phase)}\" stage.");

            case SectionAccess.Editable:
                return hasSectionPermission ? WoSectionDecision.Ok
                    : new(false, "MissingPermission", $"مجوز {RequiredPermission(section)} لازم است.", $"Permission {RequiredPermission(section)} is required.");

            case SectionAccess.Correctable:
                if (!hasSectionPermission)
                    return new(false, "MissingPermission", $"مجوز {RequiredPermission(section)} لازم است.", $"Permission {RequiredPermission(section)} is required.");
                if (!hasCorrectionPermission)
                    return new(false, "MissingCorrectionPermission",
                        $"اصلاح پس از اتمام اجرا نیازمند مجوز {CorrectionPermission} است.", $"Correcting data after execution requires permission {CorrectionPermission}.");
                if (string.IsNullOrWhiteSpace(reason))
                    return new(false, "ReasonRequired", "ثبت دلیل برای اصلاح الزامی است.", "A reason is required for a correction.");
                return WoSectionDecision.Ok;
        }
        return new(false, "Denied", "عملیات مجاز نیست.", "The operation is not allowed.");
    }

    /// <summary>آیا کاربر در این فاز می‌تواند کنترل‌های جهشی این بخش را ببیند (برای Razor)؟</summary>
    public static bool ShowsControls(WoSection section, WoPhase phase, bool hasSectionPermission, bool hasCorrectionPermission)
        => AccessFor(section, phase) switch
        {
            SectionAccess.Editable => hasSectionPermission,
            SectionAccess.Correctable => hasSectionPermission && hasCorrectionPermission,
            _ => false
        };

    public static string PhaseNameEn(WoPhase p) => p switch
    {
        WoPhase.Created => "Created", WoPhase.Planning => "Maintenance Planning", WoPhase.Preparation => "Preparation / Assignment",
        WoPhase.OperationsIssue => "Operations Issue", WoPhase.Released => "Released", WoPhase.Execution => "In Execution", WoPhase.OnHold => "On Hold",
        WoPhase.Completion => "Completion", WoPhase.Verification => "Verification", WoPhase.FinalApproval => "Final Approval",
        WoPhase.Closed => "Closed", WoPhase.Cancelled => "Cancelled", _ => "Rejected"
    };

    public static string PhaseNameFa(WoPhase p) => p switch
    {
        WoPhase.Created => "ایجاد‌شده", WoPhase.Planning => "برنامه‌ریزی نگهداری", WoPhase.Preparation => "آماده‌سازی / تخصیص",
        WoPhase.OperationsIssue => "صدور توسط عملیات", WoPhase.Released => "آزادشده", WoPhase.Execution => "در حال اجرا", WoPhase.OnHold => "متوقف",
        WoPhase.Completion => "تکمیل", WoPhase.Verification => "تأیید", WoPhase.FinalApproval => "تصویب نهایی",
        WoPhase.Closed => "بسته‌شده", WoPhase.Cancelled => "لغوشده", _ => "ردشده"
    };

    public static string SectionNameEn(WoSection s) => s switch
    {
        WoSection.RequiredSkills => "Required Skills", WoSection.RequiredMaterials => "Required Materials", WoSection.Crew => "Crew",
        WoSection.Labor => "Labor (actual hours)", WoSection.SpareParts => "Spare Parts Used", WoSection.DisciplineReview => "Multi-discipline review",
        WoSection.ResponsibleDiscipline => "Responsible discipline", WoSection.ExecutorAssignment => "Executor assignment", WoSection.Schedule => "Planned schedule",
        _ => "Job plan tasks"
    };

    public static string SectionNameFa(WoSection s) => s switch
    {
        WoSection.RequiredSkills => "مهارت‌های موردنیاز", WoSection.RequiredMaterials => "متریال موردنیاز", WoSection.Crew => "Crew",
        WoSection.Labor => "ساعت‌کار واقعی", WoSection.SpareParts => "قطعات مصرفی", WoSection.DisciplineReview => "بازبینی چند-دیسیپلینه",
        WoSection.ResponsibleDiscipline => "دیسیپلین مسئول", WoSection.ExecutorAssignment => "تخصیص مجری", WoSection.Schedule => "زمان‌بندی برنامه‌ریزی‌شده",
        _ => "چک‌لیست کار"
    };
}
