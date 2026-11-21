namespace PEMS.Application.Inventory;

/// <summary>
/// Phase 2 / P2 - محاسبه‌ی خالص و قطعی (بدون EF، DB، Controller، Workflow، ساعت یا تصادف) نیاز خالص یک (متریال، انبار).
///
/// قرارداد رزرو (برای جلوگیری از کسر دوباره‌ی رزروها):
///  • UsableStock = OnHand - Reserved (همان AvailableQuantity سرویس‌های رزرو/انتقال/مصرف) - رزروها یک‌بار از «موجودی» کم شده‌اند.
///  • تقاضایی که از قبل رزرو دارد (CoveredByReservation) از «تقاضا» کم می‌شود، نه دوباره از موجودی:
///        Unreserved = Gross - Covered
///  • StockOnlyShortage = max(0, Unreserved - max(0, UsableStock))
///  یعنی هر مقدار رزرو دقیقاً یک‌بار اثر می‌گذارد (مثال: OnHand 10، Reserved 6 برای SR-A، SR-B تأییدشده 5 ⇒ Usable 4، Unreserved 5 ⇒ کمبود 1).
///
/// ورودی (Incoming) در نسخه‌ی ۱ مدل نشده است: بدون KnownIncomingSupply هرگز صفر فرض نمی‌شود؛ نتیجه
/// StockOnlyShortageEstimate است و Projected/CommittedShortage = null. فقط وقتی ورودی «معلوم» داده شود:
///        Projected = Usable + Incoming - Unreserved ؛ CommittedShortage = max(0, -Projected)  (CompleteNetDemand).
/// Top-up نقطه‌ی سفارش جدا از کمبود است و هیچ مقداری پیشنهاد/جمع نمی‌شود (معنای Min/Max/ReorderQuantity تصمیم‌گیری نشده).
/// </summary>
public static class NetDemandCalculator
{
    public static NetDemandResult Calculate(NetDemandInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Stock is { } s0 && (s0.MaterialItemId != input.MaterialItemId || (input.WarehouseId.HasValue && s0.WarehouseId != input.WarehouseId.Value)))
            throw new ArgumentException("Stock does not belong to the requested material/warehouse.", nameof(input));

        var issues = new List<NetDemandIssue>(input.ItemLevelIssues ?? Array.Empty<NetDemandIssue>());
        var excluded = new List<ExcludedDemand>(input.Excluded);

        // ---- تکرار قطعی (همان منبع+خط): فقط وقتی کاملاً یکسان است یک‌بار شمرده می‌شود؛ متعارض ⇒ هیچ‌کدام
        var kept = new List<DemandLine>();
        foreach (var group in input.Demand
                     .OrderBy(l => l.Origin.Source).ThenBy(l => l.Origin.ParentId).ThenBy(l => l.Origin.SourceLineId)
                     .GroupBy(l => (l.Origin.Source, l.Origin.ParentId, l.Origin.SourceLineId)))
        {
            var rows = group.ToList();
            if (rows.Count == 1) { kept.Add(rows[0]); continue; }

            var first = rows[0];
            if (rows.All(r => r.GrossRemainingQuantity == first.GrossRemainingQuantity
                              && r.CoveredByReservationQuantity == first.CoveredByReservationQuantity
                              && r.MaterialItemId == first.MaterialItemId && r.WarehouseId == first.WarehouseId
                              && r.UnitStatus == first.UnitStatus))
            {
                kept.Add(first);
                issues.Add(new NetDemandIssue(NetDemandIssueCode.DuplicateSourceLineIgnored, Describe(first.Origin)));
            }
            else
            {
                foreach (var r in rows)
                    excluded.Add(new ExcludedDemand(r.Origin, r.MaterialItemId, r.WarehouseId, NetDemandIssueCode.ConflictingDuplicateSourceLine,
                        "same source line appears with different quantities", r.GrossRemainingQuantity));
            }
        }

        // ---- اعتبارسنجی هر خط (واحد، مقدار)؛ تبدیل واحد/حدس وجود ندارد
        var valid = new List<DemandLine>();
        foreach (var line in kept)
        {
            if (line.MaterialItemId != input.MaterialItemId || line.WarehouseId != input.WarehouseId)
                throw new ArgumentException("Demand line does not belong to the requested material/warehouse.", nameof(input));

            if (line.UnitStatus == DemandUnitStatus.Unknown)
                excluded.Add(new ExcludedDemand(line.Origin, line.MaterialItemId, line.WarehouseId, NetDemandIssueCode.UnitUnknown, null, line.GrossRemainingQuantity));
            else if (line.UnitStatus == DemandUnitStatus.Incompatible)
                excluded.Add(new ExcludedDemand(line.Origin, line.MaterialItemId, line.WarehouseId, NetDemandIssueCode.UnitIncompatible, null, line.GrossRemainingQuantity));
            else if (line.GrossRemainingQuantity <= 0 || line.CoveredByReservationQuantity < 0 || line.CoveredByReservationQuantity > line.GrossRemainingQuantity)
                excluded.Add(new ExcludedDemand(line.Origin, line.MaterialItemId, line.WarehouseId, NetDemandIssueCode.InvalidQuantity,
                    $"gross={line.GrossRemainingQuantity}; covered={line.CoveredByReservationQuantity}", line.GrossRemainingQuantity));
            else
                valid.Add(line);
        }

        foreach (var e in excluded) issues.Add(new NetDemandIssue(e.Reason, Describe(e.Origin)));

