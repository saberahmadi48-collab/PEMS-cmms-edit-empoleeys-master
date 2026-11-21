using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Services;

/// <summary>
/// Phase 4 - لایه‌ی مدیریتی روی Workflow Platform. هیچ موتور/مدل/جدول موازی ندارد: همه‌ی نوشتن‌ها از
/// IWorkflowDefinitionService / IWorkflowBindingService / IWorkflowVersionValidator عبور می‌کنند. اختیار = SystemAdmin AND WorkflowManagement.*
/// (سمت سرور). سیاست ویرایش (SCRUM-103): هر تعریف فعال که Binding فعال دارد قابل‌ویرایش است؛ آنچه کد ماژول از تعریف لازم دارد
/// با قرارداد ماژول (PemsWorkflowContracts) در اعتبارسنج نگه داشته می‌شود، پس ویرایش نمی‌تواند ماژول را بشکند.
/// </summary>
public class WorkflowAdministrationService : IWorkflowAdministrationService
{
    private static readonly string[] StandardActions = { "Approve", "Reject", "Return", "Hold", "Cancel", "Skip" };

    private readonly PEMSDbContext _db;
    private readonly IAccessControlService _access;
    private readonly IWorkflowDefinitionService _defs;
    private readonly IWorkflowBindingService _bindings;
    private readonly IWorkflowModuleContractRegistry? _contracts;

    public WorkflowAdministrationService(PEMSDbContext db, IAccessControlService access, IWorkflowDefinitionService defs, IWorkflowBindingService bindings,
        IWorkflowModuleContractRegistry? contracts = null)
    {
        _db = db; _access = access; _defs = defs; _bindings = bindings; _contracts = contracts;
    }

    private static string T(string fa, string en) => WorkOrderWorkflow.T(fa, en);
    private static WorkflowActor Wa(WorkflowAdminActor a) => new(a.UserId, a.Name);

    // ---------------- Authorization ----------------
    public async Task<WorkflowAdminResult> AuthorizeAsync(WorkflowAdminActor actor, string action)
    {
        if (!actor.Roles.Contains(SystemRoles.SystemAdmin))
            return WorkflowAdminResult.Fail(T("دسترسی مدیریت گردش‌کار فقط برای مدیر سیستم است.", "Workflow administration is restricted to system administrators."), forbidden: true);
        var d = await _access.AuthorizeForRolesAsync(actor.Roles, WorkflowManagementPermissions.Resource, action);
        return d.Allowed
            ? WorkflowAdminResult.Ok()
            : WorkflowAdminResult.Fail(T($"مجوز «WorkflowManagement.{action}» را ندارید.", $"You do not have the \"WorkflowManagement.{action}\" permission."), forbidden: true);
    }

    private async Task<WorkflowAdminResult?> Deny(WorkflowAdminActor actor, string action)
    {
        var r = await AuthorizeAsync(actor, action);
        return r.Success ? null : r;
    }

    // ---------------- Policy ----------------
    private (bool Editable, string Reason) EditablePolicy(WorkflowDefinition d, bool hasActiveBinding)
    {
        if (!d.IsActive) return (false, T("تعریف بایگانی شده است.", "The definition is archived."));
        if (!hasActiveBinding) return (false, T("Binding فعال ندارد.", "No active binding."));
        var contract = _contracts?.Find(d.Module, d.Category);
        return contract == null
            ? (true, T("قابل‌ویرایش.", "Editable."))
            : (true, T($"قابل‌ویرایش؛ قرارداد ماژول «{d.Module}» حفظ می‌شود ({ContractSummary(contract)}).",
                       $"Editable; the {d.Module} module contract is kept ({ContractSummary(contract)})."));
    }

    /// <summary>SCRUM-103: the parts of the definition the module code needs, shown so the administrator knows what is fixed.</summary>
    public static string ContractSummary(WorkflowModuleContract c)
    {
        var parts = new List<string>();
        if (c.RequiredActions.Count > 0) parts.Add("actions " + string.Join(", ", c.RequiredActions));
        if (c.RequiredStages.Count > 0) parts.Add("stages " + string.Join(", ", c.RequiredStages.Select(s => s.StageType == null ? s.Name : $"{s.Name} ({s.StageType})")));
        if (c.RequiredTransitions.Count > 0) parts.Add("transitions " + string.Join(", ", c.RequiredTransitions.Select(t => $"{t.FromStage ?? "start"} -{t.ActionType}-> {t.ToStage ?? "end"}")));
        if (c.RequiresBusinessStatus) parts.Add("a BusinessStatus on every stage");
        return string.Join("; ", parts);
    }

