using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-05: پیاده‌سازی مرجع «اعتبارسنجی گذار وضعیت در
/// Service، نه فقط Controller» - همان الگویی که در فاز بعدی به WorkOrderLifecycleService هم
/// اضافه می‌شود.
/// </summary>
public class WorkRequestService : IWorkRequestService
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numberingService;
    private readonly IFunctionalLocationService _functionalLocationService;

    // ------------------------------------------------------------------------------
    // LEGACY (فقط رکوردهای قبل از مهاجرت که Instance گردش‌کار ندارند). برای رکورد تحت گردش‌کار مرکزی هرگز
    // استفاده نمی‌شود: AdvanceStatusAsync آن‌ها را رد می‌کند و UI مراحل/اکشن‌ها را از مدل خواندنی می‌گیرد.
    // ------------------------------------------------------------------------------
    private static readonly Dictionary<string, string> LegacyNextStatus = new()
    {
        ["Draft"] = "Submitted",
        ["Submitted"] = "UnderReview",
        ["UnderReview"] = "Approved",
        ["Approved"] = "Planned",
    };

    private static string T(string fa, string en)
        => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa" ? fa : en;

    public string? GetLegacyNextStatus(string currentStatus)
        => LegacyNextStatus.TryGetValue(currentStatus, out var next) ? next : null;

    private static readonly Dictionary<string, string[]> LegalTransitions = new()
    {
        ["Draft"] = new[] { "Submitted", "Cancelled" },
        ["Submitted"] = new[] { "UnderReview", "Cancelled" },
        ["UnderReview"] = new[] { "Approved", "Rejected", "Cancelled" },
        ["Approved"] = new[] { "Planned", "Rejected", "Cancelled" },
        ["Planned"] = new[] { "Converted", "Cancelled" },
    };

    // --- Phase 2: اتصال به گردش‌کار مرکزی (PEMS.WorkflowPlatform). پارامترهای اختیاری تا سازنده‌ی قبلی (تست‌ها) کار کند ---
    public const string WorkflowModule = "WorkRequest";
    public const string WorkflowEntityType = "WorkRequest";
    public const string WorkflowContext = "Standard";
    private const string PermissionPrefix = "Permission:";

    private readonly IWorkflowRuntime _runtime;
    private readonly IWorkflowReadModelService _readModel;
    private readonly IWorkflowBindingService _bindings;
    private readonly IAccessControlService _access;
    private readonly INotificationService? _notifications;
    private readonly WorkRequestPolicyOptions _policy;

    public WorkRequestService(PEMSDbContext context, INumberingService numberingService, IFunctionalLocationService functionalLocationService,
        IWorkflowRuntime? runtime = null, IWorkflowReadModelService? readModel = null, IWorkflowBindingService? bindings = null,
        IAccessControlService? accessControl = null, INotificationService? notifications = null, WorkRequestPolicyOptions? policy = null)
    {
        _notifications = notifications;
        _policy = policy ?? new WorkRequestPolicyOptions();      // پیش‌فرض: تفکیک وظایف روشن
        _context = context;
        _numberingService = numberingService;
        _functionalLocationService = functionalLocationService;

        _access = accessControl ?? new AccessControlService(context);
        var hook = new AccessControlWorkflowAuthorizationHook(_access);
        _bindings = bindings ?? new WorkflowBindingService(context);
        _runtime = runtime ?? new WorkflowRuntimeService(context, _bindings, null, hook);
        _readModel = readModel ?? new WorkflowReadModelService(context, null, hook);
    }

    // ==============================================================================
    // Phase 2 - Work Request روی گردش‌کار مرکزی. تقسیم مسئولیت:
    //   * پلتفرم: مراحل، گذارها، شرکت‌کنندگان، مجوز گذار (RequiredPermission)، تاریخچه‌ی اجرایی، نسخه‌بندی.
    //   * این سرویس (دامنه‌ی Work Request): قواعد خاص Work Request (شرط تبدیل به WorkOrder، فیلدهای
    //     Reviewed/Approved/RejectionReason، نگاشت وضعیت به ستون Status قدیمی برای داشبوردها، ممیزی کسب‌وکاری).
    // مجوز فعلی «WorkRequest.Create/Approve» عیناً حفظ شده: شرکت‌کننده‌ی هر مرحله یک نقش-مجازی
    // «Permission:WorkRequest.X» است که همین سرویس از روی مجوزهای واقعی نقش‌های کاربر می‌سازد.
    // ==============================================================================
    private async Task<List<string>> ExpandRolesAsync(IReadOnlyList<string> roles)
    {
        var expanded = new List<string>(roles ?? Array.Empty<string>());
        foreach (var action in new[] { "Create", "Approve" })
            if ((await _access.AuthorizeForRolesAsync(expanded.ToList(), "WorkRequest", action)).Allowed)
                expanded.Add($"{PermissionPrefix}WorkRequest.{action}");
        return expanded;
    }

    /// <summary>
    /// ارتباط صریح Work Request ↔ Instance از طریق EntityKey. یک Work Request فقط یک‌بار وارد گردش‌کار می‌شود
    /// (Submit اگر Instance وجود داشته باشد رد می‌شود و RowVersion از Submit هم‌زمان جلوگیری می‌کند)، پس باید حداکثر
    /// یک Instance باشد. بیش از یکی = نقض ناوردا: «جدیدترین» را حدس نمی‌زنیم، خطای شفاف می‌دهیم.
    /// </summary>
    private async Task<WorkflowInstance?> CurrentInstanceAsync(int workRequestId)
    {
        var all = await _readModel.FindInstancesForEntityAsync(WorkflowModule, WorkflowEntityType, workRequestId);
        if (all.Count > 1)
            throw new InvalidOperationException(T($"برای Work Request {workRequestId} بیش از یک Instance گردش‌کار ثبت شده است ({all.Count}); رکورد ناسازگار است و نیازمند بررسی مدیر.", $"Work Request {workRequestId} has more than one workflow instance ({all.Count}); the record is inconsistent and needs administrator review."));
        return all.FirstOrDefault();
    }

    private async Task<string?> ResolveStartStatusAsync()
    {
        var res = await _bindings.ResolveAsync(WorkflowModule, WorkflowEntityType, WorkflowContext);
        if (res == null) return null;
        var start = await _context.WorkflowTransitions.Where(t => t.WorkflowVersionId == res.Version.WorkflowVersionId && t.FromStageId == null)
            .OrderBy(t => t.Priority).FirstOrDefaultAsync();
        if (start?.ToStageId == null) return null;
        return await _context.WorkflowStages.Where(s => s.WorkflowStageId == start.ToStageId).Select(s => s.BusinessStatus).FirstOrDefaultAsync();
    }

    /// <summary>وضعیت کسب‌وکاری جدید: مرحله‌ی مقصد (از داده‌ی پیکربندی) یا وضعیت پایانی از روی اکشن.</summary>
    private async Task<string?> ResolveNewStatusAsync(WorkflowAvailableAction action, string currentStatus)
    {
        if (action.ToStageId.HasValue)
        {
            var bs = await _context.WorkflowStages.Where(s => s.WorkflowStageId == action.ToStageId).Select(s => s.BusinessStatus).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(bs) ? currentStatus : bs;
        }
        return action.ActionType switch { "Reject" => "Rejected", "Cancel" => "Cancelled", "Complete" => "Converted", _ => currentStatus };
    }

    private async Task StageBusinessChangeAsync(WorkRequest wr, string newStatus, string? reason, int? employeeId)
    {
        var from = wr.Status;
        if (from == newStatus) return;
        if (newStatus == "UnderReview") { wr.ReviewedById = employeeId; wr.ReviewedDate = DateTime.Now; }
        if (newStatus == "Approved") { wr.ApprovedById = employeeId; wr.ApprovedDate = DateTime.Now; }
        if (newStatus == "Rejected") wr.RejectionReason = reason;
        wr.Status = newStatus;
        await _context.WorkRequestStatusHistories.AddAsync(new WorkRequestStatusHistory
        {
            WorkRequestId = wr.WorkRequestId, FromStatus = from, ToStatus = newStatus,
            ChangedById = employeeId, ChangedDate = DateTime.Now, Reason = reason
        });
    }

    public async Task<(bool Success, string? Error)> SubmitAsync(int workRequestId, WorkRequestActor actor)
    {
        var wr = await _context.WorkRequests.FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId);
        if (wr == null) return (false, "Work Request پیدا نشد.");
        if (wr.Status != "Draft") return (false, T("فقط پیش‌نویس قابل ارسال به گردش‌کار است.", "Only a draft can be submitted to the workflow."));
        try { if (await CurrentInstanceAsync(workRequestId) != null) return (false, T("این درخواست قبلاً وارد گردش‌کار شده است.", "This request has already entered the workflow.")); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }

        var roles = await ExpandRolesAsync(actor.Roles);
        if (!roles.Contains($"{PermissionPrefix}WorkRequest.Create"))
            return (false, T("شما مجوز ارسال Work Request را ندارید.", "You do not have permission to submit a Work Request."));

        // A2: پیش‌نویس‌های قدیمی بدون محل هم قابل ارسال نیستند
        if (!wr.EquipmentId.HasValue && !wr.AssetId.HasValue && !wr.FunctionalLocationId.HasValue)
            return (false, T("پیش از ارسال، تجهیز، Asset یا Functional Location باید مشخص شود.", "An equipment, asset or functional location must be set before submitting."));

        string? startStatus;
        try { startStatus = await ResolveStartStatusAsync(); }
        catch (WorkflowBindingAmbiguousException ex) { return (false, ex.Message); }   // ابهام Binding = خطای قطعی، نه انتخاب یکی
        if (startStatus == null)
            return (false, T("گردش‌کار Work Request پیکربندی نشده است (Binding فعال یا مرحله‌ی شروع یافت نشد).", "The Work Request workflow is not configured (no active binding or start stage found)."));

        try
        {
            await StageBusinessChangeAsync(wr, startStatus, null, actor.EmployeeId);
            // Instance + History + تغییر Status/ممیزی کسب‌وکاری در یک SaveChanges اتمیک ذخیره می‌شوند
            await _runtime.StartInstanceAsync(WorkflowModule, WorkflowEntityType, workRequestId, WorkflowContext, null, actor.EmployeeId);
            return (true, null);
        }
        catch (WorkflowConcurrencyConflictException)
        {
            _context.ChangeTracker.Clear();
            return (false, T("این درخواست هم‌زمان توسط کاربر دیگری ارسال شد؛ صفحه را دوباره بارگذاری کنید.", "This request was submitted at the same time by another user; reload the page."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return (false, ex is DbUpdateConcurrencyException ? T("این درخواست هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را دوباره بارگذاری کنید.", "This request was changed by another user at the same time; reload the page.") : ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> ExecuteWorkflowActionAsync(int workRequestId, string actionType, string? comment, WorkRequestActor actor)
    {
        var wr = await _context.WorkRequests.FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId);
        if (wr == null) return (false, "Work Request پیدا نشد.");

        WorkflowInstance? instance;
        try { instance = await CurrentInstanceAsync(workRequestId); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }
        if (instance == null) return (false, T("این درخواست Instance گردش‌کار ندارد (رکورد Legacy یا پیش‌نویس).", "This request has no workflow instance (legacy record or draft)."));
        if (instance.Status != "Running") return (false, T($"گردش‌کار در وضعیت «{instance.Status}» است.", $"The workflow is {instance.Status}."));

        // «Complete» = تبدیل به WorkOrder؛ اکشن کسب‌وکاری است و قواعد خودش (Planned، تجهیز، یک‌بار) را دارد
        if (actionType == "Complete")
            return (false, T("این اقدام از مسیر «تبدیل به WorkOrder» انجام می‌شود.", "This action is performed through \"Convert to Work Order\"."));

        var roles = await ExpandRolesAsync(actor.Roles);
        var actions = await _readModel.GetAvailableActionsAsync(instance.WorkflowInstanceId, new WorkflowUserContext(actor.EmployeeId, roles));
        var action = actions.FirstOrDefault(a => a.ActionType == actionType);
        if (action == null) return (false, T($"اقدام «{actionType}» در مرحله‌ی جاری برای شما در دسترس نیست.", $"Action \"{actionType}\" is not available for you at the current stage."));
        if (!action.IsAllowed) return (false, T($"اقدام «{actionType}» مجاز نیست ({action.DeniedReason}).", $"Action \"{actionType}\" is not allowed ({action.DeniedReason})."));

        // A1: reject/cancel/return without a reason are refused (shared rule with Work Order); the reason stays in workflow + status history
        var reasonError = WorkflowActionRules.ValidateAction(actionType, comment);
        if (reasonError != null) return (false, reasonError);
        comment = WorkflowActionRules.Normalize(comment);

        var newStatus = await ResolveNewStatusAsync(action, wr.Status);
        var sodError = await CheckSegregationAsync(wr, actionType, newStatus, actor, comment);
        if (sodError != null) return (false, sodError);
        try
        {
            await StageBusinessChangeAsync(wr, newStatus ?? wr.Status, comment, actor.EmployeeId);
            // پلتفرم مجدداً و به‌صورت مستقل مجوز/SoD/شرط را اعمال می‌کند؛ سپس همه‌ی تغییرات را اتمیک ذخیره می‌کند
            await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, actionType, actor.EmployeeId, roles, comment, null, null);
            return (true, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return (false, ex is DbUpdateConcurrencyException ? T("این درخواست هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را دوباره بارگذاری کنید.", "This request was changed by another user at the same time; reload the page.") : ex.Message);
        }
    }

    public async Task<WorkflowRuntimeViewModel?> GetWorkflowViewAsync(int workRequestId, WorkRequestActor actor)
    {
        var instance = await CurrentInstanceAsync(workRequestId);   // ارتباط صریح و سخت‌گیرانه؛ نه «جدیدترین»
        if (instance == null) return null;
        var roles = await ExpandRolesAsync(actor.Roles);
        return await _readModel.GetRuntimeViewAsync(instance.WorkflowInstanceId, new WorkflowUserContext(actor.EmployeeId, roles));
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول فیلترشده
    // با ToListAsync (بدون Skip/Take) بارگذاری می‌شد. دو Include قبلی هم غیرضروری بودند - چون
    // Select پایین‌تر مستقیم فیلد اسکالر می‌گیرد، نه خودِ Entity ناوبری‌شده را.
    public async Task<PagedResult<WorkRequestListDto>> GetListAsync(int page = 1, int pageSize = 50, string? status = null)
    {
        var query = _context.WorkRequests.Where(x => x.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);

        var totalCount = await query.CountAsync();

        page = page < 1 ? 1 : page;
        pageSize = DataGridPageSize.Normalize(pageSize);

        var items = await query
            .OrderByDescending(x => x.RequestedDate)
            .ThenByDescending(x => x.WorkRequestId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new WorkRequestListDto
            {
                WorkRequestId = x.WorkRequestId,
                RequestNumber = x.RequestNumber,
                Title = x.Title,
                Status = x.Status,
                Priority = x.Priority,
                RequestedByName = x.RequestedBy != null ? x.RequestedBy.FirstName + " " + x.RequestedBy.LastName : "",
                RequestedDate = x.RequestedDate,
                EquipmentTagNumber = x.Equipment != null ? x.Equipment.TagNumber : null
            })
            .ToListAsync();

        return new PagedResult<WorkRequestListDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<WorkRequestDetailDto?> GetDetailsAsync(int workRequestId)
    {
        var wr = await _context.WorkRequests
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.ApprovedBy)
            .Include(x => x.Equipment)
            .Include(x => x.Asset)
            .Include(x => x.SuggestedDiscipline)
            .Include(x => x.FailureMode)
            .Include(x => x.LinkedWorkOrder)
            .FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId);

        if (wr == null) return null;

        var history = await _context.WorkRequestStatusHistories
            .Include(x => x.ChangedBy)
            .Where(x => x.WorkRequestId == workRequestId)
            .OrderByDescending(x => x.ChangedDate)
            .Select(x => new WorkRequestStatusHistoryDto
            {
                FromStatus = x.FromStatus,
                ToStatus = x.ToStatus,
                ChangedByName = x.ChangedBy != null ? x.ChangedBy.FirstName + " " + x.ChangedBy.LastName : null,
                ChangedDate = x.ChangedDate,
                Reason = x.Reason
            })
            .ToListAsync();

        string? criticalityClass = null;
        if (wr.EquipmentId.HasValue)
        {
            criticalityClass = await _context.EquipmentCriticalities
                .Where(x => x.EquipmentId == wr.EquipmentId.Value && x.IsActive)
                .Select(x => x.CriticalityClass)
                .FirstOrDefaultAsync();
        }

        return new WorkRequestDetailDto
        {
            WorkRequestId = wr.WorkRequestId,
            RequestNumber = wr.RequestNumber,
            Title = wr.Title,
            Description = wr.Description,
            RequestedByName = wr.RequestedBy != null ? $"{wr.RequestedBy.FirstName} {wr.RequestedBy.LastName}" : "",
            RequestedDate = wr.RequestedDate,
            EquipmentId = wr.EquipmentId,
            EquipmentTagNumber = wr.Equipment?.TagNumber,
            AssetId = wr.AssetId,
            AssetNumber = wr.Asset?.AssetNumber,
            FunctionalLocationId = wr.FunctionalLocationId,
            FunctionalLocationBreadcrumb = await _functionalLocationService.GetBreadcrumbAsync(wr.FunctionalLocationId),
            ReportedCondition = wr.ReportedCondition,
            Priority = wr.Priority,
            EquipmentCriticalityClass = criticalityClass,
            SuggestedDisciplineId = wr.SuggestedDisciplineId,
            SuggestedDisciplineName = wr.SuggestedDiscipline?.Name,
            Status = wr.Status,
            ReviewedByName = wr.ReviewedBy != null ? $"{wr.ReviewedBy.FirstName} {wr.ReviewedBy.LastName}" : null,
            ReviewedDate = wr.ReviewedDate,
            RejectionReason = wr.RejectionReason,
            ApprovedByName = wr.ApprovedBy != null ? $"{wr.ApprovedBy.FirstName} {wr.ApprovedBy.LastName}" : null,
            ApprovedDate = wr.ApprovedDate,
            LinkedWorkOrderId = wr.LinkedWorkOrderId,
            LinkedWorkOrderNumber = wr.LinkedWorkOrder?.WorkOrderNumber,
            IsHseRelated = wr.IsHseRelated, IsEmergency = wr.IsEmergency, RiskLikelihood = wr.RiskLikelihood, RiskSeverity = wr.RiskSeverity,
            RiskScore = wr.RiskScore, RiskLevel = wr.RiskLevel, RequestedPriority = wr.RequestedPriority, PriorityRationale = wr.PriorityRationale,
            ResponseDueDate = wr.ResponseDueDate, ResolutionDueDate = wr.ResolutionDueDate,
            FailureModeName = wr.FailureMode != null ? wr.FailureMode.Code + " - " + wr.FailureMode.Name : null,
            FailureEffect = wr.FailureEffect, ReportedDowntimeHours = wr.ReportedDowntimeHours,
            StatusHistory = history
        };
    }

    public Task<(bool Success, string? Error, int? Id)> CreateAsync(
        string title, string? description, int requestedById, int? equipmentId, int? assetId,
        int? functionalLocationId, string? reportedCondition, string priority, int? suggestedDisciplineId)
        => CreateAsync(new WorkRequestCreateRequest
        {
            Title = title, Description = description, RequestedById = requestedById, EquipmentId = equipmentId, AssetId = assetId,
            FunctionalLocationId = functionalLocationId, ReportedCondition = reportedCondition, Priority = priority, SuggestedDisciplineId = suggestedDisciplineId
        });

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(WorkRequestCreateRequest r)
    {
        var title = r.Title?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(title)) return (false, T("عنوان درخواست الزامی است.", "A title is required."), null);

        if (!await _context.Employees.AnyAsync(x => x.EmployeeId == r.RequestedById && x.IsActive))
            return (false, T("کارمند درخواست‌دهنده نامعتبر است.", "The requesting employee is invalid."), null);

        if (!WorkRequestRiskAssessor.IsValidRating(r.RiskLikelihood) || !WorkRequestRiskAssessor.IsValidRating(r.RiskSeverity))
            return (false, T("احتمال و شدت ریسک باید بین ۱ تا ۵ باشند.", "Risk likelihood and severity must be between 1 and 5."), null);
        if ((r.RiskLikelihood.HasValue) != (r.RiskSeverity.HasValue))
            return (false, T("برای ارزیابی ریسک هم احتمال و هم شدت لازم است.", "Both likelihood and severity are required for a risk assessment."), null);
        if (r.ReportedDowntimeHours is < 0) return (false, T("ساعت توقف نمی‌تواند منفی باشد.", "Downtime hours cannot be negative."), null);

        // A2: هر درخواست باید به «محل» مشخصی وصل باشد: تجهیز، Asset یا Functional Location. Asset همیشه به یک تجهیز وصل است و تجهیز از آن مشتق می‌شود.
        var equipmentId = r.EquipmentId;
        if (!equipmentId.HasValue && r.AssetId.HasValue)
        {
            equipmentId = await _context.Assets.Where(a => a.AssetId == r.AssetId.Value).Select(a => (int?)a.EquipmentId).FirstOrDefaultAsync();
            if (!equipmentId.HasValue) return (false, T("Asset انتخاب‌شده یافت نشد.", "The selected asset was not found."), null);
        }
        if (!equipmentId.HasValue && !r.FunctionalLocationId.HasValue)
            return (false, T("تجهیز، Asset یا Functional Location باید مشخص شود (درخواست بدون محل پذیرفته نمی‌شود).", "An equipment, asset or functional location is required (a request without a location is not accepted)."), null);
        if (equipmentId.HasValue && !await _context.Equipments.AnyAsync(e => e.EquipmentId == equipmentId.Value))
            return (false, T("تجهیز انتخاب‌شده یافت نشد.", "The selected equipment was not found."), null);
        if (r.FunctionalLocationId.HasValue && !await _context.FunctionalLocations.AnyAsync(f => f.FunctionalLocationId == r.FunctionalLocationId.Value))
            return (false, T("Functional Location انتخاب‌شده یافت نشد.", "The selected functional location was not found."), null);
        if (r.FailureModeId.HasValue && !await _context.FailureModes.AnyAsync(f => f.FailureModeId == r.FailureModeId.Value && f.IsActive))
            return (false, T("حالت خرابی انتخاب‌شده در کاتالوگ فعال نیست.", "The selected failure mode is not an active catalog entry."), null);

        var now = DateTime.Now;
        var assessment = WorkRequestRiskAssessor.Assess(r.RiskLikelihood, r.RiskSeverity, r.IsHseRelated, r.IsEmergency, r.Priority, now);
        var failureEffect = string.IsNullOrWhiteSpace(r.FailureEffect) ? null : r.FailureEffect.Trim();
        if (failureEffect is { Length: > 500 }) failureEffect = failureEffect[..500];

        // A7: شماره و ردیف سند در «یک» تراکنش: اگر ثبت شکست بخورد شماره هم برمی‌گردد (بدون حفره)؛ ردیف شمارنده تا Commit قفل است (بدون تکرار در هم‌زمانی).
        WorkRequest? created = null;
        try
        {
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
                WorkRequest? attempt = null;
                try
                {
                    if (_context.Database.IsRelational() && _context.Database.CurrentTransaction == null) tx = await _context.Database.BeginTransactionAsync();

                    attempt = new WorkRequest
                    {
                        RequestNumber = await _numberingService.GetNextNumberAsync("WorkRequest"),
                        Title = title,
                        Description = r.Description,
                        RequestedById = r.RequestedById,
                        RequestedDate = now,
                        EquipmentId = equipmentId,
                        AssetId = r.AssetId,
                        FunctionalLocationId = r.FunctionalLocationId,
                        ReportedCondition = r.ReportedCondition,
                        Priority = assessment.Priority,
                        RequestedPriority = WorkRequestRiskAssessor.NormalizePriority(r.Priority),
                        PriorityRationale = assessment.Rationale,
                        IsHseRelated = r.IsHseRelated,
                        IsEmergency = r.IsEmergency,
                        RiskLikelihood = r.RiskLikelihood,
                        RiskSeverity = r.RiskSeverity,
                        RiskScore = assessment.Score,
                        RiskLevel = assessment.RiskLevel,
                        ResponseDueDate = assessment.ResponseDue,
                        ResolutionDueDate = assessment.ResolutionDue,
                        FailureModeId = r.FailureModeId,
                        FailureEffect = failureEffect,
                        ReportedDowntimeHours = r.ReportedDowntimeHours,
                        SuggestedDisciplineId = r.SuggestedDisciplineId,
                        Status = "Draft",
                        IsActive = true,
                        CreatedDate = now
                    };
                    await _context.WorkRequests.AddAsync(attempt);
                    await _context.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                    created = attempt;
                }
                catch
                {
                    if (tx != null) await tx.RollbackAsync();
                    if (attempt != null) _context.Entry(attempt).State = EntityState.Detached;
                    throw;
                }
                finally { if (tx != null) await tx.DisposeAsync(); }
            });
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            return (false, T("ثبت درخواست انجام نشد؛ دوباره تلاش کنید. ", "The request could not be saved; please retry. ") + ex.GetBaseException().Message, null);
        }

        await NotifyHseAsync(created!);
        return (true, null, created!.WorkRequestId);
    }

    /// <summary>A4: اعلان درون‌برنامه‌ای به کاربران HSE برای درخواست HSE-related/اضطراری. بهترین‌تلاش و پس از Commit؛ شکست اعلان هرگز ثبت درخواست را برنمی‌گرداند.</summary>
    private async Task NotifyHseAsync(WorkRequest wr)
    {
        if (_notifications == null || !(wr.IsHseRelated || wr.IsEmergency)) return;
        try
        {
            var userIds = await (from u in _context.Users
                                 join e in _context.Employees on u.EmployeeId equals (int?)e.EmployeeId
                                 where e.IsActive && ((e.Department != null && e.Department.Name == "HSE") || (e.Discipline != null && e.Discipline.Code == "HSE"))
                                 select u.Id).Distinct().ToListAsync();
            var title = wr.IsEmergency ? $"EMERGENCY Work Request {wr.RequestNumber}" : $"HSE-related Work Request {wr.RequestNumber}";
            var message = $"{wr.Title} — priority {wr.Priority}; response due {wr.ResponseDueDate:yyyy-MM-dd HH:mm}.";
            foreach (var userId in userIds)
                await _notifications.CreateAsync(userId, title, message, $"/WorkRequest/Details/{wr.WorkRequestId}", "WorkRequest", wr.WorkRequestId);
        }
        catch (Exception) { /* اعلان بهترین‌تلاش است */ }
    }

    /// <summary>
    /// A3: تفکیک وظایف (پیش‌فرض روشن، قابل‌پیکربندی). درخواست‌دهنده نمی‌تواند همان درخواست را بازبینی/تأیید/برنامه‌ریزی/رد کند و تأییدکننده نباید همان بازبین باشد.
    /// SystemAdmin فقط با ثبت ممیزی (AuthorizationAuditLog با Decision=SoDOverride + دلیل) می‌تواند Override کند. null = مجاز.
    /// </summary>
    private async Task<string?> CheckSegregationAsync(WorkRequest wr, string actionType, string? newStatus, WorkRequestActor actor, string? comment)
    {
        if (!_policy.EnforceSegregationOfDuties) return null;
        var isDecision = newStatus is "UnderReview" or "Approved" or "Planned" || actionType == "Reject";
        if (!isDecision || !actor.EmployeeId.HasValue) return null;

        string? violation = null;
        if (actor.EmployeeId == wr.RequestedById)
            violation = T("درخواست‌دهنده نمی‌تواند همان درخواست را بازبینی/تأیید/برنامه‌ریزی/رد کند (تفکیک وظایف).", "The requester cannot review, approve, plan or reject their own request (segregation of duties).");
        else if (newStatus == "Approved" && wr.ReviewedById == actor.EmployeeId)
            violation = T("تأییدکننده نمی‌تواند همان بازبین باشد (تفکیک وظایف).", "The approver cannot be the same person as the reviewer (segregation of duties).");
        if (violation == null) return null;

        if (actor.Roles.Contains(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, StringComparer.OrdinalIgnoreCase))
        {
            _context.AuthorizationAuditLogs.Add(new AuthorizationAuditLog
            {
                UserId = null, ResourceName = "WorkRequest", ActionName = actionType, ResourceId = wr.WorkRequestId.ToString(),
                Decision = "SoDOverride", Reason = $"SystemAdmin (employee {actor.EmployeeId}) overrode: {violation} Comment: {comment}", Timestamp = DateTime.Now
            });
            return null;      // ذخیره در همان SaveChanges اکشن انجام می‌شود (اتمیک با تغییر وضعیت)
        }
        return violation;
    }

    public async Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int workRequestId, string nextStatus, string? reason, int? changedByEmployeeId)
    {
        var wr = await _context.WorkRequests.FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId);
        if (wr == null) return (false, "Work Request پیدا نشد.");

        // Phase 2: مسیر Legacy فقط برای رکوردهای بدون Instance گردش‌کار است. اگر Instance دارد، دور زدن گردش‌کار مرکزی ممنوع است.
        try
        {
            if (await CurrentInstanceAsync(workRequestId) != null)
                return (false, T("این درخواست تحت گردش‌کار مرکزی است؛ تغییر وضعیت فقط از مسیر اقدامات گردش‌کار مجاز است.", "This request is governed by the central workflow; its status can only change through workflow actions."));
        }
        catch (InvalidOperationException ex) { return (false, ex.Message); }
        // پیش‌نویس جدید باید با SubmitAsync وارد گردش‌کار شود، نه با تغییر مستقیم وضعیت
        if (wr.Status == "Draft" && nextStatus == "Submitted")
        {
            bool bound;
            try { bound = await _bindings.ResolveAsync(WorkflowModule, WorkflowEntityType, WorkflowContext) != null; }
            catch (WorkflowBindingAmbiguousException) { bound = true; }   // Binding مبهم هم یعنی مسیر Legacy مجاز نیست
            if (bound) return (false, T("ارسال Work Request باید از مسیر گردش‌کار مرکزی (Submit) انجام شود.", "A Work Request must be submitted through the central workflow (Submit)."));
        }

        if (!LegalTransitions.TryGetValue(wr.Status, out var allowed) || !allowed.Contains(nextStatus))
            return (false, $"گذار از وضعیت «{wr.Status}» به «{nextStatus}» مجاز نیست.");

        var legacyReasonError = WorkflowActionRules.ValidateStatus(nextStatus, reason);
        if (legacyReasonError != null) return (false, legacyReasonError);
        reason = WorkflowActionRules.Normalize(reason);

        var fromStatus = wr.Status;

        if (nextStatus == "UnderReview")
        {
            wr.ReviewedById = changedByEmployeeId;
            wr.ReviewedDate = DateTime.Now;
        }

        if (nextStatus == "Approved")
        {
            wr.ApprovedById = changedByEmployeeId;
            wr.ApprovedDate = DateTime.Now;
        }

        if (nextStatus == "Rejected")
        {
            wr.RejectionReason = reason;
        }

        wr.Status = nextStatus;

        await _context.WorkRequestStatusHistories.AddAsync(new WorkRequestStatusHistory
        {
            WorkRequestId = wr.WorkRequestId,
            FromStatus = fromStatus,
            ToStatus = nextStatus,
            ChangedById = changedByEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = reason
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int? WorkOrderId)> ConvertToWorkOrderAsync(
        int workRequestId, string workOrderType, string? currentUserName, WorkRequestActor? actor = null, int? equipmentId = null)
    {
        var wr = await _context.WorkRequests.Include(x => x.Equipment).FirstOrDefaultAsync(x => x.WorkRequestId == workRequestId);
        if (wr == null) return (false, "Work Request پیدا نشد.", null);

        if (wr.Status != "Planned")
            return (false, "فقط درخواست‌های در وضعیت Planned قابل تبدیل به WorkOrder هستند.", null);

        if (wr.LinkedWorkOrderId.HasValue)
            return (false, "این درخواست قبلاً به یک WorkOrder تبدیل شده.", null);

        // درخواستِ فقط-Functional-Location: Work Order همیشه یک تجهیز دارد، پس در لحظه‌ی تبدیل باید تجهیز مشخص شود (انتخاب صریح؛ حدس زده نمی‌شود)
        if (!wr.EquipmentId.HasValue && equipmentId.HasValue)
        {
            if (!await _context.Equipments.AnyAsync(e => e.EquipmentId == equipmentId.Value))
                return (false, T("تجهیز انتخاب‌شده یافت نشد.", "The selected equipment was not found."), null);
            wr.EquipmentId = equipmentId;
        }
        if (!wr.EquipmentId.HasValue)
            return (false, T("برای ساخت WorkOrder باید یک تجهیز مشخص شود (این درخواست فقط به Functional Location وصل است؛ تجهیز را در فرم تبدیل انتخاب کنید).",
                "An equipment is required to create a Work Order (this request only has a functional location; select the equipment in the convert form)."), null);

        // Phase 2: اگر تحت گردش‌کار مرکزی است، اکشن «Complete» باید برای این کاربر مجاز باشد (سمت‌سرور، قبل از هر تغییر)
        WorkflowInstance? instance;
        try { instance = await CurrentInstanceAsync(workRequestId); }
        catch (InvalidOperationException ex) { return (false, ex.Message, null); }
        List<string>? actorRoles = null;
        if (instance != null)
        {
            if (actor == null) return (false, T("برای Work Request تحت گردش‌کار، اطلاعات کاربر اجراکننده الزامی است.", "The acting user is required for a workflow-governed Work Request."), null);
            if (instance.Status != "Running") return (false, T($"گردش‌کار در وضعیت «{instance.Status}» است.", $"The workflow is {instance.Status}."), null);
            actorRoles = await ExpandRolesAsync(actor.Roles);
            var actions = await _readModel.GetAvailableActionsAsync(instance.WorkflowInstanceId, new WorkflowUserContext(actor.EmployeeId, actorRoles));
            var complete = actions.FirstOrDefault(a => a.ActionType == "Complete");
            if (complete == null) return (false, T("اقدام تبدیل در مرحله‌ی جاری گردش‌کار در دسترس نیست.", "The conversion action is not available at the current workflow stage."), null);
            if (!complete.IsAllowed) return (false, T($"اقدام تبدیل مجاز نیست ({complete.DeniedReason}).", $"The conversion action is not allowed ({complete.DeniedReason})."), null);
        }

        var workOrder = new WorkOrder
        {
            EquipmentId = wr.EquipmentId.Value,
            SourceWorkRequestId = wr.WorkRequestId,
            WorkOrderType = workOrderType,
            Priority = wr.Priority,
            Status = "Open",
            Description = wr.Title + (string.IsNullOrEmpty(wr.Description) ? "" : " - " + wr.Description),
            IsActive = true,
            CreatedBy = currentUserName,
            CreatedDate = DateTime.Now,
            // A5: اطلاعات خرابی گزارش‌شده و HSE از Work Request منتقل می‌شود (پیش‌پر برای فرم تکمیل؛ داده‌ی تکمیل واقعی آن را بازنویسی می‌کند)
            IsHseRelated = wr.IsHseRelated,
            FailureEffect = wr.FailureEffect,
            DowntimeHours = wr.ReportedDowntimeHours
        };
        if (wr.FailureModeId.HasValue)
        {
            var mode = await _context.FailureModes.Where(f => f.FailureModeId == wr.FailureModeId.Value).Select(f => new { f.FailureModeId, f.Code }).FirstOrDefaultAsync();
            if (mode != null) { workOrder.FailureModeId = mode.FailureModeId; workOrder.FailureModeCode = mode.Code; }
        }
        // A6: «دیسیپلین پیشنهادی» ثبت‌کننده به‌عنوان دیسیپلین «مسئول» پیشنهادی نگهداری منتقل می‌شود (قبلاً گم می‌شد). دیسیپلین «مبدأ» همان دیسیپلین درخواست‌دهنده است
        // (پایین‌تر از Snapshot کارمند) و دیسیپلین «مسئول» را برنامه‌ریز در مرحله‌ی Planning تأیید/تغییر می‌دهد.
        if (wr.SuggestedDisciplineId.HasValue
            && await _context.Disciplines.AnyAsync(d => d.DisciplineId == wr.SuggestedDisciplineId.Value && d.IsActive))
        {
            workOrder.ResponsibleDisciplineId = wr.SuggestedDisciplineId;
            workOrder.ResponsibleOrganization = PEMS.Domain.Common.OrganizationDomains.Maintenance;
        }

        // Phase 3B: اگر گردش‌کار چرخه‌عمر Work Order با Binding فعال است، Work Order «حاکمیتی» ساخته می‌شود: مبدأ (سازمان + دیسیپلین
        // + درخواست‌دهنده) از Work Request Snapshot می‌شود (قبلاً فقط SourceWorkRequestId کپی می‌شد) و Instance چرخه‌عمر شروع می‌شود.
        // بدون Binding (مثلاً محیط‌های قدیمی/تست) رفتار قبلی Legacy عیناً حفظ می‌شود.
        bool governedWo;
        try { governedWo = await _bindings.ResolveAsync(PEMS.Persistence.Workflow.WorkOrderWorkflow.Module, PEMS.Persistence.Workflow.WorkOrderWorkflow.LifecycleEntityType, PEMS.Persistence.Workflow.WorkOrderWorkflow.LifecycleContext) != null; }
        catch (WorkflowBindingAmbiguousException ex) { return (false, ex.Message, null); }
        if (governedWo)
        {
            var (org, disc) = await PEMS.Persistence.Workflow.WorkOrderWorkflow.ResolveEmployeeAsync(_context, wr.RequestedById);
            if (org == null || !disc.HasValue)
                return (false, T("دامنه‌ی سازمانی/دیسیپلین درخواست‌دهنده مشخص نیست؛ دیسیپلین مبدأ Work Order ثبت نمی‌شود.", "The requester's organization domain / discipline is not defined; the originating discipline of the Work Order cannot be recorded."), null);
            workOrder.OriginatingOrganization = org;
            workOrder.OriginatingDisciplineId = disc;
            workOrder.OriginatingRequestedById = wr.RequestedById;
        }

        // A7: شماره فقط پس از گذراندن همه‌ی اعتبارسنجی‌ها گرفته می‌شود (قبلاً پیش از آن‌ها گرفته می‌شد و هر رد، یک شماره را می‌سوزاند)
        var workOrderNumber = await _numberingService.GetNextNumberAsync("WorkOrder");
        workOrder.WorkOrderNumber = workOrderNumber;

        var fromStatus = wr.Status;
        // از navigation property به جای FK خام استفاده شد چون workOrder هنوز Id واقعی ندارد -
        // EF Core موقع SaveChanges به‌صورت خودکار FK را از روی object reference پر می‌کند (fixup).
        wr.LinkedWorkOrder = workOrder;
        wr.Status = "Converted";
        wr.ModifiedBy = currentUserName;

        // هر دو تغییر (ساخت WorkOrder و به‌روزرسانی WorkRequest) باید یک‌جا و اتمیک ذخیره شوند
        // تا در صورت خطا بین این دو مرحله، WorkOrder یتیم بدون به‌روزرسانی WorkRequest باقی نماند.
        await _context.WorkOrders.AddAsync(workOrder);
        await _context.WorkRequestStatusHistories.AddAsync(new WorkRequestStatusHistory
        {
            WorkRequestId = wr.WorkRequestId,
            FromStatus = fromStatus,
            ToStatus = "Converted",
            Reason = $"تبدیل به {workOrderNumber}",
            ChangedDate = DateTime.Now
        });

        return await _context.Database.CreateExecutionStrategy().ExecuteAsync<(bool Success, string? Error, int? WorkOrderId)>(async () =>
        {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        try
        {
            if (governedWo)
            {
                // Work Order + Instance چرخه‌عمر (+ تکمیل گردش‌کار Work Request) در یک تراکنش؛ Id واقعی WorkOrder لازم است
                if (_context.Database.IsRelational() && _context.Database.CurrentTransaction == null) tx = await _context.Database.BeginTransactionAsync();
                _context.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory { WorkOrder = workOrder, FromStatus = "", ToStatus = "Open", ChangedById = actor?.EmployeeId, ChangedDate = DateTime.Now, Reason = $"Created from {wr.RequestNumber}" });
                await _context.SaveChangesAsync();
                await _runtime.StartInstanceAsync(PEMS.Persistence.Workflow.WorkOrderWorkflow.Module, PEMS.Persistence.Workflow.WorkOrderWorkflow.LifecycleEntityType,
                    workOrder.WorkOrderId, PEMS.Persistence.Workflow.WorkOrderWorkflow.LifecycleContext, null, actor?.EmployeeId);
            }

            if (instance != null)
                // WorkOrder + Status + ممیزی + اجرای «Complete» گردش‌کار: یک SaveChanges اتمیک در Runtime
                await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, "Complete", actor!.EmployeeId, actorRoles!, workOrderNumber, null, null);
            else if (!governedWo)
                await _context.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
            return (true, null, workOrder.WorkOrderId);
        }
        catch (Exception ex) when ((instance != null || governedWo) && (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateException))
        {
            if (tx != null) await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            return (false, ex is DbUpdateConcurrencyException or WorkflowConcurrencyConflictException
                ? T("این درخواست هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را دوباره بارگذاری کنید.", "This request was changed by another user at the same time; reload the page.") : ex.Message, null);
        }
        finally { if (tx != null) await tx.DisposeAsync(); }
        });
    }
}
