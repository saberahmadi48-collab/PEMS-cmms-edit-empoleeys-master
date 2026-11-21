using PEMS.WorkflowPlatform.Entities;

namespace PEMS.WorkflowPlatform.Interfaces;

// ==============================================================================
// قراردادهای Phase 1 Hardening پلتفرم گردش‌کار. همه‌ی این‌ها افزایشی‌اند و
// IWorkflowRuntime موجود را تغییر نمی‌دهند.
// ==============================================================================

/// <summary>کاربر انجام‌دهنده‌ی تغییر پیکربندی (برای لاگ ممیزی).</summary>
public sealed record WorkflowActor(int? UserId, string? Name = null);

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------
public enum WorkflowValidationSeverity { Error, Warning }

public sealed record WorkflowValidationIssue(
    string Code, WorkflowValidationSeverity Severity, string Message, int? StageId = null, int? TransitionId = null);

public sealed class WorkflowValidationResult
{
    public List<WorkflowValidationIssue> Issues { get; } = new();
    public IEnumerable<WorkflowValidationIssue> Errors => Issues.Where(i => i.Severity == WorkflowValidationSeverity.Error);
    public IEnumerable<WorkflowValidationIssue> Warnings => Issues.Where(i => i.Severity == WorkflowValidationSeverity.Warning);
    public bool IsValid => !Errors.Any();
}

