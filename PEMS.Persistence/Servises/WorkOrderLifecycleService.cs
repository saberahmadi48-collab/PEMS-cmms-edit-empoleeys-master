using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;
using static PEMS.Persistence.Workflow.WorkOrderWorkflow;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق PEMS_CMMS_ARCHITECTURE.md بخش ۸، ۱۰، ۱۲: منطق جابجایی وضعیت WorkOrder که قبلاً مستقیم
/// در Controller بود، حالا این‌جاست تا Controller نازک بماند (طبق CLAUDE.md).
/// </summary>
public class WorkOrderLifecycleService : IWorkOrderLifecycleService
{
    private readonly PEMSDbContext _context;
    private readonly IWorkOrderReleaseGateService _releaseGate;

    // طبق CMMS Production Baseline v1 بند ۹.۳ («State Machine Rule» - Do not rely only on
    // Controller logic): قبلاً هیچ اعتبارسنجی‌ای این‌جا نبود؛ AdvanceStatusAsync هر nextStatus
    // ای رو بدون چک اعمال می‌کرد و تنها سد واقعی، GetNextStatus داخل Controller بود. حالا خودِ
    // Service هم گذار غیرمجاز رو رد می‌کنه. OnHold/Cancelled/Rejected طبق بند ۹.۲
    // ("optional controlled states") اضافه شدن.
    private static readonly Dictionary<string, string[]> LegalTransitions = new()
    {
        ["Open"] = new[] { "Assigned", "Cancelled", "Rejected" },
        ["Assigned"] = new[] { "InProgress", "OnHold", "Cancelled" },
        ["InProgress"] = new[] { "Completed", "OnHold", "Cancelled" },
        ["OnHold"] = new[] { "InProgress", "Cancelled" },
        ["Completed"] = new[] { "Closed" },
    };

    // Phase 3B: Runtime/Read-model با هوک مخصوص Work Order (سازمان + دیسیپلین + SoD) ساخته می‌شوند؛ سازنده‌ی دو-پارامتری قبلی (تست‌ها) کار می‌کند.
    private readonly IAccessControlService _access;
    private readonly WorkflowBindingService _bindings;
    private readonly WorkflowRuntimeService _runtime;
    private readonly WorkflowReadModelService _readModel;

    public WorkOrderLifecycleService(PEMSDbContext context, IWorkOrderReleaseGateService releaseGate, IAccessControlService? accessControl = null)
    {
        _context = context;
        _releaseGate = releaseGate;
        _access = accessControl ?? new AccessControlService(context);
        var hook = new WorkOrderAuthorizationHook(context, _access);
        var registry = WorkOrderWorkflow.CreateRegistry();
        _bindings = new WorkflowBindingService(context);
        _runtime = new WorkflowRuntimeService(context, _bindings, null, hook, null, registry);
        _readModel = new WorkflowReadModelService(context, null, hook, registry);
    }

    // ==============================================================================
    // Phase 3B - چرخه‌عمر حاکمیتی. تقسیم مسئولیت:
    //   * Workflow Platform: مراحل/گذارها/شرکت‌کنندگان/مجوز گذار/تاریخچه/نسخه/همزمانی + (هوک) سازمان، دیسیپلین، SoD.
    //   * این سرویس: قواعد دامنه‌ی Work Order (Release Gate، ISO 14224، FailureRecord، MaintenanceHistory، بازبینی چند-دیسیپلینه)،
    //     Snapshot مبدأ، نگاشت مرحله → WorkOrder.Status، ممیزی کسب‌وکاری (WorkOrderStatusHistory).
    // ==============================================================================
    private static readonly string[] ValidTypes = { "Preventive", "Corrective", "Emergency", "Predictive" };
    private static readonly string[] ValidPriorities = { "Low", "Medium", "High", "Critical" };

    private async Task<WorkflowInstance?> LifecycleInstanceAsync(int workOrderId)
    {
        var all = await _readModel.FindInstancesForEntityAsync(Module, LifecycleEntityType, workOrderId);
        if (all.Count > 1)
            throw new InvalidOperationException(T($"برای Work Order {workOrderId} بیش از یک Instance چرخه‌عمر ثبت شده است؛ رکورد ناسازگار است.",
                $"Work Order {workOrderId} has more than one lifecycle instance; the record is inconsistent."));
        return all.FirstOrDefault();
    }

    /// <summary>نتیجه‌ی تطبیق حالت/علت خرابی با کاتالوگ (Error != null یعنی رد). با کاتالوگ خالی، متن آزاد مجاز می‌ماند (سازگاری با محیط‌های بدون Seed).</summary>
    private sealed record FailureCatalogMatch(string? ModeCode, int? ModeId, string? CauseCode, int? CauseId, string? Error);

    private async Task<FailureCatalogMatch> ResolveFailureCatalogAsync(string? modeCode, string? causeCode)
    {
        string? mode = modeCode?.Trim(), cause = causeCode?.Trim();
        int? modeId = null, causeId = null;
        string? error = null;

        if (await _context.FailureModes.AnyAsync(f => f.IsActive) && !string.IsNullOrWhiteSpace(mode))
        {
            var m = await _context.FailureModes.Where(f => f.IsActive && (f.Code.ToLower() == mode.ToLower() || f.Name.ToLower() == mode.ToLower()))
                .Select(f => new { f.FailureModeId, f.Code }).FirstOrDefaultAsync();
            if (m == null) error = T($"حالت خرابی «{mode}» در کاتالوگ ISO 14224 وجود ندارد؛ از فهرست انتخاب کنید.", $"Failure mode '{mode}' is not in the ISO 14224 catalog; pick one from the list.");
            else { modeId = m.FailureModeId; mode = m.Code; }
        }
        if (error == null && !string.IsNullOrWhiteSpace(cause) && await _context.FailureCauses.AnyAsync(f => f.IsActive))
        {
            var c = await _context.FailureCauses.Where(f => f.IsActive && (f.Code.ToLower() == cause.ToLower() || f.Name.ToLower() == cause.ToLower()))
                .Select(f => new { f.FailureCauseId, f.Code }).FirstOrDefaultAsync();
            if (c == null) error = T($"علت خرابی «{cause}» در کاتالوگ ISO 14224 وجود ندارد؛ از فهرست انتخاب کنید.", $"Failure cause '{cause}' is not in the ISO 14224 catalog; pick one from the list.");
            else { causeId = c.FailureCauseId; cause = c.Code; }
        }
        return new FailureCatalogMatch(mode, modeId, cause, causeId, error);
    }

