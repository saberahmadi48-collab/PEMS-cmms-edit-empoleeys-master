using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// ==============================================================================
/// دستورهای چرخه‌عمر استراتژی RCM (Draft → InReview → Approved → Active → Superseded/Retired).
/// تمام قوانین JA1011 در StrategyRules (سمت سرور) اعمال می‌شود؛ تایید از موتور گردش‌کار مرکزی
/// (IWorkflowRuntime) می‌گذرد و تفکیک وظایف اجباری است (نویسنده/ارسال‌کننده ≠ تاییدکننده).
/// نسخه‌ی تاییدشده هرگز درجا ویرایش نمی‌شود: ویرایش = نسخه‌ی جدید Draft با حفظ نسخه‌ی قبل.
/// هر رویداد در MaintenanceStrategyHistory (Append-only) و AuditLog سراسری ثبت می‌شود.
/// ==============================================================================
/// </summary>
public class MaintenanceStrategyCommandService : IMaintenanceStrategyCommandService
{
    private static readonly string[] DiffExcluded = { "RowVersion" };

    private readonly PEMSDbContext _context;
    private readonly IWorkflowRuntime _runtime;

    public MaintenanceStrategyCommandService(PEMSDbContext context, IWorkflowRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
    }

    public async Task<List<RuleError>> ValidateAsync(StrategyInputDto input)
    {
        Normalize(input);
        return await ValidateAsync(input, existing: null);
    }

    // ==================== Create ====================

    public async Task<OpResult> CreateAsync(StrategyInputDto input, StrategyActor actor)
    {
        Normalize(input);
        var errors = await ValidateAsync(input, existing: null);
        if (errors.Count > 0) return OpResult.Fail(errors);

        var mode = await _context.FailureModes.AsNoTracking().FirstAsync(x => x.FailureModeId == input.FailureModeId);

        var strategy = new MaintenanceStrategy
        {
            Status = RcmCodes.Status.Draft,
            VersionNumber = 1,
            FamilyId = Guid.NewGuid(),
            AuthorEmployeeId = actor.EmployeeId,
            IsActive = true,
            CreatedDate = DateTime.Now
        };
        Apply(strategy, input, mode);
        strategy.MaintenanceStrategyTypeId = await ResolveTypeIdAsync(strategy.StrategyType);

        _context.MaintenanceStrategies.Add(strategy);
        await _context.SaveChangesAsync();

        AddHistory(strategy, RcmCodes.HistoryAction.Created, null, strategy.Status, null, null, actor);
        await _context.SaveChangesAsync();

        return OpResult.Ok(strategy.MaintenanceStrategyId);
    }

    // ==================== Update / New version ====================

    public async Task<OpResult> UpdateAsync(int maintenanceStrategyId, StrategyInputDto input, StrategyActor actor)
    {
        var strategy = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (strategy == null) return OpResult.Fail(new RuleError("Rcm_Err_StrategyNotFound"));

        if (strategy.Status is RcmCodes.Status.InReview)
            return OpResult.Fail(new RuleError("Rcm_Err_LockedInReview"));
        if (strategy.Status is RcmCodes.Status.Superseded or RcmCodes.Status.Retired || !strategy.IsActive)
            return OpResult.Fail(new RuleError("Rcm_Err_NotEditableStatus", null, strategy.Status));

        Normalize(input);
        var errors = await ValidateAsync(input, strategy);
        if (errors.Count > 0) return OpResult.Fail(errors);

        var mode = await _context.FailureModes.AsNoTracking().FirstAsync(x => x.FailureModeId == input.FailureModeId);

        if (strategy.Status == RcmCodes.Status.Draft)
        {
            // کنترل Concurrency: RowVersion فرم باید با ردیف فعلی یکی باشد
            if (input.RowVersion != null && strategy.RowVersion != null && !input.RowVersion.SequenceEqual(strategy.RowVersion))
                return OpResult.Fail(new RuleError("Rcm_Err_ConcurrencyConflict"));
            if (input.RowVersion != null)
                _context.Entry(strategy).Property(x => x.RowVersion).OriginalValue = input.RowVersion;

            var changes = BuildDiff(strategy, input, mode);
            Apply(strategy, input, mode);
            strategy.MaintenanceStrategyTypeId = await ResolveTypeIdAsync(strategy.StrategyType);

            AddHistory(strategy, RcmCodes.HistoryAction.Edited, strategy.Status, strategy.Status, changes, null, actor);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return OpResult.Fail(new RuleError("Rcm_Err_ConcurrencyConflict"));
            }
            return OpResult.Ok(strategy.MaintenanceStrategyId);
        }

