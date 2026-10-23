using Microsoft.EntityFrameworkCore;
using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.WorkflowPlatform.Services;

public class WorkflowRuntimeService : IWorkflowRuntime, IWorkflowInstanceControl
{
    private readonly IWorkflowDbContext _db;
    private readonly IWorkflowBindingService _bindings;
    private readonly IWorkflowConditionEvaluator _conditions;
    private readonly IWorkflowAuthorizationHook? _authHook;
    private readonly IWorkflowEventSink _events;
    private readonly IWorkflowActionRegistry _registry;
    private readonly IWorkflowActorResolutionHook? _actorHook;
    private readonly IReadOnlyList<IWorkflowBusinessContextProvider> _providers;

    // پارامترهای اختیاری: new WorkflowRuntimeService(ctx) مثل قبل کار می‌کند (Phase 1 افزایشی است).
    public WorkflowRuntimeService(
        IWorkflowDbContext db,
        IWorkflowBindingService? bindings = null,
        IWorkflowConditionEvaluator? conditions = null,
        IWorkflowAuthorizationHook? authorizationHook = null,
        IWorkflowEventSink? eventSink = null,
        IWorkflowActionRegistry? actionRegistry = null,
        IWorkflowActorResolutionHook? actorResolutionHook = null,
        IEnumerable<IWorkflowBusinessContextProvider>? contextProviders = null)
    {
        _db = db;
        _bindings = bindings ?? new WorkflowBindingService(db);
        _conditions = conditions ?? new WorkflowConditionEvaluator();
        _authHook = authorizationHook;
        _events = eventSink ?? new NoOpWorkflowEventSink();
        _registry = actionRegistry ?? new WorkflowActionRegistry();
        _actorHook = actorResolutionHook;
        _providers = (contextProviders ?? Array.Empty<IWorkflowBusinessContextProvider>()).ToList();
    }

    internal static Dictionary<string, string?> Facts(WorkflowInstance i, WorkflowStage? stage) => new()
    {
        ["Priority"] = i.Priority, ["ProjectId"] = i.ProjectId?.ToString(), ["Module"] = i.Module,
        ["BusinessObjectType"] = i.BusinessObjectType, ["BusinessObjectId"] = i.BusinessObjectId.ToString(), ["StageName"] = stage?.Name
    };

    /// <summary>Factهای هسته + Factهای کسب‌وکاری ماژول (IWorkflowBusinessContextProvider).</summary>
    private async Task<Dictionary<string, string?>> BuildFactsAsync(WorkflowInstance i, WorkflowStage? stage)
    {
        var facts = Facts(i, stage);
        foreach (var p in _providers.Where(p => p.Supports(i.Module, i.BusinessObjectType)))
            foreach (var kv in await p.GetFactsAsync(i.Module, i.BusinessObjectType, i.BusinessObjectId))
                facts[kv.Key] = kv.Value;
        return facts;
    }

    /// <summary>
    /// انتخاب قطعی گذار: بر اساس Priority صعودی، اولین گذاری که شرطش (در صورت وجود) برقرار است.
    /// شرط خالی = همیشه برقرار (رفتار قبلی). شرط نامعتبر = Fail-closed.
    /// </summary>
    private async Task<WorkflowTransition?> SelectTransitionAsync(WorkflowInstance instance, int? fromStageId, string actionType, WorkflowStage? stage)
    {
        var candidates = await _db.WorkflowTransitions
            .Where(t => t.WorkflowVersionId == instance.WorkflowVersionId && t.FromStageId == fromStageId && t.ActionType == actionType)
            .OrderBy(t => t.Priority).ToListAsync();
        var facts = await BuildFactsAsync(instance, stage);
        return candidates.FirstOrDefault(t => _conditions.Evaluate(t.ConditionExpression, facts));
    }

    private async Task EmitAsync(string type, WorkflowInstance i, WorkflowStage? stage, string? action, int? actor)
    {
        try
        {
            await _events.OnEventAsync(new WorkflowRuntimeEvent(type, i.WorkflowInstanceId, i.Module, i.BusinessObjectType, i.BusinessObjectId,
                stage?.WorkflowStageId, stage?.Name, action, actor, stage?.NotificationKey));
        }
        catch { /* Extension point؛ خطای مصرف‌کننده نباید گردش‌کار را خراب کند */ }
    }

