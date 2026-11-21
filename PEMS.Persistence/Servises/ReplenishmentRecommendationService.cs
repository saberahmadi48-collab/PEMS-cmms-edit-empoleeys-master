using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Application.Replenishment;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// Phase 2 / P5 - توصیه‌های تأمین (اجرای دستی، نسخه‌ی ۱ محافظه‌کارانه).
///
/// مرز: خواندن از INetDemandService (P3) و نوشتن «فقط» در جدول‌های خودش (NetDemandRuns / ReplenishmentRecommendations / ...Sources / ...Histories).
/// هیچ مسیر کدی برای نوشتن MaterialStock، InventoryTransaction، MaterialReservation، StoreRequisition، WorkOrder، MR، PO یا مجوز وجود ندارد
/// (تست اسکن سورس این را تضمین می‌کند). مجوز: ماژول ReplenishmentRecommendation با اکشن‌های View/Create/Edit؛ هیچ Grant خودکاری وجود ندارد.
///
/// تعامل: هر اجرا = یک NetDemandRun (کلید Idempotency اختیاری با ایندکس یکتا) + یک SaveChanges اتمیک برای همه‌ی upsertها. تعارض هم‌زمان
/// (RowVersion یا ایندکس یکتای «زنده») با حداکثر ۳ تلاش از ابتدا (خواندن تازه) حل می‌شود؛ بعد از آن اجرا Failed ثبت می‌شود، نه بلعیده.
/// کل واحد کار داخل Execution Strategy است (برنامه با EnableRetryOnFailure اجرا می‌شود).
/// </summary>
public class ReplenishmentRecommendationService : IReplenishmentRecommendationService
{
    private const int MaxApplyAttempts = 3;
    private const int MaxSummaryEntries = 50;
    private const string ForbiddenMessage = "مجوز لازم برای این عملیات را ندارید. / You do not have permission for this operation.";
    private const string ConflictMessage = "این توصیه هم‌زمان توسط عملیات دیگری تغییر کرد؛ صفحه را تازه کنید و دوباره بررسی کنید. / The recommendation was changed concurrently; reload and review again.";

    private readonly PEMSDbContext _ctx;
    private readonly INetDemandService _netDemand;
    private readonly IPermissionService _permissions;

    // SCRUM-471: optional so the existing callers and tests keep their constructor; DI always provides it.
    private readonly IProcurementPipeline? _pipeline;

    public ReplenishmentRecommendationService(PEMSDbContext ctx, INetDemandService netDemand, IPermissionService permissions, IProcurementPipeline? pipeline = null)
    {
        _ctx = ctx;
        _netDemand = netDemand;
        _permissions = permissions;
        _pipeline = pipeline;
    }

    private Task<bool> Can(ReplenishmentActor actor, string action) =>
        _permissions.HasPermissionAsync(actor.Roles ?? Array.Empty<string>(), IReplenishmentRecommendationService.Module, action);

    // ───────────────────────────── اجرا ─────────────────────────────