        // Approved / Active: نسخه‌ی جدید (نسخه‌ی تاییدشده درجا تغییر نمی‌کند)
        var draftExists = await _context.MaintenanceStrategies.AnyAsync(x =>
            x.FamilyId == strategy.FamilyId && x.IsActive && (x.Status == RcmCodes.Status.Draft || x.Status == RcmCodes.Status.InReview));
        if (draftExists) return OpResult.Fail(new RuleError("Rcm_Err_DraftVersionExists"));

        var maxVersion = await _context.MaintenanceStrategies.Where(x => x.FamilyId == strategy.FamilyId).MaxAsync(x => x.VersionNumber);
        var diff = BuildDiff(strategy, input, mode);

        var next = new MaintenanceStrategy
        {
            Status = RcmCodes.Status.Draft,
            VersionNumber = maxVersion + 1,
            FamilyId = strategy.FamilyId,
            SupersedesStrategyId = strategy.MaintenanceStrategyId,
            AuthorEmployeeId = actor.EmployeeId,
            EquipmentId = strategy.EquipmentId,
            IsActive = true,
            CreatedDate = DateTime.Now
        };
        Apply(next, input, mode);
        next.MaintenanceStrategyTypeId = await ResolveTypeIdAsync(next.StrategyType);

        _context.MaintenanceStrategies.Add(next);
        await _context.SaveChangesAsync();

        AddHistory(next, RcmCodes.HistoryAction.NewVersion, null, next.Status, diff, $"v{strategy.VersionNumber} -> v{next.VersionNumber}", actor);
        await _context.SaveChangesAsync();