    private static IEnumerable<string> SplitRoles(string? responsibleRole)
    {
        return string.IsNullOrWhiteSpace(responsibleRole)
            ? Array.Empty<string>()
            : responsibleRole.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // ==============================================================================
    // باگ کشف‌شده حین طراحی: قبلاً برای مراحل Sequential هم به ازای هر نقش داخل
    // ResponsibleRole یک WorkflowInstanceStage جدا ساخته می‌شد - یعنی اگر ResponsibleRole
    // یک مرحله‌ی Sequential برابر "EngineeringSupervisor,EngineeringManager,SystemAdmin" بود
    // (یعنی "هرکدوم از این‌ها مجازند")، موتور اشتباهاً منتظر تایید هر سه نفر می‌ماند، چون
    // DetermineStageOutcome صرفاً از روی تعداد رکورد Parallel/Sequential را تشخیص می‌داد.
    // رفع شد: Sequential = فقط یک رکورد با لیست نقش‌های مجاز (رابطه‌ی OR)، Parallel = یک
    // رکورد جدا به ازای هر نقش (رابطه‌ی AND/طبق ApprovalStrategy).
    // ==============================================================================
    private static void CreateInstanceStages(
        IWorkflowDbContext db, WorkflowInstance instance, WorkflowStage stage, IReadOnlyList<string>? explicitParticipants = null)
    {
        // اگر لیست صریح اعضا داده شده (مثلاً IDC که دیسیپلین‌هایش پویاست)، همان استفاده
        // می‌شود - صرف‌نظر از StageType یا ResponsibleRole ایستای خود مرحله. هر عضو رکورد
        // مستقل خودش را می‌گیرد (رفتار Parallel واقعی).
        if (explicitParticipants != null)
        {
            foreach (var role in explicitParticipants.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                db.WorkflowInstanceStages.Add(new WorkflowInstanceStage
                {
                    Instance = instance,
                    WorkflowStageId = stage.WorkflowStageId,
                    AssignedToRole = role,
                    Status = "Pending",
                    AssignedDate = DateTime.Now
                });
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(stage.ResponsibleRole)) return;

        if (stage.StageType == "Parallel")
        {
            foreach (var role in SplitRoles(stage.ResponsibleRole))
            {
                db.WorkflowInstanceStages.Add(new WorkflowInstanceStage
                {
                    Instance = instance,
                    WorkflowStageId = stage.WorkflowStageId,
                    AssignedToRole = role,
                    Status = "Pending",
                    AssignedDate = DateTime.Now
                });
            }
        }
        else
        {
            db.WorkflowInstanceStages.Add(new WorkflowInstanceStage
            {
                Instance = instance,
                WorkflowStageId = stage.WorkflowStageId,
                AssignedToRole = stage.ResponsibleRole, // رشته‌ی کاما-جدا؛ هرکدام از این نقش‌ها مجاز به اقدام است
                Status = "Pending",
                AssignedDate = DateTime.Now
            });
        }
    }

    private static bool RoleMatches(string? assignedToRole, IReadOnlyList<string> actorRoles)
        => assignedToRole != null && SplitRoles(assignedToRole).Any(actorRoles.Contains);

    public async Task<bool> CancelInstanceAsync(int workflowInstanceId, int? actorEmployeeId, string? reason, CancellationToken ct = default)
    {
        var instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.WorkflowInstanceId == workflowInstanceId, ct);
        if (instance == null || instance.Status != "Running") return false;

        var pending = await _db.WorkflowInstanceStages.Where(x => x.WorkflowInstanceId == workflowInstanceId && x.Status == "Pending").ToListAsync(ct);
        foreach (var p in pending) { p.Status = "Cancelled"; p.Decision = "Cancel"; p.ActionDate = DateTime.Now; }

        var from = instance.CurrentStageId;
        instance.Status = "Cancelled";
        instance.CurrentStageId = null;
        instance.CompletionDate = DateTime.Now;

        _db.WorkflowHistories.Add(new WorkflowHistory
        {
            WorkflowInstanceId = workflowInstanceId, FromStageId = from, ToStageId = null, ActionType = "Cancel",
            Decision = "Cancel", Comment = reason, UserId = actorEmployeeId, ActionDate = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReopenInstanceAsync(int workflowInstanceId, int toStageId, int? actorEmployeeId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("بازگشایی گردش‌کار بدون دلیل مجاز نیست.");

        var instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.WorkflowInstanceId == workflowInstanceId, ct);
        if (instance == null || instance.Status != "Completed") return false;

        var stage = await _db.WorkflowStages.FirstOrDefaultAsync(s => s.WorkflowStageId == toStageId && s.WorkflowVersionId == instance.WorkflowVersionId, ct);
        if (stage == null) return false;

        instance.Status = "Running";
        instance.CompletionDate = null;
        instance.CurrentStageId = stage.WorkflowStageId;

        _db.WorkflowHistories.Add(new WorkflowHistory
        {
            Instance = instance, FromStageId = null, ToStageId = stage.WorkflowStageId, ActionType = "Reopen", Decision = "Reopen",
            Comment = reason, UserId = actorEmployeeId, ActionDate = DateTime.Now
        });

        await EnterStageAsync(instance, stage, null);
        await _db.SaveChangesAsync(ct);
        await EmitAsync("StageEntered", instance, stage, "Reopen", actorEmployeeId);
        return true;
    }

    public async Task<int> StartInstanceAsync(
        string module,
        string businessObjectType,
        int businessObjectId,
        string workflowCategory,
        int? projectId,
        int? initiatedByEmployeeId,
        IReadOnlyList<string>? firstStageParticipantRoles = null)
    {
        // 1) Binding صریح (اگر تعریف شده باشد)؛ ابهام = خطای قطعی.
        // 2) وگرنه مسیر Legacy (Module + Category)؛ اگر بیش از یک تعریف فعال منطبق باشد «اولین مورد» انتخاب
        //    نمی‌شود و خطای قطعی می‌دهد.
        var resolution = await _bindings.ResolveAsync(module, businessObjectType, workflowCategory);
        WorkflowDefinition definition;
        WorkflowVersion version;

        if (resolution != null)
        {
            definition = resolution.Definition;
            version = resolution.Version;

            if (await _db.WorkflowInstances.AnyAsync(i => i.Module == module && i.BusinessObjectType == businessObjectType
                                                          && i.BusinessObjectId == businessObjectId && i.Status == "Running"))
                throw new InvalidOperationException(
                    $"برای «{businessObjectType}» شماره‌ی {businessObjectId} از قبل یک گردش‌کار در حال اجرا وجود دارد.");
        }
        else
        {
            var candidates = await _db.WorkflowDefinitions
                .Where(d => d.Module == module && d.Category == workflowCategory && d.IsActive)
                .OrderBy(d => d.WorkflowDefinitionId).ToListAsync();

            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    $"هیچ تعریف گردش‌کار فعالی برای ماژول «{module}» و دسته‌ی «{workflowCategory}» یافت نشد.");
            if (candidates.Count > 1)
                throw new WorkflowBindingAmbiguousException(
                    $"بیش از یک تعریف فعال برای ماژول «{module}» و دسته‌ی «{workflowCategory}» وجود دارد؛ یک Binding صریح تعریف کنید.");

            definition = candidates[0];
            var legacyVersion = await _db.WorkflowVersions
                .Where(v => v.WorkflowDefinitionId == definition.WorkflowDefinitionId && v.Status == "Published")
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();

            if (legacyVersion == null)
                throw new InvalidOperationException($"هیچ نسخه‌ی منتشرشده‌ای برای گردش‌کار «{definition.Name}» یافت نشد.");
            version = legacyVersion;
        }

        // گذار شروع: FromStageId == null
        var startTransition = await _db.WorkflowTransitions
            .Where(t => t.WorkflowVersionId == version.WorkflowVersionId && t.FromStageId == null)
            .OrderBy(t => t.Priority)
            .FirstOrDefaultAsync();

        if (resolution != null
            && await _db.WorkflowTransitions.CountAsync(t => t.WorkflowVersionId == version.WorkflowVersionId && t.FromStageId == null) > 1)
            throw new InvalidOperationException("این نسخه بیش از یک گذار شروع دارد (نقطه‌ی شروع مبهم).");

        if (startTransition?.ToStageId == null)
            throw new InvalidOperationException("گذار شروع (Start Transition) برای این نسخه‌ی گردش‌کار تعریف نشده است.");

        var firstStage = await _db.WorkflowStages
            .FirstOrDefaultAsync(s => s.WorkflowStageId == startTransition.ToStageId);

        if (firstStage == null)
            throw new InvalidOperationException("مرحله‌ی اول گردش‌کار یافت نشد.");

        var instance = new WorkflowInstance
        {
            WorkflowVersionId = version.WorkflowVersionId,
            Module = module,
            BusinessObjectType = businessObjectType,
            BusinessObjectId = businessObjectId,
            ProjectId = projectId,
            CurrentStageId = firstStage.WorkflowStageId,
            Status = "Running",
            StartDate = DateTime.Now,
            InitiatedById = initiatedByEmployeeId,
            WorkflowBindingId = resolution?.Binding.WorkflowBindingId,
            EntityKey = $"{module}|{businessObjectType}|{businessObjectId}"
        };

        _db.WorkflowInstances.Add(instance);

        _db.WorkflowHistories.Add(new WorkflowHistory
        {
            Instance = instance,
            FromStageId = null,
            ToStageId = firstStage.WorkflowStageId,
            ActionType = "Start",
            UserId = initiatedByEmployeeId,
            ActionDate = DateTime.Now
        });

        await EnterStageAsync(instance, firstStage, firstStageParticipantRoles);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (resolution != null)
        {
            // شروع هم‌زمان دوم برای همان موجودیت: Index یکتای Instance های Running (Binding-started) رد کرده است.
            // بدون وابستگی به Provider، با بررسی مجدد به یک خطای قطعی و قابل‌فهم تبدیل می‌شود؛ خطای دیگر همان‌طور Throw می‌شود.
            var key = $"{module}|{businessObjectType}|{businessObjectId}";
            if (await _db.WorkflowInstances.AsNoTracking().AnyAsync(i => i.EntityKey == key && i.Status == "Running" && i.WorkflowBindingId != null))
                throw new WorkflowConcurrencyConflictException(
                    $"برای «{businessObjectType}» شماره‌ی {businessObjectId} هم‌زمان یک گردش‌کار در حال اجرا ایجاد شد.");
            throw;
        }

        await EmitAsync("StageEntered", instance, firstStage, "Start", initiatedByEmployeeId);
        return instance.WorkflowInstanceId;
    }


    public async Task ExecuteActionAsync(
        int workflowInstanceId,
        string actionType,
        int? actorEmployeeId,
        IReadOnlyList<string> actorRoles,
        string? comment,
        string? ipAddress,
        string? userAgent,
        IReadOnlyList<string>? nextStageParticipantRoles = null)
    {
        var instance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(x => x.WorkflowInstanceId == workflowInstanceId);

        if (instance == null)
            throw new InvalidOperationException("Workflow instance یافت نشد.");

        if (instance.Status != "Running")
            throw new InvalidOperationException($"این گردش‌کار در وضعیت «{instance.Status}» است و اقدام جدید نمی‌پذیرد.");

        if (instance.CurrentStageId == null)
            throw new InvalidOperationException("این گردش‌کار مرحله‌ی جاری معتبری ندارد.");

        var currentStage = await _db.WorkflowStages
            .FirstOrDefaultAsync(s => s.WorkflowStageId == instance.CurrentStageId);

        if (currentStage == null)
            throw new InvalidOperationException("مرحله‌ی جاری یافت نشد.");

        // ==============================================================================
        // پیدا کردن دقیق رکورد InstanceStage متعلق به این کاربر برای این مرحله - این همان
        // مکانیزمی است که از Race Condition در بازبینی موازی جلوگیری می‌کند: هر نقش رکورد
        // مستقل خودش را دارد و فقط همان رکورد به‌روزرسانی می‌شود.
        // ==============================================================================
        var pendingForStage = await _db.WorkflowInstanceStages
            .Include(x => x.Candidates)
            .Where(x => x.WorkflowInstanceId == workflowInstanceId
                        && x.WorkflowStageId == currentStage.WorkflowStageId
                        && x.Status == "Pending")
            .ToListAsync();

        WorkflowInstanceStage? myInstanceStage;
        if (!string.IsNullOrWhiteSpace(currentStage.ResponsibilityKey))
        {
            // مرحله‌ی مبتنی بر مسئولیت: مجوز اقدام فقط از Snapshot کاندیدها (نه نقش، نه داده‌ی زنده‌ی سازمان) و بدون Bypass.
            if (!actorEmployeeId.HasValue)
                throw new UnauthorizedAccessException("برای اقدام روی این مرحله باید کارمند شناخته‌شده باشید.");
            if (actorEmployeeId == instance.InitiatedById)
                throw new UnauthorizedAccessException("تفکیک وظایف: آغازکننده‌ی گردش‌کار نمی‌تواند روی مراحل تأیید آن اقدام کند.");
            myInstanceStage = pendingForStage.FirstOrDefault(x => x.Candidates.Any(c => c.IsSelected && c.EmployeeId == actorEmployeeId.Value));
        }
        else
            myInstanceStage = pendingForStage.FirstOrDefault(x => RoleMatches(x.AssignedToRole, actorRoles));

        if (myInstanceStage == null)
            throw new UnauthorizedAccessException(
                "شما نقش لازم برای اقدام روی مرحله‌ی جاری این گردش‌کار را ندارید، یا قبلاً اقدام کرده‌اید.");

        // --- Phase 1: مسیر Binding فقط اکشن‌های ثبت‌شده را می‌پذیرد؛ Legacy بدون تغییر ---
        if (instance.WorkflowBindingId != null && _registry.Find(actionType, instance.Module) == null)
            throw new InvalidOperationException($"اکشن «{actionType}» در رجیستری اکشن‌ها ثبت نشده است.");

        // --- Phase 1: SoD و مجوز PEMS روی گذارهایی که RequiredPermission/ForbidInitiator دارند ---
        var guardTransition = await SelectTransitionAsync(instance, currentStage.WorkflowStageId, actionType, currentStage);
        if (guardTransition != null)
        {
            if (guardTransition.ForbidInitiator && actorEmployeeId.HasValue && actorEmployeeId == instance.InitiatedById)
                throw new UnauthorizedAccessException("تفکیک وظایف: آغازکننده‌ی گردش‌کار نمی‌تواند این اقدام را انجام دهد.");

            if (!string.IsNullOrWhiteSpace(guardTransition.RequiredPermission))
            {
                if (_authHook == null)
                    throw new UnauthorizedAccessException("این گذار مجوز می‌خواهد ولی هوک مجوز پیکربندی نشده است (Fail-closed).");
                var decision = await _authHook.AuthorizeAsync(new WorkflowAuthorizationContext(
                    instance, currentStage, guardTransition, actionType, actorEmployeeId, actorRoles));
                if (!decision.Allowed)
                    throw new UnauthorizedAccessException(decision.Reason ?? "مجوز اجرای این اقدام وجود ندارد.");
            }
        }

        myInstanceStage.Status = actionType;
        myInstanceStage.Decision = actionType;
        myInstanceStage.Comment = comment;
        myInstanceStage.ActionDate = DateTime.Now;

        var stageOutcome = DetermineStageOutcome(currentStage, await _db.WorkflowInstanceStages
            .Where(x => x.WorkflowInstanceId == workflowInstanceId && x.WorkflowStageId == currentStage.WorkflowStageId)
            .ToListAsync());

        // اگر مرحله هنوز کامل نشده (منتظر بقیه‌ی بازبینان موازی) - فقط اقدام همین کاربر ثبت
        // می‌شود، بدون گذار به مرحله‌ی بعد
        if (stageOutcome == null)
        {
            _db.WorkflowHistories.Add(new WorkflowHistory
            {
                Instance = instance,
                FromStageId = currentStage.WorkflowStageId,
                ToStageId = currentStage.WorkflowStageId,
                ActionType = actionType,
                Decision = actionType,
                Comment = comment,
                UserId = actorEmployeeId,
                Role = string.Join(",", actorRoles),
                ActionDate = DateTime.Now,
                IPAddress = ipAddress,
                Browser = userAgent
            });

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "این آیتم هم‌زمان توسط کاربر دیگری تغییر داده شد. صفحه را دوباره بارگذاری کن و دوباره امتحان کن.");
            }
            return;
        }

        // مرحله کامل شده - گذار بعدی را طبق WorkflowTransition پیدا کن
        var transition = await SelectTransitionAsync(instance, currentStage.WorkflowStageId, stageOutcome, currentStage);

        if (transition == null)
            throw new InvalidOperationException(
                $"برای مرحله‌ی «{currentStage.Name}» با نتیجه‌ی «{stageOutcome}» هیچ گذاری تعریف نشده است.");

        var previousStageId = currentStage.WorkflowStageId;

        if (transition.ToStageId == null)
        {
            // پایان گردش‌کار
            instance.CurrentStageId = null;
            instance.Status = stageOutcome == "Reject" ? "Rejected" : stageOutcome == "Cancel" ? "Cancelled" : stageOutcome == "Return" ? "Returned" : "Completed";
            instance.CompletionDate = DateTime.Now;
        }
        else
        {
            var nextStage = await _db.WorkflowStages
                .FirstOrDefaultAsync(s => s.WorkflowStageId == transition.ToStageId);

            if (nextStage == null)
                throw new InvalidOperationException("مرحله‌ی بعدی گردش‌کار یافت نشد.");

            instance.CurrentStageId = nextStage.WorkflowStageId;

            await EnterStageAsync(instance, nextStage, nextStageParticipantRoles);
        }

        _db.WorkflowHistories.Add(new WorkflowHistory
        {
            Instance = instance,
            FromStageId = previousStageId,
            ToStageId = instance.CurrentStageId,
            ActionType = actionType,
            Decision = stageOutcome,
            Comment = comment,
            UserId = actorEmployeeId,
            Role = string.Join(",", actorRoles),
            ActionDate = DateTime.Now,
            IPAddress = ipAddress,
            Browser = userAgent
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "این آیتم هم‌زمان توسط کاربر دیگری تغییر داده شد. صفحه را دوباره بارگذاری کن و دوباره امتحان کن.");
        }

        await EmitAsync("ActionExecuted", instance, currentStage, actionType, actorEmployeeId);
        if (instance.Status == "Completed") await EmitAsync("InstanceCompleted", instance, currentStage, actionType, actorEmployeeId);
        else if (instance.Status == "Rejected") await EmitAsync("InstanceRejected", instance, currentStage, actionType, actorEmployeeId);
        else if (instance.CurrentStageId != previousStageId)
            await EmitAsync("StageEntered", instance, await _db.WorkflowStages.FirstOrDefaultAsync(x => x.WorkflowStageId == instance.CurrentStageId), actionType, actorEmployeeId);
    }


