using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;

namespace PEMS.Persistence.Services;

/// <summary>
/// Phase 2 / P1-P3 - ترکیب‌کننده‌ی فقط‌خواندنی: مجوز → جمع‌آوری تقاضا → موجودی قابل‌استفاده → NetDemandCalculator → پوشاندن هویت منبع.
/// هیچ نوشتن/توصیه/ذخیره/خرید/رزروی ندارد؛ هیچ مجوز جدیدی نمی‌سازد و نمی‌دهد.
///
/// مجوز (کمترین امتیاز، بدون نقش/Grant تازه): دیدن هر نتیجه ⇒ «MaterialItem.View» (همان ماژول موجودی/متریال). هویت منبع (شماره/شناسه‌ی SR یا WO) فقط
/// برای کسی که «StoreRequisition.View» / «WorkOrder.View» دارد نمایش داده می‌شود؛ وگرنه IsRestricted=true و شناسه‌ها صفر می‌شوند ولی مقدار
/// همچنان در مجموع می‌ماند (حذف بی‌صدا کمبود را کمتر از واقع نشان می‌داد). مالکیت درخواست‌دهنده در مدل مشاهده‌ی SR/WO موجود نیست و اعمال نمی‌شود.
///
/// سازگاری خواندن: Queryها پشت‌سرهم و بدون قفل/ایزولیشن خاص اجرا می‌شوند؛ نتیجه «مشاوره‌ای» است و در صورت Post هم‌زمان ممکن است
/// کمی ناهمگون باشد (دوباره‌محاسبه‌پذیر). هیچ Snapshot-Isolation تحمیل نمی‌شود (تصمیم D14).
/// </summary>
public class NetDemandService : INetDemandService
{
    private readonly INetDemandCollector _collector;
    private readonly IUsableStockService _stock;
    private readonly IPermissionService _permissions;
    private readonly IIncomingSupplyProvider? _incoming;

    /// <param name="incoming">SCRUM-473: open PO lines as incoming supply. Without it the result stays a stock-only estimate (IncomingSupplyNotModeled).</param>
    public NetDemandService(INetDemandCollector collector, IUsableStockService stock, IPermissionService permissions, IIncomingSupplyProvider? incoming = null)
    {
        _collector = collector;
        _stock = stock;
        _permissions = permissions;
        _incoming = incoming;
    }

    public async Task<NetDemandReport> CalculateAsync(NetDemandScope scope, NetDemandCaller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!await _permissions.HasPermissionAsync(caller.Roles, "MaterialItem", "View"))
            return new NetDemandReport(false, Array.Empty<NetDemandResult>());

        var canSeeRequisitions = await _permissions.HasPermissionAsync(caller.Roles, "StoreRequisition", "View");
        var canSeeWorkOrders = await _permissions.HasPermissionAsync(caller.Roles, "WorkOrder", "View");