        var gross = valid.Sum(l => l.GrossRemainingQuantity);
        var covered = valid.Sum(l => l.CoveredByReservationQuantity);
        var unreserved = gross - covered;
        var contributions = valid
            .Select(l => new DemandContribution(l.Origin, l.GrossRemainingQuantity, l.CoveredByReservationQuantity, l.GrossRemainingQuantity - l.CoveredByReservationQuantity))
            .ToList();

        NetDemandResultKind kind;
        decimal? raw = null, forNetting = null, stockOnly = null, projected = null, committed = null;
        var incoming = input.KnownIncomingSupply.HasValue ? IncomingSupplyStatus.Known : IncomingSupplyStatus.NotModeled;

        if (input.WarehouseId == null)
        {
            kind = NetDemandResultKind.NotComparableNoWarehouse;
            if (gross > 0 || excluded.Count > 0)
                issues.Add(new NetDemandIssue(NetDemandIssueCode.WorkOrderDemandNotAttributableToWarehouse, "demand has no warehouse; stock is never pooled across warehouses"));
        }
        else if (input.Stock == null)
        {
            kind = NetDemandResultKind.NotCalculable;
            issues.Add(new NetDemandIssue(NetDemandIssueCode.StockMissing));
        }
        else
        {
            var stock = input.Stock;
            issues.AddRange(stock.Issues);
            if (stock.State is StockState.AmbiguousRows or StockState.NotCalculable)
            {
                kind = NetDemandResultKind.NotCalculable;
            }
            else
            {
                raw = stock.State == StockState.NoStockRecord ? 0m : stock.RawUsable ?? ((stock.OnHand ?? 0m) - (stock.Reserved ?? 0m));
                // دفاعی: نامعتبربودن اعداد حتی اگر تولیدکننده‌ی Stock علامتش را نزده باشد صریح گزارش می‌شود
                if ((stock.OnHand ?? 0m) < 0) issues.Add(new NetDemandIssue(NetDemandIssueCode.StockNegativeOnHand, $"on hand {stock.OnHand}"));
                if ((stock.Reserved ?? 0m) < 0) issues.Add(new NetDemandIssue(NetDemandIssueCode.StockNegativeReserved, $"reserved {stock.Reserved}"));
                if ((stock.Reserved ?? 0m) > (stock.OnHand ?? 0m)) issues.Add(new NetDemandIssue(NetDemandIssueCode.StockReservedExceedsOnHand, $"reserved {stock.Reserved} > on hand {stock.OnHand}"));
                // Clamp فقط در محاسبه (مقدار پایگاه داده دست‌نخورده)؛ منفی‌بودن در Issues صریح گزارش و نتیجه غیرقطعی می‌شود
                forNetting = Math.Max(0m, raw.Value);
                stockOnly = Math.Max(0m, unreserved - forNetting.Value);

                if (input.KnownIncomingSupply.HasValue)
                {
                    kind = NetDemandResultKind.CompleteNetDemand;
                    projected = forNetting.Value + input.KnownIncomingSupply.Value - unreserved;
                    committed = Math.Max(0m, -projected.Value);
                }
                else
                {
                    kind = NetDemandResultKind.StockOnlyShortageEstimate;
                }
            }
        }

        if (incoming == IncomingSupplyStatus.NotModeled)
            issues.Add(new NetDemandIssue(NetDemandIssueCode.IncomingSupplyNotModeled, "no incoming-supply source (open PO lines) was given"));

        ReorderTopUp reorder;
        if (kind is NetDemandResultKind.NotCalculable or NetDemandResultKind.NotComparableNoWarehouse)
            reorder = new ReorderTopUp(ReorderTopUpStatus.NotCalculable, null);
        else if (input.Stock?.Policy?.ReorderPoint is null)
            reorder = new ReorderTopUp(ReorderTopUpStatus.NotConfigured, input.Stock?.Policy);
        else
        {
            reorder = new ReorderTopUp(ReorderTopUpStatus.PolicyPresentNotEvaluated, input.Stock!.Policy);
            issues.Add(new NetDemandIssue(NetDemandIssueCode.ReorderPolicyNotEvaluated, "min/max/reorder semantics and lead time are undecided"));
        }

        var orderedIssues = issues
            .DistinctBy(i => (i.Code, i.Detail))
            .OrderBy(i => i.Code).ThenBy(i => i.Detail, StringComparer.Ordinal)
            .ToList();

        var definitive = kind is NetDemandResultKind.StockOnlyShortageEstimate or NetDemandResultKind.CompleteNetDemand
                         && !orderedIssues.Any(i => i.IsBlocking);

        return new NetDemandResult(
            input.MaterialItemId, input.WarehouseId, kind, definitive,
            gross, covered, unreserved, raw, forNetting, incoming,
            stockOnly, projected, committed, reorder,
            contributions.OrderBy(c => c.Origin.Source).ThenBy(c => c.Origin.ParentId).ThenBy(c => c.Origin.SourceLineId).ToList(),
            excluded.OrderBy(e => e.Reason).ThenBy(e => e.Origin.Source).ThenBy(e => e.Origin.ParentId).ThenBy(e => e.Origin.SourceLineId).ToList(),
            orderedIssues,
            input.Stock?.OnHand, input.Stock?.Reserved);
    }

    private static string Describe(DemandOrigin o) => $"{o.Source}#{o.SourceLineId} (parent {o.ParentId})";
}