    // ==============================================================================
    // ورود به مرحله. مراحل بدون ResponsibilityKey دقیقاً مثل قبل (نقش/شرکت‌کننده‌ی صریح) عمل می‌کنند.
    // مراحل مبتنی بر مسئولیت: ۱) شرط ورود (EntryCondition) ۲) حل بازیگران از طریق IWorkflowActorResolutionHook
    // ۳) Snapshot کاندیدها. نتیجه‌ی Skipped یا شرط نادرست ⇒ Skip خودکار (زنجیره‌ای تا مرحله‌ی بعدیِ قابل‌اجرا).
    // نتیجه‌ی Failed ⇒ WorkflowActorResolutionException و هیچ تغییری ذخیره نمی‌شود (Fail-closed).
    // ==============================================================================
    private async Task EnterStageAsync(WorkflowInstance instance, WorkflowStage stage, IReadOnlyList<string>? explicitParticipants)
    {
        for (var guard = 0; guard < 100; guard++)
        {
            if (string.IsNullOrWhiteSpace(stage.ResponsibilityKey) || explicitParticipants != null)
            {
                CreateInstanceStages(_db, instance, stage, explicitParticipants);
                await TryAutoSkipEmptyStageAsync(instance, stage, explicitParticipants);
                return;
            }

            if (_actorHook == null)
                throw new InvalidOperationException(
                    $"مرحله‌ی «{stage.Name}» مبتنی بر مسئولیت است ولی Actor Resolver پیکربندی نشده است (Fail-closed).");

            var facts = await BuildFactsAsync(instance, stage);
            string? skipReason = null;

            if (!string.IsNullOrWhiteSpace(stage.EntryCondition) && !_conditions.Evaluate(stage.EntryCondition, facts))
                skipReason = "EntryCondition";
            else
            {
                var outcome = await _actorHook.ResolveAsync(new WorkflowActorResolutionContext(instance, stage, facts, instance.InitiatedById));
                if (outcome.Kind == WorkflowActorResolutionKind.Failed)
                    throw new WorkflowActorResolutionException(outcome.State, stage.Name, outcome.DetailsJson);

                if (outcome.Kind == WorkflowActorResolutionKind.Skipped)
                    skipReason = outcome.SkipReason ?? "NotApplicable";
                else
                {
                    var selected = outcome.Candidates.Where(c => c.IsSelected).ToList();
                    var instStage = new WorkflowInstanceStage
                    {
                        Instance = instance,
                        WorkflowStageId = stage.WorkflowStageId,
                        AssignedToEmployeeId = selected.Count == 1 ? selected[0].EmployeeId : null,
                        Status = "Pending",
                        AssignedDate = DateTime.Now,
                        ResolutionState = outcome.State,
                        ResolutionDetails = outcome.DetailsJson,
                        ResolvedDate = DateTime.Now
                    };
                    foreach (var c in outcome.Candidates)
                        instStage.Candidates.Add(new WorkflowInstanceStageCandidate { EmployeeId = c.EmployeeId, IsSelected = c.IsSelected, Reason = c.Reason });
                    _db.WorkflowInstanceStages.Add(instStage);
                    return;
                }
            }

            // ---- Skip خودکار
            var skipTransition = await SelectTransitionAsync(instance, stage.WorkflowStageId, "Approve", stage);
            _db.WorkflowHistories.Add(new WorkflowHistory
            {
                Instance = instance,
                FromStageId = stage.WorkflowStageId,
                ToStageId = skipTransition?.ToStageId,
                ActionType = "AutoSkip",
                Comment = skipReason,
                ActionDate = DateTime.Now
            });

            if (skipTransition?.ToStageId == null)
            {
                instance.CurrentStageId = null;
                instance.Status = "Completed";
                instance.CompletionDate = DateTime.Now;
                return;
            }

            var next = await _db.WorkflowStages.FirstOrDefaultAsync(s => s.WorkflowStageId == skipTransition.ToStageId);
            if (next == null) throw new InvalidOperationException("مرحله‌ی بعدی گردش‌کار یافت نشد.");
            instance.CurrentStageId = next.WorkflowStageId;
            stage = next;
            explicitParticipants = null;
        }

        throw new InvalidOperationException("حلقه‌ی Skip خودکار مراحل گردش‌کار از حد مجاز گذشت.");
    }