    public async Task<ReplenishmentRunResult> RunAsync(ReplenishmentRunRequest request, ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (!await Can(actor, "Create")) return ReplenishmentRunResult.Failure(ForbiddenMessage, forbidden: true);

        var key = string.IsNullOrWhiteSpace(request.RequestKey) ? null : request.RequestKey.Trim();
        if (key is { Length: > 64 }) return ReplenishmentRunResult.Failure("کلید درخواست نامعتبر است (حداکثر ۶۴ نویسه). / Invalid request key (max 64 characters).");

        if (key != null)
        {
            var previous = await _ctx.NetDemandRuns.AsNoTracking().FirstOrDefaultAsync(r => r.RequestKey == key, ct);
            if (previous != null) return Replay(previous);
        }

        var materialIds = request.MaterialItemIds is { Count: > 0 } ? request.MaterialItemIds.Distinct().OrderBy(x => x).ToList() : null;
        var warehouseIds = request.WarehouseIds is { Count: > 0 } ? request.WarehouseIds.Distinct().OrderBy(x => x).ToList() : null;

        var run = new NetDemandRun
        {
            RequestKey = key,
            RequestedByEmployeeId = actor.EmployeeId,
            StartedAt = DateTime.Now,
            Status = "Started",
            CalculatorVersion = ReplenishmentRules.CalculatorVersion,
            ScopeMaterialItemIds = materialIds == null ? null : Truncate(string.Join(",", materialIds), 2000),
            ScopeWarehouseIds = warehouseIds == null ? null : Truncate(string.Join(",", warehouseIds), 2000)
        };
        _ctx.NetDemandRuns.Add(run);
        try
        {
            await _ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (key != null && IsUniqueViolation(ex))
        {
            // اجرای هم‌کلید هم‌زمان: برنده را بخوان و همان خلاصه را برگردان (هیچ اجرای دومی انجام نمی‌شود)
            _ctx.Entry(run).State = EntityState.Detached;
            var winner = await _ctx.NetDemandRuns.AsNoTracking().FirstAsync(r => r.RequestKey == key, ct);
            return Replay(winner);
        }

        var runId = run.NetDemandRunId;
        var scope = new NetDemandScope(materialIds, warehouseIds);
        var executions = 0;
        return await _ctx.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (++executions > 1) _ctx.ChangeTracker.Clear();
            return await ApplyWithRetryAsync(runId, scope, actor, ct);
        });
    }

    private async Task<ReplenishmentRunResult> ApplyWithRetryAsync(int runId, NetDemandScope scope, ReplenishmentActor actor, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(runId, scope, actor, ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException dbx && IsUniqueViolation(dbx)))
            {
                _ctx.ChangeTracker.Clear();
                if (attempt < MaxApplyAttempts) continue;

                var failed = await _ctx.NetDemandRuns.FirstAsync(r => r.NetDemandRunId == runId, ct);
                failed.Status = "Failed";
                failed.CompletedAt = DateTime.Now;
                failed.FailureMessage = "تعارض هم‌زمان پس از ۳ تلاش حل نشد؛ هیچ توصیه‌ای تغییر نکرد. / Concurrent changes were not resolved after 3 attempts; no recommendation was changed.";
                await _ctx.SaveChangesAsync(ct);
                return ReplenishmentRunResult.Failure(failed.FailureMessage);
            }
        }
    }

    private async Task<ReplenishmentRunResult> ApplyOnceAsync(int runId, NetDemandScope scope, ReplenishmentActor actor, CancellationToken ct)
    {
        var run = await _ctx.NetDemandRuns.FirstAsync(r => r.NetDemandRunId == runId, ct);
        var now = DateTime.Now;

        var results = await _netDemand.CalculateUnmaskedForSystemAsync(scope, ct);

        var liveQuery = _ctx.ReplenishmentRecommendations.Where(r => r.Status == ReplenishmentStatuses.Open || r.Status == ReplenishmentStatuses.Dismissed);
        if (scope.MaterialItemIds is { Count: > 0 }) liveQuery = liveQuery.Where(r => scope.MaterialItemIds.Contains(r.MaterialItemId));
        if (scope.WarehouseIds is { Count: > 0 }) liveQuery = liveQuery.Where(r => scope.WarehouseIds.Contains(r.WarehouseId));
        var live = await liveQuery.ToListAsync(ct);
        var liveByKey = live.ToDictionary(r => (r.MaterialItemId, r.WarehouseId));

        var needUom = results.Where(r => r.WarehouseId.HasValue).Select(r => r.MaterialItemId).Distinct().ToList();
        var uomByItem = await _ctx.MaterialItems.AsNoTracking().Where(m => needUom.Contains(m.MaterialItemId))
            .Select(m => new { m.MaterialItemId, m.UomId }).ToDictionaryAsync(m => m.MaterialItemId, m => m.UomId, ct);

        var fresh = await LoadFreshnessAsync(results, ct);
        var names = await LoadNamesAsync(results, ct);
        // SCRUM-471 (design §6.1 PlannedSupply): quantity already in procurement for (item, warehouse) is not recommended again
        var planned = _pipeline == null
            ? new Dictionary<(int MaterialItemId, int WarehouseId), decimal>()
            : await _pipeline.GetPlannedSupplyAsync(scope.MaterialItemIds, ct);

        int evaluated = 0, created = 0, updated = 0, unchanged = 0, closed = 0, autoReopened = 0, notDefinitive = 0, noWarehouse = 0;
        var summary = new List<string>();
        var seen = new HashSet<(int, int)>();

        foreach (var r in results.OrderBy(x => x.MaterialItemId).ThenBy(x => x.WarehouseId ?? int.MaxValue))
        {
            if (!r.WarehouseId.HasValue)
            {
                noWarehouse++;
                if (summary.Count < MaxSummaryEntries)
                    summary.Add($"{names.Item(r.MaterialItemId)}: {ReplenishmentRules.RequiresWarehouseAttribution} (work-order demand {r.GrossEligibleDemand:0.###} has no warehouse; it is not assigned to one by inference)");
                continue;
            }

            evaluated++;
            var key = (r.MaterialItemId, r.WarehouseId.Value);
            seen.Add(key);
            liveByKey.TryGetValue(key, out var rec);
            var ev = ReplenishmentRules.Evaluate(r);
            var inProcurement = planned.GetValueOrDefault(key);
            var coveredByProcurement = false;
            if (ev.IsDefinitive && inProcurement > 0m && ev.Shortage > 0m)
            {
                coveredByProcurement = ev.Shortage <= inProcurement;
                ev = ev with { Shortage = Math.Max(0m, ev.Shortage - inProcurement) };
            }

            if (!ev.IsDefinitive)
            {
                notDefinitive++;
                if (summary.Count < MaxSummaryEntries) summary.Add($"{names.Item(key.Item1)} @ {names.Warehouse(key.Item2)}: {string.Join(",", ev.BlockingCodes)}");
                if (rec != null) MarkNotDefinitive(rec, ev, run.NetDemandRunId, now);
                continue;
            }

            if (ev.Shortage <= 0m)
            {
                if (rec != null)
                {
                    Close(rec, coveredByProcurement ? ReplenishmentCloseReasons.CoveredByProcurement : ReplenishmentCloseReasons.ShortageResolved, run.NetDemandRunId, now);
                    closed++;
                }
                else unchanged++;
                continue;
            }

            var fingerprint = ReplenishmentRules.Fingerprint(r, ev);
            var sourceKeys = ReplenishmentRules.SourceKeys(r);

            if (rec == null)
            {
                var created1 = new ReplenishmentRecommendation
                {
                    MaterialItemId = r.MaterialItemId,
                    WarehouseId = r.WarehouseId.Value,
                    Status = ReplenishmentStatuses.Open,
                    Revision = 1,
                    UomId = uomByItem.GetValueOrDefault(r.MaterialItemId),
                    FirstRunId = run.NetDemandRunId,
                    LastRunId = run.NetDemandRunId,
                    FirstDetectedAt = now,
                    LastEvaluatedAt = now
                };
                ApplySnapshot(created1, r, ev, fingerprint, fresh);
                AddSources(created1, r);
                AddHistory(created1, ReplenishmentHistoryActions.Created, null, run.NetDemandRunId, actor.EmployeeId, null, now);
                _ctx.ReplenishmentRecommendations.Add(created1);
                created++;
                continue;
            }

            if (rec.InputFingerprint == fingerprint && rec.IsCurrentlyDefinitive)
            {
                rec.OnHandQuantity = r.OnHandStock ?? 0m;          // اطلاع‌رسانی: ترکیب OnHand/Reserved می‌تواند بدون تغییر Usable عوض شود (بدون Revision)
                rec.ReservedQuantity = r.ReservedStock ?? 0m;
                (rec.StockLastUpdatedAt, rec.DemandLastChangedAt) = fresh.For(r);
                rec.LastEvaluatedAt = now;
                rec.LastRunId = run.NetDemandRunId;
                unchanged++;
                continue;
            }

            // تغییر مادی ورودی ⇒ Revision جدید (Snapshot، منابع و تاریخچه‌ی فقط‌افزودنی)
            var fromStatus = rec.Status;
            var wasDefinitive = rec.IsCurrentlyDefinitive;
            var action = wasDefinitive ? ReplenishmentHistoryActions.Updated : ReplenishmentHistoryActions.DefinitiveAgain;
            string? note = null;

            if (rec.Status == ReplenishmentStatuses.Dismissed)
            {
                if (ReplenishmentRules.IsMaterialChange(rec.DismissedShortageQuantity, rec.DismissedSourceKeys, ev.Shortage, sourceKeys))
                {
                    note = ev.Shortage > (rec.DismissedShortageQuantity ?? 0m)
                        ? $"shortage increased from {rec.DismissedShortageQuantity ?? 0m:0.###} (at dismissal) to {ev.Shortage:0.###}"
                        : "a demand source that did not exist at dismissal now contributes";
                    rec.Status = ReplenishmentStatuses.Open;
                    rec.DismissedAt = null; rec.DismissedByEmployeeId = null; rec.DismissedReason = null;
                    rec.DismissedShortageQuantity = null; rec.DismissedSourceKeys = null;
                    action = ReplenishmentHistoryActions.AutoReopened;
                    autoReopened++;
                }
                else
                {
                    note = "dismissed recommendation re-evaluated; no material increase, stays dismissed";
                }
            }

            rec.Revision++;
            ApplySnapshot(rec, r, ev, fingerprint, fresh);
            rec.LastEvaluatedAt = now;
            rec.LastRunId = run.NetDemandRunId;
            AddSources(rec, r);
            AddHistory(rec, action, fromStatus, run.NetDemandRunId, null, note, now);
            updated++;
        }

        // توصیه‌ی زنده‌ای که اصلاً تقاضای بازمانده‌ای ندارد (هیچ نتیجه‌ای برایش ساخته نشد) ⇒ بسته می‌شود
        foreach (var rec in live.Where(l => !seen.Contains((l.MaterialItemId, l.WarehouseId))))
        {
            Close(rec, ReplenishmentCloseReasons.NoRemainingDemand, run.NetDemandRunId, now);
            closed++;
        }

        run.ResultsEvaluated = evaluated;
        run.Created = created;
        run.Updated = updated;
        run.Unchanged = unchanged;
        run.Closed = closed;
        run.AutoReopened = autoReopened;
        run.NotDefinitive = notDefinitive;
        run.NotComparableNoWarehouse = noWarehouse;
        run.NotDefinitiveSummary = summary.Count == 0 ? null : Truncate(string.Join(" ; ", summary), 4000);
        run.Status = "Completed";
        run.CompletedAt = DateTime.Now;

        await _ctx.SaveChangesAsync(ct);
        return new ReplenishmentRunResult(true, null, false, run.NetDemandRunId, evaluated, created, updated, unchanged, closed, autoReopened, notDefinitive, noWarehouse, summary);
    }

    private static void ApplySnapshot(ReplenishmentRecommendation rec, NetDemandResult r, ReplenishmentRules.Evaluation ev, string fingerprint, Freshness fresh)
    {
        (rec.StockLastUpdatedAt, rec.DemandLastChangedAt) = fresh.For(r);
        rec.OnHandQuantity = r.OnHandStock ?? 0m;
        rec.ReservedQuantity = r.ReservedStock ?? 0m;
        rec.UsableQuantity = r.RawUsableStock ?? 0m;
        rec.GrossEligibleDemand = r.GrossEligibleDemand;
        rec.CoveredByReservations = r.CoveredByReservations;
        rec.UnreservedDemand = r.UnreservedEligibleDemand;
        rec.ShortageQuantity = ev.Shortage;
        rec.RecommendedQuantity = ev.Shortage;           // دقیقاً کمبود؛ بدون Reorder/Lead Time/گرد کردن (تصمیم مالک)
        rec.NoticeCodes = ev.NoticeCodes.Count == 0 ? null : Truncate(string.Join(",", ev.NoticeCodes), 500);
        rec.IsCurrentlyDefinitive = true;
        rec.BlockingIssueCodes = null;
        rec.InputFingerprint = fingerprint;
    }

    private static void AddSources(ReplenishmentRecommendation rec, NetDemandResult r)
    {
        foreach (var c in r.Contributions)
            rec.Sources.Add(new ReplenishmentRecommendationSource
            {
                Revision = rec.Revision,
                SourceType = (int)c.Origin.Source,
                StoreRequisitionLineId = c.Origin.Source == DemandSourceType.StoreRequisitionLine ? c.Origin.SourceLineId : null,
                WorkOrderMaterialId = c.Origin.Source == DemandSourceType.WorkOrderMaterial ? c.Origin.SourceLineId : null,
                GrossRemainingQuantity = c.GrossRemaining,
                CoveredByReservation = c.CoveredByReservation,
                UnreservedQuantity = c.Unreserved
            });
    }

    private static void AddHistory(ReplenishmentRecommendation rec, string action, string? fromStatus, int? runId, int? actorEmployeeId, string? note, DateTime now) =>
        rec.History.Add(new ReplenishmentRecommendationHistory
        {
            Revision = rec.Revision,
            Action = action,
            FromStatus = fromStatus,
            ToStatus = rec.Status,
            ShortageQuantity = rec.ShortageQuantity,
            UsableQuantity = rec.UsableQuantity,
            UnreservedDemand = rec.UnreservedDemand,
            InputFingerprint = rec.InputFingerprint,
            RunId = runId,
            ActorEmployeeId = actorEmployeeId,
            Note = note == null ? null : Truncate(note, 500),
            OccurredAt = now
        });

    private static void MarkNotDefinitive(ReplenishmentRecommendation rec, ReplenishmentRules.Evaluation ev, int runId, DateTime now)
    {
        var codes = Truncate(string.Join(",", ev.BlockingCodes), 500);
        var transition = rec.IsCurrentlyDefinitive;
        rec.IsCurrentlyDefinitive = false;
        rec.BlockingIssueCodes = codes;
        rec.LastEvaluatedAt = now;
        rec.LastRunId = runId;
        if (transition) AddHistory(rec, ReplenishmentHistoryActions.NotDefinitive, rec.Status, runId, null, $"no longer definitive: {codes}", now);
    }

    private static void Close(ReplenishmentRecommendation rec, string reason, int runId, DateTime now)
    {
        var from = rec.Status;
        rec.Status = ReplenishmentStatuses.Closed;
        rec.ClosedAt = now;
        rec.ClosedReason = reason;
        rec.LastEvaluatedAt = now;
        rec.LastRunId = runId;
        AddHistory(rec, ReplenishmentHistoryActions.Closed, from, runId, null, reason, now);
    }

    private static ReplenishmentRunResult Replay(NetDemandRun r) =>
        new(r.Status == "Completed", r.Status == "Completed" ? null : r.FailureMessage ?? "اجرای قبلی با همین کلید کامل نشده است. / The earlier run with this key did not complete.",
            true, r.NetDemandRunId, r.ResultsEvaluated, r.Created, r.Updated, r.Unchanged, r.Closed, r.AutoReopened, r.NotDefinitive,
            r.NotComparableNoWarehouse, SplitSummary(r.NotDefinitiveSummary));

    private static IReadOnlyList<string> SplitSummary(string? s) =>
        string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(" ; ", StringSplitOptions.RemoveEmptyEntries);

    // ───────────────────────────── رد / بازگشایی ─────────────────────────────

    public async Task<ReplenishmentCommandResult> DismissAsync(int recommendationId, byte[]? rowVersion, string reason, ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await Can(actor, "Edit")) return new ReplenishmentCommandResult(false, ForbiddenMessage, Forbidden: true);
        if (string.IsNullOrWhiteSpace(reason)) return new ReplenishmentCommandResult(false, "دلیل رد کردن الزامی است. / A dismissal reason is required.");
        if (MissingVersion(rowVersion)) return new ReplenishmentCommandResult(false, MissingVersionMessage);

        var rec = await _ctx.ReplenishmentRecommendations.FirstOrDefaultAsync(r => r.ReplenishmentRecommendationId == recommendationId, ct);
        if (rec == null) return new ReplenishmentCommandResult(false, "توصیه پیدا نشد. / Recommendation not found.", NotFound: true);
        if (rec.Status != ReplenishmentStatuses.Open)
            return new ReplenishmentCommandResult(false, $"فقط توصیه‌ی Open قابل رد کردن است (وضعیت فعلی: {rec.Status}). / Only an Open recommendation can be dismissed.");

        if (rowVersion is { Length: > 0 }) _ctx.Entry(rec).Property(r => r.RowVersion).OriginalValue = rowVersion;

        var keys = await _ctx.ReplenishmentRecommendationSources.AsNoTracking()
            .Where(s => s.ReplenishmentRecommendationId == rec.ReplenishmentRecommendationId && s.Revision == rec.Revision)
            .Select(s => new { s.SourceType, s.StoreRequisitionLineId, s.WorkOrderMaterialId }).ToListAsync(ct);
        var sourceKeys = keys.Select(k => k.SourceType == (int)DemandSourceType.StoreRequisitionLine
            ? ReplenishmentRules.SourceKey(DemandSourceType.StoreRequisitionLine, k.StoreRequisitionLineId ?? 0)
            : ReplenishmentRules.SourceKey(DemandSourceType.WorkOrderMaterial, k.WorkOrderMaterialId ?? 0));

        var now = DateTime.Now;
        var from = rec.Status;
        rec.Status = ReplenishmentStatuses.Dismissed;
        rec.DismissedAt = now;
        rec.DismissedByEmployeeId = actor.EmployeeId;
        rec.DismissedReason = Truncate(reason.Trim(), 500);
        rec.DismissedShortageQuantity = rec.ShortageQuantity;
        rec.DismissedSourceKeys = Truncate(ReplenishmentRules.JoinKeys(sourceKeys), 4000);
        AddHistory(rec, ReplenishmentHistoryActions.Dismissed, from, null, actor.EmployeeId, rec.DismissedReason, now);

        return await SaveCommandAsync(ct);
    }

    public async Task<ReplenishmentCommandResult> ReopenAsync(int recommendationId, byte[]? rowVersion, string? note, ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await Can(actor, "Edit")) return new ReplenishmentCommandResult(false, ForbiddenMessage, Forbidden: true);
        if (MissingVersion(rowVersion)) return new ReplenishmentCommandResult(false, MissingVersionMessage);

        var rec = await _ctx.ReplenishmentRecommendations.FirstOrDefaultAsync(r => r.ReplenishmentRecommendationId == recommendationId, ct);
        if (rec == null) return new ReplenishmentCommandResult(false, "توصیه پیدا نشد. / Recommendation not found.", NotFound: true);
        if (rec.Status != ReplenishmentStatuses.Dismissed)
            return new ReplenishmentCommandResult(false, $"فقط توصیه‌ی Dismissed قابل بازگشایی است (وضعیت فعلی: {rec.Status}). / Only a Dismissed recommendation can be reopened.");

        if (rowVersion is { Length: > 0 }) _ctx.Entry(rec).Property(r => r.RowVersion).OriginalValue = rowVersion;

        var now = DateTime.Now;
        var from = rec.Status;
        rec.Status = ReplenishmentStatuses.Open;
        rec.DismissedAt = null; rec.DismissedByEmployeeId = null; rec.DismissedReason = null;
        rec.DismissedShortageQuantity = null; rec.DismissedSourceKeys = null;
        AddHistory(rec, ReplenishmentHistoryActions.Reopened, from, null, actor.EmployeeId, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), now);

        return await SaveCommandAsync(ct);
    }

    private const string MissingVersionMessage = "نسخه‌ی توصیه ارسال نشده است؛ صفحه را تازه کنید. / Missing version; reload the page.";

    /// <summary>روی SQL Server نسخه‌ی خوانده‌شده الزامی است (بررسی کهنگی اجباری)؛ Providerهای بدون rowversion (InMemory) همیشه null می‌دهند.</summary>
    private bool MissingVersion(byte[]? rowVersion) => (rowVersion == null || rowVersion.Length == 0) && _ctx.Database.IsRelational();

    private async Task<ReplenishmentCommandResult> SaveCommandAsync(CancellationToken ct)
    {
        try
        {
            await _ctx.SaveChangesAsync(ct);
            return new ReplenishmentCommandResult(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _ctx.ChangeTracker.Clear();
            return new ReplenishmentCommandResult(false, ConflictMessage, Conflict: true);
        }
    }

    // ───────────────────────────── خواندن ─────────────────────────────

    public async Task<ReplenishmentQuery<PagedResult<ReplenishmentListItem>>> GetPagedAsync(ReplenishmentFilter filter, int page, int pageSize, ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await Can(actor, "View")) return new(false, null);
        filter ??= new ReplenishmentFilter();

        var q = _ctx.ReplenishmentRecommendations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(r => r.Status == filter.Status);
        if (filter.DefinitiveOnly == true) q = q.Where(r => r.IsCurrentlyDefinitive);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            q = q.Where(r => r.MaterialItem!.MaterialCode.Contains(term) || r.MaterialItem.Description.Contains(term) || r.Warehouse!.Code.Contains(term));
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : Math.Min(pageSize, 200);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderBy(r => r.Status == ReplenishmentStatuses.Open ? 0 : r.Status == ReplenishmentStatuses.Dismissed ? 1 : 2)
            .ThenByDescending(r => r.ShortageQuantity).ThenBy(r => r.ReplenishmentRecommendationId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReplenishmentListItem
            {
                Id = r.ReplenishmentRecommendationId,
                MaterialItemId = r.MaterialItemId,
                MaterialCode = r.MaterialItem!.MaterialCode,
                MaterialDescription = r.MaterialItem.Description,
                WarehouseId = r.WarehouseId,
                WarehouseCode = r.Warehouse!.Code,
                Status = r.Status,
                Revision = r.Revision,
                UsableQuantity = r.UsableQuantity,
                UnreservedDemand = r.UnreservedDemand,
                ShortageQuantity = r.ShortageQuantity,
                RecommendedQuantity = r.RecommendedQuantity,
                IsCurrentlyDefinitive = r.IsCurrentlyDefinitive,
                LastEvaluatedAt = r.LastEvaluatedAt
            }).ToListAsync(ct);

        return new(true, new PagedResult<ReplenishmentListItem> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    public async Task<ReplenishmentQuery<ReplenishmentDetails>> GetDetailsAsync(int recommendationId, ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await Can(actor, "View")) return new(false, null);

        var rec = await _ctx.ReplenishmentRecommendations.AsNoTracking()
            .Include(r => r.MaterialItem).Include(r => r.Warehouse)
            .FirstOrDefaultAsync(r => r.ReplenishmentRecommendationId == recommendationId, ct);
        if (rec == null) return new(true, null);

        var canSeeRequisitions = await _permissions.HasPermissionAsync(actor.Roles ?? Array.Empty<string>(), "StoreRequisition", "View");
        var canSeeWorkOrders = await _permissions.HasPermissionAsync(actor.Roles ?? Array.Empty<string>(), "WorkOrder", "View");

        var details = new ReplenishmentDetails
        {
            Summary = new ReplenishmentListItem
            {
                Id = rec.ReplenishmentRecommendationId, MaterialItemId = rec.MaterialItemId, MaterialCode = rec.MaterialItem?.MaterialCode ?? "",
                MaterialDescription = rec.MaterialItem?.Description ?? "", WarehouseId = rec.WarehouseId, WarehouseCode = rec.Warehouse?.Code ?? "",
                Status = rec.Status, Revision = rec.Revision, UsableQuantity = rec.UsableQuantity, UnreservedDemand = rec.UnreservedDemand,
                ShortageQuantity = rec.ShortageQuantity, RecommendedQuantity = rec.RecommendedQuantity,
                IsCurrentlyDefinitive = rec.IsCurrentlyDefinitive, LastEvaluatedAt = rec.LastEvaluatedAt
            },
            OnHandQuantity = rec.OnHandQuantity,
            ReservedQuantity = rec.ReservedQuantity,
            GrossEligibleDemand = rec.GrossEligibleDemand,
            CoveredByReservations = rec.CoveredByReservations,
            NoticeCodes = rec.NoticeCodes,
            BlockingIssueCodes = rec.BlockingIssueCodes,
            FirstDetectedAt = rec.FirstDetectedAt,
            DismissedAt = rec.DismissedAt,
            DismissedReason = rec.DismissedReason,
            DismissedShortageQuantity = rec.DismissedShortageQuantity,
            ClosedReason = rec.ClosedReason,
            RowVersion = rec.RowVersion,
            StockLastUpdatedAt = rec.StockLastUpdatedAt,
            DemandLastChangedAt = rec.DemandLastChangedAt,
            RequiresWarehouseAttribution = (rec.BlockingIssueCodes ?? "").Contains(ReplenishmentRules.RequiresWarehouseAttribution, StringComparison.Ordinal)
        };
        var lastRun = await _ctx.NetDemandRuns.AsNoTracking().FirstOrDefaultAsync(x => x.NetDemandRunId == rec.LastRunId, ct);
        details.LastRunStartedAt = lastRun?.StartedAt;
        details.LastRunCompletedAt = lastRun?.CompletedAt;

        var sources = await _ctx.ReplenishmentRecommendationSources.AsNoTracking()
            .Where(s => s.ReplenishmentRecommendationId == rec.ReplenishmentRecommendationId && s.Revision == rec.Revision)
            .OrderBy(s => s.SourceType).ThenBy(s => s.StoreRequisitionLineId).ThenBy(s => s.WorkOrderMaterialId).ToListAsync(ct);

        var lineIds = sources.Where(s => s.StoreRequisitionLineId.HasValue).Select(s => s.StoreRequisitionLineId!.Value).ToList();
        var srNumbers = lineIds.Count == 0 || !canSeeRequisitions
            ? new Dictionary<int, string>()
            : await _ctx.StoreRequisitionLines.AsNoTracking().Where(l => lineIds.Contains(l.StoreRequisitionLineId))
                .Select(l => new { l.StoreRequisitionLineId, Number = l.StoreRequisition!.RequisitionNumber })
                .ToDictionaryAsync(x => x.StoreRequisitionLineId, x => x.Number, ct);
        var woIds = sources.Where(s => s.WorkOrderMaterialId.HasValue).Select(s => s.WorkOrderMaterialId!.Value).ToList();
        var woNumbers = woIds.Count == 0 || !canSeeWorkOrders
            ? new Dictionary<int, string>()
            : await _ctx.WorkOrderMaterials.AsNoTracking().Where(m => woIds.Contains(m.WorkOrderMaterialId))
                .Select(m => new { m.WorkOrderMaterialId, Number = m.WorkOrder!.WorkOrderNumber })
                .ToDictionaryAsync(x => x.WorkOrderMaterialId, x => x.Number, ct);

        foreach (var s in sources)
        {
            var isSr = s.SourceType == (int)DemandSourceType.StoreRequisitionLine;
            var visible = isSr ? canSeeRequisitions : canSeeWorkOrders;
            var id = isSr ? s.StoreRequisitionLineId ?? 0 : s.WorkOrderMaterialId ?? 0;
            string? number = null;
            if (visible) (isSr ? srNumbers : woNumbers).TryGetValue(id, out number);
            details.Sources.Add(new ReplenishmentSourceView
            {
                SourceType = s.SourceType,
                SourceLineId = visible ? id : 0,
                ParentNumber = number,
                IsRestricted = !visible,
                GrossRemainingQuantity = s.GrossRemainingQuantity,
                CoveredByReservation = s.CoveredByReservation,
                UnreservedQuantity = s.UnreservedQuantity
            });
        }

        var history = await _ctx.ReplenishmentRecommendationHistories.AsNoTracking()
            .Where(h => h.ReplenishmentRecommendationId == rec.ReplenishmentRecommendationId)
            .OrderByDescending(h => h.ReplenishmentRecommendationHistoryId).Take(200).ToListAsync(ct);
        var actorIds = history.Where(h => h.ActorEmployeeId.HasValue).Select(h => h.ActorEmployeeId!.Value).Distinct().ToList();
        var names = actorIds.Count == 0 ? new Dictionary<int, string>()
            : await _ctx.Employees.AsNoTracking().Where(e => actorIds.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, Name = e.FirstName + " " + e.LastName }).ToDictionaryAsync(e => e.EmployeeId, e => e.Name, ct);
        details.History = history.Select(h => new ReplenishmentHistoryView
        {
            Revision = h.Revision, Action = h.Action, FromStatus = h.FromStatus, ToStatus = h.ToStatus,
            ShortageQuantity = h.ShortageQuantity, UsableQuantity = h.UsableQuantity,
            ActorName = h.ActorEmployeeId.HasValue ? names.GetValueOrDefault(h.ActorEmployeeId.Value) : null,
            Note = h.Note, OccurredAt = h.OccurredAt
        }).ToList();

        return new(true, details);
    }

    public async Task<ReplenishmentQuery<ReplenishmentRunSummary>> GetLastRunAsync(ReplenishmentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await Can(actor, "View")) return new(false, null);

        var r = await _ctx.NetDemandRuns.AsNoTracking().OrderByDescending(x => x.NetDemandRunId).FirstOrDefaultAsync(ct);
        return new(true, r == null ? null : new ReplenishmentRunSummary(r.NetDemandRunId, r.Status, r.StartedAt, r.CompletedAt, r.ResultsEvaluated, r.Created,
            r.Updated, r.Unchanged, r.Closed, r.AutoReopened, r.NotDefinitive, r.NotComparableNoWarehouse, SplitSummary(r.NotDefinitiveSummary)));
    }

    // ───────────────────────────── تازگی ورودی‌ها / نام‌ها ─────────────────────────────

    private sealed record Freshness(Dictionary<(int, int), DateTime?> Stock, Dictionary<int, DateTime?> RequisitionChange)
    {
        /// <summary>null اگر حتی یک ورودی بی‌زمان باشد (نامعلوم)؛ وگرنه بیشینه‌ی زمان‌ها.</summary>
        public (DateTime? Stock, DateTime? Demand) For(NetDemandResult r)
        {
            DateTime? stock = r.WarehouseId.HasValue && Stock.TryGetValue((r.MaterialItemId, r.WarehouseId.Value), out var s) ? s : null;

            DateTime? demand = null;
            var all = r.Contributions.Count > 0;
            foreach (var c in r.Contributions)
            {
                if (c.Origin.Source == DemandSourceType.StoreRequisitionLine && RequisitionChange.TryGetValue(c.Origin.ParentId, out var t) && t.HasValue)
                    demand = demand == null || t > demand ? t : demand;
                else
                    all = false;                       // منبع Work Order (بدون زمان تغییر) یا درخواست بدون تاریخچه ⇒ تازگی تقاضا نامعلوم
            }
            return (stock, all ? demand : null);
        }
    }

    private async Task<Freshness> LoadFreshnessAsync(IReadOnlyList<NetDemandResult> results, CancellationToken ct)
    {
        var keys = results.Where(r => r.WarehouseId.HasValue).Select(r => (r.MaterialItemId, WarehouseId: r.WarehouseId!.Value)).Distinct().ToList();
        var itemIds = keys.Select(k => k.MaterialItemId).Distinct().ToList();
        var whIds = keys.Select(k => k.WarehouseId).Distinct().ToList();

        var stockRows = itemIds.Count == 0
            ? new List<(int MaterialItemId, int WarehouseId, DateTime? LastUpdated)>()
            : (await _ctx.MaterialStocks.AsNoTracking()
                .Where(s => s.IsActive && itemIds.Contains(s.MaterialItemId) && whIds.Contains(s.WarehouseId))
                .Select(s => new { s.MaterialItemId, s.WarehouseId, s.LastUpdated }).ToListAsync(ct))
                .Select(x => (x.MaterialItemId, x.WarehouseId, x.LastUpdated)).ToList();
        var stock = new Dictionary<(int, int), DateTime?>();
        foreach (var g in stockRows.GroupBy(x => (x.MaterialItemId, x.WarehouseId)))
            stock[g.Key] = g.Any(x => x.LastUpdated == null) ? null : g.Max(x => x.LastUpdated);    // ردیفی بدون LastUpdated ⇒ نامعلوم

        var srIds = results.SelectMany(r => r.Contributions).Where(c => c.Origin.Source == DemandSourceType.StoreRequisitionLine).Select(c => c.Origin.ParentId).Distinct().ToList();
        var history = srIds.Count == 0 ? new Dictionary<int, DateTime?>() : (await _ctx.StoreRequisitionStatusHistories.AsNoTracking()
            .Where(h => srIds.Contains(h.StoreRequisitionId)).GroupBy(h => h.StoreRequisitionId)
            .Select(g => new { Id = g.Key, Last = g.Max(h => h.ChangedDate) }).ToListAsync(ct)).ToDictionary(x => x.Id, x => (DateTime?)x.Last);

        return new Freshness(stock, history);
    }

    private sealed class Names
    {
        public Dictionary<int, string> Items { get; init; } = new();
        public Dictionary<int, string> Warehouses { get; init; } = new();
        public string Item(int id) => Items.TryGetValue(id, out var c) ? $"{c} (I{id})" : $"I{id}";
        public string Warehouse(int id) => Warehouses.TryGetValue(id, out var c) ? $"{c} (W{id})" : $"W{id}";
    }

    private async Task<Names> LoadNamesAsync(IReadOnlyList<NetDemandResult> results, CancellationToken ct)
    {
        var itemIds = results.Select(r => r.MaterialItemId).Distinct().ToList();
        var whIds = results.Where(r => r.WarehouseId.HasValue).Select(r => r.WarehouseId!.Value).Distinct().ToList();
        return new Names
        {
            Items = itemIds.Count == 0 ? new() : await _ctx.MaterialItems.AsNoTracking().Where(m => itemIds.Contains(m.MaterialItemId)).ToDictionaryAsync(m => m.MaterialItemId, m => m.MaterialCode, ct),
            Warehouses = whIds.Count == 0 ? new() : await _ctx.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct)
        };
    }

    // ───────────────────────────── کمکی ─────────────────────────────

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number is 2601 or 2627)) return true;
        return false;
    }
}
