using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>پروژکشن مشترک «هویت تجهیز» (تگ، نام، کلاس، ناحیه/واحد، بحرانی‌بودن) برای همه‌ی صفحه‌ها.</summary>
internal static class EquipmentIdentityProjection
{
    public static IQueryable<EquipmentIdentityDto> Project(PEMSDbContext ctx, IQueryable<Equipment> query)
        => query.Select(e => new EquipmentIdentityDto
        {
            EquipmentId = e.EquipmentId,
            Tag = e.TagNumber,
            Name = e.Name,
            EquipmentClassId = e.EquipmentClassId,
            EquipmentClassName = e.EquipmentClassRef != null ? e.EquipmentClassRef.Name : null,
            EquipmentClassCode = e.EquipmentClassRef != null ? e.EquipmentClassRef.Code : null,
            Area = e.FunctionalLocationRef != null && e.FunctionalLocationRef.Area != null ? e.FunctionalLocationRef.Area : e.Area,
            Unit = e.FunctionalLocationRef != null ? e.FunctionalLocationRef.Unit : null,
            CriticalityClass = ctx.EquipmentCriticalities
                .Where(c => c.IsActive && c.EquipmentId == e.EquipmentId)
                .Select(c => c.CriticalityClass).FirstOrDefault() ?? RcmCodes.Criticality.Unrated
        });
}

/// <summary>
/// ==============================================================================
/// تخصیص نسخه‌ی استراتژی به تگ‌ها (RCM کلاس‌محور). قوانین:
///  • فقط نسخه‌ی Approved/Active قابل تخصیص است.
///  • برای هر (نسخه، تگ) فقط یک تخصیص فعال (تکراری رد می‌شود، نه خطا).
///  • تگ با تخصیص فعال به استراتژی دیگری برای «همان حالت خرابی» = تعارض (پیش‌نمایش نشان می‌دهد).
///  • بحرانی‌بودن A نوع Run-to-Failure/Corrective را مسدود می‌کند.
///  • تخصیص دسته‌جمعی اتمیک است: شکست ذخیره‌سازی = Rollback کامل + گزارش.
///  • Override سطح تگ (تناوب/توضیح) فقط با تایید مستقل (درخواست‌دهنده ≠ تاییدکننده) اعمال می‌شود.
/// ==============================================================================
/// </summary>
public class StrategyAssignmentService : IStrategyAssignmentService
{
    public const int MaxAssignBatch = 2000;
    private static readonly string[] OpenWorkOrderStatuses = { "Open", "Assigned", "InProgress", "OnHold", "Completed" };

    private readonly PEMSDbContext _context;

    public StrategyAssignmentService(PEMSDbContext context)
    {
        _context = context;
    }

    // ==================== Preview ====================

    public async Task<BulkAssignPreviewDto> PreviewAsync(int maintenanceStrategyId, BulkAssignFilter filter, int maxRows = 500)
    {
        var strategy = await _context.MaintenanceStrategies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        var preview = new BulkAssignPreviewDto { MaintenanceStrategyId = maintenanceStrategyId };
        if (strategy == null) return preview;

        var classIds = await ResolveClassScopeAsync(strategy.EquipmentClassId, filter);
        var query = _context.Equipments.AsNoTracking().Where(e => e.IsActive);

        if (classIds != null) query = query.Where(e => e.EquipmentClassId != null && classIds.Contains(e.EquipmentClassId.Value));
        if (filter.Areas.Count > 0)
            query = query.Where(e => (e.FunctionalLocationRef != null && e.FunctionalLocationRef.Area != null && filter.Areas.Contains(e.FunctionalLocationRef.Area))
                                     || (e.Area != null && filter.Areas.Contains(e.Area)));
        if (filter.DisciplineIds.Count > 0) query = query.Where(e => filter.DisciplineIds.Contains(e.DisciplineId));
        if (!string.IsNullOrWhiteSpace(filter.TagSearch))
        {
            var term = filter.TagSearch.Trim();
            query = query.Where(e => e.TagNumber.Contains(term));
        }

        preview.TotalMatching = await query.CountAsync();
        preview.Truncated = preview.TotalMatching > maxRows;

        var identities = await EquipmentIdentityProjection.Project(_context, query.OrderBy(e => e.TagNumber).Take(maxRows)).ToListAsync();
        preview.Candidates = await EvaluateAsync(strategy, identities);
        return preview;
    }