    // ==============================================================================
    // اگر یک مرحله (چه اولین مرحله‌ی گردش‌کار، چه مرحله‌ای که از یک گذار به آن رسیده‌ایم)
    // قرار بود اعضای پویا داشته باشد ولی هیچ عضوی تعیین نشد (یعنی تشخیص داده شد نیازی به
    // بازبینی نیست)، هیچ‌کس هرگز نمی‌تواند روی آن اقدام کند و Instance برای همیشه گیر
    // می‌کند. این متد فوری و خودکار از چنین مرحله‌ای عبور می‌کند (طبق گذار با ActionType
    // "Approve" از همان مرحله).
    // ==============================================================================
    private async Task TryAutoSkipEmptyStageAsync(
        WorkflowInstance instance, WorkflowStage stage, IReadOnlyList<string>? explicitParticipants)
    {
        var hasAnyParticipant = explicitParticipants != null
            ? explicitParticipants.Any(r => !string.IsNullOrWhiteSpace(r))
            : !string.IsNullOrWhiteSpace(stage.ResponsibleRole);

        if (hasAnyParticipant) return;

        var skipTransition = await _db.WorkflowTransitions
            .Where(t => t.WorkflowVersionId == instance.WorkflowVersionId
                        && t.FromStageId == stage.WorkflowStageId
                        && t.ActionType == "Approve")
            .OrderBy(t => t.Priority)
            .FirstOrDefaultAsync();

        _db.WorkflowHistories.Add(new WorkflowHistory
        {
            Instance = instance,
            FromStageId = stage.WorkflowStageId,
            ToStageId = skipTransition?.ToStageId,
            ActionType = "AutoSkip",
            Comment = "این مرحله نیازی به بازبینی تشخیص داده نشد و به‌صورت خودکار رد شد.",
            ActionDate = DateTime.Now
        });

        if (skipTransition?.ToStageId == null)
        {
            instance.CurrentStageId = null;
            instance.Status = "Completed";
            instance.CompletionDate = DateTime.Now;
        }
        else
        {
            var afterSkipStage = await _db.WorkflowStages
                .FirstOrDefaultAsync(s => s.WorkflowStageId == skipTransition.ToStageId);

            if (afterSkipStage != null)
            {
                instance.CurrentStageId = afterSkipStage.WorkflowStageId;
                CreateInstanceStages(_db, instance, afterSkipStage);

                // ==============================================================================
                // اگر بعد از Skip، مرحله‌ی بعدی هم بدون عضو باشد (زنجیره‌ی چندتایی) - عمداً اینجا
                // متوقف می‌شویم (فقط یک سطح Skip خودکار). طبق محدودیت شناخته‌شده‌ی فعلی، فقط یک
                // مرحله‌ی Parallel پویا در هر گردش‌کار پشتیبانی می‌شود (IDC یا DisciplineReview) -
                // زنجیره‌ی چندسطحی نیاز به بازطراحی این متد به شکل بازگشتی دارد که فعلاً لازم نیست.
                // ==============================================================================
            }
        }
    }


