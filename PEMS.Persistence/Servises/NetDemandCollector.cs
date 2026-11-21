using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// Phase 2 / P3 - جمع‌آوری تقاضا، فقط‌خواندنی، فقط از منابعی که رابطه/وضعیت/مقدارشان در کد و تست تأیید شده است.
///
/// ۱) StoreRequisitionLine - وضعیت‌های متعهد: Approved، Reserved، PartiallyIssued (StoreRequisitionStatuses). Submitted «بالقوه» است و
///    شمرده نمی‌شود (PotentialDemandNotCommitted). Draft/Rejected/Cancelled/Issued/Closed تقاضا نیستند. مقدار باقی‌مانده =
///    (ApprovedQuantity ?? RequestedQuantity) - IssuedQuantity (همان فرمول ReserveLinesAsync/IssueCoreAsync). پوشش رزرو = Σ(Quantity -
///    IssuedQuantity) رزروهای Active/PartiallyIssued همان خط (MaterialReservation.StoreRequisitionLineId)؛ این مقدار قبلاً در
///    MaterialStock.ReservedQuantity لحاظ شده، پس فقط از «تقاضا» کم می‌شود (قرارداد NetDemandCalculator). رزرو قدیمی بدون خط فقط وقتی به
///    خط منتسب می‌شود که در همان SR دقیقاً یک خط فعال برای آن متریال باشد (همان قاعده‌ی IssueCoreAsync)، وگرنه مبهم و خارج از مجموع قطعی.
/// ۲) WorkOrderMaterial - فقط WO فعال در Assigned/InProgress/OnHold. Open «بالقوه» (برنامه هنوز قابل‌ویرایش)، Completed/Closed/Cancelled/Rejected تقاضا
///    نیستند، وضعیت ناشناخته ⇒ UnknownLifecycleStatus. باقی‌مانده = RequiredQuantity - Σ(MaintenanceSpareUsage.Quantity - ReturnedQuantity). WO انبار ندارد ⇒
///    WarehouseId = null (هرگز به یک انبار نسبت داده/Pool نمی‌شود). WorkOrderMaterial با (WorkOrderId، MaterialItemId) یکتاست (ایندکس یکتا).
/// ۳) هم‌پوشانی WO↔SR: هیچ FK بین خط SR و WorkOrderMaterial نیست؛ تنها رابطه‌ی ثبت‌شده StoreRequisition.WorkOrderId است. اگر برای همان
///    (WO، متریال) یک SR غیر Rejected/Cancelled با خط فعال وجود داشته باشد، نمی‌توان فهمید یک نیاز است یا دو نیاز ⇒ نیاز WO حذف و مبهم
///    گزارش می‌شود (SR همچنان به‌تنهایی شمرده می‌شود). هیچ حدس بر پایه‌ی متن/توضیح انجام نمی‌شود.
///
/// واحد: MaterialStock و رزروها بدون تبدیل روی «واحد پایه‌ی متریال» کار می‌کنند و ثبت خط SR/WO در کنترلرها UomId نمی‌گذارد (null = پایه).
/// پس UomId خالی ⇒ واحد پایه؛ UomId پر و متفاوت از پایه ⇒ Incompatible؛ متریال بدون واحد پایه ⇒ Unknown. هیچ تبدیلی انجام نمی‌شود.
/// MaterialRequisition، پیش‌بینی PM، تقاضای مهندسی/پروژه و قطعه‌ی ناشناخته عمداً جمع نمی‌شوند (تصمیم نسخه‌ی ۱).
/// مجوز و پوشاندن هویت منبع در NetDemandService اعمال می‌شود (این کلاس داخلی/فقط‌خواندنی است).
/// </summary>
public class NetDemandCollector : INetDemandCollector
{
    private static readonly string[] CommittedStoreRequisitionStatuses =
        { StoreRequisitionStatuses.Approved, StoreRequisitionStatuses.Reserved, StoreRequisitionStatuses.PartiallyIssued };

    // وضعیت‌های WorkOrder از WorkOrderLifecycleService.LegalTransitions (+ گذارهای Approve→Closed، Reject→Rejected، Cancel→Cancelled)
    private static readonly string[] CommittedWorkOrderStatuses = { "Assigned", "InProgress", "OnHold" };
    private const string OpenWorkOrderStatus = "Open";
    private static readonly string[] TerminalWorkOrderStatuses = { "Completed", "Closed", "Cancelled", "Rejected" };

    private static readonly string[] DeadStoreRequisitionStatuses = { StoreRequisitionStatuses.Rejected, StoreRequisitionStatuses.Cancelled };

    private const int ChunkSize = 1000;
    private readonly PEMSDbContext _ctx;

    public NetDemandCollector(PEMSDbContext ctx) => _ctx = ctx;

