using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-471: the pre-procurement intake (owner decision 2026-10-29: every purchase enters the procurement cartable first).
/// Sources: a replenishment recommendation the warehouse sends, a direct-purchase work-order line (SCRUM-472), an engineering
/// MR issued to procurement. Each demand is one task in the procurement role queue of My Work; the buyer converts it into an
/// MR line (two-way link), accepts the MR, or dismisses / returns it with a reason. One live demand per source (filtered unique
/// index plus a pre-check); concurrent decisions are caught on the demand's RowVersion.
/// </summary>
public class PreProcurementService : IPreProcurementService, IProcurementPipeline
{
    public const string Module = "PreProcurement";
    private const string MrIssuedToProcurement = "Issued to Procurement";
    private const string MrApproved = "Approved";
    private const string MrDraft = "Draft";
    private static readonly string[] EndedMrStatuses = { "Closed", "Cancelled" };
    private const string RunningInstance = "Running";

    private readonly PEMSDbContext _context;
    private readonly IWorkflowRuntime _runtime;
    private readonly IPermissionService _permissions;
    private readonly IMaterialRequisitionService _requisitions;

    public PreProcurementService(PEMSDbContext context, IWorkflowRuntime runtime, IPermissionService permissions, IMaterialRequisitionService requisitions)
    {
        _context = context;
        _runtime = runtime;
        _permissions = permissions;
        _requisitions = requisitions;
    }

    // ================================================================== raise

    public async Task<PreProcurementResult> SendRecommendationAsync(int recommendationId, PreProcurementActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "ReplenishmentRecommendation", "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");

        var rec = await _context.ReplenishmentRecommendations.Include(r => r.MaterialItem)
            .FirstOrDefaultAsync(r => r.ReplenishmentRecommendationId == recommendationId);
        if (rec == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (rec.Status != ReplenishmentStatuses.Open || !rec.IsCurrentlyDefinitive || rec.RecommendedQuantity <= 0)
            return PreProcurementResult.Fail("Ppr_Err_RecommendationNotOpen");
        if (await IsLiveAsync(ProcurementDemandSources.Replenishment, recommendationId)) return PreProcurementResult.Fail("Ppr_Err_AlreadyRaised");

        var demand = new ProcurementDemand
        {
            SourceType = ProcurementDemandSources.Replenishment,
            SourceId = recommendationId,
            MaterialItemId = rec.MaterialItemId,
            Description = Truncate(rec.MaterialItem?.Description is { Length: > 0 } d ? d : rec.MaterialItem?.MaterialCode ?? $"#{rec.MaterialItemId}", 500),
            Quantity = rec.RecommendedQuantity,
            UomId = rec.UomId,
            WarehouseId = rec.WarehouseId,
        };

        var from = rec.Status;
        rec.Status = ReplenishmentStatuses.Converted;
        rec.History.Add(new ReplenishmentRecommendationHistory
        {
            Revision = rec.Revision, Action = ReplenishmentHistoryActions.SentToProcurement, FromStatus = from, ToStatus = rec.Status,
            ShortageQuantity = rec.ShortageQuantity, UsableQuantity = rec.UsableQuantity, UnreservedDemand = rec.UnreservedDemand,
            InputFingerprint = rec.InputFingerprint, ActorEmployeeId = actor.EmployeeId, Note = "Sent to the pre-procurement queue", OccurredAt = DateTime.Now
        });

        return await RaiseAsync(new[] { demand }, actor, $"Recommendation #{recommendationId}");
    }

    public async Task<PreProcurementResult> RaiseDirectPurchaseLinesAsync(int workOrderId, PreProcurementActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "WorkOrder", "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");