        var results = await ComputeAsync(scope, ct);
        return new NetDemandReport(true, results.Select(r => Mask(r, canSeeRequisitions, canSeeWorkOrders)).ToList());
    }

    public Task<IReadOnlyList<NetDemandResult>> CalculateUnmaskedForSystemAsync(NetDemandScope scope, CancellationToken ct = default) => ComputeAsync(scope, ct);

    private async Task<IReadOnlyList<NetDemandResult>> ComputeAsync(NetDemandScope? scope, CancellationToken ct)
    {
        var demand = await _collector.CollectAsync(scope ?? new NetDemandScope(), ct);

        // کلید موجودی: فقط (متریال، انبار) هایی که تقاضای انبار-دار دارند
        var keys = demand.Lines.Where(l => l.WarehouseId.HasValue).Select(l => new StockKey(l.MaterialItemId, l.WarehouseId!.Value))
            .Concat(demand.Excluded.Where(e => e.WarehouseId.HasValue).Select(e => new StockKey(e.MaterialItemId, e.WarehouseId!.Value)))
            .Distinct().ToList();
        var stock = (await _stock.GetUsableStockAsync(keys, ct)).ToDictionary(s => new StockKey(s.MaterialItemId, s.WarehouseId));
        var incoming = _incoming == null
            ? null
            : await _incoming.GetIncomingSupplyAsync(keys.Select(k => k.MaterialItemId).Distinct().ToList(), ct);

        var groupKeys = demand.Lines.Select(l => (l.MaterialItemId, l.WarehouseId))
            .Concat(demand.Excluded.Select(e => (e.MaterialItemId, e.WarehouseId)))
            .Distinct()
            .OrderBy(k => k.MaterialItemId).ThenBy(k => k.WarehouseId ?? int.MaxValue)
            .ToList();

        // مسائل سطح متریال که همه‌ی نتایج انبارِ آن متریال را غیرقطعی می‌کنند: نیاز WO بی‌انبار یا نیاز WO حذف‌شده به‌خاطر ابهام
        var itemLevel = new Dictionary<int, List<NetDemandIssue>>();
        void AddItemIssue(int materialId, NetDemandIssue issue)
        {
            if (!itemLevel.TryGetValue(materialId, out var list)) itemLevel[materialId] = list = new List<NetDemandIssue>();
            list.Add(issue);
        }
        foreach (var l in demand.Lines.Where(l => l.WarehouseId == null))
            AddItemIssue(l.MaterialItemId, new NetDemandIssue(NetDemandIssueCode.WorkOrderDemandNotAttributableToWarehouse, "work-order demand exists without a warehouse"));
        foreach (var e in demand.Excluded.Where(e => e.Origin.Source == DemandSourceType.WorkOrderMaterial && NetDemandIssues.IsBlocking(e.Reason)))
            AddItemIssue(e.MaterialItemId, new NetDemandIssue(e.Reason, "work-order requirement not counted"));

        var results = new List<NetDemandResult>(groupKeys.Count);
        foreach (var (materialId, warehouseId) in groupKeys)
        {
            UsableStockResult? s = null;
            if (warehouseId.HasValue) stock.TryGetValue(new StockKey(materialId, warehouseId.Value), out s);

            var levelIssues = warehouseId.HasValue && itemLevel.TryGetValue(materialId, out var inherited) ? inherited : null;
            decimal? known = null;
            if (incoming != null && warehouseId.HasValue)
            {
                var key = new StockKey(materialId, warehouseId.Value);
                known = incoming.QuantityFor(key);
                var incomingIssues = incoming.IssuesFor(key);
                if (incomingIssues.Count > 0) levelIssues = (levelIssues ?? new List<NetDemandIssue>()).Concat(incomingIssues).ToList();
            }

            var input = new NetDemandInput(
                materialId, warehouseId, s,
                demand.Lines.Where(l => l.MaterialItemId == materialId && l.WarehouseId == warehouseId).ToList(),
                demand.Excluded.Where(e => e.MaterialItemId == materialId && e.WarehouseId == warehouseId).ToList(),
                levelIssues,
                known);

            results.Add(NetDemandCalculator.Calculate(input));
        }

        return results;
    }

    private static NetDemandResult Mask(NetDemandResult r, bool canSeeRequisitions, bool canSeeWorkOrders)
    {
        DemandOrigin M(DemandOrigin o) =>
            (o.Source == DemandSourceType.StoreRequisitionLine && canSeeRequisitions) || (o.Source == DemandSourceType.WorkOrderMaterial && canSeeWorkOrders)
                ? o
                : new DemandOrigin(o.Source, 0, 0, null, true);

        return r with
        {
            Contributions = r.Contributions.Select(c => c with { Origin = M(c.Origin) }).ToList(),
            Excluded = r.Excluded.Select(e => e with { Origin = M(e.Origin), Detail = M(e.Origin).IsRestricted ? null : e.Detail }).ToList(),
            // Detail از Issues هم ممکن است شناسه‌ی منبع داشته باشد
            Issues = r.Issues.Select(i => i.Detail != null && (!canSeeRequisitions || !canSeeWorkOrders) ? i with { Detail = null } : i).ToList()
        };
    }
}