    private sealed record SrLineRow(int LineId, int RequisitionId, string Number, string Status, int WarehouseId, int MaterialItemId,
        decimal Requested, decimal? Approved, decimal Issued, int? UomId);
    private sealed record ReservationRow(int ReservationId, int RequisitionId, int? LineId, int MaterialItemId, decimal Quantity, decimal Issued);
    private sealed record WoRow(int Id, int WorkOrderId, string Number, string Status, int MaterialItemId, decimal Required, int? UomId, string SupplyCode);
    private sealed record UsageRow(int WorkOrderId, int MaterialItemId, decimal Quantity, decimal? Returned);
    private sealed record MaterialRow(int Id, bool IsActive, bool IsStockItem, int? UomId);

    public async Task<DemandCollection> CollectAsync(NetDemandScope scope, CancellationToken ct = default)
    {
        scope ??= new NetDemandScope();
        var materialFilter = scope.MaterialItemIds?.ToList();
        var warehouseFilter = scope.WarehouseIds?.ToList();
        var lines = new List<DemandLine>();
        var excluded = new List<ExcludedDemand>();

        // ------------------------------------------------------------ Store Requisition
        var srStatuses = CommittedStoreRequisitionStatuses.Append(StoreRequisitionStatuses.Submitted).ToArray();
        var srQuery = _ctx.StoreRequisitionLines.AsNoTracking()
            .Where(l => l.IsActive && l.StoreRequisition!.IsActive && srStatuses.Contains(l.StoreRequisition.Status));
        if (materialFilter != null) srQuery = srQuery.Where(l => materialFilter.Contains(l.MaterialItemId));
        if (warehouseFilter != null) srQuery = srQuery.Where(l => warehouseFilter.Contains(l.StoreRequisition!.WarehouseId));
        var srRows = await srQuery
            .Select(l => new SrLineRow(l.StoreRequisitionLineId, l.StoreRequisitionId, l.StoreRequisition!.RequisitionNumber, l.StoreRequisition.Status,
                l.StoreRequisition.WarehouseId, l.MaterialItemId, l.RequestedQuantity, l.ApprovedQuantity, l.IssuedQuantity, l.UomId))
            .ToListAsync(ct);

        var resQuery = _ctx.MaterialReservations.AsNoTracking()
            .Where(r => r.IsActive && r.StoreRequisitionId != null
                        && (r.Status == MaterialReservationStatuses.Active || r.Status == MaterialReservationStatuses.PartiallyIssued)
                        && r.StoreRequisition!.IsActive && CommittedStoreRequisitionStatuses.Contains(r.StoreRequisition.Status));
        if (materialFilter != null) resQuery = resQuery.Where(r => materialFilter.Contains(r.MaterialItemId));
        var reservations = await resQuery
            .Select(r => new ReservationRow(r.MaterialReservationId, r.StoreRequisitionId!.Value, r.StoreRequisitionLineId, r.MaterialItemId, r.Quantity, r.IssuedQuantity))
            .ToListAsync(ct);

        // ------------------------------------------------------------ Work Order
        var woQuery = _ctx.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.WorkOrder!.IsActive && !TerminalWorkOrderStatuses.Contains(m.WorkOrder.Status));
        if (materialFilter != null) woQuery = woQuery.Where(m => materialFilter.Contains(m.MaterialItemId));
        var woRows = await woQuery
            .Select(m => new WoRow(m.WorkOrderMaterialId, m.WorkOrderId, m.WorkOrder!.WorkOrderNumber, m.WorkOrder.Status, m.MaterialItemId, m.RequiredQuantity, m.UomId, m.SupplyCode))
            .ToListAsync(ct);

        // SCRUM-472: requisition lines raised by the warehouse intake carry a persisted link to the work-order line they supply.
        // Their quantity is counted on the requisition side (with its warehouse); the work-order line keeps only what is not sourced yet.
        var linkedQuery = _ctx.StoreRequisitionLines.AsNoTracking()
            .Where(l => l.IsActive && l.WorkOrderMaterialId != null && l.StoreRequisition!.IsActive
                        && !DeadStoreRequisitionStatuses.Contains(l.StoreRequisition.Status));
        if (materialFilter != null) linkedQuery = linkedQuery.Where(l => materialFilter.Contains(l.MaterialItemId));
        var sourcedByWoLine = (await linkedQuery
                .Select(l => new { Id = l.WorkOrderMaterialId!.Value, Qty = l.ApprovedQuantity ?? l.RequestedQuantity })
                .ToListAsync(ct))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        var usageQuery = _ctx.MaintenanceSpareUsages.AsNoTracking()
            .Where(u => u.IsActive && u.WorkOrder!.IsActive && !TerminalWorkOrderStatuses.Contains(u.WorkOrder.Status));
        if (materialFilter != null) usageQuery = usageQuery.Where(u => materialFilter.Contains(u.MaterialItemId));
        var usages = await usageQuery.Select(u => new UsageRow(u.WorkOrderId, u.MaterialItemId, u.Quantity, u.ReturnedQuantity)).ToListAsync(ct);