    public async Task<List<BulkAssignCandidateDto>> EvaluateAsync(int maintenanceStrategyId, IReadOnlyCollection<int> equipmentIds)
    {
        var strategy = await _context.MaintenanceStrategies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (strategy == null) return new List<BulkAssignCandidateDto>();

        var ids = equipmentIds.Distinct().ToList();
        var identities = await EquipmentIdentityProjection.Project(_context,
            _context.Equipments.AsNoTracking().Where(e => ids.Contains(e.EquipmentId)).OrderBy(e => e.TagNumber)).ToListAsync();
        return await EvaluateAsync(strategy, identities);
    }

    private async Task<List<BulkAssignCandidateDto>> EvaluateAsync(MaintenanceStrategy strategy, List<EquipmentIdentityDto> identities)
    {
        var ids = identities.Select(i => i.EquipmentId).ToList();

        var active = await _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.Status == RcmCodes.AssignmentStatus.Active && ids.Contains(a.EquipmentId))
            .Select(a => new { a.EquipmentId, a.MaintenanceStrategyId, a.MaintenanceStrategy!.FamilyId, a.MaintenanceStrategy.FailureModeId })
            .ToListAsync();

        var result = new List<BulkAssignCandidateDto>();
        foreach (var identity in identities)
        {
            var c = new BulkAssignCandidateDto { Equipment = identity };
            var mine = active.Where(a => a.EquipmentId == identity.EquipmentId).ToList();

            if (mine.Any(a => a.MaintenanceStrategyId == strategy.MaintenanceStrategyId || a.FamilyId == strategy.FamilyId))
            {
                c.Conflict = BulkAssignConflict.AlreadyAssigned;
            }
            else if (mine.FirstOrDefault(a => a.FailureModeId == strategy.FailureModeId && strategy.FailureModeId != null) is { } other)
            {
                c.Conflict = BulkAssignConflict.ConflictingStrategy;
                c.ConflictingStrategyId = other.MaintenanceStrategyId;
            }
            else if (StrategyRules.CheckCriticalityAllowsType(identity.CriticalityClass, strategy.StrategyType) != null)
            {
                c.Conflict = BulkAssignConflict.CriticalityBlocks;
            }
            result.Add(c);
        }
        return result;
    }

    // ==================== Assign ====================

    public async Task<BulkAssignResultDto> AssignAsync(int maintenanceStrategyId, IReadOnlyCollection<int> equipmentIds, StrategyActor actor)
    {
        var result = new BulkAssignResultDto();

        var strategy = await _context.MaintenanceStrategies.FirstOrDefaultAsync(x => x.MaintenanceStrategyId == maintenanceStrategyId);
        if (strategy == null) { result.Errors.Add(new RuleError("Rcm_Err_StrategyNotFound")); return result; }
        if (!RcmCodes.Status.Usable.Contains(strategy.Status) || !strategy.IsActive)
        { result.Errors.Add(new RuleError("Rcm_Err_StrategyNotApproved")); return result; }

        var distinct = (equipmentIds ?? Array.Empty<int>()).Distinct().ToList();
        if (distinct.Count == 0) { result.Errors.Add(new RuleError("Rcm_Err_NoEquipmentSelected")); return result; }
        if (distinct.Count > MaxAssignBatch) { result.Errors.Add(new RuleError("Rcm_Err_AssignBatchTooLarge", null, MaxAssignBatch.ToString())); return result; }

        var scope = strategy.EquipmentClassId.HasValue ? await ClassDescendantsAsync(strategy.EquipmentClassId.Value) : null;

        var identities = await EquipmentIdentityProjection.Project(_context,
            _context.Equipments.AsNoTracking().Where(e => e.IsActive && distinct.Contains(e.EquipmentId))).ToListAsync();
        result.SkippedNotFound = distinct.Count - identities.Count;
        foreach (var missingId in distinct.Except(identities.Select(i => i.EquipmentId)))
            result.Skipped.Add(new BulkAssignSkipDto { Tag = "#" + missingId, Reason = "NotFound" });

        var candidates = await EvaluateAsync(strategy, identities);
        var added = new List<(MaintenanceStrategyAssignment A, string Tag)>();

        foreach (var c in candidates)
        {
            // تگ باید داخل زیردرخت کلاس الگو باشد
            var inScope = scope == null || (c.Equipment.EquipmentClassId.HasValue && scope.Contains(c.Equipment.EquipmentClassId.Value));
            if (!inScope) { result.Errors.Add(new RuleError("Rcm_Err_TagClassMismatch", null, c.Equipment.Tag)); continue; }

            switch (c.Conflict)
            {
                case BulkAssignConflict.AlreadyAssigned: result.SkippedAlreadyAssigned++; break;
                case BulkAssignConflict.ConflictingStrategy: result.SkippedConflict++; break;
                case BulkAssignConflict.CriticalityBlocks: result.SkippedCriticality++; break;
            }
            if (!c.WillReceive)
            {
                result.Skipped.Add(new BulkAssignSkipDto { Tag = c.Equipment.Tag, Reason = c.Conflict, OtherStrategyId = c.ConflictingStrategyId });
                continue;
            }

            var a = new MaintenanceStrategyAssignment
            {
                MaintenanceStrategyId = strategy.MaintenanceStrategyId,
                EquipmentId = c.Equipment.EquipmentId,
                Status = RcmCodes.AssignmentStatus.Active,
                AssignedDate = DateTime.Now,
                AssignedByEmployeeId = actor.EmployeeId,
                IsActive = true,
                CreatedDate = DateTime.Now
            };
            _context.MaintenanceStrategyAssignments.Add(a);
            added.Add((a, c.Equipment.Tag));
        }

        // تگ‌های خارج از کلاس = خطای صریح؛ هیچ تخصیصی انجام نمی‌شود (همه یا هیچ)
        if (result.Errors.Count > 0)
        {
            foreach (var (a, _) in added) _context.Entry(a).State = EntityState.Detached;
            return result;
        }

        if (added.Count > 0)
        {
            var shown = string.Join(", ", added.Take(50).Select(x => x.Tag)) + (added.Count > 50 ? $" … (+{added.Count - 50})" : "");
            _context.MaintenanceStrategyHistories.Add(new MaintenanceStrategyHistory
            {
                MaintenanceStrategyId = strategy.MaintenanceStrategyId,
                VersionNumber = strategy.VersionNumber,
                Action = RcmCodes.HistoryAction.Assigned,
                ChangesSummary = $"{added.Count} tag(s): {shown}",
                ActorEmployeeId = actor.EmployeeId,
                ActorName = actor.UserName,
                Timestamp = DateTime.Now
            });
        }

        try
        {
            await _context.SaveChangesAsync();
            result.Assigned = added.Count;
        }
        catch (DbUpdateException)
        {
            // همه یا هیچ: هیچ تخصیص نیمه‌کاره‌ای باقی نمی‌ماند
            _context.ChangeTracker.Clear();
            result.Assigned = 0;
            result.RolledBack = true;
            result.Errors.Add(new RuleError("Rcm_Err_AssignRolledBack"));
        }

        return result;
    }

    public async Task<OpResult> UnassignAsync(int assignmentId, string? comment, StrategyActor actor)
    {
        var a = await _context.MaintenanceStrategyAssignments.Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyAssignmentId == assignmentId);
        if (a == null) return OpResult.Fail(new RuleError("Rcm_Err_AssignmentNotFound"));
        if (!a.IsActive) return OpResult.Fail(new RuleError("Rcm_Err_AssignmentNotActive"));

        a.Status = RcmCodes.AssignmentStatus.Retired;
        a.IsActive = false;
        await AddHistoryAsync(a, RcmCodes.HistoryAction.Unassigned, a.Equipment?.TagNumber, comment, actor);
        await _context.SaveChangesAsync();
        return OpResult.Ok(a.MaintenanceStrategyAssignmentId);
    }

    // ==================== Override ====================

    public async Task<OpResult> RequestOverrideAsync(int assignmentId, int? intervalDays, string? taskNote, string? justification, StrategyActor actor)
    {
        var a = await _context.MaintenanceStrategyAssignments.Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyAssignmentId == assignmentId && x.IsActive);
        if (a == null) return OpResult.Fail(new RuleError("Rcm_Err_AssignmentNotFound"));

        var errors = new List<RuleError>();
        if (!intervalDays.HasValue && string.IsNullOrWhiteSpace(taskNote))
            errors.Add(new RuleError("Rcm_Err_OverrideEmpty", "overrideIntervalDays"));
        if (intervalDays is <= 0)
            errors.Add(new RuleError("Rcm_Err_IntervalMustBePositive", "overrideIntervalDays"));
        if ((justification ?? "").Trim().Length < StrategyRules.MinJustificationLength)
            errors.Add(new RuleError("Rcm_Err_JustificationTooShort", "overrideJustification", StrategyRules.MinJustificationLength.ToString()));
        if (errors.Count > 0) return OpResult.Fail(errors);

        a.OverrideIntervalDays = intervalDays;
        a.OverrideTaskNote = string.IsNullOrWhiteSpace(taskNote) ? null : taskNote.Trim();
        a.OverrideJustification = justification!.Trim();
        a.OverrideStatus = RcmCodes.OverrideStatus.Pending;
        a.OverrideRequestedByEmployeeId = actor.EmployeeId;
        a.OverrideApprovedByEmployeeId = null;
        a.OverrideApprovedAt = null;

        await AddHistoryAsync(a, RcmCodes.HistoryAction.OverrideRequested,
            $"{a.Equipment?.TagNumber}: interval={intervalDays?.ToString() ?? "-"}", a.OverrideJustification, actor);
        await _context.SaveChangesAsync();
        return OpResult.Ok(a.MaintenanceStrategyAssignmentId);
    }

    public async Task<OpResult> DecideOverrideAsync(int assignmentId, bool approve, string? comment, StrategyActor actor)
    {
        var a = await _context.MaintenanceStrategyAssignments.Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.MaintenanceStrategyAssignmentId == assignmentId && x.IsActive);
        if (a == null) return OpResult.Fail(new RuleError("Rcm_Err_AssignmentNotFound"));
        if (a.OverrideStatus != RcmCodes.OverrideStatus.Pending)
            return OpResult.Fail(new RuleError("Rcm_Err_NoPendingOverride"));

        var sameActor = actor.EmployeeId.HasValue && actor.EmployeeId == a.OverrideRequestedByEmployeeId;
        if (sameActor) return OpResult.Fail(new RuleError("Rcm_Err_SeparationOfDuties"));

        if (!approve && string.IsNullOrWhiteSpace(comment))
            return OpResult.Fail(new RuleError("Rcm_Err_RejectCommentRequired", "comment"));

        a.OverrideStatus = approve ? RcmCodes.OverrideStatus.Approved : RcmCodes.OverrideStatus.Rejected;
        a.OverrideApprovedByEmployeeId = actor.EmployeeId;
        a.OverrideApprovedAt = DateTime.Now;

        await AddHistoryAsync(a, approve ? RcmCodes.HistoryAction.OverrideApproved : RcmCodes.HistoryAction.OverrideRejected,
            a.Equipment?.TagNumber, comment, actor);
        await _context.SaveChangesAsync();
        return OpResult.Ok(a.MaintenanceStrategyAssignmentId);
    }

    // ==================== Tag view / choices ====================

    public async Task<TagMaintenanceViewDto?> GetTagViewAsync(int equipmentId)
    {
        var identity = await GetEquipmentIdentityAsync(equipmentId);
        if (identity == null) return null;

        var view = new TagMaintenanceViewDto { Equipment = identity };

        view.Assignments = await _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.EquipmentId == equipmentId && a.IsActive)
            .OrderBy(a => a.MaintenanceStrategyId)
            .Select(a => new StrategyAssignmentRowDto
            {
                AssignmentId = a.MaintenanceStrategyAssignmentId,
                MaintenanceStrategyId = a.MaintenanceStrategyId,
                StrategyVersion = a.MaintenanceStrategy!.VersionNumber,
                Status = a.Status,
                OverrideStatus = a.OverrideStatus,
                OverrideIntervalDays = a.OverrideIntervalDays,
                OverrideTaskNote = a.OverrideTaskNote,
                OverrideJustification = a.OverrideJustification,
                AssignedDate = a.AssignedDate,
                Equipment = identity,
                PlanCount = _context.MaintenancePlans.Count(p => p.IsActive && p.MaintenanceStrategyAssignmentId == a.MaintenanceStrategyAssignmentId)
            }).ToListAsync();

        view.Plans = await _context.MaintenancePlans.AsNoTracking()
            .Where(p => p.IsActive && p.EquipmentId == equipmentId)
            .OrderBy(p => p.NextDueDate)
            .Select(p => new TagPlanRowDto
            {
                MaintenancePlanId = p.MaintenancePlanId,
                PlanNumber = p.PlanNumber,
                Title = p.Title,
                NextDueDate = p.NextDueDate,
                StrategyId = p.MaintenanceStrategyId,
                StrategyVersion = p.MaintenanceStrategy != null ? p.MaintenanceStrategy.VersionNumber : (int?)null
            }).Take(100).ToListAsync();

        view.OpenWorkOrders = await _context.WorkOrders.AsNoTracking()
            .Where(w => w.IsActive && w.EquipmentId == equipmentId && OpenWorkOrderStatuses.Contains(w.Status))
            .OrderByDescending(w => w.WorkOrderId)
            .Select(w => new TagWorkOrderRowDto { WorkOrderId = w.WorkOrderId, WorkOrderNumber = w.WorkOrderNumber, Status = w.Status, Priority = w.Priority })
            .Take(50).ToListAsync();

        return view;
    }

    public async Task<List<AssignmentChoiceDto>> GetPlanEligibleAssignmentsAsync(int? equipmentId = null, int? strategyId = null)
    {
        var query = _context.MaintenanceStrategyAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.Status == RcmCodes.AssignmentStatus.Active
                        && a.MaintenanceStrategy!.IsActive
                        && RcmCodes.Status.Usable.Contains(a.MaintenanceStrategy.Status)
                        && RcmCodes.StrategyType.PlanGenerating.Contains(a.MaintenanceStrategy.StrategyType));

        if (equipmentId.HasValue) query = query.Where(a => a.EquipmentId == equipmentId.Value);
        if (strategyId.HasValue) query = query.Where(a => a.MaintenanceStrategyId == strategyId.Value);

        var rows = await query.OrderBy(a => a.Equipment!.TagNumber).ThenBy(a => a.MaintenanceStrategyId)
            .Select(a => new AssignmentChoiceDto
            {
                AssignmentId = a.MaintenanceStrategyAssignmentId,
                Tag = a.Equipment!.TagNumber,
                StrategyType = a.MaintenanceStrategy!.StrategyType,
                FailureModeName = a.MaintenanceStrategy.FailureMode != null ? a.MaintenanceStrategy.FailureMode.Name : null,
                FailureModeNameFa = a.MaintenanceStrategy.FailureMode != null ? a.MaintenanceStrategy.FailureMode.NameFa : null,
                StrategyVersion = a.MaintenanceStrategy.VersionNumber,
                EquipmentClassName = a.MaintenanceStrategy.EquipmentClass != null ? a.MaintenanceStrategy.EquipmentClass.Name : null
            })
            .Take(500).ToListAsync();

        // برچسب متمایز: تگ · نوع · حالت خرابی · نسخه (رفع «برچسب‌های یکسان» لیست)
        foreach (var r in rows)
            r.Label = $"{r.Tag} · {r.StrategyType} · {r.FailureModeName ?? "-"} · v{r.StrategyVersion}";
        return rows;
    }

    public Task<EquipmentIdentityDto?> GetEquipmentIdentityAsync(int equipmentId)
        => EquipmentIdentityProjection.Project(_context, _context.Equipments.AsNoTracking().Where(e => e.EquipmentId == equipmentId))
            .FirstOrDefaultAsync();

    // ==================== Helpers ====================

    private async Task AddHistoryAsync(MaintenanceStrategyAssignment a, string action, string? changes, string? comment, StrategyActor actor)
    {
        var version = await _context.MaintenanceStrategies.Where(s => s.MaintenanceStrategyId == a.MaintenanceStrategyId)
            .Select(s => s.VersionNumber).FirstOrDefaultAsync();
        _context.MaintenanceStrategyHistories.Add(new MaintenanceStrategyHistory
        {
            MaintenanceStrategyId = a.MaintenanceStrategyId,
            MaintenanceStrategyAssignmentId = a.MaintenanceStrategyAssignmentId,
            VersionNumber = version,
            Action = action,
            ChangesSummary = changes,
            Comment = comment,
            ActorEmployeeId = actor.EmployeeId,
            ActorName = actor.UserName,
            Timestamp = DateTime.Now
        });
    }

    private async Task<HashSet<int>?> ResolveClassScopeAsync(int? strategyClassId, BulkAssignFilter filter)
    {
        HashSet<int>? strategyScope = strategyClassId.HasValue ? await ClassDescendantsAsync(strategyClassId.Value) : null;
        if (!filter.EquipmentClassId.HasValue) return strategyScope;

        var filterScope = filter.IncludeClassDescendants
            ? await ClassDescendantsAsync(filter.EquipmentClassId.Value)
            : new HashSet<int> { filter.EquipmentClassId.Value };

        // اشتراک با دامنه‌ی الگو: فیلتر هرگز تگ خارج از کلاس الگو را وارد نمی‌کند
        if (strategyScope != null) filterScope.IntersectWith(strategyScope);
        return filterScope;
    }

    /// <summary>کلاس + تمام زیرکلاس‌ها (طبقه‌بندی ISO 14224: class / sub-class).</summary>
    internal async Task<HashSet<int>> ClassDescendantsAsync(int classId)
    {
        var all = await _context.EquipmentClasses.AsNoTracking()
            .Select(c => new { c.EquipmentClassId, c.ParentEquipmentClassId }).ToListAsync();
        var result = new HashSet<int> { classId };
        var frontier = new Queue<int>();
        frontier.Enqueue(classId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in all.Where(c => c.ParentEquipmentClassId == current))
                if (result.Add(child.EquipmentClassId)) frontier.Enqueue(child.EquipmentClassId);
        }
        return result;
    }
}