        return OpResult.Ok(next.MaintenanceStrategyId, "Rcm_Info_NewVersionCreated");
    }

    // ==================== Workflow ====================

    public async Task<OpResult> SubmitForReviewAsync(int maintenanceStrategyId, StrategyActor actor)
    {
        var s = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (s == null) return OpResult.Fail(new RuleError("Rcm_Err_StrategyNotFound"));
        if (s.Status != RcmCodes.Status.Draft)
            return OpResult.Fail(new RuleError("Rcm_Err_InvalidTransition", null, s.Status, RcmCodes.Status.InReview));

        // دوباره اعتبارسنجی کامل (Draft ممکن است قدیمی/ناقص باشد)
        var errors = await ValidateAsync(Snapshot(s), s, skipStartDate: true);
        if (errors.Count > 0) return OpResult.Fail(errors);

        try
        {
            await _runtime.StartInstanceAsync(
                MaintenanceStrategyWorkflowSeeder.Module, MaintenanceStrategyWorkflowSeeder.BusinessObjectType,
                s.MaintenanceStrategyId, MaintenanceStrategyWorkflowSeeder.Category, null, actor.EmployeeId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return OpResult.Fail(new RuleError("Rcm_Err_WorkflowUnavailable", null, ex.Message));
        }

        s.Status = RcmCodes.Status.InReview;
        s.SubmittedByEmployeeId = actor.EmployeeId;
        s.SubmittedAt = DateTime.Now;
        AddHistory(s, RcmCodes.HistoryAction.Submitted, RcmCodes.Status.Draft, s.Status, null, null, actor);
        await _context.SaveChangesAsync();
        return OpResult.Ok(s.MaintenanceStrategyId);
    }

    public async Task<OpResult> ReviewAsync(int maintenanceStrategyId, bool approve, string? comment, StrategyActor actor)
    {
        var s = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (s == null) return OpResult.Fail(new RuleError("Rcm_Err_StrategyNotFound"));
        if (s.Status != RcmCodes.Status.InReview)
            return OpResult.Fail(new RuleError("Rcm_Err_InvalidTransition", null, s.Status, approve ? RcmCodes.Status.Approved : RcmCodes.Status.Draft));

        // تفکیک وظایف: نویسنده یا ارسال‌کننده نمی‌تواند تاییدکننده/ردکننده‌ی خودش باشد
        if (IsAuthorOrSubmitter(s, actor))
            return OpResult.Fail(new RuleError("Rcm_Err_SeparationOfDuties"));

        if (!approve && string.IsNullOrWhiteSpace(comment))
            return OpResult.Fail(new RuleError("Rcm_Err_RejectCommentRequired", "comment"));

        var instance = await _runtime.GetInstanceAsync(MaintenanceStrategyWorkflowSeeder.BusinessObjectType, s.MaintenanceStrategyId);
        if (instance == null) return OpResult.Fail(new RuleError("Rcm_Err_WorkflowUnavailable"));

        try
        {
            await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, approve ? "Approve" : "Reject",
                actor.EmployeeId, actor.Roles, comment, null, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return OpResult.Fail(new RuleError("Rcm_Err_WorkflowActorNotAllowed", null, ex.Message));
        }

        var from = s.Status;
        if (approve)
        {
            s.Status = RcmCodes.Status.Approved;
            s.ApprovedByEmployeeId = actor.EmployeeId;
            s.ApprovedBy = actor.UserName;
            s.ApprovedAt = DateTime.Now;
            AddHistory(s, RcmCodes.HistoryAction.Approved, from, s.Status, null, comment, actor);
        }
        else
        {
            s.Status = RcmCodes.Status.Draft;
            AddHistory(s, RcmCodes.HistoryAction.Rejected, from, s.Status, null, comment, actor);
        }

        await _context.SaveChangesAsync();
        return OpResult.Ok(s.MaintenanceStrategyId);
    }

    public async Task<OpResult> ActivateAsync(int maintenanceStrategyId, bool moveAssignmentsFromPreviousVersion, StrategyActor actor)
    {
        var s = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (s == null) return OpResult.Fail(new RuleError("Rcm_Err_StrategyNotFound"));
        if (s.Status != RcmCodes.Status.Approved)
            return OpResult.Fail(new RuleError("Rcm_Err_InvalidTransition", null, s.Status, RcmCodes.Status.Active));

        var previous = s.SupersedesStrategyId.HasValue
            ? await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == s.SupersedesStrategyId.Value)
            : null;

        List<MaintenanceStrategyAssignment> toMove = new();
        if (previous != null && moveAssignmentsFromPreviousVersion)
        {
            toMove = await _context.MaintenanceStrategyAssignments
                .Include(a => a.Equipment)
                .Where(a => a.MaintenanceStrategyId == previous.MaintenanceStrategyId && a.IsActive)
                .ToListAsync();

            // بحرانی‌بودن تگ باید نوع جدید را مجاز کند، وگرنه کل فعال‌سازی رد می‌شود
            var tagIds = toMove.Select(a => a.EquipmentId).ToList();
            var crit = await _context.EquipmentCriticalities.AsNoTracking()
                .Where(c => c.IsActive && tagIds.Contains(c.EquipmentId))
                .Select(c => new { c.EquipmentId, c.CriticalityClass }).ToListAsync();
            var blocked = toMove
                .Select(a => StrategyRules.CheckCriticalityAllowsType(crit.FirstOrDefault(c => c.EquipmentId == a.EquipmentId)?.CriticalityClass, s.StrategyType))
                .Where(e => e != null).Select(e => e!).DistinctBy(e => e.Code).ToList();
            if (blocked.Count > 0) return OpResult.Fail(blocked);
        }

        var from = s.Status;
        s.Status = RcmCodes.Status.Active;
        AddHistory(s, RcmCodes.HistoryAction.Activated, from, s.Status, null, null, actor);

        if (previous != null)
        {
            var prevFrom = previous.Status;
            previous.Status = RcmCodes.Status.Superseded;
            AddHistory(previous, RcmCodes.HistoryAction.Superseded, prevFrom, previous.Status, null, $"-> v{s.VersionNumber}", actor);

            foreach (var a in toMove)
            {
                a.MaintenanceStrategyId = s.MaintenanceStrategyId;
                AddHistory(s, RcmCodes.HistoryAction.Assigned, null, null, null, $"{a.Equipment?.TagNumber} (moved from v{previous.VersionNumber})", actor, a.MaintenanceStrategyAssignmentId);
            }
        }

        await _context.SaveChangesAsync();
        return OpResult.Ok(s.MaintenanceStrategyId);
    }

    public async Task<OpResult> RetireAsync(int maintenanceStrategyId, string? comment, StrategyActor actor)
    {
        var s = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (s == null) return OpResult.Fail(new RuleError("Rcm_Err_StrategyNotFound"));
        if (s.Status == RcmCodes.Status.Retired || !s.IsActive)
            return OpResult.Fail(new RuleError("Rcm_Err_AlreadyRetired"));

        var from = s.Status;
        s.Status = RcmCodes.Status.Retired;
        s.IsActive = false;
        s.RetiredAt = DateTime.Now;

        // تخصیص‌های این نسخه بسته می‌شوند: Plan های متصل دیگر WO تولید نمی‌کنند
        var assignments = await _context.MaintenanceStrategyAssignments
            .Where(a => a.MaintenanceStrategyId == s.MaintenanceStrategyId && a.IsActive).ToListAsync();
        foreach (var a in assignments)
        {
            a.Status = RcmCodes.AssignmentStatus.Retired;
            a.IsActive = false;
        }

        AddHistory(s, RcmCodes.HistoryAction.Retired, from, s.Status, null, comment, actor);
        await _context.SaveChangesAsync();
        return OpResult.Ok(s.MaintenanceStrategyId);
    }

    // ==================== Validation ====================

    private async Task<List<RuleError>> ValidateAsync(StrategyInputDto input, MaintenanceStrategy? existing, bool skipStartDate = false)
    {
        var errors = StrategyRules.Validate(new StrategyRuleInput
        {
            StrategyType = input.StrategyType,
            ConsequenceCategory = input.ConsequenceCategory,
            IsHiddenFailure = input.IsHiddenFailure,
            FailureModeId = input.FailureModeId,
            EquipmentClassId = input.EquipmentClassId,
            Justification = input.Justification,
            Frequency = input.Frequency,
            DefaultIntervalDays = input.DefaultIntervalDays,
            RecommendedTriggerType = input.RecommendedTriggerType,
            // تاریخ شروع فقط وقتی تغییر کرده/جدید است باید «گذشته» نباشد
            StartDate = skipStartDate || (existing != null && existing.StartDate == input.StartDate) ? null : input.StartDate,
            TaskType = input.TaskType,
            Source = input.Source,
            CbmParameter = input.CbmParameter,
            PfIntervalDays = input.PfIntervalDays,
            InspectionIntervalDays = input.InspectionIntervalDays,
            OwnerDisciplineId = input.OwnerDisciplineId,
            EstimatedDurationHours = input.EstimatedDurationHours
        });

        if (!input.EquipmentClassId.HasValue)
        {
            // استراتژی قدیمی تک‌تگی بدون کلاس (طبقه‌بندی‌نشده) هنگام ویرایش معاف است
            if (existing?.EquipmentId == null)
                errors.Add(new RuleError("Rcm_Err_ClassRequired", "equipmentClassId"));
        }
        else if (!await _context.EquipmentClasses.AnyAsync(x => x.EquipmentClassId == input.EquipmentClassId && x.IsActive))
        {
            errors.Add(new RuleError("Rcm_Err_ClassNotFound", "equipmentClassId"));
        }

        if (input.FailureModeId.HasValue)
        {
            var mode = await _context.FailureModes.AsNoTracking().FirstOrDefaultAsync(x => x.FailureModeId == input.FailureModeId && x.IsActive);
            if (mode == null)
            {
                errors.Add(new RuleError("Rcm_Err_FailureModeNotFound", "failureModeId"));
            }
            else if (mode.EquipmentClassId.HasValue && input.EquipmentClassId.HasValue
                && !await ClassChainContainsAsync(input.EquipmentClassId.Value, mode.EquipmentClassId.Value))
            {
                errors.Add(new RuleError("Rcm_Err_FailureModeClassMismatch", "failureModeId"));
            }
        }

        if (input.FailureCauseId.HasValue && !await _context.FailureCauses.AnyAsync(x => x.FailureCauseId == input.FailureCauseId && x.IsActive))
            errors.Add(new RuleError("Rcm_Err_FailureCauseNotFound", "failureCauseId"));

        if (input.OwnerDisciplineId.HasValue && !await _context.Disciplines.AnyAsync(x => x.DisciplineId == input.OwnerDisciplineId && x.IsActive))
            errors.Add(new RuleError("Rcm_Err_DisciplineNotFound", "ownerDisciplineId"));

        // تکراری: همان کلاس + همان حالت خرابی + همان نوع (فقط ردیف‌های زنده‌ی خانواده‌ی دیگر)
        if (errors.Count == 0 && input.FailureModeId.HasValue)
        {
            var familyId = existing?.FamilyId;
            var duplicate = await _context.MaintenanceStrategies.AnyAsync(x =>
                x.IsActive && RcmCodes.Status.Live.Contains(x.Status)
                && x.EquipmentClassId == input.EquipmentClassId
                && x.FailureModeId == input.FailureModeId
                && x.StrategyType == input.StrategyType
                && (familyId == null || x.FamilyId != familyId));
            if (duplicate) errors.Add(new RuleError("Rcm_Err_DuplicateStrategy", "failureModeId"));
        }

        return errors;
    }

    private async Task<bool> ClassChainContainsAsync(int classId, int ancestorOrSelfId)
    {
        var current = (int?)classId;
        var guard = 0;
        while (current.HasValue && guard++ < 10)
        {
            if (current == ancestorOrSelfId) return true;
            current = await _context.EquipmentClasses.AsNoTracking()
                .Where(x => x.EquipmentClassId == current.Value).Select(x => x.ParentEquipmentClassId).FirstOrDefaultAsync();
        }
        return false;
    }

    // ==================== Helpers ====================

    private static void Normalize(StrategyInputDto i)
    {
        i.StrategyType = LegacyMaps.StrategyType(i.StrategyType) ?? i.StrategyType?.Trim();
        i.ConsequenceCategory = LegacyMaps.Consequence(i.ConsequenceCategory) ?? i.ConsequenceCategory?.Trim();
        if (!string.IsNullOrWhiteSpace(i.Frequency)) i.Frequency = LegacyMaps.Frequency(i.Frequency) ?? i.Frequency.Trim();
        if (i.ConsequenceCategory == RcmCodes.Consequence.Hidden) i.IsHiddenFailure = true;
        i.Justification = i.Justification?.Trim();
        i.Title = string.IsNullOrWhiteSpace(i.Title) ? null : i.Title.Trim();
        i.Description = string.IsNullOrWhiteSpace(i.Description) ? null : i.Description.Trim();
        i.RecommendedTriggerType = string.IsNullOrWhiteSpace(i.RecommendedTriggerType) ? RcmCodes.Trigger.Calendar : i.RecommendedTriggerType.Trim();
        // فرکانس معنا دارد → تناوب پیش‌فرض (روز) از آن مشتق می‌شود، مگر این‌که صریح داده شده باشد
        if (!i.DefaultIntervalDays.HasValue) i.DefaultIntervalDays = RcmCodes.Frequency.ToDays(i.Frequency);
    }

    private static StrategyInputDto Snapshot(MaintenanceStrategy s) => MaintenanceStrategyService.ToInput(s);

    private static void Apply(MaintenanceStrategy s, StrategyInputDto i, FailureMode mode)
    {
        s.StrategyType = i.StrategyType!;
        s.ConsequenceCategory = i.ConsequenceCategory!;
        s.IsHiddenFailure = i.IsHiddenFailure;
        s.EquipmentClassId = i.EquipmentClassId ?? s.EquipmentClassId;
        s.FailureModeId = mode.FailureModeId;
        s.FailureModeCode = mode.Code;
        s.FailureCauseId = i.FailureCauseId;
        s.Title = i.Title;
        s.FunctionalFailure = i.FunctionalFailure;
        s.FailureEffect = i.FailureEffect;
        s.Justification = i.Justification;
        s.RecommendedTriggerType = i.RecommendedTriggerType ?? RcmCodes.Trigger.Calendar;
        s.Frequency = i.Frequency;
        s.DefaultIntervalDays = i.DefaultIntervalDays;
        s.Description = i.Description;
        s.StartDate = i.StartDate;
        s.TaskType = i.TaskType;
        s.OwnerDisciplineId = i.OwnerDisciplineId;
        s.RequiredSkills = i.RequiredSkills;
        s.EstimatedDurationHours = i.EstimatedDurationHours;
        s.CbmParameter = i.CbmParameter;
        s.ThresholdUnit = i.ThresholdUnit;
        s.AlarmThreshold = i.AlarmThreshold;
        s.TripThreshold = i.TripThreshold;
        s.PfIntervalDays = i.PfIntervalDays;
        s.InspectionIntervalDays = i.InspectionIntervalDays;
        s.Source = i.Source;
        s.ReviewDate = i.ReviewDate;
        s.ConditionMonitoringPointId = i.ConditionMonitoringPointId;
    }

    private static string? BuildDiff(MaintenanceStrategy current, StrategyInputDto input, FailureMode mode)
    {
        var before = MaintenanceStrategyService.ToInput(current);
        // FailureModeCode همیشه از ردیف کاتالوگ می‌آید؛ در ورودی نیست و دیف جداگانه ندارد
        var lines = new List<string>();
        foreach (var p in typeof(StrategyInputDto).GetProperties())
        {
            if (DiffExcluded.Contains(p.Name)) continue;
            var oldV = p.GetValue(before)?.ToString() ?? "";
            var newV = p.GetValue(input)?.ToString() ?? "";
            if (p.Name == nameof(StrategyInputDto.FailureModeId)) { newV = mode.FailureModeId.ToString(); }
            if (oldV != newV) lines.Add($"{p.Name}: {(oldV.Length == 0 ? "null" : oldV)} -> {(newV.Length == 0 ? "null" : newV)}");
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private async Task<int?> ResolveTypeIdAsync(string strategyType)
    {
        var code = strategyType switch
        {
            RcmCodes.StrategyType.Preventive => "PM",
            RcmCodes.StrategyType.Predictive => "PDM",
            RcmCodes.StrategyType.Cbm => "CBM",
            RcmCodes.StrategyType.Corrective => "CM",
            RcmCodes.StrategyType.RunToFailure => "RTF",
            _ => null
        };
        if (code == null) return null;
        return await _context.MaintenanceStrategyTypes.AsNoTracking()
            .Where(x => x.Code == code).Select(x => (int?)x.MaintenanceStrategyTypeId).FirstOrDefaultAsync();
    }

    private static bool IsAuthorOrSubmitter(MaintenanceStrategy s, StrategyActor actor)
    {
        if (actor.EmployeeId.HasValue && (actor.EmployeeId == s.AuthorEmployeeId || actor.EmployeeId == s.SubmittedByEmployeeId))
            return true;
        // اگر هویت کارمندی نیست، به نام کاربری سازنده‌ی رکورد (BaseEntity.CreatedBy) برمی‌گردیم
        return !actor.EmployeeId.HasValue && !string.IsNullOrEmpty(actor.UserName)
            && string.Equals(s.CreatedBy, actor.UserName, StringComparison.OrdinalIgnoreCase);
    }

    private void AddHistory(MaintenanceStrategy s, string action, string? from, string? to, string? changes,
        string? comment, StrategyActor actor, int? assignmentId = null)
    {
        _context.MaintenanceStrategyHistories.Add(new MaintenanceStrategyHistory
        {
            MaintenanceStrategy = s,
            MaintenanceStrategyAssignmentId = assignmentId,
            VersionNumber = s.VersionNumber,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            ChangesSummary = changes,
            Comment = comment,
            ActorEmployeeId = actor.EmployeeId,
            ActorName = actor.UserName,
            Timestamp = DateTime.Now
        });
    }
}