    private async Task<(WorkflowDefinition Def, bool Editable, string Reason)?> DefinitionPolicyAsync(int definitionId)
    {
        var d = await _db.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowDefinitionId == definitionId);
        if (d == null) return null;
        var bound = await _db.WorkflowBindings.AnyAsync(b => b.IsActive && b.WorkflowDefinitionId == definitionId);
        var (e, r) = EditablePolicy(d, bound);
        return (d, e, r);
    }

    private static WorkflowAdminResult NotEditable((WorkflowDefinition Def, bool Editable, string Reason)? p)
        => WorkflowAdminResult.Fail(p == null
            ? T("تعریف یافت نشد.", "Definition not found.")
            : T("این تعریف قابل‌ویرایش نیست: ", "This definition cannot be edited: ") + p.Value.Reason);

    private static byte[]? Token(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return Convert.FromBase64String(s); } catch (FormatException) { return null; }
    }

    private static string TokenOf(WorkflowVersion v) => v.RowVersion == null ? "" : Convert.ToBase64String(v.RowVersion);

    private async Task<string> CurrentTokenAsync(int versionId)
    {
        var v = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == versionId);
        return v == null ? "" : TokenOf(v);
    }

    /// <summary>پیام‌های پلتفرم (فارسی) برای UI انگلیسی به معادل انگلیسی نگاشت می‌شوند.</summary>
    private static string Localize(string message)
    {
        if (message.Contains(" | ")) { var p = message.Split(" | ", 2); return T(p[0], p[1]); }
        if (WorkOrderWorkflow.T("fa", "en") == "fa") return message;
        var map = new (string Fa, string En)[]
        {
            ("تغییرناپذیر", "The version is immutable. Clone it to make changes."),
            ("Draft/Validated باز", "This definition already has an open Draft/Validated version; publish or discard it first."),
            ("Binding فعالی به این نسخه", "An active binding pins this version; change the binding first."),
            ("Binding فعالی به این تعریف", "An active binding uses this definition; deactivate it first."),
            ("اعتبارسنجی در لحظه‌ی انتشار", "Validation failed at publication time."),
            ("فقط نسخه‌ی Validated", "Only a Validated version can be published."),
            ("همین Module/Category/Name", "A definition with the same Module/Category/Name already exists."),
            ("بایگانی شده", "The definition is archived."),
            ("Instance ای", "An instance references this version; it cannot be discarded."),
            ("Binding ای", "A binding pins this version; it cannot be discarded."),
            ("مبدأ/مقصد گذار", "The transition's source/target stage does not belong to this version."),
            ("یافت نشد", "Not found."),
        };
        foreach (var (fa, en) in map) if (message.Contains(fa)) return en;
        return message;
    }

    private async Task<WorkflowAdminResult> Run(int? versionId, Func<Task<WorkflowAdminResult>> op)
    {
        try { return await op(); }
        catch (WorkflowConcurrencyConflictException ex) { return WorkflowAdminResult.Fail(Localize(ex.Message), conflict: true); }
        catch (DbUpdateConcurrencyException) { _db.ChangeTracker.Clear(); return WorkflowAdminResult.Fail(T("این Draft گردش‌کار هم‌زمان توسط مدیر دیگری تغییر کرد؛ صفحه را دوباره بارگذاری کنید.", "This workflow draft was changed by another administrator. Reload before continuing."), conflict: true); }
        catch (WorkflowImmutableVersionException ex) { return WorkflowAdminResult.Fail(Localize(ex.Message)); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { _db.ChangeTracker.Clear(); return WorkflowAdminResult.Fail(Localize(ex.Message)); }
    }

    /// <summary>نسخه باید Draft/Validated و تعریفش قابل‌ویرایش باشد.</summary>
    private async Task<(WorkflowVersion? V, WorkflowAdminResult? Error)> EditableDraftAsync(int versionId)
    {
        var v = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == versionId);
        if (v == null) return (null, WorkflowAdminResult.Fail(T("نسخه یافت نشد.", "Version not found.")));
        var p = await DefinitionPolicyAsync(v.WorkflowDefinitionId);
        if (p == null || !p.Value.Editable) return (null, NotEditable(p));
        if (!WorkflowVersionStatus.IsEditable(v.Status)) return (null, WorkflowAdminResult.Fail(T($"نسخه‌ی {v.Status} تغییرناپذیر است؛ ابتدا Clone کنید.", $"A {v.Status} version is immutable; clone it first.")));
        return (v, null);
    }

    // ---------------- Queries ----------------
    public async Task<List<WorkflowDefinitionRow>> ListAsync(WorkflowAdminActor actor)
    {
        if (await Deny(actor, WorkflowManagementPermissions.View) != null) return new();
        var defs = await _db.WorkflowDefinitions.AsNoTracking().OrderBy(d => d.Module).ThenBy(d => d.Category).ThenBy(d => d.Name).ToListAsync();
        var versions = await _db.WorkflowVersions.AsNoTracking().ToListAsync();
        var binds = await _db.WorkflowBindings.AsNoTracking().Where(b => b.IsActive).ToListAsync();
        var audits = await _db.WorkflowConfigAudits.AsNoTracking().GroupBy(a => a.WorkflowDefinitionId).Select(g => new { g.Key, Last = g.Max(a => a.Timestamp) }).ToListAsync();
        var created = await _db.WorkflowConfigAudits.AsNoTracking().Where(a => a.Action == WorkflowConfigAuditActions.DefinitionCreated).Select(a => new { a.WorkflowDefinitionId, a.ActorName }).ToListAsync();
        var rows = new List<WorkflowDefinitionRow>();
        foreach (var d in defs)
        {
            var vs = versions.Where(v => v.WorkflowDefinitionId == d.WorkflowDefinitionId).ToList();
            var bs = binds.Where(b => b.WorkflowDefinitionId == d.WorkflowDefinitionId).ToList();
            var pub = vs.Where(v => v.Status == WorkflowVersionStatus.Published).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            var draft = vs.Where(v => WorkflowVersionStatus.IsEditable(v.Status)).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            var (ed, reason) = EditablePolicy(d, bs.Count > 0);
            var pinnedNums = bs.Select(b => vs.FirstOrDefault(v => v.WorkflowVersionId == b.PinnedVersionId)?.VersionNumber).Where(n => n.HasValue).Select(n => "v" + n);
            rows.Add(new WorkflowDefinitionRow
            {
                DefinitionId = d.WorkflowDefinitionId, Name = d.Name, NameFa = d.NameFa, Module = d.Module, Category = d.Category, IsActive = d.IsActive,
                PublishedVersion = pub?.VersionNumber, OpenDraftStatus = draft?.Status, OpenDraftVersion = draft?.VersionNumber,
                ActiveBindings = bs.Count, BindingSummary = bs.Count == 0 ? null : string.Join(", ", pinnedNums.DefaultIfEmpty("latest")),
                Editable = ed, EditableReason = reason,
                LastModified = new[] { audits.FirstOrDefault(a => a.Key == d.WorkflowDefinitionId)?.Last, vs.Max(v => v.PublishedDate), d.CreatedDate }.Where(x => x.HasValue).Max(),
                LastPublished = pub?.PublishedDate,
                CreatedBy = created.FirstOrDefault(c => c.WorkflowDefinitionId == d.WorkflowDefinitionId)?.ActorName
            });
        }
        return rows;
    }

    private async Task<List<WorkflowAuditRow>> AuditRowsAsync(int? definitionId, int? versionId, int take)
    {
        var q = _db.WorkflowConfigAudits.AsNoTracking().AsQueryable();
        if (definitionId.HasValue) q = q.Where(a => a.WorkflowDefinitionId == definitionId);
        if (versionId.HasValue) q = q.Where(a => a.WorkflowVersionId == versionId);
        var rows = await q.OrderByDescending(a => a.WorkflowConfigAuditId).Take(take).ToListAsync();
        var vids = rows.Where(r => r.WorkflowVersionId.HasValue).Select(r => r.WorkflowVersionId!.Value).Distinct().ToList();
        var nums = await _db.WorkflowVersions.AsNoTracking().Where(v => vids.Contains(v.WorkflowVersionId)).ToDictionaryAsync(v => v.WorkflowVersionId, v => v.VersionNumber);
        return rows.Select(r => new WorkflowAuditRow
        {
            Timestamp = r.Timestamp, Action = r.Action, Actor = r.ActorName ?? (r.ActorUserId.HasValue ? "#" + r.ActorUserId : null),
            VersionNumber = r.WorkflowVersionId.HasValue && nums.TryGetValue(r.WorkflowVersionId.Value, out var n) ? n : null,
            Previous = r.PreviousState, Current = r.CurrentState, Details = r.Details
        }).ToList();
    }

    private async Task<List<WorkflowUsageRow>> UsageAsync(int definitionId)
    {
        var vs = await _db.WorkflowVersions.AsNoTracking().Where(v => v.WorkflowDefinitionId == definitionId).ToListAsync();
        var vids = vs.Select(v => v.WorkflowVersionId).ToList();
        var inst = await _db.WorkflowInstances.AsNoTracking().Where(i => vids.Contains(i.WorkflowVersionId)).GroupBy(i => new { i.WorkflowVersionId, i.Status }).Select(g => new { g.Key.WorkflowVersionId, g.Key.Status, N = g.Count() }).ToListAsync();
        return vs.OrderBy(v => v.VersionNumber).Select(v =>
        {
            int C(string s) => inst.Where(i => i.WorkflowVersionId == v.WorkflowVersionId && i.Status == s).Sum(i => i.N);
            return new WorkflowUsageRow { VersionNumber = v.VersionNumber, Running = C("Running"), Completed = C("Completed"), Cancelled = C("Cancelled"), Rejected = C("Rejected") };
        }).ToList();
    }

    public async Task<WorkflowDefinitionDetails?> GetDetailsAsync(WorkflowAdminActor actor, int definitionId)
    {
        if (await Deny(actor, WorkflowManagementPermissions.View) != null) return null;
        var row = (await ListAsync(actor)).FirstOrDefault(r => r.DefinitionId == definitionId);
        if (row == null) return null;
        var d = await _db.WorkflowDefinitions.AsNoTracking().FirstAsync(x => x.WorkflowDefinitionId == definitionId);
        var vs = await _db.WorkflowVersions.AsNoTracking().Where(v => v.WorkflowDefinitionId == definitionId).OrderByDescending(v => v.VersionNumber).ToListAsync();
        var vids = vs.Select(v => v.WorkflowVersionId).ToList();
        var stageCounts = await _db.WorkflowStages.AsNoTracking().Where(s => vids.Contains(s.WorkflowVersionId)).GroupBy(s => s.WorkflowVersionId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var trCounts = await _db.WorkflowTransitions.AsNoTracking().Where(s => vids.Contains(s.WorkflowVersionId)).GroupBy(s => s.WorkflowVersionId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var inst = await _db.WorkflowInstances.AsNoTracking().Where(i => vids.Contains(i.WorkflowVersionId)).GroupBy(i => new { i.WorkflowVersionId, i.Status }).Select(g => new { g.Key.WorkflowVersionId, g.Key.Status, N = g.Count() }).ToListAsync();
        var binds = await _db.WorkflowBindings.AsNoTracking().Where(b => b.WorkflowDefinitionId == definitionId).OrderByDescending(b => b.IsActive).ToListAsync();
        var pinned = binds.Where(b => b.IsActive && b.PinnedVersionId.HasValue).Select(b => b.PinnedVersionId!.Value).ToHashSet();
        var det = new WorkflowDefinitionDetails { Definition = row, Description = d.Description };
        det.Versions = vs.Select(v => new WorkflowVersionRow
        {
            VersionId = v.WorkflowVersionId, VersionNumber = v.VersionNumber, Status = v.Status,
            Stages = stageCounts.FirstOrDefault(x => x.Key == v.WorkflowVersionId)?.N ?? 0, Transitions = trCounts.FirstOrDefault(x => x.Key == v.WorkflowVersionId)?.N ?? 0,
            PublishedDate = v.PublishedDate, RetiredDate = v.RetiredDate, ChangeSummary = v.ChangeSummary, IsBound = pinned.Contains(v.WorkflowVersionId),
            ClonedFromVersion = v.ClonedFromVersionId.HasValue ? vs.FirstOrDefault(x => x.WorkflowVersionId == v.ClonedFromVersionId)?.VersionNumber : null,
            RunningInstances = inst.Where(i => i.WorkflowVersionId == v.WorkflowVersionId && i.Status == "Running").Sum(i => i.N),
            TotalInstances = inst.Where(i => i.WorkflowVersionId == v.WorkflowVersionId).Sum(i => i.N)
        }).ToList();
        det.Bindings = binds.Select(b => new WorkflowBindingRow
        {
            BindingId = b.WorkflowBindingId, Module = b.Module, EntityType = b.EntityType, Context = b.Context, IsActive = b.IsActive, PinnedVersionId = b.PinnedVersionId,
            PinnedVersionNumber = vs.FirstOrDefault(v => v.WorkflowVersionId == b.PinnedVersionId)?.VersionNumber, DefinitionId = definitionId, DefinitionName = d.Name, Editable = row.Editable
        }).ToList();
        det.Usage = await UsageAsync(definitionId);
        det.TotalInstances = det.Usage.Sum(u => u.Running + u.Completed + u.Cancelled + u.Rejected);
        det.Audit = await AuditRowsAsync(definitionId, null, 60);
        return det;
    }

    public async Task<WorkflowDesignerModel?> GetDesignerAsync(WorkflowAdminActor actor, int versionId)
    {
        if (await Deny(actor, WorkflowManagementPermissions.View) != null) return null;
        var v = await _db.WorkflowVersions.AsNoTracking().Include(x => x.Definition).FirstOrDefaultAsync(x => x.WorkflowVersionId == versionId);
        if (v == null) return null;
        var pol = (await DefinitionPolicyAsync(v.WorkflowDefinitionId))!.Value;
        var stages = await _db.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == versionId).OrderBy(s => s.StageOrder).ThenBy(s => s.WorkflowStageId).ToListAsync();
        var trs = await _db.WorkflowTransitions.AsNoTracking().Where(t => t.WorkflowVersionId == versionId).OrderBy(t => t.Priority).ThenBy(t => t.WorkflowTransitionId).ToListAsync();
        var m = new WorkflowDesignerModel
        {
            DefinitionId = v.WorkflowDefinitionId, DefinitionName = v.Definition!.Name, DefinitionNameFa = v.Definition.NameFa, VersionId = v.WorkflowVersionId,
            VersionNumber = v.VersionNumber, Status = v.Status, DefinitionEditable = pol.Editable, DefinitionEditableReason = pol.Reason,
            CanEditStructure = pol.Editable && WorkflowVersionStatus.IsEditable(v.Status), RowVersion = TokenOf(v), ChangeSummary = v.ChangeSummary,
            Graph = WorkflowGraphLayout.Compute(stages, trs)
        };
        var last = await LastEditorAsync(versionId);
        m.LastEditor = last.Name ?? (last.UserId.HasValue ? "#" + last.UserId : null);
        m.Stages = stages.Select(s => new WorkflowStageEditModel
        {
            StageId = s.WorkflowStageId, Name = s.Name, NameFa = s.NameFa, BusinessStatus = s.BusinessStatus, StageOrder = s.StageOrder, StageType = s.StageType,
            ResponsibleRole = s.ResponsibleRole, ResponsibleEmployeeId = s.ResponsibleEmployeeId, ApprovalStrategy = s.ApprovalStrategy, MinimumApprovalCount = s.MinimumApprovalCount,
            IsMandatory = s.IsMandatory, TimeoutHours = s.TimeoutHours, Description = s.Description
        }).ToList();
        m.Transitions = trs.Select(t => new WorkflowTransitionEditModel
        {
            TransitionId = t.WorkflowTransitionId, FromStageId = t.FromStageId, ToStageId = t.ToStageId, ActionType = t.ActionType, Label = t.Label, LabelFa = t.LabelFa,
            RequiredPermission = t.RequiredPermission, ConditionExpression = t.ConditionExpression, Priority = t.Priority, ForbidInitiator = t.ForbidInitiator
        }).ToList();
        m.Roles = await _db.Roles.AsNoTracking().Where(r => r.Name != null).OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync();
        m.Permissions = await _db.Permissions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();
        m.ActionTypes = StandardActions.Concat(trs.Select(t => t.ActionType)).Distinct().OrderBy(x => x).ToList();
        m.Employees = (await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FirstName).ThenBy(e => e.LastName).Take(500).Select(e => new { e.EmployeeId, e.FirstName, e.LastName }).ToListAsync())
            .Select(e => (e.EmployeeId, (e.FirstName + " " + e.LastName).Trim())).ToList();
        m.RunningInstances = await _db.WorkflowInstances.AsNoTracking().CountAsync(i => i.WorkflowVersionId == versionId && i.Status == "Running");
        m.Audit = await AuditRowsAsync(null, versionId, 30);
        return m;
    }

    public async Task<List<WorkflowBindingRow>> ListBindingsAsync(WorkflowAdminActor actor)
    {
        if (await Deny(actor, WorkflowManagementPermissions.View) != null) return new();
        var defs = await _db.WorkflowDefinitions.AsNoTracking().ToListAsync();
        var vs = await _db.WorkflowVersions.AsNoTracking().ToListAsync();
        var bs = await _db.WorkflowBindings.AsNoTracking().OrderBy(b => b.Module).ThenBy(b => b.EntityType).ToListAsync();
        var actives = bs.Where(b => b.IsActive).Select(b => b.WorkflowDefinitionId).ToHashSet();
        return bs.Select(b =>
        {
            var d = defs.First(x => x.WorkflowDefinitionId == b.WorkflowDefinitionId);
            return new WorkflowBindingRow
            {
                BindingId = b.WorkflowBindingId, Module = b.Module, EntityType = b.EntityType, Context = b.Context, IsActive = b.IsActive, PinnedVersionId = b.PinnedVersionId,
                PinnedVersionNumber = vs.FirstOrDefault(v => v.WorkflowVersionId == b.PinnedVersionId)?.VersionNumber, DefinitionId = d.WorkflowDefinitionId, DefinitionName = d.Name,
                Editable = EditablePolicy(d, actives.Contains(d.WorkflowDefinitionId)).Editable
            };
        }).ToList();
    }

    public async Task<WorkflowBindingImpact?> GetBindingImpactAsync(WorkflowAdminActor actor, int bindingId, int targetVersionId)
    {
        if (await Deny(actor, WorkflowManagementPermissions.View) != null) return null;
        var b = await _db.WorkflowBindings.AsNoTracking().Include(x => x.Definition).FirstOrDefaultAsync(x => x.WorkflowBindingId == bindingId);
        var target = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == targetVersionId);
        if (b == null || target == null) return null;
        var cur = b.PinnedVersionId.HasValue ? await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == b.PinnedVersionId) : null;
        var usage = await UsageAsync(b.WorkflowDefinitionId);
        return new WorkflowBindingImpact
        {
            BindingId = bindingId, DefinitionId = b.WorkflowDefinitionId, Definition = b.Definition!.Name, CurrentVersion = cur?.VersionNumber, TargetVersion = target.VersionNumber,
            RunningByVersion = usage.Where(u => u.Running > 0).ToList(), RunningInstances = usage.Sum(u => u.Running)
        };
    }

    private async Task<(int? UserId, string? Name)> LastEditorAsync(int versionId)
    {
        var editing = new[] { WorkflowConfigAuditActions.VersionCreated, WorkflowConfigAuditActions.VersionCloned, WorkflowConfigAuditActions.DraftEdited };
        var a = await _db.WorkflowConfigAudits.AsNoTracking().Where(x => x.WorkflowVersionId == versionId && editing.Contains(x.Action))
            .OrderByDescending(x => x.WorkflowConfigAuditId).FirstOrDefaultAsync();
        return (a?.ActorUserId, a?.ActorName);
    }

    // ---------------- Commands ----------------
    public async Task<WorkflowAdminResult> CloneVersionAsync(WorkflowAdminActor actor, int sourceVersionId, string? changeSummary)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Edit) is { } d) return d;
        var src = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == sourceVersionId);
        if (src == null) return WorkflowAdminResult.Fail(T("نسخه یافت نشد.", "Version not found."));
        var p = await DefinitionPolicyAsync(src.WorkflowDefinitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        if (src.Status == WorkflowVersionStatus.Discarded) return WorkflowAdminResult.Fail(T("Draft رهاشده قابل Clone نیست.", "A discarded draft cannot be cloned."));
        return await Run(null, async () =>
        {
            var v = await _defs.CloneVersionAsync(sourceVersionId, Wa(actor), changeSummary);
            return WorkflowAdminResult.Ok(v.WorkflowVersionId, TokenOf(v));
        });
    }

    public async Task<WorkflowAdminResult> UpdateDefinitionAsync(WorkflowAdminActor actor, int definitionId, string name, string? nameFa, string? description)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Edit) is { } d) return d;
        var p = await DefinitionPolicyAsync(definitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        return await Run(null, async () => { await _defs.UpdateDefinitionAsync(definitionId, name, nameFa, description, Wa(actor)); return WorkflowAdminResult.Ok(definitionId); });
    }

    public async Task<WorkflowAdminResult> DiscardDraftAsync(WorkflowAdminActor actor, int versionId, string? rowVersion)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Edit) is { } d) return d;
        var (v, err) = await EditableDraftAsync(versionId);
        if (err != null) return err;
        return await Run(versionId, async () => { await _defs.DiscardDraftAsync(versionId, Wa(actor), default, Token(rowVersion)); return WorkflowAdminResult.Ok(v!.WorkflowDefinitionId); });
    }

    private async Task<WorkflowAdminResult> Edit(WorkflowAdminActor actor, int versionId, Func<Task> op, int? stageIdCheck = null, int? transitionIdCheck = null)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Edit) is { } d) return d;
        var (_, err) = await EditableDraftAsync(versionId);
        if (err != null) return err;
        if (stageIdCheck.HasValue && !await _db.WorkflowStages.AnyAsync(s => s.WorkflowStageId == stageIdCheck && s.WorkflowVersionId == versionId))
            return WorkflowAdminResult.Fail(T("مرحله متعلق به این نسخه نیست.", "The stage does not belong to this version."));
        if (transitionIdCheck.HasValue && !await _db.WorkflowTransitions.AnyAsync(t => t.WorkflowTransitionId == transitionIdCheck && t.WorkflowVersionId == versionId))
            return WorkflowAdminResult.Fail(T("گذار متعلق به این نسخه نیست.", "The transition does not belong to this version."));
        return await Run(versionId, async () => { await op(); return WorkflowAdminResult.Ok(versionId, await CurrentTokenAsync(versionId)); });
    }

    private static WorkflowStageInput ToInput(WorkflowStageEditModel m, WorkflowStage? existing) => new()
    {
        Name = m.Name, NameFa = string.IsNullOrWhiteSpace(m.NameFa) ? null : m.NameFa.Trim(), BusinessStatus = string.IsNullOrWhiteSpace(m.BusinessStatus) ? null : m.BusinessStatus.Trim(),
        StageOrder = m.StageOrder, StageType = m.StageType, ResponsibleRole = string.IsNullOrWhiteSpace(m.ResponsibleRole) ? null : m.ResponsibleRole.Trim(),
        ResponsibleEmployeeId = m.ResponsibleEmployeeId, ApprovalStrategy = m.ApprovalStrategy, MinimumApprovalCount = m.MinimumApprovalCount, IsMandatory = m.IsMandatory,
        TimeoutHours = m.TimeoutHours, Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim(),
        // فیلدهایی که UI ویرایش نمی‌کند حفظ می‌شوند (UpdateStage همه‌ی فیلدها را بازنویسی می‌کند)
        IsSkippable = existing?.IsSkippable ?? false, IsDelegatable = existing?.IsDelegatable ?? true, IsEscalatable = existing?.IsEscalatable ?? false,
        EscalationRole = existing?.EscalationRole, NotificationKey = existing?.NotificationKey
    };

    private static WorkflowTransitionInput ToInput(WorkflowTransitionEditModel m) => new()
    {
        FromStageId = m.FromStageId, ToStageId = m.ToStageId, ActionType = (m.ActionType ?? "").Trim(), Priority = m.Priority, ForbidInitiator = m.ForbidInitiator,
        Label = string.IsNullOrWhiteSpace(m.Label) ? null : m.Label.Trim(), LabelFa = string.IsNullOrWhiteSpace(m.LabelFa) ? null : m.LabelFa.Trim(),
        RequiredPermission = string.IsNullOrWhiteSpace(m.RequiredPermission) ? null : m.RequiredPermission.Trim(),
        ConditionExpression = string.IsNullOrWhiteSpace(m.ConditionExpression) ? null : m.ConditionExpression.Trim()
    };

    public Task<WorkflowAdminResult> AddStageAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, WorkflowStageEditModel input)
        => Edit(actor, versionId, async () =>
        {
            if (input.StageOrder <= 0)
                input.StageOrder = ((await _db.WorkflowStages.Where(s => s.WorkflowVersionId == versionId).MaxAsync(s => (int?)s.StageOrder)) ?? 0) + 1;
            await _defs.AddStageAsync(versionId, ToInput(input, null), Wa(actor), default, Token(rowVersion));
        });

    public Task<WorkflowAdminResult> UpdateStageAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, WorkflowStageEditModel input)
        => Edit(actor, versionId, async () =>
        {
            var existing = await _db.WorkflowStages.AsNoTracking().FirstAsync(s => s.WorkflowStageId == input.StageId);
            await _defs.UpdateStageAsync(input.StageId, ToInput(input, existing), Wa(actor), default, Token(rowVersion));
        }, stageIdCheck: input.StageId);

    public Task<WorkflowAdminResult> RemoveStageAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, int stageId)
        => Edit(actor, versionId, () => _defs.RemoveStageAsync(stageId, Wa(actor), default, Token(rowVersion)), stageIdCheck: stageId);

    public Task<WorkflowAdminResult> AddTransitionAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, WorkflowTransitionEditModel input)
        => Edit(actor, versionId, () => _defs.AddTransitionAsync(versionId, ToInput(input), Wa(actor), default, Token(rowVersion)));

    public Task<WorkflowAdminResult> UpdateTransitionAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, WorkflowTransitionEditModel input)
        => Edit(actor, versionId, () => _defs.UpdateTransitionAsync(input.TransitionId, ToInput(input), Wa(actor), default, Token(rowVersion)), transitionIdCheck: input.TransitionId);

    public Task<WorkflowAdminResult> RemoveTransitionAsync(WorkflowAdminActor actor, int versionId, string? rowVersion, int transitionId)
        => Edit(actor, versionId, () => _defs.RemoveTransitionAsync(transitionId, Wa(actor), default, Token(rowVersion)), transitionIdCheck: transitionId);

    public async Task<WorkflowAdminResult> ValidateAsync(WorkflowAdminActor actor, int versionId, string? rowVersion)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Validate) is { } d) return d;
        var (_, err) = await EditableDraftAsync(versionId);
        if (err != null) return err;
        return await Run(versionId, async () =>
        {
            var r = await _defs.ValidateVersionAsync(versionId, Wa(actor), default, Token(rowVersion));
            var token = await CurrentTokenAsync(versionId);
            return r.IsValid
                ? WorkflowAdminResult.Ok(versionId, token, r)
                : new WorkflowAdminResult { Success = false, Id = versionId, RowVersion = token, Validation = r, Error = T("گردش‌کار قابل انتشار نیست.", "The workflow cannot be published.") };
        });
    }

    public async Task<WorkflowAdminResult> PublishAsync(WorkflowAdminActor actor, int versionId, string? rowVersion)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Publish) is { } d) return d;
        var (v, err) = await EditableDraftAsync(versionId);
        if (err != null) return err;
        // چهارچشمی: ناشر نباید آخرین ویرایشگر همین Draft باشد (آخرین ویرایشگر از WorkflowConfigAudits)
        var last = await LastEditorAsync(versionId);
        if (last.UserId.HasValue && last.UserId.Value == actor.UserId)
            return WorkflowAdminResult.Fail(T("ناشر نمی‌تواند همان آخرین ویرایشگر این Draft باشد؛ مدیر دیگری باید منتشر کند.", "The publisher cannot be the last editor of this draft; another administrator must publish it."), forbidden: true);
        return await Run(versionId, async () =>
        {
            var pub = await _defs.PublishVersionAsync(versionId, Wa(actor), null, default, Token(rowVersion));
            return WorkflowAdminResult.Ok(pub.WorkflowVersionId, TokenOf(pub));
        });
    }

    public async Task<WorkflowAdminResult> RetireAsync(WorkflowAdminActor actor, int versionId)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Retire) is { } d) return d;
        var v = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == versionId);
        if (v == null) return WorkflowAdminResult.Fail(T("نسخه یافت نشد.", "Version not found."));
        var p = await DefinitionPolicyAsync(v.WorkflowDefinitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        return await Run(versionId, async () => { await _defs.RetireVersionAsync(versionId, Wa(actor)); return WorkflowAdminResult.Ok(versionId); });
    }

    public async Task<WorkflowAdminResult> ArchiveDefinitionAsync(WorkflowAdminActor actor, int definitionId)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Retire) is { } d) return d;
        var p = await DefinitionPolicyAsync(definitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        return await Run(null, async () => { await _defs.ArchiveDefinitionAsync(definitionId, Wa(actor)); return WorkflowAdminResult.Ok(definitionId); });
    }

    public async Task<WorkflowAdminResult> ChangeBindingAsync(WorkflowAdminActor actor, int bindingId, int targetVersionId, bool confirmed)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Bind) is { } d) return d;
        var b = await _db.WorkflowBindings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowBindingId == bindingId);
        if (b == null) return WorkflowAdminResult.Fail(T("Binding یافت نشد.", "Binding not found."));
        var p = await DefinitionPolicyAsync(b.WorkflowDefinitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        var target = await _db.WorkflowVersions.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowVersionId == targetVersionId);
        if (target == null || target.WorkflowDefinitionId != b.WorkflowDefinitionId || target.Status != WorkflowVersionStatus.Published)
            return WorkflowAdminResult.Fail(T("نسخه‌ی هدف باید نسخه‌ی Published همین تعریف باشد.", "The target must be a Published version of the same definition."));
        if (!confirmed) return WorkflowAdminResult.Fail(T("تأیید صریح لازم است.", "Explicit confirmation is required."));
        return await Run(null, async () => { await _bindings.ChangeAsync(bindingId, targetVersionId, b.EffectiveFrom, b.EffectiveTo, Wa(actor)); return WorkflowAdminResult.Ok(bindingId); });
    }

    public async Task<WorkflowAdminResult> DeactivateBindingAsync(WorkflowAdminActor actor, int bindingId, bool confirmed)
    {
        if (await Deny(actor, WorkflowManagementPermissions.Bind) is { } d) return d;
        var b = await _db.WorkflowBindings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkflowBindingId == bindingId);
        if (b == null) return WorkflowAdminResult.Fail(T("Binding یافت نشد.", "Binding not found."));
        var p = await DefinitionPolicyAsync(b.WorkflowDefinitionId);
        if (p == null || !p.Value.Editable) return NotEditable(p);
        if (!confirmed) return WorkflowAdminResult.Fail(T("تأیید صریح لازم است.", "Explicit confirmation is required."));
        return await Run(null, async () => { await _bindings.DeactivateAsync(bindingId, Wa(actor)); return WorkflowAdminResult.Ok(bindingId); });
    }
}
