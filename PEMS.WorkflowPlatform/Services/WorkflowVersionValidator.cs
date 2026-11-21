using Microsoft.EntityFrameworkCore;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.WorkflowPlatform.Services;

/// <summary>
/// اعتبارسنج قابل‌استفاده‌ی مجدد قبل از انتشار. خطاها (Error) مانع Validate/Publish می‌شوند؛ هشدارها (Warning) نه.
/// سه گروه: ساختاری، امنیتی، اجرایی (+ اعتبار Binding/تعریف).
/// </summary>
public class WorkflowVersionValidator : IWorkflowVersionValidator
{
    private readonly IWorkflowDbContext _db;
    private readonly IWorkflowActionRegistry _registry;
    private readonly IWorkflowConditionEvaluator _conditions;
    private readonly IWorkflowReferenceCatalog? _catalog;
    private readonly IWorkflowModuleContractRegistry? _contracts;

    public WorkflowVersionValidator(IWorkflowDbContext db, IWorkflowActionRegistry? registry = null,
        IWorkflowConditionEvaluator? conditions = null, IWorkflowReferenceCatalog? catalog = null,
        IWorkflowModuleContractRegistry? contracts = null)
    {
        _db = db;
        _registry = registry ?? new WorkflowActionRegistry();
        _conditions = conditions ?? new WorkflowConditionEvaluator();
        _catalog = catalog;
        _contracts = contracts;
    }

    private static WorkflowValidationIssue Err(string code, string msg, int? stage = null, int? tr = null)
        => new(code, WorkflowValidationSeverity.Error, msg, stage, tr);
    private static WorkflowValidationIssue Warn(string code, string msg, int? stage = null, int? tr = null)
        => new(code, WorkflowValidationSeverity.Warning, msg, stage, tr);

