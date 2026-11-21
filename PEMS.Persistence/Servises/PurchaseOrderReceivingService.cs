using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-473: receiving against purchase-order lines and incoming supply (design PHASE2_NET_DEMAND_AUDIT_AND_DESIGN.md §5, P7).
/// <list type="bullet">
/// <item>An IRN of an issued PO is built from its lines; an over-receipt, an unreleased inspection or another warehouse is a warning
/// (owner 2026-10-30), never a silent change.</item>
/// <item>Quantities per PO line come from <see cref="PoLineSupply"/> (one rule set for the PO view, the OS&amp;D report and net demand).</item>
/// <item>D6b: the buyer decides replace / close short for a rejected quantity; D7: the Warehouse Manager resolves Hold from My Work
/// (posting stays in <see cref="IIrnReceiptService"/>).</item>
/// <item>As <see cref="IIncomingSupplyProvider"/>: open quantity of stock lines of <b>issued</b> POs, at the line's delivery warehouse,
/// plus quantity in QC at the IRN warehouse. Imported POs without lines carry no quantities and are left out (D8).</item>
/// </list>
/// </summary>
public class PurchaseOrderReceivingService : IPurchaseOrderReceivingService, IIncomingSupplyProvider
{
    private const string Running = "Running";

    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissions;
    private readonly IIrnReceiptService _irn;
    private readonly IWorkflowRuntime _runtime;

    public PurchaseOrderReceivingService(PEMSDbContext context, IPermissionService permissions, IIrnReceiptService irn, IWorkflowRuntime runtime)
    {
        _context = context;
        _permissions = permissions;
        _irn = irn;
        _runtime = runtime;
    }

    // ================================================================== PO lines with their buckets

    public async Task<IReadOnlyList<PoLineReceivingView>> GetPoLinesAsync(int poId)
    {
        var lines = await _context.PurchaseOrderLines.AsNoTracking().Where(l => l.PoId == poId).OrderBy(l => l.LineNo)
            .Select(l => new
            {
                l.PurchaseOrderLineId, l.LineNo, l.Description, l.PartNumber, l.MaterialItemId,
                MaterialCode = l.MaterialItem != null ? l.MaterialItem.MaterialCode : null,
                l.Unit, l.IsStockLine, l.WarehouseId, WarehouseCode = l.Warehouse != null ? l.Warehouse.Code : null,
                l.Quantity, l.TenderId, l.InspectionReleasedAt, l.InspectionReleasedByUserName,
                ItpNumber = l.InspectionTestPlan != null ? l.InspectionTestPlan.ITPNumber : null
            })
            .ToListAsync();
        if (lines.Count == 0) return Array.Empty<PoLineReceivingView>();

        var receipts = await ReceiptsByLineAsync(lines.Select(l => l.PurchaseOrderLineId).ToList(), beforeIrnId: null);
        var inspected = await TendersRequiringInspectionAsync(lines.Where(l => l.TenderId.HasValue).Select(l => l.TenderId!.Value).ToList());

        return lines.Select(l =>
        {
            var b = PoLineSupply.Compute(l.Quantity, receipts.GetValueOrDefault(l.PurchaseOrderLineId) ?? new List<PoLineReceipt>());
            return new PoLineReceivingView(l.PurchaseOrderLineId, l.LineNo, l.Description, l.PartNumber, l.MaterialItemId, l.MaterialCode, l.Unit,
                l.IsStockLine, l.WarehouseId, l.WarehouseCode, b.Ordered, b.Received, b.InQc, b.Accepted, b.OnHold, b.RejectedReplaced,
                b.RejectedClosedShort, b.Outstanding, b.OverReceived, l.TenderId.HasValue && inspected.Contains(l.TenderId.Value),
                l.InspectionReleasedAt, l.InspectionReleasedByUserName, l.ItpNumber);
        }).ToList();
    }

    // ================================================================== receiving