        var wo = await _context.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.WorkOrderId == workOrderId && w.IsActive);
        if (wo == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (!WarehouseIntakeService.ReleasedWorkOrderStatuses.Contains(wo.Status)) return PreProcurementResult.Fail("Whi_Err_NotReleased");

        var lines = await _context.WorkOrderMaterials.AsNoTracking().Include(m => m.MaterialItem)
            .Where(m => m.WorkOrderId == workOrderId && m.SupplyCode == WorkOrderMaterialSupplyCodes.DirectPurchase)
            .ToListAsync();
        var raised = await GetLiveSourceIdsAsync(ProcurementDemandSources.WorkOrderDirectPurchase, lines.Select(l => l.WorkOrderMaterialId).ToList());
        var demands = lines.Where(l => !raised.Contains(l.WorkOrderMaterialId) && l.RequiredQuantity > 0).Select(l => new ProcurementDemand
        {
            SourceType = ProcurementDemandSources.WorkOrderDirectPurchase,
            SourceId = l.WorkOrderMaterialId,
            MaterialItemId = l.MaterialItemId,
            Description = Truncate(l.MaterialItem?.Description is { Length: > 0 } d ? d : l.MaterialItem?.MaterialCode ?? $"#{l.MaterialItemId}", 500),
            Quantity = l.RequiredQuantity,
            UomId = l.UomId ?? l.MaterialItem?.UomId,
            WorkOrderId = workOrderId,
        }).ToList();
        if (demands.Count == 0) return new PreProcurementResult(true, null, null, 0);

        return await RaiseAsync(demands, actor, $"WorkOrder {wo.WorkOrderNumber}");
    }

    public async Task<PreProcurementResult> RaiseMaterialRequisitionAsync(int materialRequisitionId, PreProcurementActor actor)
    {
        var mr = await _context.MaterialRequisitions.AsNoTracking().FirstOrDefaultAsync(m => m.MRId == materialRequisitionId);
        if (mr == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (mr.Status != MrIssuedToProcurement) return PreProcurementResult.Fail("Ppr_Err_MrNotIssued");
        if (await IsLiveAsync(ProcurementDemandSources.MaterialRequisition, materialRequisitionId)) return new PreProcurementResult(true, null, null, 0);

        var demand = new ProcurementDemand
        {
            SourceType = ProcurementDemandSources.MaterialRequisition,
            SourceId = materialRequisitionId,
            Description = Truncate($"{mr.MRNumber} - {mr.Description}", 500),
            Quantity = 0,
            ProjectId = mr.ProjectId,
            MaterialRequisitionId = materialRequisitionId,
        };
        return await RaiseAsync(new[] { demand }, actor, $"MR {mr.MRNumber}");
    }

    private async Task<PreProcurementResult> RaiseAsync(IReadOnlyList<ProcurementDemand> demands, PreProcurementActor actor, string what)
    {
        var now = DateTime.Now;
        foreach (var d in demands)
        {
            d.Status = ProcurementDemandStatuses.Open;
            d.RaisedAt = now;
            d.RaisedByEmployeeId = actor.EmployeeId;
            d.RaisedByUserName = actor.UserName;
        }

        try
        {
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                _context.ProcurementDemands.AddRange(demands);
                await _context.SaveChangesAsync();
                foreach (var d in demands)
                    await _runtime.StartInstanceAsync(PreProcurementWorkflowSeeder.Module, PreProcurementWorkflowSeeder.BusinessObjectType,
                        d.ProcurementDemandId, PreProcurementWorkflowSeeder.Category, d.ProjectId, actor.EmployeeId);
                Audit(demands[0].ProcurementDemandId, "Raise",
                    $"{what}\nSource: {demands[0].SourceType}\nItems: {string.Join(", ", demands.Select(d => $"#{d.ProcurementDemandId} {d.Description} x {Qty(d.Quantity)}"))}", actor);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        catch (DbUpdateException)
        {
            // the filtered unique index (live source) is the final guard against a concurrent second raise
            _context.ChangeTracker.Clear();
            return PreProcurementResult.Fail("Ppr_Err_AlreadyRaised");
        }

        return new PreProcurementResult(true, null, demands[0].ProcurementDemandId, demands.Count);
    }

    // ================================================================== read

    public async Task<IReadOnlyList<ProcurementDemandRow>> GetQueueAsync(string? status, string? sourceType)
    {
        var q = _context.ProcurementDemands.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(sourceType)) q = q.Where(d => d.SourceType == sourceType);
        var rows = await q.OrderBy(d => d.Status == ProcurementDemandStatuses.Open ? 0 : 1).ThenByDescending(d => d.RaisedAt).ThenByDescending(d => d.ProcurementDemandId)
            .Take(500).Select(RowProjection).ToListAsync();
        return await WithSourceNumbersAsync(rows);
    }

    public async Task<ProcurementDemandDetails?> GetAsync(int procurementDemandId)
    {
        var d = await _context.ProcurementDemands.AsNoTracking().FirstOrDefaultAsync(x => x.ProcurementDemandId == procurementDemandId);
        if (d == null) return null;
        var row = (await WithSourceNumbersAsync(await _context.ProcurementDemands.AsNoTracking()
            .Where(x => x.ProcurementDemandId == procurementDemandId).Select(RowProjection).ToListAsync())).Single();
        var drafts = await _context.MaterialRequisitions.AsNoTracking().Where(m => m.Status == MrDraft && m.IsActive)
            .OrderByDescending(m => m.MRId).Take(200).Select(m => new { m.MRId, m.MRNumber, m.Description }).ToListAsync();
        return new ProcurementDemandDetails(row, d.MaterialItemId, d.WorkOrderId, d.ProjectId, d.DecisionReason, d.DecidedAt, d.DecidedByUserName,
            drafts.Select(m => (m.MRId, m.MRNumber, m.Description)).ToList());
    }

    public async Task<IReadOnlySet<int>> GetLiveSourceIdsAsync(string sourceType, IReadOnlyCollection<int> sourceIds)
    {
        if (sourceIds.Count == 0) return new HashSet<int>();
        var ids = sourceIds.ToList();
        return (await _context.ProcurementDemands.AsNoTracking()
            .Where(d => d.SourceType == sourceType && ids.Contains(d.SourceId) && ProcurementDemandStatuses.Live.Contains(d.Status))
            .Select(d => d.SourceId).ToListAsync()).ToHashSet();
    }

    // ================================================================== decide

    public async Task<PreProcurementResult> ConvertToRequisitionAsync(int procurementDemandId, int? existingMrId, NewRequisitionInput? newMr, PreProcurementActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, Module, "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");
        var demand = await _context.ProcurementDemands.Include(d => d.MaterialItem).Include(d => d.Uom)
            .FirstOrDefaultAsync(d => d.ProcurementDemandId == procurementDemandId);
        if (demand == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (demand.Status != ProcurementDemandStatuses.Open) return PreProcurementResult.Fail("Ppr_Err_AlreadyDecided");
        if (demand.SourceType == ProcurementDemandSources.MaterialRequisition) return PreProcurementResult.Fail("Ppr_Err_NotMaterialDemand");

        if (existingMrId.HasValue)
        {
            if (!await _permissions.HasPermissionAsync(actor.Roles, "MaterialRequisition", "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");
            var mr = await _context.MaterialRequisitions.AsNoTracking().FirstOrDefaultAsync(m => m.MRId == existingMrId.Value && m.IsActive);
            if (mr == null) return PreProcurementResult.Fail("Ppr_Err_MrNotFound");
            if (mr.Status != MrDraft) return PreProcurementResult.Fail("Ppr_Err_MrNotDraft");
        }
        else
        {
            if (newMr == null) return PreProcurementResult.Fail("Ppr_Err_NoTarget");
            if (!await _permissions.HasPermissionAsync(actor.Roles, "MaterialRequisition", "Create")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");
        }

        demand.Status = ProcurementDemandStatuses.Converted;
        Decide(demand, actor, null);

        // the new MR (if any), its line and the decision are one transaction: a failure never leaves an empty MR behind
        return await SaveDecisionAsync(demand, "Complete", actor, async () =>
        {
            var mrId = existingMrId;
            if (mrId == null)
            {
                var description = string.IsNullOrWhiteSpace(newMr!.Description) ? demand.Description : newMr.Description.Trim();
                var created = await _requisitions.CreateAsync(newMr.MrNumber?.Trim() ?? string.Empty, newMr.ProjectId, newMr.DisciplineId, null,
                    description, newMr.RequiredDate, null, null, new List<(string desc, string? part, double qty, string? unit)>());
                if (!created.Success) throw new DecisionRefused(created.Error ?? "Ppr_Err_MrNotFound");   // MR service messages are user text
                mrId = created.Id!.Value;
            }
            demand.MaterialRequisitionId = mrId;
            _context.MaterialRequisitionItems.Add(new MaterialRequisitionItem
            {
                MRId = mrId.Value,
                ItemDescription = demand.Description,
                PartNumber = demand.MaterialItem?.MaterialCode,
                MaterialItemId = demand.MaterialItemId,   // the registered part (SCRUM-428)
                Quantity = (double)demand.Quantity,
                Unit = demand.Uom?.Name,
                ProcurementDemandId = demand.ProcurementDemandId,
                Remarks = $"From pre-procurement #{demand.ProcurementDemandId} ({demand.SourceType})",
            });
            Audit(demand.ProcurementDemandId, "ConvertToMR", $"MR id: {mrId}\nItem: {demand.Description} x {Qty(demand.Quantity)}", actor);
            return mrId;
        });
    }

    public async Task<PreProcurementResult> AcceptAsync(int procurementDemandId, PreProcurementActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, Module, "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");
        var demand = await _context.ProcurementDemands.FirstOrDefaultAsync(d => d.ProcurementDemandId == procurementDemandId);
        if (demand == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (demand.Status != ProcurementDemandStatuses.Open) return PreProcurementResult.Fail("Ppr_Err_AlreadyDecided");
        if (demand.SourceType != ProcurementDemandSources.MaterialRequisition) return PreProcurementResult.Fail("Ppr_Err_NotMrDemand");

        demand.Status = ProcurementDemandStatuses.Accepted;
        Decide(demand, actor, null);
        Audit(demand.ProcurementDemandId, "AcceptMR", $"MR id: {demand.MaterialRequisitionId}", actor);
        return await SaveDecisionAsync(demand, "Complete", actor, () => Task.FromResult(demand.MaterialRequisitionId));
    }

    public async Task<PreProcurementResult> DismissAsync(int procurementDemandId, string reason, PreProcurementActor actor)
    {
        if (string.IsNullOrWhiteSpace(reason)) return PreProcurementResult.Fail("Ppr_Err_ReasonRequired");
        if (!await _permissions.HasPermissionAsync(actor.Roles, Module, "Edit")) return PreProcurementResult.Fail("Ppr_Err_Forbidden");
        var demand = await _context.ProcurementDemands.FirstOrDefaultAsync(d => d.ProcurementDemandId == procurementDemandId);
        if (demand == null) return PreProcurementResult.Fail("Ppr_Err_NotFound");
        if (demand.Status != ProcurementDemandStatuses.Open) return PreProcurementResult.Fail("Ppr_Err_AlreadyDecided");

        demand.Status = ProcurementDemandStatuses.Dismissed;
        Decide(demand, actor, reason.Trim());

        if (demand.SourceType == ProcurementDemandSources.MaterialRequisition && demand.MaterialRequisitionId.HasValue)
        {
            // returned to engineering: the MR goes back to Approved (the one way back from "Issued to Procurement", only through here)
            var mr = await _context.MaterialRequisitions.FirstOrDefaultAsync(m => m.MRId == demand.MaterialRequisitionId.Value);
            if (mr?.Status == MrIssuedToProcurement) mr.Status = MrApproved;
        }
        // a dismissed replenishment demand stops counting as planned supply, so the next run recommends the shortage again
        Audit(demand.ProcurementDemandId, demand.SourceType == ProcurementDemandSources.MaterialRequisition ? "ReturnMR" : "Dismiss", $"Reason: {demand.DecisionReason}", actor);
        return await SaveDecisionAsync(demand, "Cancel", actor, () => Task.FromResult(demand.MaterialRequisitionId));
    }

    private sealed class DecisionRefused(string error) : Exception(error)
    {
        public string Error { get; } = error;
    }

    private async Task<PreProcurementResult> SaveDecisionAsync(ProcurementDemand demand, string workflowAction, PreProcurementActor actor, Func<Task<int?>> prepare)
    {
        int? mrId = null;
        try
        {
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                mrId = await prepare();
                await _context.SaveChangesAsync();
                var instance = await _runtime.GetInstanceAsync(PreProcurementWorkflowSeeder.BusinessObjectType, demand.ProcurementDemandId);
                if (instance?.Status == RunningInstance)
                    await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, workflowAction, actor.EmployeeId, actor.Roles,
                        demand.DecisionReason ?? demand.Status, null, null);
                await tx.CommitAsync();
            });
        }
        catch (DecisionRefused r)
        {
            _context.ChangeTracker.Clear();
            return PreProcurementResult.Fail(r.Error);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return PreProcurementResult.Fail("Ppr_Err_AlreadyDecided");   // another buyer decided it first
        }
        return new PreProcurementResult(true, null, mrId);
    }

    // ================================================================== planned supply (for the replenishment run)

    public async Task<IReadOnlyDictionary<(int MaterialItemId, int WarehouseId), decimal>> GetPlannedSupplyAsync(IReadOnlyCollection<int>? materialItemIds, CancellationToken ct = default)
    {
        var q = _context.ProcurementDemands.AsNoTracking().Where(d => d.SourceType == ProcurementDemandSources.Replenishment
            && d.MaterialItemId != null && d.WarehouseId != null
            && (d.Status == ProcurementDemandStatuses.Open
                || (d.Status == ProcurementDemandStatuses.Converted && d.MaterialRequisition != null && !EndedMrStatuses.Contains(d.MaterialRequisition.Status))));
        if (materialItemIds is { Count: > 0 }) q = q.Where(d => materialItemIds.Contains(d.MaterialItemId!.Value));
        var rows = await q.Select(d => new { d.ProcurementDemandId, Item = d.MaterialItemId!.Value, Wh = d.WarehouseId!.Value, d.Quantity }).ToListAsync(ct);

        // SCRUM-473: once the MR item is on a line of an issued (or closed) PO, that quantity is incoming supply from the PO line
        // (net demand, IIncomingSupplyProvider); it leaves planned supply so the same unit is never counted twice.
        var ids = rows.Select(r => r.ProcurementDemandId).ToList();
        var onIssuedPo = ids.Count == 0 ? new Dictionary<int, decimal>() : (await _context.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.MaterialRequisitionItem != null && l.MaterialRequisitionItem.ProcurementDemandId != null
                            && ids.Contains(l.MaterialRequisitionItem.ProcurementDemandId.Value)
                            && (l.PurchaseOrder!.Status == PurchaseOrderStatuses.Issued || l.PurchaseOrder.Status == PurchaseOrderStatuses.Closed))
                .Select(l => new { DemandId = l.MaterialRequisitionItem!.ProcurementDemandId!.Value, l.Quantity })
                .ToListAsync(ct))
            .GroupBy(x => x.DemandId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        return rows.GroupBy(r => (r.Item, r.Wh))
            .ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, x.Quantity - onIssuedPo.GetValueOrDefault(x.ProcurementDemandId))));
    }

    // ================================================================== helpers

    private static readonly System.Linq.Expressions.Expression<Func<ProcurementDemand, ProcurementDemandRow>> RowProjection = d => new ProcurementDemandRow(
        d.ProcurementDemandId, d.SourceType, d.SourceId, null,
        d.MaterialItem != null ? d.MaterialItem.MaterialCode : null, d.Description, d.Quantity,
        d.Uom != null ? d.Uom.Name : null, d.Warehouse != null ? d.Warehouse.Code : null, d.Status, d.RaisedAt, d.RaisedByUserName,
        d.MaterialRequisitionId, d.MaterialRequisition != null ? d.MaterialRequisition.MRNumber : null);

    /// <summary>Readable source reference: the work-order number, the MR number or the recommendation id.</summary>
    private async Task<IReadOnlyList<ProcurementDemandRow>> WithSourceNumbersAsync(List<ProcurementDemandRow> rows)
    {
        var woLineIds = rows.Where(r => r.SourceType == ProcurementDemandSources.WorkOrderDirectPurchase).Select(r => r.SourceId).ToList();
        var woNumbers = woLineIds.Count == 0 ? new Dictionary<int, string>() : await _context.WorkOrderMaterials.AsNoTracking()
            .Where(m => woLineIds.Contains(m.WorkOrderMaterialId)).Select(m => new { m.WorkOrderMaterialId, m.WorkOrder!.WorkOrderNumber })
            .ToDictionaryAsync(x => x.WorkOrderMaterialId, x => x.WorkOrderNumber);
        return rows.Select(r => r with
        {
            SourceNumber = r.SourceType switch
            {
                ProcurementDemandSources.WorkOrderDirectPurchase => woNumbers.GetValueOrDefault(r.SourceId),
                ProcurementDemandSources.MaterialRequisition => r.MaterialRequisitionNumber,
                _ => "#" + r.SourceId.ToString(CultureInfo.InvariantCulture)
            }
        }).ToList();
    }

    private Task<bool> IsLiveAsync(string sourceType, int sourceId) =>
        _context.ProcurementDemands.AnyAsync(d => d.SourceType == sourceType && d.SourceId == sourceId && ProcurementDemandStatuses.Live.Contains(d.Status));

    private static void Decide(ProcurementDemand d, PreProcurementActor actor, string? reason)
    {
        d.DecidedAt = DateTime.Now;
        d.DecidedByEmployeeId = actor.EmployeeId;
        d.DecidedByUserName = actor.UserName;
        if (reason != null) d.DecisionReason = Truncate(reason, 1000);
    }

    private void Audit(int id, string action, string summary, PreProcurementActor actor) =>
        _context.AuditLogs.Add(new AuditLog
        {
            EntityName = nameof(ProcurementDemand),
            EntityKeyValue = id.ToString(CultureInfo.InvariantCulture),
            Action = action,
            ChangedFieldsSummary = summary,
            UserName = actor.UserName,
            Timestamp = DateTime.Now
        });

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
    private static string Qty(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