    /// <summary>
    /// طبق ApprovalStrategy مرحله، مشخص می‌کند آیا مرحله کامل شده یا نه، و اگر شده، نتیجه‌ی
    /// نهایی‌اش (Approve/Reject) چیست. اگر هنوز کامل نشده null برمی‌گرداند.
    /// </summary>
    private static string? DetermineStageOutcome(WorkflowStage stage, List<WorkflowInstanceStage> allInstanceStages)
    {
        var pendingCount = allInstanceStages.Count(x => x.Status == "Pending");

        if (stage.StageType != "Parallel" || allInstanceStages.Count <= 1)
        {
            if (pendingCount > 0) return null;

            // فقط یک بازبین دارد - نتیجه‌ی مرحله دقیقاً همان اقدام واقعی اوست (Approve/Reject/
            // Skip/Return/...)، نه صرفاً فرض دوتایی Approve-یا-Reject
            return allInstanceStages.OrderByDescending(x => x.ActionDate).First().Status;
        }

        // رفع باگ بحرانی: قبلاً Comment هم مثل Reject حساب می‌شد و کل مرحله‌ی موازی رو (حتی
        // با یک نظر غیرمسدودکننده از یک عضو) رد می‌کرد. حالا فقط Reject واقعی مسدودکننده‌ست؛
        // Comment مثل تایید نرم عمل می‌کنه (پیشروی می‌ده) و خودِ متن نظر در WorkflowHistory
        // برای تصمیم‌گیرنده‌ی بعدی حفظ می‌شه.
        var rejectedCount = allInstanceStages.Count(x => x.Status == "Reject");
        var approvedCount = allInstanceStages.Count(x => x.Status == "Approve" || x.Status == "Comment");

        switch (stage.ApprovalStrategy)
        {
            case "FirstResponse":
                return rejectedCount > 0 ? "Reject" : (approvedCount > 0 ? "Approve" : null);

            case "MinimumCount":
                var minRequired = stage.MinimumApprovalCount ?? allInstanceStages.Count;
                if (rejectedCount > 0) return "Reject";
                return approvedCount >= minRequired ? "Approve" : (pendingCount == 0 ? "Reject" : null);

            case "AllApprovalsRequired":
            case "MandatoryRolesOnly": // ساده‌سازی فاز A: فعلاً معادل AllApprovalsRequired
            default:
                if (pendingCount > 0) return null;
                return rejectedCount > 0 ? "Reject" : "Approve";
        }
    }