public interface IWorkflowVersionValidator
{
    Task<WorkflowValidationResult> ValidateAsync(int workflowVersionId, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Definition / version lifecycle
// ---------------------------------------------------------------------------
public sealed class WorkflowStageInput
{
    public string Name { get; set; } = "";
    public int StageOrder { get; set; }
    public string StageType { get; set; } = "Sequential";
    public string? ResponsibleRole { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string ApprovalStrategy { get; set; } = "AllApprovalsRequired";
    public int? MinimumApprovalCount { get; set; }
    public bool IsMandatory { get; set; } = true;
    public bool IsSkippable { get; set; }
    public bool IsDelegatable { get; set; } = true;
    public bool IsEscalatable { get; set; }
    public int? TimeoutHours { get; set; }
    public string? EscalationRole { get; set; }
    public string? NotificationKey { get; set; }
    public string? Description { get; set; }
    public string? NameFa { get; set; }
    public string? BusinessStatus { get; set; }
    public string? ResponsibilityKey { get; set; }
    public string? EntryCondition { get; set; }
}

public sealed class WorkflowTransitionInput
{
    public int? FromStageId { get; set; }
    public int? ToStageId { get; set; }
    public string ActionType { get; set; } = "";
    public string? ConditionExpression { get; set; }
    public int Priority { get; set; }
    public string? RequiredPermission { get; set; }
    public bool ForbidInitiator { get; set; }
    public string? Label { get; set; }
    public string? LabelFa { get; set; }
}

public interface IWorkflowDefinitionService
{
    Task<WorkflowDefinition> CreateDefinitionAsync(string name, string category, string module, string? description, WorkflowActor actor, CancellationToken ct = default);

    /// <summary>ساخت Draft خالی با شماره‌ی نسخه‌ی بعدی. اگر Draft/Validated باز وجود دارد خطا می‌دهد (یک نسخه‌ی در حال ویرایش در هر زمان).</summary>
    Task<WorkflowVersion> CreateDraftVersionAsync(int definitionId, WorkflowActor actor, string? changeSummary = null, CancellationToken ct = default);

    /// <summary>کپی ساختار (مراحل، گذارها، شرط‌ها، تنظیمات) از هر نسخه به یک Draft جدید. Instance/History هرگز کپی نمی‌شوند.</summary>
    Task<WorkflowVersion> CloneVersionAsync(int sourceVersionId, WorkflowActor actor, string? changeSummary = null, CancellationToken ct = default);

    /// <summary>تعریف جدید با نام جدید که آخرین نسخه‌ی مبدأ را به‌صورت Draft v1 کپی می‌کند.</summary>
    Task<WorkflowDefinition> CloneDefinitionAsync(int sourceDefinitionId, string newName, WorkflowActor actor, CancellationToken ct = default);

    Task<WorkflowStage> AddStageAsync(int versionId, WorkflowStageInput input, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);
    Task<WorkflowStage> UpdateStageAsync(int stageId, WorkflowStageInput input, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);
    Task RemoveStageAsync(int stageId, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);
    Task<WorkflowTransition> AddTransitionAsync(int versionId, WorkflowTransitionInput input, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);

    /// <summary>Phase 4: ویرایش اتمیک گذار در Draft (مبدأ/مقصد/اکشن/برچسب/مجوز/شرط). فقط Draft/Validated.</summary>
    Task<WorkflowTransition> UpdateTransitionAsync(int transitionId, WorkflowTransitionInput input, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);
    Task RemoveTransitionAsync(int transitionId, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);

    /// <summary>Draft → Validated در صورت نداشتن خطا. نتیجه‌ی کامل Validator برگردانده می‌شود.</summary>
    Task<WorkflowValidationResult> ValidateVersionAsync(int versionId, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);

    /// <summary>Validated → Published (اعتبارسنجی مجدد در لحظه‌ی انتشار). پس از انتشار نسخه تغییرناپذیر است.</summary>
    Task<WorkflowVersion> PublishVersionAsync(int versionId, WorkflowActor actor, DateTime? effectiveDate = null, CancellationToken ct = default, byte[]? expectedRowVersion = null);

    /// <summary>Published → Retired. اگر Binding فعالی به این نسخه پین شده باشد رد می‌شود.</summary>
    Task<WorkflowVersion> RetireVersionAsync(int versionId, WorkflowActor actor, CancellationToken ct = default);

    Task ArchiveDefinitionAsync(int definitionId, WorkflowActor actor, CancellationToken ct = default);

    /// <summary>Phase 4: ویرایش فراداده‌ی تعریف (نام/نام فارسی/توضیح) - مشترک بین همه‌ی نسخه‌ها؛ نسخه‌ها را تغییر نمی‌دهد.</summary>
    Task<WorkflowDefinition> UpdateDefinitionAsync(int definitionId, string name, string? nameFa, string? description, WorkflowActor actor, CancellationToken ct = default);

    /// <summary>Phase 4: رها کردن Draft/Validated (ردیف حذف نمی‌شود؛ وضعیت Discarded). اگر Instance یا Binding به آن ارجاع دارد رد می‌شود.</summary>
    Task<WorkflowVersion> DiscardDraftAsync(int versionId, WorkflowActor actor, CancellationToken ct = default, byte[]? expectedRowVersion = null);

    Task<IReadOnlyList<WorkflowConfigAudit>> GetAuditAsync(int? definitionId = null, int? versionId = null, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Binding
// ---------------------------------------------------------------------------
public sealed record WorkflowBindingResolution(WorkflowBinding Binding, WorkflowDefinition Definition, WorkflowVersion Version);

/// <summary>بیش از یک Binding فعال و هم‌پوشان برای یک کلید. هرگز «اولین مورد» انتخاب نمی‌شود.</summary>
public class WorkflowBindingAmbiguousException : InvalidOperationException
{
    public WorkflowBindingAmbiguousException(string message) : base(message) { }
}

/// <summary>شروع هم‌زمان دوم برای موجودیتی که همین الان یک Instance در حال اجرا دارد (رد شده توسط Index یکتای دیتابیس).</summary>
public class WorkflowConcurrencyConflictException : InvalidOperationException
{
    public WorkflowConcurrencyConflictException(string message) : base(message) { }
}

/// <summary>
/// کنترل اداری Instance (جدا از IWorkflowRuntime تا امضای آن دست‌نخورده بماند): لغو یک Instance در حال اجرا بدون نیاز به
/// تطابق شرکت‌کننده - برای وقتی که فرآیند والد (مثلاً لغو Work Order) باید فرآیند وابسته‌ی باز (بازبینی چند-دیسیپلینه) را ببندد.
/// وضعیت Instance = Cancelled، مراحل در انتظار = Cancelled، و یک رکورد WorkflowHistory (ActionType=Cancel) با کاربر و دلیل ثبت می‌شود.
/// </summary>
public interface IWorkflowInstanceControl
{
    /// <returns>true اگر Instance در حال اجرا بود و لغو شد؛ false اگر Running نبود.</returns>
    Task<bool> CancelInstanceAsync(int workflowInstanceId, int? actorEmployeeId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// بازگشایی Instance «تکمیل‌شده» (Status=Completed) به یک مرحله‌ی مشخص از همان نسخه (نه Cancelled/Rejected). برای ماژولی که بازگشایی رسمی
    /// (مجوز جدا + دلیل + ممیزی) دارد، مثل Work Order بسته‌شده. Instance دوباره Running می‌شود، تاریخچه‌ی «Reopen» ثبت می‌شود و شرکت‌کنندگان مرحله‌ی مقصد
    /// مانند ورود عادی به مرحله ساخته می‌شوند. false اگر Instance Completed نبود یا مرحله به همان نسخه تعلق نداشت. ذخیره با SaveChanges خودِ متد انجام می‌شود.
    /// </summary>
    Task<bool> ReopenInstanceAsync(int workflowInstanceId, int toStageId, int? actorEmployeeId, string reason, CancellationToken ct = default);
}

public interface IWorkflowBindingService
{
    Task<WorkflowBinding> CreateAsync(string module, string entityType, string context, int definitionId, int? pinnedVersionId,
        DateTime? effectiveFrom, DateTime? effectiveTo, WorkflowActor actor, CancellationToken ct = default);

    Task<WorkflowBinding> ChangeAsync(int bindingId, int? pinnedVersionId, DateTime? effectiveFrom, DateTime? effectiveTo, WorkflowActor actor, CancellationToken ct = default);
    Task DeactivateAsync(int bindingId, WorkflowActor actor, CancellationToken ct = default);
    Task ActivateAsync(int bindingId, WorkflowActor actor, CancellationToken ct = default);

    /// <summary>null = هیچ Binding صریحی نیست (مسیر Legacy). چند Binding = WorkflowBindingAmbiguousException.</summary>
    Task<WorkflowBindingResolution?> ResolveAsync(string module, string entityType, string context, DateTime? at = null, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Action registry / conditions
// ---------------------------------------------------------------------------
public enum WorkflowActionKind
{
    /// <summary>نتیجه‌ی مرحله (مجاز به‌عنوان ActionType یک گذار).</summary>
    Outcome,
    /// <summary>اثر جانبی (Assign/Notify/Escalate). فقط از طریق Event Sink اعلام می‌شود؛ گذار نیست و پلتفرم اجرایش نمی‌کند.</summary>
    SideEffect
}

public sealed record WorkflowActionDescriptor(string Name, WorkflowActionKind Kind, string? Module, string Description);

public interface IWorkflowActionRegistry
{
    /// <summary>ثبت اکشن ماژول (فقط نام + نوع؛ هیچ Delegate/کدی اجرا نمی‌شود).</summary>
    void RegisterModuleAction(string module, string name, WorkflowActionKind kind, string description);
    WorkflowActionDescriptor? Find(string name, string? module);
    IReadOnlyList<WorkflowActionDescriptor> All(string? module = null);
}

public interface IWorkflowConditionEvaluator
{
    IReadOnlyCollection<string> SupportedFields { get; }
    /// <summary>خطاهای نحوی/فیلدهای نامعتبر. لیست خالی = معتبر.</summary>
    IReadOnlyList<string> Validate(string? expression);
    /// <summary>عبارت خالی = true. عبارت نامعتبر = InvalidOperationException (Fail-closed).</summary>
    bool Evaluate(string? expression, IReadOnlyDictionary<string, string?> facts);
}

// ---------------------------------------------------------------------------
// Security hooks / catalogs / events
// ---------------------------------------------------------------------------
public sealed record WorkflowAuthorizationContext(
    WorkflowInstance Instance, WorkflowStage Stage, WorkflowTransition Transition, string ActionType,
    int? ActorEmployeeId, IReadOnlyList<string> ActorRoles);

public sealed record WorkflowAuthorizationResult(bool Allowed, string? Reason = null)
{
    public static WorkflowAuthorizationResult Allow() => new(true);
    public static WorkflowAuthorizationResult Deny(string reason) => new(false, reason);
}

/// <summary>اتصال به سیستم مجوز موجود PEMS (IAccessControlService). پلتفرم خودش سیستم مجوز جدید نمی‌سازد.</summary>
public interface IWorkflowAuthorizationHook
{
    Task<WorkflowAuthorizationResult> AuthorizeAsync(WorkflowAuthorizationContext context, CancellationToken ct = default);
}

/// <summary>کاتالوگ مرجع برای اعتبارسنجی ارجاعات (نقش، کارمند، مجوز). اگر ثبت نشود، این بررسی‌ها هشدار «تأیید نشد» می‌دهند.</summary>
public interface IWorkflowReferenceCatalog
{
    Task<bool> RoleExistsAsync(string role, CancellationToken ct = default);
    Task<bool> EmployeeExistsAsync(int employeeId, CancellationToken ct = default);
    Task<bool> PermissionExistsAsync(string permissionName, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Enterprise Actor Resolution (additive extension points; the platform stays module-neutral)
// ---------------------------------------------------------------------------

/// <summary>یک بازیگر واجد شرایط مرحله (Snapshot هنگام ورود به مرحله).</summary>
public sealed record WorkflowActorCandidate(int EmployeeId, string? Reason, bool IsSelected = true);

public enum WorkflowActorResolutionKind { Resolved, Skipped, Failed }

/// <summary>
/// نتیجه‌ی حل بازیگران یک مرحله. Resolved ⇒ کاندیدها Snapshot می‌شوند؛ Skipped ⇒ مرحله خودکار رد می‌شود
/// (مثلاً مدیر مستقیم، مدیرعامل است)؛ Failed ⇒ ورود به مرحله رد می‌شود (Fail-closed) با State تشخیصی.
/// </summary>
public sealed record WorkflowActorResolutionOutcome(
    WorkflowActorResolutionKind Kind, string State, IReadOnlyList<WorkflowActorCandidate> Candidates,
    string? SkipReason = null, string? DetailsJson = null);

public sealed record WorkflowActorResolutionContext(
    WorkflowInstance Instance, WorkflowStage Stage, IReadOnlyDictionary<string, string?> Facts, int? InitiatedByEmployeeId);

/// <summary>اتصال به Enterprise Actor Resolver (پیاده‌سازی در Persistence). پلتفرم خودش بازیگر حل نمی‌کند.</summary>
public interface IWorkflowActorResolutionHook
{
    Task<WorkflowActorResolutionOutcome> ResolveAsync(WorkflowActorResolutionContext context, CancellationToken ct = default);
}

public class WorkflowActorResolutionException : InvalidOperationException
{
    public string State { get; }
    public string? DetailsJson { get; }
    public string? StageName { get; }

    public WorkflowActorResolutionException(string state, string? stageName, string? detailsJson)
        : base($"Actor resolution failed ({state}) for stage '{stageName}'.")
    {
        State = state;
        StageName = stageName;
        DetailsJson = detailsJson;
    }
}

/// <summary>
/// تأمین Fact های کسب‌وکاری یک موجودیت برای شرط‌های مرحله/گذار (مثلاً IsNewManufacturer). فیلدهای اعلام‌شده
/// به گرامر ارزیاب شرط افزوده می‌شوند؛ مقدارها به‌صورت رشته ("true"/"false"/عدد/متن) برگردانده می‌شوند.
/// </summary>
public interface IWorkflowBusinessContextProvider
{
    IReadOnlyCollection<string> SupportedFields { get; }
    bool Supports(string module, string businessObjectType);
    Task<IReadOnlyDictionary<string, string?>> GetFactsAsync(string module, string businessObjectType, int businessObjectId, CancellationToken ct = default);
}

public sealed record WorkflowRuntimeEvent(
    string EventType, int WorkflowInstanceId, string Module, string BusinessObjectType, int BusinessObjectId,
    int? StageId, string? StageName, string? ActionType, int? ActorEmployeeId, string? NotificationKey);

/// <summary>
/// نقطه‌ی توسعه‌ی اعلان/ادغام. پیش‌فرض No-op است؛ پلتفرم در Phase 1 هیچ اعلانی ارسال نمی‌کند.
/// EventType: StageEntered, ActionExecuted, InstanceCompleted, InstanceRejected.
/// </summary>
public interface IWorkflowEventSink
{
    Task OnEventAsync(WorkflowRuntimeEvent evt, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Runtime read model
// ---------------------------------------------------------------------------
public sealed record WorkflowUserContext(int? EmployeeId, IReadOnlyList<string> Roles);

public sealed class WorkflowAvailableAction
{
    public string ActionType { get; set; } = "";
    public string? Label { get; set; }
    public string? LabelFa { get; set; }
    public int TransitionId { get; set; }
    public int? ToStageId { get; set; }
    public string? ToStageName { get; set; }
    public bool IsAllowed { get; set; }
    public string? DeniedReason { get; set; }
}

public sealed class WorkflowParticipantView
{
    public string? Role { get; set; }
    public int? EmployeeId { get; set; }
    public string Status { get; set; } = "";
    public string? Decision { get; set; }
    public DateTime AssignedDate { get; set; }
    public DateTime? ActionDate { get; set; }
}

public sealed class WorkflowSlaView
{
    public int? TimeoutHours { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsEscalatable { get; set; }
    public string? EscalationRole { get; set; }
    /// <summary>همیشه false در Phase 1: Escalation خودکار اجرا نمی‌شود، فقط داده/وضعیت محاسبه می‌شود.</summary>
    public bool EscalationExecuted { get; set; }
}

public sealed class WorkflowStageView
{
    public int StageId { get; set; }
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string? BusinessStatus { get; set; }
    public int Order { get; set; }
    public string StageType { get; set; } = "";
    /// <summary>Completed | Current | Upcoming | Skipped | Rejected | Cancelled</summary>
    public string Status { get; set; } = "Upcoming";
    public bool IsMandatory { get; set; }
    public string? ResponsibleRole { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string ApprovalStrategy { get; set; } = "";
    public int? MinimumApprovalCount { get; set; }
    public int ApprovedCount { get; set; }
    public int PendingCount { get; set; }
    public List<WorkflowParticipantView> Participants { get; set; } = new();
    public WorkflowSlaView Sla { get; set; } = new();
}

public sealed class WorkflowHistorySummaryItem
{
    public DateTime ActionDate { get; set; }
    public string ActionType { get; set; } = "";
    public string? FromStageName { get; set; }
    public string? ToStageName { get; set; }
    public int? UserId { get; set; }
    public string? Comment { get; set; }
}

public sealed class WorkflowRuntimeViewModel
{
    public int WorkflowInstanceId { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string WorkflowName { get; set; } = "";
    public int WorkflowVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public int? CurrentStageId { get; set; }
    public string? CurrentStageName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public List<WorkflowStageView> Stages { get; set; } = new();
    public List<WorkflowAvailableAction> AvailableActions { get; set; } = new();
    public List<WorkflowHistorySummaryItem> HistorySummary { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public interface IWorkflowReadModelService
{
    Task<WorkflowRuntimeViewModel?> GetRuntimeViewAsync(int workflowInstanceId, WorkflowUserContext user, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowAvailableAction>> GetAvailableActionsAsync(int workflowInstanceId, WorkflowUserContext user, CancellationToken ct = default);

    /// <summary>همه‌ی Instance های یک موجودیت (جدیدترین اول). ارتباط صریح Module+Type+Id، نه صرفاً «جدیدترین».</summary>
    Task<IReadOnlyList<WorkflowInstance>> FindInstancesForEntityAsync(string module, string businessObjectType, int businessObjectId, CancellationToken ct = default);

    /// <summary>Instance «جاری» یک موجودیت: Running (در صورت وجود، جدیدترین)، وگرنه جدیدترین. null اگر Instance ندارد (مثلاً رکورد Legacy).</summary>
    Task<WorkflowInstance?> GetCurrentInstanceForEntityAsync(string module, string businessObjectType, int businessObjectId, CancellationToken ct = default);

    /// <summary>مدل خواندنی کامل برای جدیدترین Instance موجودیت؛ null اگر Instance ندارد.</summary>
    Task<WorkflowRuntimeViewModel?> GetRuntimeViewForEntityAsync(string module, string businessObjectType, int businessObjectId, WorkflowUserContext user, CancellationToken ct = default);
}