    /// <summary>B11: نسخه‌ی بدون استثنا؛ رکورد ناسازگار (چند Instance) به‌جای 500 بی‌صدا، پیام مشخص برمی‌گرداند.</summary>
    private async Task<(WorkflowInstance? Instance, string? Error)> TryLifecycleInstanceAsync(int workOrderId)
    {
        try { return (await LifecycleInstanceAsync(workOrderId), null); }
        catch (InvalidOperationException ex) { return (null, ex.Message); }
    }

    public async Task<bool> IsGovernedAsync(int workOrderId) => await LifecycleInstanceAsync(workOrderId) != null;

    /// <summary>زمان فعال‌شدن گردش‌کار (اولین Binding چرخه‌عمر). Work Order های ساخته‌شده قبل از آن Legacy هستند؛ null = هنوز فعال نشده.</summary>
    private async Task<DateTime?> GovernanceCutoffAsync()
        => await _context.WorkflowBindings.Where(b => b.Module == Module && b.EntityType == LifecycleEntityType && b.Context == LifecycleContext)
            .MinAsync(b => (DateTime?)b.CreatedDate);

    public async Task<(bool Success, string? Error, int Count)> GovernPendingNewAsync(WorkOrderActor actor)
    {
        var cutoff = await GovernanceCutoffAsync();
        if (cutoff == null) return (true, null, 0);

        var pending = await _context.WorkOrders.Where(w => w.OriginatingOrganization == null && w.CreatedDate >= cutoff && w.IsActive).ToListAsync();
        var ungoverned = new List<WorkOrder>();
        foreach (var w in pending) if (!await IsGovernedAsync(w.WorkOrderId)) ungoverned.Add(w);   // (DbContext تک‌نخی: ترتیبی)
        pending = ungoverned;
        if (pending.Count == 0) return (true, null, 0);

        var (org, disc) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (org == null || !disc.HasValue)
            return (false, T("دامنه‌ی سازمانی/دیسیپلین شما مشخص نیست؛ Work Order های تولیدشده حاکمیتی نمی‌شوند و قابل پیشروی نخواهند بود.", "Your organization domain / discipline is not defined; the generated Work Orders cannot be governed and will not be able to progress."), 0);
        var (startStatus, err) = await ResolveStartAsync();
        if (err != null) return (false, err, 0);

        var count = 0;
        foreach (var w in pending)
        {
            var failure = await InStrategyAsync<string?>(async () =>
            {
                await using var tx = await BeginAsync();
                try
                {
                    w.OriginatingOrganization = org; w.OriginatingDisciplineId = disc; w.OriginatingRequestedById = actor.EmployeeId; w.Status = startStatus!;
                    await _context.SaveChangesAsync();
                    await _runtime.StartInstanceAsync(Module, LifecycleEntityType, w.WorkOrderId, LifecycleContext, null, actor.EmployeeId);
                    _context.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory { WorkOrderId = w.WorkOrderId, FromStatus = "", ToStatus = startStatus!, ChangedById = actor.EmployeeId, ChangedDate = DateTime.Now, Reason = "Governed after generation" });
                    await _context.SaveChangesAsync();
                    if (tx != null) await tx.CommitAsync();
                    return null;
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    if (tx != null) await tx.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    return ex is DbUpdateConcurrencyException or WorkflowConcurrencyConflictException ? ConflictMessage() : ex.Message;
                }
            });
            if (failure != null) return (false, failure, count);
            count++;
        }
        return (true, null, count);
    }

    /// <summary>
    /// اجرای تراکنش کاربر در برنامه‌ی واقعی (EnableRetryOnFailure) فقط داخل Execution Strategy مجاز است؛ در InMemory بدون اثر.
    /// </summary>
    private Task<T> InStrategyAsync<T>(Func<Task<T>> body) => _context.Database.CreateExecutionStrategy().ExecuteAsync(body);

    private async Task<IDbContextTransaction?> BeginAsync()
        => _context.Database.IsRelational() && _context.Database.CurrentTransaction == null ? await _context.Database.BeginTransactionAsync() : null;

    private string ConflictMessage() => T("این Work Order هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را دوباره بارگذاری کنید.",
        "This Work Order was changed by another user at the same time; reload the page.");

    private async Task<(string? StartStatus, string? Error)> ResolveStartAsync()
    {
        WorkflowBindingResolution? res;
        try { res = await _bindings.ResolveAsync(Module, LifecycleEntityType, LifecycleContext); }
        catch (WorkflowBindingAmbiguousException ex) { return (null, ex.Message); }
        if (res == null)
            return (null, T("گردش‌کار Work Order پیکربندی نشده است (Binding فعال یافت نشد).", "The Work Order workflow is not configured (no active binding)."));
        var start = await _context.WorkflowTransitions.Where(t => t.WorkflowVersionId == res.Version.WorkflowVersionId && t.FromStageId == null).OrderBy(t => t.Priority).FirstOrDefaultAsync();
        var status = start?.ToStageId == null ? null
            : await _context.WorkflowStages.Where(s => s.WorkflowStageId == start.ToStageId).Select(s => s.BusinessStatus).FirstOrDefaultAsync();
        return (status ?? "Open", null);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateGovernedAsync(WorkOrderCreateRequest r, WorkOrderActor actor)
    {
        if (!ValidTypes.Contains(r.WorkOrderType, StringComparer.Ordinal)) return (false, T("نوع WorkOrder نامعتبر است.", "Invalid Work Order type."), null);
        if (!ValidPriorities.Contains(r.Priority, StringComparer.Ordinal)) return (false, T("اولویت نامعتبر است.", "Invalid priority."), null);
        if (r.EquipmentId <= 0 || !await _context.Equipments.AnyAsync(x => x.EquipmentId == r.EquipmentId && x.IsActive))
            return (false, T("تجهیز انتخاب‌شده نامعتبر است.", "The selected equipment is invalid."), null);

        var roles = await ExpandRolesAsync(_access, actor.Roles);
        if (!roles.Contains(ParticipantPrefix + Perm.Create))
            return (false, T("شما اختیار ایجاد Work Order را ندارید.", "You do not have Work Order creation authority."), null);

        var (org, disc) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (org == null || !disc.HasValue)
            return (false, T("دامنه‌ی سازمانی/دیسیپلین شما مشخص نیست؛ دیسیپلین مبدأ ثبت نمی‌شود.", "Your organization domain / discipline is not defined; the originating discipline cannot be recorded."), null);

        var (startStatus, err) = await ResolveStartAsync();
        if (err != null) return (false, err, null);

        var wo = new WorkOrder
        {
            EquipmentId = r.EquipmentId, WorkOrderNumber = r.WorkOrderNumber, WorkOrderType = r.WorkOrderType, Priority = r.Priority,
            Description = r.Description, MaintenancePlanId = r.MaintenancePlanId, DueDate = r.DueDate, Status = startStatus!, IsActive = true,
            CreatedDate = DateTime.Now, OriginatingOrganization = org, OriginatingDisciplineId = disc, OriginatingRequestedById = actor.EmployeeId
        };

return await InStrategyAsync<(bool, string?, int?)>(async () =>
        {
            await using var tx = await BeginAsync();
            try
            {
                _context.WorkOrders.Add(wo);
                await _context.SaveChangesAsync();
                await _runtime.StartInstanceAsync(Module, LifecycleEntityType, wo.WorkOrderId, LifecycleContext, null, actor.EmployeeId);
                _context.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory
                {
                    WorkOrderId = wo.WorkOrderId, FromStatus = "", ToStatus = startStatus!, ChangedById = actor.EmployeeId, ChangedDate = DateTime.Now, Reason = "Created"
                });
                await _context.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                return (true, null, wo.WorkOrderId);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateException)
            {
                if (tx != null) await tx.RollbackAsync();
                _context.ChangeTracker.Clear();
                return (false, ex is DbUpdateConcurrencyException or WorkflowConcurrencyConflictException ? ConflictMessage() : ex.Message, null);
            }
        });

    }

    public async Task<WorkflowRuntimeViewModel?> GetWorkflowViewAsync(int workOrderId, WorkOrderActor actor)
    {
        var instance = await LifecycleInstanceAsync(workOrderId);
        if (instance == null) return null;
        var roles = await ExpandRolesAsync(_access, actor.Roles);
        return await _readModel.GetRuntimeViewAsync(instance.WorkflowInstanceId, new WorkflowUserContext(actor.EmployeeId, roles));
    }

    private Task<List<WorkflowInstance>> RunningReviewsAsync(int workOrderId) => ReviewInstancesAsync(_context, workOrderId, runningOnly: true);

    private async Task<string?> ResolveNewStatusAsync(WorkflowAvailableAction action, string current)
    {
        if (action.ToStageId.HasValue)
        {
            var bs = await _context.WorkflowStages.Where(s => s.WorkflowStageId == action.ToStageId).Select(s => s.BusinessStatus).FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(bs) ? current : bs;
        }
        // پایان گردش‌کار: Reject/Cancel → وضعیت مربوط؛ تصویب نهایی (Approve) → Closed
        return action.ActionType switch { "Reject" => "Rejected", "Cancel" => "Cancelled", "Approve" => "Closed", _ => current };
    }

    public async Task<(bool Success, string? Error)> ExecuteWorkflowActionAsync(
        int workOrderId, string actionType, string? comment, WorkOrderActor actor, WorkOrderCompletionData? completion = null)
    {
        var wo = await _context.WorkOrders.Include(x => x.AssignedEmployee).Include(x => x.AssignedTeam).FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (wo == null) return (false, T("Work Order یافت نشد.", "Work Order not found."));

        WorkflowInstance? instance;
        try { instance = await LifecycleInstanceAsync(workOrderId); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }
        if (instance == null) return (false, T("این Work Order Instance گردش‌کار ندارد (رکورد Legacy).", "This Work Order has no workflow instance (legacy record)."));
        if (instance.Status != "Running") return (false, T($"گردش‌کار در وضعیت «{instance.Status}» است.", $"The workflow is {instance.Status}."));

        var roles = await ExpandRolesAsync(_access, actor.Roles);
        var actions = await _readModel.GetAvailableActionsAsync(instance.WorkflowInstanceId, new WorkflowUserContext(actor.EmployeeId, roles));
        var action = actions.FirstOrDefault(a => a.ActionType == actionType);
        if (action == null) return (false, T($"اقدام «{actionType}» در مرحله‌ی جاری برای شما در دسترس نیست.", $"Action \"{actionType}\" is not available for you at the current stage."));
        if (!action.IsAllowed) return (false, T($"اقدام «{actionType}» مجاز نیست: {action.DeniedReason}", $"Action \"{actionType}\" is not allowed: {action.DeniedReason}"));

        // A1/B14: Reject/Cancel/Hold/Return without a reason are refused (shared rule; the reason stays in workflow + status history)
        var reasonError = WorkflowActionRules.ValidateAction(actionType, comment);
        if (reasonError != null) return (false, reasonError);
        comment = WorkflowActionRules.Normalize(comment);

        var transition = await _context.WorkflowTransitions.AsNoTracking().FirstAsync(t => t.WorkflowTransitionId == action.TransitionId);
        var perm = transition.RequiredPermission;

        // ---------- قواعد دامنه (Work Order) که به گذار وابسته‌اند؛ کلید = مجوز وظیفه‌ای + وضعیت پروژکت‌شده‌ی مرحله‌ی جاری ----------
        var fromStatus = wo.Status;
        if (perm == Perm.Plan && actionType == "Approve" && !wo.ResponsibleDisciplineId.HasValue)
            return (false, T("قبل از تکمیل برنامه‌ریزی، دیسیپلین مسئول نگهداری باید تعیین شود.", "The responsible maintenance discipline must be set before planning is completed."));
        if (perm == Perm.Prepare && actionType == "Approve" && wo.AssignedTeamId == null && wo.AssignedEmployeeId == null)
            return (false, T("قبل از درخواست صدور، تیم یا مجری باید تخصیص داده شود.", "A team or executor must be assigned before requesting issue."));

        // طبق «PEMS — Work Order Governance Remediation» F2: Issue/Release فقط اختیار Operations را
        // اعمال و گیت آزادسازی (Permit/LOTO/GasTest) را بررسی می‌کند - StartDate دیگر این‌جا ست
        // نمی‌شود؛ آن رویداد جداگانه («شروع واقعی کار») به مرحله‌ی Start Work (پایین) منتقل شده.
        if (perm == Perm.Issue && actionType == "Approve")
        {
            var gate = await _releaseGate.EvaluateAsync(workOrderId);
            if (!gate.CanRelease)
                return (false, T("آزادسازی ممکن نیست: ", "Cannot release: ") + string.Join(" | ", gate.Reasons));
        }

        // طبق F2: این‌جا دقیقاً همان لحظه‌ای است که مجری نگهداری واقعاً کار را شروع می‌کند - جدا از
        // تصمیم آزادسازی Operations. StartDate فقط یک‌بار ست می‌شود (اگر از قبل مقداردهی نشده).
        if (perm == Perm.Start && actionType == "Approve" && !wo.StartDate.HasValue)
            wo.StartDate = DateTime.Now;

        FailureCatalogMatch? failureMatch = null;
        var recordsCompletion = perm == Perm.Execute && actionType == "Approve" && fromStatus == "InProgress";
        if (recordsCompletion)
        {
            if ((await RunningReviewsAsync(workOrderId)).Count > 0)
                return (false, T("یک بازبینی چند-دیسیپلینه‌ی باز وجود دارد؛ قبل از تکمیل همه‌ی دیسیپلین‌ها باید پاسخ دهند.", "An open multi-discipline review exists; all referred disciplines must respond before completion."));
            completion ??= new WorkOrderCompletionData();
            if (wo.WorkOrderType is "Corrective" or "Emergency" && string.IsNullOrWhiteSpace(completion.FailureModeCode))
                return (false, T("برای کار اصلاحی/اضطراری حالت خرابی (ISO 14224) الزامی است.", "The failure mode (ISO 14224) is required for Corrective/Emergency Work Orders."));

            // B9: وقتی کاتالوگ فعال ISO 14224 وجود دارد، حالت/علت خرابی باید از همان کاتالوگ باشد (متن آزاد پذیرفته نمی‌شود)؛ کد به شکل استاندارد کاتالوگ ذخیره می‌شود
            if (wo.WorkOrderType is "Corrective" or "Emergency")
            {
                var catalog = await ResolveFailureCatalogAsync(completion.FailureModeCode, completion.FailureCauseCode);
                if (catalog.Error != null) return (false, catalog.Error);
                failureMatch = catalog;
            }
        }

        var newStatus = await ResolveNewStatusAsync(action, fromStatus) ?? fromStatus;
return await InStrategyAsync<(bool, string?)>(async () =>
        {
            await using var tx = await BeginAsync();
            try
            {
                if (recordsCompletion)
                {
                    wo.CompletionDate = DateTime.Now;
                    wo.ContractorCost = completion!.ContractorCost;
                    wo.ContractorName = completion.ContractorName;
                    if (wo.WorkOrderType is "Corrective" or "Emergency")
                    {
                        wo.FailureModeCode = failureMatch?.ModeCode ?? completion.FailureModeCode; wo.FailureCauseCode = failureMatch?.CauseCode ?? completion.FailureCauseCode;
                        wo.FailureModeId = failureMatch?.ModeId ?? wo.FailureModeId; wo.FailureCauseId = failureMatch?.CauseId ?? wo.FailureCauseId;
                        wo.FailureEffect = string.IsNullOrWhiteSpace(completion.FailureEffect) ? wo.FailureEffect : completion.FailureEffect;
                        wo.RootCause = completion.RootCause; wo.DowntimeHours = completion.DowntimeHours;
                        await SyncFailureRecordAsync(wo, completion.FailureModeCode!, completion.FailureCauseCode, completion.RootCause, completion.DowntimeHours, completion.FailureEffect, completion.CorrectiveAction);
                    }
                    await CreateMaintenanceHistoryAsync(wo);
                }

                // H29: در لحظه‌ی بستن، ردیف MaintenanceHistory با مجموع نهایی ساعت‌کار/قطعات/پیمانکار هم‌خوان می‌شود (پس از اصلاح‌های مجاز و بازگشایی)؛ از این پس
                // رکوردهای فرزند تغییرناپذیرند، پس هزینه‌ی نهایی منجمد است.
                if (newStatus == "Closed")
                    await RefreshMaintenanceHistoryAsync(wo);

                if (newStatus != fromStatus)
                {
                    wo.Status = newStatus;
                    _context.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory
                    {
                        WorkOrderId = wo.WorkOrderId, FromStatus = fromStatus, ToStatus = newStatus, ChangedById = actor.EmployeeId, ChangedDate = DateTime.Now, Reason = comment
                    });
                }

                // پلتفرم مجدداً و مستقل مجوز/سازمان/SoD را اعمال می‌کند و همه‌ی تغییرات را ذخیره می‌کند
                await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, actionType, actor.EmployeeId, roles, comment, null, null);

                // لغو Work Order، بازبینی چند-دیسیپلینه‌ی باز را هم می‌بندد (Architecture A)
                if (actionType == "Cancel")
                    foreach (var review in await RunningReviewsAsync(workOrderId))
                        await ((IWorkflowInstanceControl)_runtime).CancelInstanceAsync(review.WorkflowInstanceId, actor.EmployeeId, comment ?? "Work Order cancelled");

                if (tx != null) await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateException)
            {
                if (tx != null) await tx.RollbackAsync();
                _context.ChangeTracker.Clear();
                return (false, ex is DbUpdateConcurrencyException or WorkflowConcurrencyConflictException ? ConflictMessage() : ex.Message);
            }
        });

    }

    public async Task<(bool Success, string? Error)> ReopenAsync(int workOrderId, string? reason, WorkOrderActor actor, string? userName = null, int? userId = null, string? ipAddress = null)
    {
        reason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) return (false, T("دلیل بازگشایی الزامی است.", "A reason is required to reopen a Work Order."));

        var wo = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (wo == null) return (false, T("Work Order یافت نشد.", "Work Order not found."));
        if (wo.Status != "Closed") return (false, T("فقط Work Order بسته‌شده قابل بازگشایی است.", "Only a closed Work Order can be reopened."));

        if (!(await _access.AuthorizeForRolesAsync(actor.Roles, "WorkOrder", "Reopen")).Allowed)
        {
            await LogReopenAsync(wo, actor, userName, userId, ipAddress, "Denied", "MissingPermission", reason, "Closed", null);
            return (false, T("مجوز WorkOrder.Reopen لازم است.", "Permission WorkOrder.Reopen is required."));
        }

        WorkflowInstance? instance;
        try { instance = await LifecycleInstanceAsync(workOrderId); }
        catch (InvalidOperationException ex) { return (false, ex.Message); }

        int? targetStageId = null;
        if (instance != null)
        {
            if (instance.Status != "Completed") return (false, T($"گردش‌کار در وضعیت «{instance.Status}» است و قابل بازگشایی نیست.", $"The workflow is {instance.Status} and cannot be reopened."));
            // مرحله‌ی مقصد از «داده‌ی پیکربندی» خوانده می‌شود، نه نام ثابت: مرحله‌ای که گذار خروجی‌اش مجوز Verify می‌خواهد (جایی که اصلاح ساعت‌کار/قطعات هنوز مجاز است
            // و تأیید دوباره انجام می‌شود، وضعیت Completed). با تغییر نام/ترتیب مراحل در نسخه‌های بعدی گردش‌کار، کد تغییر نمی‌کند.
            targetStageId = await _context.WorkflowTransitions
                .Where(t => t.WorkflowVersionId == instance.WorkflowVersionId && t.FromStageId != null && t.RequiredPermission == Perm.Verify)
                .OrderBy(t => t.Priority).Select(t => t.FromStageId).FirstOrDefaultAsync();
            if (targetStageId == null) return (false, T("مرحله‌ی مقصد بازگشایی در این نسخه‌ی گردش‌کار تعریف نشده است.", "The reopen target stage is not defined in this workflow version."));
        }

        return await InStrategyAsync<(bool, string?)>(async () =>
        {
            await using var tx = await BeginAsync();
            try
            {
                const string from = "Closed", to = "Completed";
                wo.Status = to;
                _context.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory
                {
                    WorkOrderId = wo.WorkOrderId, FromStatus = from, ToStatus = to, ChangedById = actor.EmployeeId, ChangedDate = DateTime.Now, Reason = "Reopened: " + reason
                });
                _context.WorkOrderChangeLogs.Add(new WorkOrderChangeLog
                {
                    WorkOrderId = wo.WorkOrderId, Section = "WorkOrder", Action = "Reopen", Outcome = "Allowed", Code = "Allowed", WorkOrderStatus = from,
                    WorkflowStage = null, ChangedByEmployeeId = actor.EmployeeId, ChangedByUserId = userId, ChangedByUserName = userName, ChangedDate = DateTime.Now,
                    Reason = reason.Length <= 500 ? reason : reason[..500], BeforeValue = from, AfterValue = to + (instance != null ? " @ stage#" + targetStageId : ""), IpAddress = ipAddress
                });
                await _context.SaveChangesAsync();

                if (instance != null && !await _runtime.ReopenInstanceAsync(instance.WorkflowInstanceId, targetStageId!.Value, actor.EmployeeId, reason))
                    throw new InvalidOperationException(T("بازگشایی گردش‌کار انجام نشد.", "The workflow could not be reopened."));

                if (tx != null) await tx.CommitAsync();
                return (true, null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or DbUpdateException)
            {
                if (tx != null) await tx.RollbackAsync();
                _context.ChangeTracker.Clear();
                return (false, ex is DbUpdateConcurrencyException or WorkflowConcurrencyConflictException ? ConflictMessage() : ex.Message);
            }
        });
    }

    private async Task LogReopenAsync(WorkOrder wo, WorkOrderActor actor, string? userName, int? userId, string? ip, string outcome, string code, string? reason, string status, string? stage)
    {
        try
        {
            _context.WorkOrderChangeLogs.Add(new WorkOrderChangeLog
            {
                WorkOrderId = wo.WorkOrderId, Section = "WorkOrder", Action = "Reopen", Outcome = outcome, Code = code, WorkOrderStatus = status, WorkflowStage = stage,
                ChangedByEmployeeId = actor.EmployeeId, ChangedByUserId = userId, ChangedByUserName = userName, ChangedDate = DateTime.Now, Reason = reason,
                IsSecurityEvent = outcome == "Denied", IpAddress = ip
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception) { _context.ChangeTracker.Clear(); }
    }

    public async Task<(bool Success, string? Error)> SetPlanningAsync(int workOrderId, int responsibleDisciplineId, WorkOrderActor actor)
    {
        var wo = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (wo == null) return (false, T("Work Order یافت نشد.", "Work Order not found."));
        var (instance, instanceError) = await TryLifecycleInstanceAsync(workOrderId);
        if (instanceError != null) return (false, instanceError);
        if (instance == null || instance.Status != "Running") return (false, T("Work Order تحت گردش‌کار فعال نیست.", "The Work Order is not under an active workflow."));
        if (wo.Status is not ("Open" or "Assigned")) return (false, T("تغییر برنامه پس از صدور مجاز نیست؛ ابتدا باید به برنامه‌ریزی بازگردانده شود.", "Planning cannot change after issue; return it to Planning first."));

        var roles = await ExpandRolesAsync(_access, actor.Roles);
        if (!roles.Contains(ParticipantPrefix + Perm.Plan)) return (false, T("شما اختیار برنامه‌ریزی ندارید.", "You do not have planning authority."));
        var (org, _) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (org != OrganizationDomains.Maintenance) return (false, T("برنامه‌ریزی فقط برای سازمان نگهداری و تعمیرات مجاز است.", "Planning is restricted to the Maintenance organization."));
        if (!await _context.Disciplines.AnyAsync(d => d.DisciplineId == responsibleDisciplineId && d.IsActive))
            return (false, T("دیسیپلین نامعتبر است.", "Invalid discipline."));

        wo.ResponsibleDisciplineId = responsibleDisciplineId;
        wo.ResponsibleOrganization = OrganizationDomains.Maintenance;
        try { await _context.SaveChangesAsync(); return (true, null); }
        catch (DbUpdateConcurrencyException) { _context.ChangeTracker.Clear(); return (false, ConflictMessage()); }
        catch (DbUpdateException ex) { _context.ChangeTracker.Clear(); return (false, T("ذخیره انجام نشد: ", "Could not save: ") + ex.GetBaseException().Message); }
    }

    public async Task<(bool Success, string? Error)> AssignExecutorAsync(int workOrderId, int? teamId, int? employeeId, WorkOrderActor actor)
    {
        if (!teamId.HasValue && !employeeId.HasValue) return (false, T("تیم یا مجری را انتخاب کنید.", "Select a team or an executor."));
        var wo = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (wo == null) return (false, T("Work Order یافت نشد.", "Work Order not found."));
        var (instance, instanceError) = await TryLifecycleInstanceAsync(workOrderId);
        if (instanceError != null) return (false, instanceError);
        if (instance == null || instance.Status != "Running") return (false, T("Work Order تحت گردش‌کار فعال نیست.", "The Work Order is not under an active workflow."));
        if (wo.Status != "Assigned") return (false, T("تخصیص مجری فقط در مرحله‌ی آماده‌سازی/تخصیص مجاز است.", "Executors can only be assigned during Preparation / Assignment."));
        if (!wo.ResponsibleDisciplineId.HasValue) return (false, T("دیسیپلین مسئول هنوز تعیین نشده است.", "The responsible discipline has not been set."));

        var roles = await ExpandRolesAsync(_access, actor.Roles);
        if (!roles.Contains(ParticipantPrefix + Perm.Assign)) return (false, T("شما اختیار تخصیص مجری ندارید.", "You do not have executor-assignment authority."));
        var (org, disc) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (org != OrganizationDomains.Maintenance || disc != wo.ResponsibleDisciplineId)
            return (false, T("تخصیص فقط توسط سرگروه/معاون دیسیپلین مسئول نگهداری مجاز است.", "Assignment is restricted to the head/deputy of the responsible maintenance discipline."));

        if (employeeId.HasValue)
        {
            var (eOrg, eDisc) = await ResolveEmployeeAsync(_context, employeeId);
            if (eOrg != OrganizationDomains.Maintenance || eDisc != wo.ResponsibleDisciplineId)
                return (false, T("مجری باید عضو دیسیپلین مسئول نگهداری (سازمان نگهداری) باشد.", "The executor must belong to the responsible maintenance discipline (Maintenance organization)."));
            wo.AssignedEmployeeId = employeeId;
        }
        if (teamId.HasValue)
        {
            var teamDisciplineId = await _context.Teams.Where(t => t.TeamId == teamId.Value)
                .Select(t => (int?)t.MaintenanceGroup!.DisciplineId).FirstOrDefaultAsync();
            if (teamDisciplineId != wo.ResponsibleDisciplineId)
                return (false, T("گروه کاری باید متعلق به دیسیپلین مسئول نگهداری باشد.", "The team must belong to the responsible maintenance discipline."));
            wo.AssignedTeamId = teamId;
        }
        try { await _context.SaveChangesAsync(); return (true, null); }
        catch (DbUpdateConcurrencyException) { _context.ChangeTracker.Clear(); return (false, ConflictMessage()); }
        catch (DbUpdateException ex) { _context.ChangeTracker.Clear(); return (false, T("ذخیره انجام نشد: ", "Could not save: ") + ex.GetBaseException().Message); }
    }

    // طبق «PEMS — Work Request/Work Order Scheduling & Maintenance Calendar»: دقیقاً هم‌الگو با
    // AssignExecutorAsync (همان مرحله، همان مجوز Perm.Assign، همان اسکوپ دیسیپلین) چون زمان‌بندی
    // مفهوماً بخشی از همان مسئولیت «آماده‌سازی/تخصیص» است - نه یک گذار گردش‌کار تازه. مستقل از
    // AssignExecutorAsync فراخوانی می‌شود (ترتیب تعیین تیم/کارمند در برابر تاریخ الزامی نیست).
    // PlannedStart/PlannedFinish فیلدهای واقعی WorkOrder را تغییر می‌دهند - Audit خودکار از همان
    // Interceptor سراسری PEMSDbContext.SaveChangesAsync می‌آید، بدون کد Audit تازه.
    public async Task<(bool Success, string? Error)> ScheduleWorkOrderAsync(int workOrderId, DateTime plannedStart, DateTime plannedFinish, WorkOrderActor actor)
    {
        if (plannedFinish <= plannedStart) return (false, T("پایان برنامه‌ریزی‌شده باید بعد از شروع باشد.", "Planned finish must be after planned start."));
        var wo = await _context.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);
        if (wo == null) return (false, T("Work Order یافت نشد.", "Work Order not found."));
        var (instance, instanceError) = await TryLifecycleInstanceAsync(workOrderId);
        if (instanceError != null) return (false, instanceError);
        if (instance == null || instance.Status != "Running") return (false, T("Work Order تحت گردش‌کار فعال نیست.", "The Work Order is not under an active workflow."));
        if (wo.Status != "Assigned") return (false, T("زمان‌بندی فقط در مرحله‌ی آماده‌سازی/تخصیص مجاز است.", "Scheduling is only allowed during Preparation / Assignment."));
        if (!wo.ResponsibleDisciplineId.HasValue) return (false, T("دیسیپلین مسئول هنوز تعیین نشده است.", "The responsible discipline has not been set."));

        var roles = await ExpandRolesAsync(_access, actor.Roles);
        if (!roles.Contains(ParticipantPrefix + Perm.Assign)) return (false, T("شما اختیار زمان‌بندی ندارید.", "You do not have scheduling authority."));
        var (org, disc) = await ResolveEmployeeAsync(_context, actor.EmployeeId);
        if (org != OrganizationDomains.Maintenance || disc != wo.ResponsibleDisciplineId)
            return (false, T("زمان‌بندی فقط توسط سرگروه/معاون دیسیپلین مسئول نگهداری مجاز است.", "Scheduling is restricted to the head/deputy of the responsible maintenance discipline."));

        wo.PlannedStart = plannedStart;
        wo.PlannedFinish = plannedFinish;
        try { await _context.SaveChangesAsync(); return (true, null); }
        catch (DbUpdateConcurrencyException) { _context.ChangeTracker.Clear(); return (false, ConflictMessage()); }
        catch (DbUpdateException ex) { _context.ChangeTracker.Clear(); return (false, T("ذخیره انجام نشد: ", "Could not save: ") + ex.GetBaseException().Message); }
    }

    public async Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int workOrderId, string nextStatus, string? reason, int? changedByEmployeeId,
        int? assignedTeamId, int? assignedEmployeeId,
        string? failureModeCode, string? failureCauseCode, string? rootCause, decimal? downtimeHours,
        string? failureEffect = null, string? correctiveAction = null,
        decimal? contractorCost = null, string? contractorName = null)
    {
        var workOrder = await _context.WorkOrders
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedEmployee)
            .FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId);

        if (workOrder == null) return (false, "Work order not found.");

        // Phase 3B: Work Order حاکمیتی فقط از مسیر اکشن‌های گردش‌کار تغییر می‌کند؛ مسیر Legacy برای رکوردهای بدون Instance است
        if (await IsGovernedAsync(workOrderId))
            return (false, T("این Work Order تحت گردش‌کار مرکزی است؛ تغییر وضعیت فقط از مسیر اقدامات گردش‌کار مجاز است.",
                "This Work Order is governed by the central workflow; its status can only change through workflow actions."));

        // Work Order ساخته‌شده پس از فعال‌شدن گردش‌کار ولی بدون Instance: هرگز از مسیر Legacy قابل پیشروی نیست (بدون دور زدن حاکمیت)
        var cutoff = await GovernanceCutoffAsync();
        if (cutoff != null && workOrder.CreatedDate >= cutoff)
            return (false, T("این Work Order باید تحت گردش‌کار مرکزی باشد و Instance ندارد؛ از مسیر Legacy قابل پیشروی نیست.",
                "This Work Order must be governed by the central workflow but has no instance; it cannot advance through the legacy path."));

        var fromStatus = workOrder.Status;

        if (!LegalTransitions.TryGetValue(fromStatus, out var allowed) || !allowed.Contains(nextStatus))
            return (false, $"گذار از وضعیت «{fromStatus}» به «{nextStatus}» مجاز نیست.");

        var legacyReasonError = WorkflowActionRules.ValidateStatus(nextStatus, reason);
        if (legacyReasonError != null) return (false, legacyReasonError);

        // طبق «PEMS Phase 6» بند ۱۸: قبل از اجرا، گیت ایمنی (Permit/LOTO/GasTest، اگر JobPlan
        // آن‌ها را الزامی کرده باشد) بررسی می‌شود - تنها نقطه‌ی اجرای این گیت، همین‌جاست (نه
        // یک مسیر دوم قابل‌دورزدن در Controller).
        if (nextStatus == "InProgress")
        {
            var gate = await _releaseGate.EvaluateAsync(workOrderId);
            if (!gate.CanRelease)
                return (false, "Cannot release Work Order.\n" + string.Join("\n", gate.Reasons.Select(r => $"Reason: {r}")));
        }

        // ISO 14224 (قاعده‌ی پروژه): برای Corrective/Emergency حالت خرابی هنگام تکمیل الزامی است؛ مسیر حاکمیتی همین را اعمال می‌کند
        // (ExecuteWorkflowActionAsync) و مسیر Legacy نباید ضعیف‌تر باشد.
        if (nextStatus == "Completed" && workOrder.WorkOrderType is "Corrective" or "Emergency" && string.IsNullOrWhiteSpace(failureModeCode))
            return (false, T("برای کار اصلاحی/اضطراری حالت خرابی (ISO 14224) الزامی است.", "The failure mode (ISO 14224) is required for Corrective/Emergency Work Orders."));

        if (nextStatus == "Assigned")
        {
            if (assignedTeamId.HasValue) workOrder.AssignedTeamId = assignedTeamId;
            if (assignedEmployeeId.HasValue) workOrder.AssignedEmployeeId = assignedEmployeeId;
        }

        if (nextStatus == "InProgress" && !workOrder.StartDate.HasValue)
            workOrder.StartDate = DateTime.Now;

        if (nextStatus == "Completed")
        {
            workOrder.CompletionDate = DateTime.Now;
            workOrder.ContractorCost = contractorCost;
            workOrder.ContractorName = contractorName;

            if (workOrder.WorkOrderType is "Corrective" or "Emergency")
            {
                workOrder.FailureModeCode = failureModeCode;
                workOrder.FailureCauseCode = failureCauseCode;
                workOrder.RootCause = rootCause;
                workOrder.DowntimeHours = downtimeHours;

                // طبق بخش ۱۰: FailureRecord نباید منبع حقیقت مستقل دوم باشد - از فیلدهای
                // ISO-14224 خودِ WorkOrder (که همین‌جا بالا ثبت شد) مشتق/همگام می‌شود.
                if (!string.IsNullOrWhiteSpace(failureModeCode))
                    await SyncFailureRecordAsync(workOrder, failureModeCode, failureCauseCode, rootCause, downtimeHours, failureEffect, correctiveAction);
            }

            // طبق بخش ۱۲: WorkOrder = تراکنش نگهداری معتبر؛ MaintenanceHistory باید از روی
            // تراکنش تکمیل‌شده مشتق شود، نه یک Entity یتیم که هیچ‌کد ی پرش نمی‌کند.
            await CreateMaintenanceHistoryAsync(workOrder);
        }

        workOrder.Status = nextStatus;

        await _context.WorkOrderStatusHistories.AddAsync(new WorkOrderStatusHistory
        {
            WorkOrderId = workOrder.WorkOrderId,
            FromStatus = fromStatus,
            ToStatus = nextStatus,
            ChangedById = changedByEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = reason
        });

        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _context.ChangeTracker.Clear(); return (false, ConflictMessage()); }
        return (true, null);
    }

    private async Task SyncFailureRecordAsync(
        WorkOrder workOrder, string failureModeCode, string? failureCauseCode, string? rootCause, decimal? downtimeHours,
        string? failureEffect, string? correctiveAction)
    {
        var failureRecord = await _context.FailureRecords.FirstOrDefaultAsync(x => x.WorkOrderId == workOrder.WorkOrderId);

        if (failureRecord == null)
        {
            failureRecord = new FailureRecord
            {
                EquipmentId = workOrder.EquipmentId,
                WorkOrderId = workOrder.WorkOrderId,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            await _context.FailureRecords.AddAsync(failureRecord);
        }

        failureRecord.FailureDate = workOrder.CompletionDate ?? DateTime.Now;
        failureRecord.FailureMode = failureModeCode;
        failureRecord.FailureCause = failureCauseCode;
        failureRecord.RootCause = rootCause;
        failureRecord.DowntimeHours = downtimeHours;
        failureRecord.FailureEffect = failureEffect;
        failureRecord.CorrectiveAction = correctiveAction;
    }

    private async Task RefreshMaintenanceHistoryAsync(WorkOrder workOrder)
    {
        var history = await _context.MaintenanceHistories.FirstOrDefaultAsync(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive);
        if (history == null) return;      // سابقه‌ی تکمیل ساخته نشده (مسیر Legacy/ناقص): چیزی جعل نمی‌شود

        var durationHours = await _context.WorkOrderLabors.Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive)
            .SumAsync(x => (decimal?)(x.RegularHours + x.OvertimeHours)) ?? 0;
        var laborCost = await _context.WorkOrderLabors.Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive).SumAsync(x => (decimal?)x.LaborCost) ?? 0;
        var spareCost = await _context.MaintenanceSpareUsages.Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive).SumAsync(x => (decimal?)x.TotalCost) ?? 0;

        history.DurationHours = durationHours > 0 ? durationHours : null;
        history.Cost = laborCost + spareCost + (workOrder.ContractorCost ?? 0);
    }

    private async Task CreateMaintenanceHistoryAsync(WorkOrder workOrder)
    {
        var technician = workOrder.AssignedEmployee != null
            ? $"{workOrder.AssignedEmployee.FirstName} {workOrder.AssignedEmployee.LastName}"
            : workOrder.AssignedTeam?.Name ?? workOrder.AssignedTo;

        var durationHours = await _context.WorkOrderLabors
            .Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive)
            .SumAsync(x => (decimal?)(x.RegularHours + x.OvertimeHours)) ?? 0;

        var laborCost = await _context.WorkOrderLabors
            .Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive)
            .SumAsync(x => (decimal?)x.LaborCost) ?? 0;
        var spareCost = await _context.MaintenanceSpareUsages
            .Where(x => x.WorkOrderId == workOrder.WorkOrderId && x.IsActive)
            .SumAsync(x => (decimal?)x.TotalCost) ?? 0;

        await _context.MaintenanceHistories.AddAsync(new MaintenanceHistory
        {
            EquipmentId = workOrder.EquipmentId,
            WorkOrderId = workOrder.WorkOrderId,
            ExecutionDate = workOrder.CompletionDate ?? DateTime.Now,
            ActionTaken = workOrder.Description,
            Technician = technician,
            DurationHours = durationHours > 0 ? durationHours : null,
            Cost = laborCost + spareCost + (workOrder.ContractorCost ?? 0),
            IsActive = true,
            CreatedDate = DateTime.Now
        });
    }
}