    public async Task<WorkflowInstanceDto?> GetInstanceAsync(string businessObjectType, int businessObjectId)
    {
        var instance = await _db.WorkflowInstances
            .Where(x => x.BusinessObjectType == businessObjectType && x.BusinessObjectId == businessObjectId)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

        return instance == null ? null : await MapInstanceAsync(instance);
    }

    public async Task<WorkflowInstanceDto?> GetInstanceByIdAsync(int workflowInstanceId)
    {
        var instance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(x => x.WorkflowInstanceId == workflowInstanceId);

        return instance == null ? null : await MapInstanceAsync(instance);
    }

    private async Task<WorkflowInstanceDto> MapInstanceAsync(WorkflowInstance instance)
    {
        var currentStageName = instance.CurrentStageId == null
            ? null
            : await _db.WorkflowStages
                .Where(s => s.WorkflowStageId == instance.CurrentStageId)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();

        var pendingStages = await _db.WorkflowInstanceStages
            .Where(x => x.WorkflowInstanceId == instance.WorkflowInstanceId && x.Status == "Pending")
            .Select(x => new WorkflowInstanceStageDto
            {
                WorkflowInstanceStageId = x.WorkflowInstanceStageId,
                AssignedToEmployeeId = x.AssignedToEmployeeId,
                AssignedToRole = x.AssignedToRole,
                Status = x.Status,
                StageName = currentStageName ?? "",
                AssignedDate = x.AssignedDate
            })
            .ToListAsync();

        return new WorkflowInstanceDto
        {
            WorkflowInstanceId = instance.WorkflowInstanceId,
            Module = instance.Module,
            BusinessObjectType = instance.BusinessObjectType,
            BusinessObjectId = instance.BusinessObjectId,
            Status = instance.Status,
            CurrentStageId = instance.CurrentStageId,
            CurrentStageName = currentStageName,
            StartDate = instance.StartDate,
            CompletionDate = instance.CompletionDate,
            PendingStages = pendingStages
        };
    }