        var overlapQuery = _ctx.StoreRequisitionLines.AsNoTracking()
            .Where(l => l.IsActive && l.StoreRequisition!.IsActive && l.StoreRequisition.WorkOrderId != null
                        && l.WorkOrderMaterialId == null   // SCRUM-472: a linked line is a proven relation, not an ambiguous overlap
                        && !DeadStoreRequisitionStatuses.Contains(l.StoreRequisition.Status));
        if (materialFilter != null) overlapQuery = overlapQuery.Where(l => materialFilter.Contains(l.MaterialItemId));
        var overlapPairs = (await overlapQuery.Select(l => new { WorkOrderId = l.StoreRequisition!.WorkOrderId!.Value, l.MaterialItemId }).Distinct().ToListAsync(ct))
            .Select(x => (x.WorkOrderId, x.MaterialItemId)).ToHashSet();

        // ------------------------------------------------------------ متریال‌ها (یک‌جا، بدون N+1)
        var materialIds = srRows.Select(r => r.MaterialItemId).Concat(woRows.Select(r => r.MaterialItemId)).Distinct().ToList();
        var materials = new Dictionary<int, MaterialRow>();
        foreach (var chunk in materialIds.Chunk(ChunkSize))
        {
            var ids = chunk.ToList();
            foreach (var m in await _ctx.MaterialItems.AsNoTracking().Where(x => ids.Contains(x.MaterialItemId))
                         .Select(x => new MaterialRow(x.MaterialItemId, x.IsActive, x.IsStockItem, x.UomId)).ToListAsync(ct))
                materials[m.Id] = m;
        }