    public async Task<PoReceivingResult> ReceiveAgainstPoAsync(int irnReceiptId, IReadOnlyList<IrnPoLineInput> lines, PoReceivingActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "IRN", "Create")) return PoReceivingResult.Fail("Rcv_Err_Forbidden");

        var irn = await _context.IrnReceipts.Include(r => r.Lines).FirstOrDefaultAsync(r => r.IrnReceiptId == irnReceiptId);
        if (irn == null) return PoReceivingResult.Fail("Rcv_Err_NotFound");
        if (irn.Status != "Draft") return PoReceivingResult.Fail("Rcv_Err_NotDraft");
        if (irn.PurchaseOrderId == null) return PoReceivingResult.Fail("Rcv_Err_NoPo");

        var po = await _context.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.PoId == irn.PurchaseOrderId.Value);
        if (po == null) return PoReceivingResult.Fail("Rcv_Err_NotFound");
        if (po.Status != PurchaseOrderStatuses.Issued) return PoReceivingResult.Fail("Rcv_Err_PoNotIssued");
        if (po.VendorId != irn.VendorId) return PoReceivingResult.Fail("Rcv_Err_VendorMismatch");

        var wanted = (lines ?? Array.Empty<IrnPoLineInput>()).Where(l => l.Quantity != 0m).ToList();
        if (wanted.Count == 0) return PoReceivingResult.Fail("Rcv_Err_NoLines");
        if (wanted.Any(l => l.Quantity < 0m)) return PoReceivingResult.Fail("Rcv_Err_Negative");
        if (wanted.Select(l => l.PurchaseOrderLineId).Distinct().Count() != wanted.Count) return PoReceivingResult.Fail("Rcv_Err_Duplicate");

        var ids = wanted.Select(l => l.PurchaseOrderLineId).ToList();
        var poLines = await _context.PurchaseOrderLines.AsNoTracking()
            .Where(l => ids.Contains(l.PurchaseOrderLineId) && l.PoId == po.PoId)
            .Select(l => new { l.PurchaseOrderLineId, l.MaterialItemId, l.Quantity, l.WarehouseId, l.TenderId, l.InspectionReleasedAt, ItemUomId = l.MaterialItem != null ? l.MaterialItem.UomId : null })
            .ToListAsync();
        if (poLines.Count != ids.Count) return PoReceivingResult.Fail("Rcv_Err_LineNotOnPo");
        if (poLines.Any(l => l.MaterialItemId == null)) return PoReceivingResult.Fail("Rcv_Err_LineNotCatalogued");

        var receipts = await ReceiptsByLineAsync(ids, beforeIrnId: null);
        var inspected = await TendersRequiringInspectionAsync(poLines.Where(l => l.TenderId.HasValue).Select(l => l.TenderId!.Value).ToList());
        var warnings = new List<string>();

        foreach (var input in wanted)
        {
            var pl = poLines.First(l => l.PurchaseOrderLineId == input.PurchaseOrderLineId);
            var open = PoLineSupply.Compute(pl.Quantity, receipts.GetValueOrDefault(pl.PurchaseOrderLineId) ?? new List<PoLineReceipt>()).Outstanding;
            // lines of this Draft IRN are not received yet; they still use up the open quantity
            var alreadyOnThisIrn = irn.Lines.Where(x => x.PurchaseOrderLineId == pl.PurchaseOrderLineId).Sum(x => x.ReceivedQuantity);
            if (alreadyOnThisIrn + input.Quantity > open) warnings.Add("Rcv_Warn_OverReceipt");
            if (pl.TenderId.HasValue && inspected.Contains(pl.TenderId.Value) && pl.InspectionReleasedAt == null) warnings.Add("Rcv_Warn_InspectionNotReleased");
            if (pl.WarehouseId.HasValue && pl.WarehouseId != irn.WarehouseId) warnings.Add("Rcv_Warn_OtherWarehouse");

            irn.Lines.Add(new IrnReceiptLine
            {
                IrnReceiptId = irn.IrnReceiptId,
                PurchaseOrderLineId = pl.PurchaseOrderLineId,
                MaterialItemId = pl.MaterialItemId!.Value,
                ReceivedQuantity = input.Quantity,
                UomId = pl.ItemUomId,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
        return PoReceivingResult.Ok(warnings.Distinct().ToList());
    }

    // ================================================================== D6b rejection decision

    public async Task<PoReceivingResult> DecideRejectionAsync(int irnReceiptLineId, string action, PoReceivingActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "PurchaseOrder", "Edit")) return PoReceivingResult.Fail("Rcv_Err_Forbidden");
        if (!IrnRejectionActions.All.Contains(action)) return PoReceivingResult.Fail("Rcv_Err_Action");

        var line = await _context.IrnReceiptLines.Include(l => l.IrnReceipt).FirstOrDefaultAsync(l => l.IrnReceiptLineId == irnReceiptLineId);
        if (line?.IrnReceipt == null) return PoReceivingResult.Fail("Rcv_Err_NotFound");
        if (line.PurchaseOrderLineId == null) return PoReceivingResult.Fail("Rcv_Err_NoPoLine");
        if (line.IrnReceipt.Status != "Closed") return PoReceivingResult.Fail("Rcv_Err_NotDispositioned");
        if ((line.RejectedQuantity ?? 0m) + (line.HoldRejectedQuantity ?? 0m) <= 0m) return PoReceivingResult.Fail("Rcv_Err_NothingRejected");

        line.RejectionAction = action;
        line.RejectionDecidedAt = DateTime.Now;
        line.RejectionDecidedByUserName = actor.UserName;
        await _context.SaveChangesAsync();
        return PoReceivingResult.Ok();
    }

    // ================================================================== D7 Hold resolution

    public async Task<PoReceivingResult> StartHoldResolutionAsync(int irnReceiptId, int? employeeId)
    {
        var hasOpenHold = await _context.IrnReceiptLines.AnyAsync(l => l.IrnReceiptId == irnReceiptId && l.IsActive
            && l.HoldQuantity > 0m && l.HoldResolvedAt == null && l.IrnReceipt!.Status == "Closed");
        if (!hasOpenHold) return PoReceivingResult.Ok();

        var instance = await _runtime.GetInstanceAsync(IrnHoldResolutionWorkflowSeeder.BusinessObjectType, irnReceiptId);
        if (instance?.Status == Running) return PoReceivingResult.Ok();

        await _runtime.StartInstanceAsync(IrnHoldResolutionWorkflowSeeder.Module, IrnHoldResolutionWorkflowSeeder.BusinessObjectType, irnReceiptId,
            IrnHoldResolutionWorkflowSeeder.Category, null, employeeId);
        return PoReceivingResult.Ok();
    }

    public async Task<PoReceivingResult> ResolveHoldAsync(int irnReceiptLineId, decimal accepted, decimal rejected, string? notes, PoReceivingActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "IRN", "Edit")) return PoReceivingResult.Fail("Rcv_Err_Forbidden");
        if (!actor.Roles.Contains(RoleCatalog.WarehouseManager) && !actor.Roles.Contains(SystemRoles.SystemAdmin))
            return PoReceivingResult.Fail("Rcv_Err_NotWarehouseManager");

        var (ok, error, irnId) = await _irn.ResolveHoldAsync(irnReceiptLineId, accepted, rejected, notes, actor.UserName, actor.EmployeeId);
        if (!ok) return PoReceivingResult.Fail(error ?? "Rcv_Err_NotFound");

        var anyOpen = await _context.IrnReceiptLines.AnyAsync(l => l.IrnReceiptId == irnId && l.IsActive && l.HoldQuantity > 0m && l.HoldResolvedAt == null);
        if (anyOpen) return PoReceivingResult.Ok();

        // the last Hold of the IRN is resolved: the task in My Work is done (stock is already posted, so a failure here is only a warning)
        try
        {
            var instance = await _runtime.GetInstanceAsync(IrnHoldResolutionWorkflowSeeder.BusinessObjectType, irnId!.Value);
            if (instance?.Status == Running)
                await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, "Complete", actor.EmployeeId, actor.Roles, "Hold resolved", null, null);
        }
        catch (Exception)
        {
            return PoReceivingResult.Ok(new[] { "Rcv_Warn_TaskNotClosed" });
        }
        return PoReceivingResult.Ok();
    }

    // ================================================================== inspection release

    public async Task<PoReceivingResult> ReleaseInspectionAsync(int purchaseOrderLineId, int? inspectionTestPlanId, PoReceivingActor actor)
    {
        if (!await _permissions.HasPermissionAsync(actor.Roles, "IRN", "Approve")) return PoReceivingResult.Fail("Rcv_Err_Forbidden");

        var line = await _context.PurchaseOrderLines.Include(l => l.PurchaseOrder).FirstOrDefaultAsync(l => l.PurchaseOrderLineId == purchaseOrderLineId);
        if (line?.PurchaseOrder == null) return PoReceivingResult.Fail("Rcv_Err_NotFound");
        if (line.PurchaseOrder.Status != PurchaseOrderStatuses.Issued) return PoReceivingResult.Fail("Rcv_Err_PoNotIssued");
        if (line.InspectionReleasedAt != null) return PoReceivingResult.Fail("Rcv_Err_AlreadyReleased");
        if (inspectionTestPlanId.HasValue && !await _context.InspectionTestPlans.AnyAsync(p => p.InspectionTestPlanId == inspectionTestPlanId.Value))
            return PoReceivingResult.Fail("Rcv_Err_ItpNotFound");

        line.InspectionTestPlanId = inspectionTestPlanId;
        line.InspectionReleasedAt = DateTime.Now;
        line.InspectionReleasedByUserName = actor.UserName;
        await _context.SaveChangesAsync();
        return PoReceivingResult.Ok();
    }

    // ================================================================== OS&D report

    public async Task<OsdReport?> GetOsdReportAsync(int irnReceiptId)
    {
        var irn = await _context.IrnReceipts.AsNoTracking()
            .Include(r => r.Vendor).Include(r => r.Warehouse).Include(r => r.PurchaseOrder).Include(r => r.ReceivedBy)
            .Include(r => r.Lines).ThenInclude(l => l.MaterialItem)
            .Include(r => r.Lines).ThenInclude(l => l.Uom)
            .Include(r => r.Lines).ThenInclude(l => l.PurchaseOrderLine)
            .FirstOrDefaultAsync(r => r.IrnReceiptId == irnReceiptId);
        if (irn == null) return null;

        var poLineIds = irn.Lines.Where(l => l.PurchaseOrderLineId.HasValue).Select(l => l.PurchaseOrderLineId!.Value).Distinct().ToList();
        var earlier = await ReceiptsByLineAsync(poLineIds, beforeIrnId: irn.IrnReceiptId);
        var usedOnThisIrn = new Dictionary<int, decimal>();

        var rows = new List<OsdLine>();
        foreach (var l in irn.Lines.Where(l => l.IsActive).OrderBy(l => l.IrnReceiptLineId))
        {
            var rejected = (l.RejectedQuantity ?? 0m) + (l.HoldResolvedAt != null ? l.HoldRejectedQuantity ?? 0m : 0m);
            var hold = l.HoldResolvedAt == null ? l.HoldQuantity ?? 0m : 0m;
            decimal? accepted = l.AcceptedQuantity.HasValue ? l.AcceptedQuantity.Value + (l.HoldAcceptedQuantity ?? 0m) : null;
            var code = l.MaterialItem?.MaterialCode ?? "";
            var description = l.MaterialItem?.Description ?? "";

            if (l.PurchaseOrderLine == null)
            {
                rows.Add(new OsdLine(l.IrnReceiptLineId, null, code, description, l.Uom?.Code, true, 0m, 0m, l.ReceivedQuantity, 0m, 0m,
                    accepted, rejected, hold, l.RejectionAction, l.Notes));
                continue;
            }

            var pl = l.PurchaseOrderLine;
            var before = PoLineSupply.Compute(pl.Quantity, earlier.GetValueOrDefault(pl.PurchaseOrderLineId) ?? new List<PoLineReceipt>());
            var used = usedOnThisIrn.GetValueOrDefault(pl.PurchaseOrderLineId);
            var openBefore = Math.Max(0m, before.Outstanding - used);
            usedOnThisIrn[pl.PurchaseOrderLineId] = used + l.ReceivedQuantity;

            rows.Add(new OsdLine(l.IrnReceiptLineId, pl.LineNo, code, string.IsNullOrEmpty(description) ? pl.Description : description,
                l.Uom?.Code ?? pl.Unit, false, pl.Quantity, before.Received, l.ReceivedQuantity,
                Over: Math.Max(0m, l.ReceivedQuantity - openBefore), Short: Math.Max(0m, openBefore - l.ReceivedQuantity),
                accepted, rejected, hold, l.RejectionAction, l.Notes));
        }

        return new OsdReport(irn.IrnReceiptId, irn.IrnNumber, irn.Status, irn.ReceivingDate, irn.Vendor?.Name ?? "", irn.Warehouse?.Code ?? "",
            irn.PurchaseOrder?.PoNumber, irn.ReceivedBy != null ? irn.ReceivedBy.FirstName + " " + irn.ReceivedBy.LastName : null, rows);
    }

    // ================================================================== incoming supply (net demand)

    public async Task<IncomingSupplySnapshot> GetIncomingSupplyAsync(IReadOnlyCollection<int>? materialItemIds, CancellationToken ct = default)
    {
        var q = _context.PurchaseOrderLines.AsNoTracking().Where(l => l.PurchaseOrder!.Status == PurchaseOrderStatuses.Issued
            && l.PurchaseOrder.IsActive && l.MaterialItemId != null && l.IsStockLine);
        if (materialItemIds is { Count: > 0 }) q = q.Where(l => materialItemIds.Contains(l.MaterialItemId!.Value));
        var lines = await q.Select(l => new
        {
            l.PurchaseOrderLineId, Item = l.MaterialItemId!.Value, l.WarehouseId, l.Quantity, l.Unit,
            UomCode = l.MaterialItem!.Uom != null ? l.MaterialItem.Uom.Code : null,
            UomName = l.MaterialItem.Uom != null ? l.MaterialItem.Uom.Name : null
        }).ToListAsync(ct);

        var quantities = new Dictionary<StockKey, decimal>();
        var keyIssues = new Dictionary<StockKey, List<NetDemandIssue>>();
        var itemIssues = new Dictionary<int, List<NetDemandIssue>>();
        if (lines.Count == 0) return IncomingSupplySnapshot.Empty;

        var receipts = await ReceiptsByLineAsync(lines.Select(l => l.PurchaseOrderLineId).ToList(), beforeIrnId: null, ct);

        void Add(StockKey key, decimal qty)
        {
            if (qty <= 0m) return;
            quantities[key] = quantities.GetValueOrDefault(key) + qty;
        }

        foreach (var l in lines)
        {
            if (!SameUnit(l.Unit, l.UomCode, l.UomName))
            {
                var issue = new NetDemandIssue(NetDemandIssueCode.IncomingUnitIncompatible, $"PO line {l.PurchaseOrderLineId}: unit '{l.Unit}'");
                if (l.WarehouseId.HasValue) Push(keyIssues, new StockKey(l.Item, l.WarehouseId.Value), issue);
                else Push(itemIssues, l.Item, issue);
                continue;
            }

            var lineReceipts = receipts.GetValueOrDefault(l.PurchaseOrderLineId) ?? new List<PoLineReceipt>();
            var b = PoLineSupply.Compute(l.Quantity, lineReceipts);

            if (b.Outstanding > 0m)
            {
                if (l.WarehouseId.HasValue) Add(new StockKey(l.Item, l.WarehouseId.Value), b.Outstanding);
                else Push(itemIssues, l.Item, new NetDemandIssue(NetDemandIssueCode.IncomingWarehouseUnknown, $"PO line {l.PurchaseOrderLineId}"));
            }

            // D6a: in QC counts as incoming at the warehouse where it was received
            foreach (var r in lineReceipts.Where(r => PoLineSupply.IsInQc(r.IrnStatus)))
                Add(new StockKey(l.Item, r.WarehouseId), r.Received);
        }

        return new IncomingSupplySnapshot(quantities,
            keyIssues.ToDictionary(k => k.Key, k => (IReadOnlyList<NetDemandIssue>)k.Value),
            itemIssues.ToDictionary(k => k.Key, k => (IReadOnlyList<NetDemandIssue>)k.Value));
    }

    // ================================================================== helpers

    private static void Push<TKey>(Dictionary<TKey, List<NetDemandIssue>> map, TKey key, NetDemandIssue issue) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<NetDemandIssue>();
        list.Add(issue);
    }

    /// <summary>A PO line without a unit is in the material's unit; otherwise the text must name that unit (no conversion, D5).</summary>
    internal static bool SameUnit(string? lineUnit, string? uomCode, string? uomName)
    {
        if (string.IsNullOrWhiteSpace(lineUnit)) return true;
        var u = lineUnit.Trim();
        return (uomCode != null && string.Equals(u, uomCode.Trim(), StringComparison.OrdinalIgnoreCase))
               || (uomName != null && string.Equals(u, uomName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Dictionary<int, List<PoLineReceipt>>> ReceiptsByLineAsync(IReadOnlyCollection<int> poLineIds, int? beforeIrnId, CancellationToken ct = default)
    {
        if (poLineIds.Count == 0) return new Dictionary<int, List<PoLineReceipt>>();
        var q = _context.IrnReceiptLines.AsNoTracking()
            .Where(l => l.PurchaseOrderLineId != null && poLineIds.Contains(l.PurchaseOrderLineId.Value) && l.IsActive && l.IrnReceipt!.IsActive);
        // OS&D: only receipts raised before this one (IRN ids grow with time), so an old report does not change after a later receipt
        if (beforeIrnId.HasValue) q = q.Where(l => l.IrnReceiptId < beforeIrnId.Value);
        var rows = await q.Select(l => new
        {
            LineId = l.PurchaseOrderLineId!.Value, l.IrnReceipt!.Status, l.IrnReceipt.WarehouseId, l.ReceivedQuantity, l.AcceptedQuantity,
            l.RejectedQuantity, l.HoldQuantity, l.HoldAcceptedQuantity, l.HoldRejectedQuantity, Resolved = l.HoldResolvedAt != null, l.RejectionAction
        }).ToListAsync(ct);

        return rows.GroupBy(r => r.LineId).ToDictionary(g => g.Key, g => g.Select(r => new PoLineReceipt(r.Status, r.WarehouseId, r.ReceivedQuantity,
            r.AcceptedQuantity, r.RejectedQuantity, r.HoldQuantity, r.HoldAcceptedQuantity, r.HoldRejectedQuantity, r.Resolved, r.RejectionAction)).ToList());
    }

    private async Task<HashSet<int>> TendersRequiringInspectionAsync(IReadOnlyCollection<int> tenderIds)
    {
        if (tenderIds.Count == 0) return new HashSet<int>();
        var ids = await _context.Tenders.AsNoTracking()
            .Where(t => tenderIds.Contains(t.TenderId) && t.ProcurementPackage!.RequiresInspection)
            .Select(t => t.TenderId).ToListAsync();
        return ids.ToHashSet();
    }
}