    public async Task<IReadOnlyList<WorkflowHistoryDto>> GetHistoryAsync(int workflowInstanceId)
    {
        var stageNames = await _db.WorkflowStages.ToDictionaryAsync(s => s.WorkflowStageId, s => s.Name);

        var history = await _db.WorkflowHistories
            .Where(x => x.WorkflowInstanceId == workflowInstanceId)
            .OrderBy(x => x.ActionDate)
            .ToListAsync();

        return history.Select(h => new WorkflowHistoryDto
        {
            FromStageName = h.FromStageId.HasValue ? stageNames.GetValueOrDefault(h.FromStageId.Value) : null,
            ToStageName = h.ToStageId.HasValue ? stageNames.GetValueOrDefault(h.ToStageId.Value) : null,
            ActionType = h.ActionType,
            Decision = h.Decision,
            Comment = h.Comment,
            UserId = h.UserId,
            Role = h.Role,
            ActionDate = h.ActionDate,
            IPAddress = h.IPAddress
        }).ToList();
    }


    /// <summary>
    /// کارتابل: مراحل Pending که کاربر مجاز به اقدام روی آن‌هاست. دو منبع (دقیقاً هم‌قاعده با ExecuteActionAsync):
    /// ۱) مراحل مبتنی بر نقش (AssignedToRole) ۲) مراحل مبتنی بر مسئولیت (فقط Snapshot کاندیدهای انتخاب‌شده، بدون آغازکننده).
    /// شاخه‌ی نقش قبلاً «همه‌ی» مراحل Pending سیستم را در حافظه می‌آورد و بعد فیلتر می‌کرد؛ حالا یک پیش‌فیلتر SQL
    /// (شامل بودن نام نقش در رشته‌ی CSV) تعداد ردیف‌ها را کم می‌کند و تطبیق دقیق همچنان در حافظه (RoleMatches) انجام می‌شود،
    /// پس نتیجه عیناً همان است.
    /// </summary>
    public async Task<IReadOnlyList<PendingTaskDto>> GetPendingTasksAsync(int employeeId, IReadOnlyList<string> actorRoles)
    {
        var now = DateTime.Now;
        var byRole = new List<PendingTaskDto>();
        var roles = (actorRoles ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();

        if (roles.Count > 0)
        {
            var roleFilter = AssignedToRoleContainsAny(roles);
            var candidates = await (
                from instStage in _db.WorkflowInstanceStages.Where(roleFilter)
                join instance in _db.WorkflowInstances on instStage.WorkflowInstanceId equals instance.WorkflowInstanceId
                join stage in _db.WorkflowStages on instStage.WorkflowStageId equals stage.WorkflowStageId
                where instStage.Status == "Pending" && instance.Status == "Running"
                select new
                {
                    instStage.AssignedToRole,
                    Task = new PendingTaskDto
                    {
                        WorkflowInstanceId = instance.WorkflowInstanceId,
                        WorkflowInstanceStageId = instStage.WorkflowInstanceStageId,
                        Module = instance.Module,
                        BusinessObjectType = instance.BusinessObjectType,
                        BusinessObjectId = instance.BusinessObjectId,
                        StageName = stage.Name,
                        StageNameFa = stage.NameFa,
                        AssignedDate = instStage.AssignedDate,
                        TimeoutHours = stage.TimeoutHours
                    }
                }
            ).ToListAsync();

            byRole = candidates.Where(x => RoleMatches(x.AssignedToRole, roles)).Select(x => x.Task).ToList();
        }

        // مراحل مبتنی بر مسئولیت: فقط کاندیدهای Snapshot شده
        var byCandidate = await (
            from cand in _db.WorkflowInstanceStageCandidates
            join instStage in _db.WorkflowInstanceStages on cand.WorkflowInstanceStageId equals instStage.WorkflowInstanceStageId
            join instance in _db.WorkflowInstances on instStage.WorkflowInstanceId equals instance.WorkflowInstanceId
            join stage in _db.WorkflowStages on instStage.WorkflowStageId equals stage.WorkflowStageId
            where cand.EmployeeId == employeeId && cand.IsSelected && instStage.Status == "Pending" && instance.Status == "Running"
                  && instance.InitiatedById != employeeId
            select new PendingTaskDto
            {
                WorkflowInstanceId = instance.WorkflowInstanceId,
                WorkflowInstanceStageId = instStage.WorkflowInstanceStageId,
                Module = instance.Module,
                BusinessObjectType = instance.BusinessObjectType,
                BusinessObjectId = instance.BusinessObjectId,
                StageName = stage.Name,
                StageNameFa = stage.NameFa,
                AssignedDate = instStage.AssignedDate,
                TimeoutHours = stage.TimeoutHours,
                IsPersonal = true
            }).ToListAsync();

        var all = byRole.Concat(byCandidate).ToList();
        foreach (var t in all)
        {
            if (!t.TimeoutHours.HasValue) continue;
            t.DueDate = t.AssignedDate.AddHours(t.TimeoutHours.Value);
            t.IsOverdue = t.DueDate.Value < now;
        }
        return all;
    }

    // (x.AssignedToRole != null && (x.AssignedToRole.Contains(r1) || x.AssignedToRole.Contains(r2) ...)) - فقط پیش‌فیلتر؛ تطبیق دقیق: RoleMatches
    private static System.Linq.Expressions.Expression<Func<WorkflowInstanceStage, bool>> AssignedToRoleContainsAny(IReadOnlyList<string> roles)
    {
        var x = System.Linq.Expressions.Expression.Parameter(typeof(WorkflowInstanceStage), "x");
        var prop = System.Linq.Expressions.Expression.Property(x, nameof(WorkflowInstanceStage.AssignedToRole));
        var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
        System.Linq.Expressions.Expression? any = null;
        foreach (var r in roles)
        {
            var call = System.Linq.Expressions.Expression.Call(prop, contains, System.Linq.Expressions.Expression.Constant(r));
            any = any == null ? call : System.Linq.Expressions.Expression.OrElse(any, call);
        }
        var notNull = System.Linq.Expressions.Expression.NotEqual(prop, System.Linq.Expressions.Expression.Constant(null, typeof(string)));
        return System.Linq.Expressions.Expression.Lambda<Func<WorkflowInstanceStage, bool>>(
            System.Linq.Expressions.Expression.AndAlso(notNull, any!), x);
    }
}