        // ------------------------------------------------------------ پردازش SR
        var srByRequisition = srRows.Where(r => r.Status != StoreRequisitionStatuses.Submitted).GroupBy(r => r.RequisitionId).ToDictionary(g => g.Key, g => g.ToList());
        var reservationsByRequisition = reservations.GroupBy(r => r.RequisitionId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var row in srRows.OrderBy(r => r.RequisitionId).ThenBy(r => r.LineId))
        {
            var origin = new DemandOrigin(DemandSourceType.StoreRequisitionLine, row.LineId, row.RequisitionId, row.Number);

            if (row.Status == StoreRequisitionStatuses.Submitted)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, row.WarehouseId, NetDemandIssueCode.PotentialDemandNotCommitted, "store requisition not approved yet", row.Requested));
                continue;
            }

            if (!TryMaterial(materials, row.MaterialItemId, origin, row.WarehouseId, row.Approved ?? row.Requested, excluded, out var material)) continue;

            var approved = row.Approved ?? row.Requested;
            if (approved < 0 || row.Issued < 0)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, row.WarehouseId, NetDemandIssueCode.InvalidQuantity, $"approved={approved}; issued={row.Issued}"));
                continue;
            }
            var remaining = approved - row.Issued;
            if (remaining <= 0) continue;   // کاملاً تحویل شده/تأیید صفر: تقاضای باقی‌مانده ندارد

            // ---- پوشش رزرو
            decimal outstanding = 0;
            var ambiguous = false;
            if (reservationsByRequisition.TryGetValue(row.RequisitionId, out var reqReservations))
            {
                foreach (var res in reqReservations.Where(r => r.MaterialItemId == row.MaterialItemId))
                {
                    if (res.LineId == row.LineId) outstanding += Math.Max(0m, res.Quantity - res.Issued);
                    else if (res.LineId == null)
                    {
                        var candidates = srByRequisition.GetValueOrDefault(row.RequisitionId)?.Count(l => l.MaterialItemId == row.MaterialItemId) ?? 0;
                        if (candidates == 1) outstanding += Math.Max(0m, res.Quantity - res.Issued);
                        else ambiguous = true;
                    }
                }
            }
            if (ambiguous)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, row.WarehouseId, NetDemandIssueCode.AmbiguousReservationAttribution,
                    "legacy reservation without a requisition line cannot be attributed to one line", remaining));
                continue;
            }

            var covered = Math.Min(remaining, outstanding);
            if (outstanding > remaining)
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, row.WarehouseId, NetDemandIssueCode.ReservationExceedsRemainingDemand,
                    $"reserved outstanding {outstanding} > remaining demand {remaining}", outstanding - remaining));

            lines.Add(new DemandLine(origin, row.MaterialItemId, row.WarehouseId, remaining, covered, UnitOf(row.UomId, material)));
        }

        // ------------------------------------------------------------ پردازش WO
        var consumedByPair = new Dictionary<(int, int), decimal>();
        var invalidUsagePairs = new HashSet<(int, int)>();
        foreach (var u in usages)
        {
            var net = u.Quantity - (u.Returned ?? 0m);
            if (u.Quantity < 0 || net < 0) { invalidUsagePairs.Add((u.WorkOrderId, u.MaterialItemId)); continue; }
            consumedByPair[(u.WorkOrderId, u.MaterialItemId)] = consumedByPair.GetValueOrDefault((u.WorkOrderId, u.MaterialItemId)) + net;
        }

        foreach (var row in woRows.OrderBy(r => r.WorkOrderId).ThenBy(r => r.Id))
        {
            var origin = new DemandOrigin(DemandSourceType.WorkOrderMaterial, row.Id, row.WorkOrderId, row.Number);

            if (row.Status == OpenWorkOrderStatus)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.PotentialDemandNotCommitted, "work order is Open: plan still editable", row.Required));
                continue;
            }
            if (!CommittedWorkOrderStatuses.Contains(row.Status))
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.UnknownLifecycleStatus, $"unrecognised work order status '{row.Status}'", row.Required));
                continue;
            }

            if (row.SupplyCode == WorkOrderMaterialSupplyCodes.DirectPurchase)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.DirectPurchaseNotStockDemand,
                    "supply code DirectPurchase: bought for the work order, not supplied from stock", row.Required));
                continue;
            }

            if (!TryMaterial(materials, row.MaterialItemId, origin, null, row.Required, excluded, out var material)) continue;

            if (row.Required < 0)
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.InvalidQuantity, $"required={row.Required}"));
                continue;
            }
            if (row.Required == 0) continue;

            if (overlapPairs.Contains((row.WorkOrderId, row.MaterialItemId)))
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition,
                    "a store requisition exists for the same work order and material; no persisted link proves whether this is one need or two", row.Required));
                continue;
            }
            if (invalidUsagePairs.Contains((row.WorkOrderId, row.MaterialItemId)))
            {
                excluded.Add(new ExcludedDemand(origin, row.MaterialItemId, null, NetDemandIssueCode.InvalidQuantity, "inconsistent spare-usage rows (negative or over-returned)", row.Required));
                continue;
            }

            // SCRUM-472: quantity already sourced through linked requisition lines is demand on the requisition side.
            // Consumption normally comes from those same requisitions, so the larger of the two is subtracted, never the sum
            // (subtracting both would hide a real shortage when the issued spares are also booked as usage).
            var consumed = consumedByPair.GetValueOrDefault((row.WorkOrderId, row.MaterialItemId));
            var sourced = sourcedByWoLine.GetValueOrDefault(row.Id);
            var remaining = row.Required - Math.Max(consumed, sourced);
            if (remaining <= 0) continue;   // کاملاً مصرف یا از انبار تأمین شده

            lines.Add(new DemandLine(origin, row.MaterialItemId, null, remaining, 0m, UnitOf(row.UomId, material)));
        }

        return new DemandCollection(
            lines.OrderBy(l => l.Origin.Source).ThenBy(l => l.Origin.ParentId).ThenBy(l => l.Origin.SourceLineId).ToList(),
            excluded.OrderBy(e => e.Reason).ThenBy(e => e.Origin.Source).ThenBy(e => e.Origin.ParentId).ThenBy(e => e.Origin.SourceLineId).ToList());
    }

    private static bool TryMaterial(Dictionary<int, MaterialRow> materials, int materialId, DemandOrigin origin, int? warehouseId, decimal quantity,
        List<ExcludedDemand> excluded, out MaterialRow material)
    {
        if (!materials.TryGetValue(materialId, out material!))
        {
            excluded.Add(new ExcludedDemand(origin, materialId, warehouseId, NetDemandIssueCode.MaterialNotFound, null, quantity));
            return false;
        }
        if (!material.IsActive)
        {
            excluded.Add(new ExcludedDemand(origin, materialId, warehouseId, NetDemandIssueCode.InactiveMaterial, null, quantity));
            return false;
        }
        if (!material.IsStockItem)
        {
            excluded.Add(new ExcludedDemand(origin, materialId, warehouseId, NetDemandIssueCode.NotStockItem, "demand for a non-stock item cannot be met from stock", quantity));
            return false;
        }
        return true;
    }

    private static DemandUnitStatus UnitOf(int? lineUomId, MaterialRow material)
    {
        if (material.UomId == null) return DemandUnitStatus.Unknown;
        if (lineUomId == null || lineUomId == material.UomId) return DemandUnitStatus.Compatible;
        return DemandUnitStatus.Incompatible;
    }
}