    public async Task<WorkflowValidationResult> ValidateAsync(int workflowVersionId, CancellationToken ct = default)
    {
        var result = new WorkflowValidationResult();
        var version = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(v => v.WorkflowVersionId == workflowVersionId, ct);
        if (version == null) { result.Issues.Add(Err("VERSION_NOT_FOUND", "نسخه یافت نشد.")); return result; }

        var definition = await _db.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.WorkflowDefinitionId == version.WorkflowDefinitionId, ct);
        var stages = await _db.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == workflowVersionId).ToListAsync(ct);
        var transitions = await _db.WorkflowTransitions.AsNoTracking().Where(t => t.WorkflowVersionId == workflowVersionId).ToListAsync(ct);
        var stageIds = stages.Select(s => s.WorkflowStageId).ToHashSet();

        Structural(result, stages, transitions, stageIds);
        await SecurityAsync(result, stages, transitions, ct);
        Runtime(result, definition, stages, transitions);
        Contract(result, definition, stages, transitions);
        await BindingAsync(result, definition, ct);
        return result;
    }

    private static void Structural(WorkflowValidationResult r, List<WorkflowStage> stages, List<WorkflowTransition> transitions, HashSet<int> stageIds)
    {
        if (stages.Count == 0) { r.Issues.Add(Err("NO_STAGES", "نسخه هیچ مرحله‌ای ندارد.")); return; }

        foreach (var g in stages.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            r.Issues.Add(Err("DUPLICATE_STAGE_NAME", $"نام مرحله‌ی «{g.Key}» تکراری است.", g.First().WorkflowStageId));
        foreach (var s in stages.Where(s => string.IsNullOrWhiteSpace(s.Name)))
            r.Issues.Add(Err("STAGE_NAME_EMPTY", "نام مرحله خالی است.", s.WorkflowStageId));
        foreach (var g in stages.GroupBy(s => s.StageOrder).Where(g => g.Count() > 1))
            r.Issues.Add(Warn("DUPLICATE_STAGE_ORDER", $"ترتیب {g.Key} برای چند مرحله تکرار شده؛ نمایش ترتیبی مبهم است.", g.First().WorkflowStageId));

        var starts = transitions.Where(t => t.FromStageId == null).ToList();
        if (starts.Count == 0) r.Issues.Add(Err("START_MISSING", "گذار شروع (From = null) تعریف نشده است."));
        if (starts.Count > 1) r.Issues.Add(Err("START_MULTIPLE", "بیش از یک گذار شروع تعریف شده؛ نقطه‌ی شروع باید یکتا باشد."));
        foreach (var st in starts.Where(t => t.ToStageId == null))
            r.Issues.Add(Err("START_NO_TARGET", "گذار شروع مرحله‌ی مقصد ندارد.", null, st.WorkflowTransitionId));

        foreach (var t in transitions)
        {
            if (t.FromStageId.HasValue && !stageIds.Contains(t.FromStageId.Value))
                r.Issues.Add(Err("BROKEN_REFERENCE", "مرحله‌ی مبدأ گذار در این نسخه وجود ندارد.", null, t.WorkflowTransitionId));
            if (t.ToStageId.HasValue && !stageIds.Contains(t.ToStageId.Value))
                r.Issues.Add(Err("BROKEN_REFERENCE", "مرحله‌ی مقصد گذار در این نسخه وجود ندارد.", null, t.WorkflowTransitionId));
            if (t.FromStageId == null && t.ToStageId == null)
                r.Issues.Add(Err("EMPTY_TRANSITION", "گذار بدون مبدأ و مقصد.", null, t.WorkflowTransitionId));
            if (t.FromStageId.HasValue && t.FromStageId == t.ToStageId)
                r.Issues.Add(Warn("SELF_LOOP", "گذار به همان مرحله (حلقه‌ی خودی).", t.FromStageId, t.WorkflowTransitionId));
        }

        foreach (var g in transitions.GroupBy(t => (t.FromStageId, ActionType: t.ActionType.ToLowerInvariant(), t.Priority)).Where(g => g.Count() > 1))
            r.Issues.Add(Err("DUPLICATE_TRANSITION", $"گذار تکراری «{g.Key.ActionType}» با اولویت {g.Key.Priority} از یک مرحله؛ انتخاب گذار غیرقطعی است.", g.Key.FromStageId, g.First().WorkflowTransitionId));

        if (!transitions.Any(t => t.FromStageId != null && t.ToStageId == null))
            r.Issues.Add(Err("NO_TERMINAL", "هیچ گذار پایانی (To = null) وجود ندارد؛ گردش‌کار هرگز تمام نمی‌شود."));

        // دسترس‌پذیری
        var start = starts.FirstOrDefault(t => t.ToStageId.HasValue)?.ToStageId;
        if (start == null) return;
        var reach = new HashSet<int> { start.Value };
        var queue = new Queue<int>(); queue.Enqueue(start.Value);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var t in transitions.Where(t => t.FromStageId == cur && t.ToStageId.HasValue))
                if (reach.Add(t.ToStageId!.Value)) queue.Enqueue(t.ToStageId.Value);
        }
        foreach (var s in stages.Where(s => !reach.Contains(s.WorkflowStageId)))
            r.Issues.Add(Err("UNREACHABLE_STAGE", $"مرحله‌ی «{s.Name}» از نقطه‌ی شروع قابل دسترسی نیست.", s.WorkflowStageId));
        foreach (var s in stages.Where(s => reach.Contains(s.WorkflowStageId) && !transitions.Any(t => t.FromStageId == s.WorkflowStageId)))
            r.Issues.Add(Err("DEAD_END", $"مرحله‌ی «{s.Name}» هیچ گذار خروجی ندارد.", s.WorkflowStageId));
        foreach (var s in stages.Where(s => reach.Contains(s.WorkflowStageId)))
        {
            var outs = transitions.Where(t => t.FromStageId == s.WorkflowStageId).ToList();
            if (outs.Count > 0 && !outs.Any(t => t.ActionType == "Approve"))
                r.Issues.Add(Warn("NO_APPROVE_TRANSITION", $"مرحله‌ی «{s.Name}» گذار Approve ندارد (Auto-skip و تایید عادی کار نمی‌کند).", s.WorkflowStageId));
        }
    }

    private async Task SecurityAsync(WorkflowValidationResult r, List<WorkflowStage> stages, List<WorkflowTransition> transitions, CancellationToken ct)
    {
        if (_catalog == null)
            r.Issues.Add(Warn("CATALOG_UNAVAILABLE", "کاتالوگ نقش/کارمند/مجوز در دسترس نیست؛ ارجاعات امنیتی بررسی نشد."));

        foreach (var s in stages)
        {
            var roles = (s.ResponsibleRole ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Length == 0 && s.ResponsibleEmployeeId == null && string.IsNullOrWhiteSpace(s.ResponsibilityKey))
                r.Issues.Add(Warn("STAGE_NO_PARTICIPANT", $"مرحله‌ی «{s.Name}» نقش/کارمند ثابت ندارد (فقط با شرکت‌کننده‌ی پویا در زمان اجرا قابل استفاده است؛ در غیر این‌صورت Auto-skip می‌شود).", s.WorkflowStageId));

            if (_catalog == null) continue;
            foreach (var role in roles.Where(x => !x.StartsWith("Discipline:", StringComparison.OrdinalIgnoreCase)))
            {
                // «Permission:Resource.Action» = شرکت‌کننده‌ی مبتنی بر مجوز؛ ماژول فراخوان آن را به نقش‌های اجراکننده اضافه می‌کند
                if (role.StartsWith("Permission:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await _catalog.PermissionExistsAsync(role["Permission:".Length..], ct))
                        r.Issues.Add(Err("PERMISSION_UNKNOWN", $"مجوز شرکت‌کننده‌ی «{role}» در مرحله‌ی «{s.Name}» وجود ندارد.", s.WorkflowStageId));
                }
                else if (!await _catalog.RoleExistsAsync(role, ct))
                    r.Issues.Add(Err("ROLE_UNKNOWN", $"نقش «{role}» در مرحله‌ی «{s.Name}» وجود ندارد.", s.WorkflowStageId));
            }
            if (s.ResponsibleEmployeeId.HasValue && !await _catalog.EmployeeExistsAsync(s.ResponsibleEmployeeId.Value, ct))
                r.Issues.Add(Err("EMPLOYEE_UNKNOWN", $"کارمند {s.ResponsibleEmployeeId} در مرحله‌ی «{s.Name}» وجود ندارد.", s.WorkflowStageId));
            if (!string.IsNullOrWhiteSpace(s.EscalationRole) && !await _catalog.RoleExistsAsync(s.EscalationRole!, ct))
                r.Issues.Add(Err("ESCALATION_ROLE_UNKNOWN", $"نقش Escalation «{s.EscalationRole}» وجود ندارد.", s.WorkflowStageId));
        }

        foreach (var t in transitions.Where(t => !string.IsNullOrWhiteSpace(t.RequiredPermission)))
        {
            var p = t.RequiredPermission!.Trim();
            var dot = p.IndexOf('.');
            if (dot <= 0 || dot == p.Length - 1)
            { r.Issues.Add(Err("PERMISSION_FORMAT", $"مجوز «{p}» باید به شکل Resource.Action باشد.", t.FromStageId, t.WorkflowTransitionId)); continue; }
            if (_catalog != null && !await _catalog.PermissionExistsAsync(p, ct))
                r.Issues.Add(Err("PERMISSION_UNKNOWN", $"مجوز «{p}» در کاتالوگ Permissions وجود ندارد.", t.FromStageId, t.WorkflowTransitionId));
        }

        foreach (var t in transitions.Where(t => t.ForbidInitiator && t.FromStageId == null))
            r.Issues.Add(Warn("SOD_ON_START", "تفکیک وظایف روی گذار شروع بی‌اثر است.", null, t.WorkflowTransitionId));
    }

    private void Runtime(WorkflowValidationResult r, WorkflowDefinition def, List<WorkflowStage> stages, List<WorkflowTransition> transitions)
    {
        foreach (var t in transitions)
        {
            var d = _registry.Find(t.ActionType, def.Module);
            if (d == null)
                r.Issues.Add(Err("ACTION_NOT_REGISTERED", $"اکشن «{t.ActionType}» در رجیستری ثبت نشده است.", t.FromStageId, t.WorkflowTransitionId));
            else if (d.Kind != WorkflowActionKind.Outcome)
                r.Issues.Add(Err("ACTION_NOT_TRANSITION", $"اکشن «{t.ActionType}» از نوع اثر جانبی است و نمی‌تواند گذار باشد.", t.FromStageId, t.WorkflowTransitionId));

            foreach (var e in _conditions.Validate(t.ConditionExpression))
                r.Issues.Add(Err("CONDITION_INVALID", $"شرط گذار «{t.ConditionExpression}» نامعتبر است: {e}", t.FromStageId, t.WorkflowTransitionId));
        }

        foreach (var s in stages)
        {
            var roleCount = (s.ResponsibleRole ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (s.StageType is not ("Sequential" or "Parallel"))
                r.Issues.Add(Err("STAGE_TYPE_INVALID", $"نوع مرحله‌ی «{s.Name}» نامعتبر است.", s.WorkflowStageId));
            if (s.ApprovalStrategy is not ("AllApprovalsRequired" or "MandatoryRolesOnly" or "MinimumCount" or "FirstResponse"))
                r.Issues.Add(Err("APPROVAL_STRATEGY_INVALID", $"راهبرد تایید مرحله‌ی «{s.Name}» نامعتبر است.", s.WorkflowStageId));
            if (s.ApprovalStrategy == "MinimumCount")
            {
                if (s.MinimumApprovalCount is null or < 1)
                    r.Issues.Add(Err("MIN_COUNT_MISSING", $"مرحله‌ی «{s.Name}» راهبرد MinimumCount دارد ولی حداقل تعداد تعریف نشده.", s.WorkflowStageId));
                else if (s.StageType == "Parallel" && roleCount > 0 && s.MinimumApprovalCount > roleCount)
                    r.Issues.Add(Err("MIN_COUNT_TOO_HIGH", $"حداقل تعداد تایید ({s.MinimumApprovalCount}) از تعداد نقش‌ها ({roleCount}) بیشتر است.", s.WorkflowStageId));
            }
            if (s.ApprovalStrategy == "MandatoryRolesOnly")
                r.Issues.Add(Warn("MANDATORY_ROLES_ALIAS", $"راهبرد MandatoryRolesOnly در مرحله‌ی «{s.Name}» فعلاً مثل AllApprovalsRequired اجرا می‌شود.", s.WorkflowStageId));
            if (s.TimeoutHours is <= 0)
                r.Issues.Add(Err("SLA_INVALID", $"مهلت (SLA) مرحله‌ی «{s.Name}» باید مثبت باشد.", s.WorkflowStageId));
            if (s.IsEscalatable && string.IsNullOrWhiteSpace(s.EscalationRole))
                r.Issues.Add(Warn("ESCALATION_NO_ROLE", $"مرحله‌ی «{s.Name}» Escalation‌پذیر است ولی نقش Escalation ندارد (اجرای خودکار Escalation هم در Phase 1 وجود ندارد).", s.WorkflowStageId));
            if (!string.IsNullOrWhiteSpace(s.EntryCondition) && string.IsNullOrWhiteSpace(s.ResponsibilityKey))
                r.Issues.Add(Warn("ENTRY_CONDITION_NO_RESPONSIBILITY", $"شرط ورود مرحله‌ی «{s.Name}» فقط برای مراحل مبتنی بر مسئولیت اعمال می‌شود.", s.WorkflowStageId));
            foreach (var e in _conditions.Validate(s.EntryCondition))
                r.Issues.Add(Err("ENTRY_CONDITION_INVALID", $"شرط ورود مرحله‌ی «{s.Name}» نامعتبر است: {e}", s.WorkflowStageId));
            if (s.IsEscalatable && s.TimeoutHours == null)
                r.Issues.Add(Warn("ESCALATION_NO_SLA", $"مرحله‌ی «{s.Name}» Escalation‌پذیر است ولی مهلتی ندارد.", s.WorkflowStageId));
        }
    }

    /// <summary>
    /// SCRUM-103: قرارداد ماژول (آنچه کد ماژول از تعریف انتظار دارد). بدون قرارداد ثبت‌شده (فرآیند ساخته‌شده توسط کاربر) چیزی بررسی نمی‌شود.
    /// </summary>
    private void Contract(WorkflowValidationResult r, WorkflowDefinition def, List<WorkflowStage> stages, List<WorkflowTransition> transitions)
    {
        var contract = _contracts?.Find(def.Module, def.Category);
        if (contract == null) return;

        var byId = stages.ToDictionary(s => s.WorkflowStageId);
        string? NameOf(int? id) => id.HasValue && byId.TryGetValue(id.Value, out var s) ? s.Name : null;

        foreach (var action in contract.RequiredActions)
            if (!transitions.Any(t => t.ActionType == action))
                r.Issues.Add(Err("CONTRACT_ACTION_MISSING",
                    $"ماژول «{def.Module}» اکشن «{action}» را اجرا می‌کند؛ دست‌کم یک گذار با این اکشن لازم است."));

        foreach (var req in contract.RequiredStages)
        {
            var stage = stages.FirstOrDefault(s => s.Name == req.Name);
            if (stage == null)
                r.Issues.Add(Err("CONTRACT_STAGE_MISSING",
                    $"ماژول «{def.Module}» به مرحله‌ای با نام «{req.Name}» وابسته است؛ نام آن را تغییر ندهید و حذفش نکنید (برچسب نمایشی آزاد است)."));
            else if (req.StageType != null && stage.StageType != req.StageType)
                r.Issues.Add(Err("CONTRACT_STAGE_TYPE",
                    $"مرحله‌ی «{req.Name}» باید از نوع {req.StageType} باشد (ماژول «{def.Module}» شرکت‌کننده‌های آن را پویا تعیین می‌کند).", stage.WorkflowStageId));
        }

        foreach (var req in contract.RequiredTransitions)
            if (!transitions.Any(t => t.ActionType == req.ActionType && NameOf(t.FromStageId) == req.FromStage && NameOf(t.ToStageId) == req.ToStage))
                r.Issues.Add(Err("CONTRACT_TRANSITION_MISSING",
                    $"ماژول «{def.Module}» به گذار «{req.FromStage ?? "شروع"}» --{req.ActionType}--> «{req.ToStage ?? "پایان"}» وابسته است."));

        if (contract.RequiresBusinessStatus)
            foreach (var s in stages.Where(s => string.IsNullOrWhiteSpace(s.BusinessStatus)))
                r.Issues.Add(Err("CONTRACT_BUSINESS_STATUS_MISSING",
                    $"وضعیت رکورد «{def.Module}» از BusinessStatus مرحله خوانده می‌شود؛ مرحله‌ی «{s.Name}» BusinessStatus ندارد.", s.WorkflowStageId));
    }

    private async Task BindingAsync(WorkflowValidationResult r, WorkflowDefinition def, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(def.Module) || string.IsNullOrWhiteSpace(def.Category))
            r.Issues.Add(Err("DEFINITION_KEY_MISSING", "تعریف باید Module و Category داشته باشد تا قابل Binding باشد."));
        if (!def.IsActive)
            r.Issues.Add(Warn("DEFINITION_INACTIVE", "تعریف بایگانی/غیرفعال است؛ برای Instance جدید استفاده نمی‌شود."));

        var siblings = await _db.WorkflowDefinitions.AsNoTracking()
            .CountAsync(d => d.IsActive && d.Module == def.Module && d.Category == def.Category && d.WorkflowDefinitionId != def.WorkflowDefinitionId, ct);
        if (siblings > 0 && def.IsActive)
            r.Issues.Add(Warn("LEGACY_AMBIGUITY", "تعریف فعال دیگری با همان Module و Category وجود دارد؛ پیدا کردن Legacy مبهم می‌شود و فقط با Binding صریح قابل حل است."));

        var badPins = await _db.WorkflowBindings.AsNoTracking()
            .Where(b => b.WorkflowDefinitionId == def.WorkflowDefinitionId && b.PinnedVersionId != null)
            .Join(_db.WorkflowVersions, b => b.PinnedVersionId, v => v.WorkflowVersionId, (b, v) => new { b, v })
            .Where(x => x.v.WorkflowDefinitionId != def.WorkflowDefinitionId).CountAsync(ct);
        if (badPins > 0) r.Issues.Add(Err("BINDING_PIN_MISMATCH", "Binding به نسخه‌ای از تعریف دیگر پین شده است."));
    }
}
