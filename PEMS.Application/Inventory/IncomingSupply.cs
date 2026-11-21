using PEMS.Domain.Entities;

namespace PEMS.Application.Inventory;

// SCRUM-473 (design PHASE2_NET_DEMAND_AUDIT_AND_DESIGN.md §5, phase P7): incoming supply from open purchase-order lines.
// Pure models and rules (no EF). Owner decisions 2026-10-30:
//   D6a - quantity received on an IRN that is still in QC (Submitted / PendingApproval) counts as incoming until the IRN is Closed;
//         it is never usable stock.
//   D6b - a rejected quantity reopens the PO line unless the buyer chose "close short" (undecided = reopen).
//   D7  - Hold is neither usable nor incoming until the Warehouse Manager resolves it (accepted part -> stock, rejected part -> D6b).
//   D8  - imported POs without lines carry no quantities and are left out of incoming supply.

/// <summary>One receipt (IRN line) against a PO line, as the bucket rules need it.</summary>
public sealed record PoLineReceipt(
    string IrnStatus,
    int WarehouseId,
    decimal Received,
    decimal? Accepted,
    decimal? Rejected,
    decimal? Hold,
    decimal? HoldAccepted,
    decimal? HoldRejected,
    bool HoldResolved,
    string? RejectionAction);

/// <summary>
/// The quantity of one PO line split into buckets; a unit is in exactly one of InQc / Accepted / OnHold / Rejected* / Outstanding,
/// apart from an over-receipt (received above what was ordered), which is reported in <see cref="OverReceived"/>.
/// Invariant: Ordered + RejectedReplaced + OverReceived = Outstanding + Received, and
///            Received = InQc + Accepted + OnHold + RejectedReplaced + RejectedClosedShort.
/// </summary>
public sealed record PoLineSupplyBuckets(
    decimal Ordered,
    decimal Received,
    decimal InQc,
    decimal Accepted,
    decimal OnHold,
    decimal RejectedReplaced,
    decimal RejectedClosedShort,
    decimal Outstanding,
    decimal OverReceived)
{
    /// <summary>D6a: still to arrive plus what is in QC. Accepted is already in stock; Hold and Rejected are neither.</summary>
    public decimal Incoming => Outstanding + InQc;
}

public static class PoLineSupply
{
    public const string IrnSubmitted = "Submitted";
    public const string IrnPendingApproval = "PendingApproval";
    public const string IrnClosed = "Closed";

    /// <summary>IRN statuses whose lines count as received (Draft and Cancelled do not).</summary>
    public static readonly string[] ReceivedStatuses = { IrnSubmitted, IrnPendingApproval, IrnClosed };

    public static bool IsInQc(string status) => status is IrnSubmitted or IrnPendingApproval;

    public static PoLineSupplyBuckets Compute(decimal ordered, IEnumerable<PoLineReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        decimal received = 0, inQc = 0, accepted = 0, onHold = 0, replaced = 0, closedShort = 0;

        foreach (var r in receipts)
        {
            if (!ReceivedStatuses.Contains(r.IrnStatus)) continue;
            received += r.Received;

            if (IsInQc(r.IrnStatus))
            {
                inQc += r.Received;
                continue;
            }

            // Closed: the disposition was approved; Accepted is posted, Hold waits for the Warehouse Manager
            accepted += (r.Accepted ?? 0m) + (r.HoldResolved ? r.HoldAccepted ?? 0m : 0m);
            if (!r.HoldResolved) onHold += r.Hold ?? 0m;
            var rejected = (r.Rejected ?? 0m) + (r.HoldResolved ? r.HoldRejected ?? 0m : 0m);
            if (r.RejectionAction == IrnRejectionActions.CloseShort) closedShort += rejected;
            else replaced += rejected;
        }

        var open = ordered - received + replaced;
        return new PoLineSupplyBuckets(ordered, received, inQc, accepted, onHold, replaced, closedShort,
            Outstanding: Math.Max(0m, open), OverReceived: Math.Max(0m, -open));
    }
}

/// <summary>Incoming supply per (material, warehouse), with the keys whose incoming could not be counted.</summary>
public sealed record IncomingSupplySnapshot(
    IReadOnlyDictionary<StockKey, decimal> Quantities,
    IReadOnlyDictionary<StockKey, IReadOnlyList<NetDemandIssue>> Issues,
    IReadOnlyDictionary<int, IReadOnlyList<NetDemandIssue>>? ItemIssues = null)
{
    public static readonly IncomingSupplySnapshot Empty =
        new(new Dictionary<StockKey, decimal>(), new Dictionary<StockKey, IReadOnlyList<NetDemandIssue>>());

    public decimal QuantityFor(StockKey key) => Quantities.TryGetValue(key, out var q) ? q : 0m;

    /// <summary>Issues of this key plus those of the material as a whole (e.g. an open stock line without a delivery warehouse).</summary>
    public IReadOnlyList<NetDemandIssue> IssuesFor(StockKey key)
    {
        var own = Issues.TryGetValue(key, out var list) ? list : Array.Empty<NetDemandIssue>();
        return ItemIssues != null && ItemIssues.TryGetValue(key.MaterialItemId, out var item) ? own.Concat(item).ToList() : own;
    }
}

/// <summary>Read-only source of incoming supply for the net-demand calculation (implemented on the purchase-order side).</summary>
public interface IIncomingSupplyProvider
{
    Task<IncomingSupplySnapshot> GetIncomingSupplyAsync(IReadOnlyCollection<int>? materialItemIds, CancellationToken ct = default);
}
