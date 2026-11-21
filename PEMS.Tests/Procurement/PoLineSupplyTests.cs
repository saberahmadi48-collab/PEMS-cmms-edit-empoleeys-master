using PEMS.Application.Inventory;
using PEMS.Domain.Entities;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-473: the quantity buckets of one PO line (design §5 and example E4) and the invariant that no unit is in two buckets.
/// Owner decisions 2026-10-30: in QC counts as incoming (D6a); a rejection reopens the line unless closed short (D6b);
/// Hold is neither usable nor incoming until resolved (D7).
/// </summary>
public class PoLineSupplyTests
{
    private static PoLineReceipt InQc(decimal qty, string status = "Submitted", int wh = 1) =>
        new(status, wh, qty, null, null, null, null, null, false, null);

    private static PoLineReceipt Closed(decimal received, decimal accepted, decimal rejected, decimal hold,
        string? action = null, decimal? holdAccepted = null, decimal? holdRejected = null) =>
        new("Closed", 1, received, accepted, rejected, hold, holdAccepted, holdRejected, holdAccepted.HasValue, action);

    private static void AssertInvariant(PoLineSupplyBuckets b)
    {
        Assert.Equal(b.Ordered + b.RejectedReplaced + b.OverReceived, b.Outstanding + b.Received);
        Assert.Equal(b.Received, b.InQc + b.Accepted + b.OnHold + b.RejectedReplaced + b.RejectedClosedShort);
        Assert.True(b.Outstanding >= 0 && b.OverReceived >= 0);
        Assert.False(b.Outstanding > 0 && b.OverReceived > 0);
    }

    [Fact]
    public void NothingReceived_AllIsOutstanding()
    {
        var b = PoLineSupply.Compute(20, Array.Empty<PoLineReceipt>());
        Assert.Equal(20, b.Outstanding);
        Assert.Equal(20, b.Incoming);
        AssertInvariant(b);
    }

    [Fact]
    public void E4_PartialReceiptInQc_CountsAsIncoming_D6a()
    {
        var b = PoLineSupply.Compute(20, new[] { InQc(12) });
        Assert.Equal(8, b.Outstanding);
        Assert.Equal(12, b.InQc);
        Assert.Equal(20, b.Incoming);   // 8 still to come + 12 in QC; nothing usable yet
        AssertInvariant(b);
    }

    [Fact]
    public void E4_AfterDisposition_AcceptedLeavesIncoming_HoldIsNeither_RejectedReopens()
    {
        var b = PoLineSupply.Compute(20, new[] { Closed(12, accepted: 9, rejected: 1, hold: 2) });
        Assert.Equal(9, b.Accepted);      // already in stock: never incoming as well
        Assert.Equal(2, b.OnHold);
        Assert.Equal(1, b.RejectedReplaced);
        Assert.Equal(9, b.Outstanding);   // 8 open + 1 reopened for replacement (the default)
        Assert.Equal(9, b.Incoming);
        AssertInvariant(b);
    }

    [Fact]
    public void CloseShort_DoesNotReopen_D6b()
    {
        var b = PoLineSupply.Compute(20, new[] { Closed(12, 9, 1, 2, IrnRejectionActions.CloseShort) });
        Assert.Equal(8, b.Outstanding);
        Assert.Equal(1, b.RejectedClosedShort);
        Assert.Equal(0, b.RejectedReplaced);
        AssertInvariant(b);
    }

    [Fact]
    public void ResolvedHold_SplitsIntoAcceptedAndRejected_D7()
    {
        var b = PoLineSupply.Compute(20, new[] { Closed(12, 9, 1, 2, holdAccepted: 1.5m, holdRejected: 0.5m) });
        Assert.Equal(10.5m, b.Accepted);
        Assert.Equal(0, b.OnHold);
        Assert.Equal(1.5m, b.RejectedReplaced);
        Assert.Equal(9.5m, b.Outstanding);
        AssertInvariant(b);
    }

    [Fact]
    public void OverReceipt_IsReported_AndNothingIsOutstanding()
    {
        var b = PoLineSupply.Compute(10, new[] { Closed(12, 12, 0, 0) });
        Assert.Equal(0, b.Outstanding);
        Assert.Equal(2, b.OverReceived);
        AssertInvariant(b);
    }

    [Fact]
    public void DraftAndCancelledReceipts_AreNotReceived()
    {
        var b = PoLineSupply.Compute(10, new[] { InQc(4, "Draft"), InQc(3, "Cancelled") });
        Assert.Equal(10, b.Outstanding);
        Assert.Equal(0, b.Received);
        AssertInvariant(b);
    }

    [Fact]
    public void Invariant_HoldsForManyMixes()
    {
        var rnd = new Random(473);
        for (var i = 0; i < 500; i++)
        {
            var receipts = new List<PoLineReceipt>();
            for (var r = rnd.Next(0, 4); r > 0; r--)
            {
                decimal received = rnd.Next(0, 15);
                if (rnd.Next(3) == 0) { receipts.Add(InQc(received, rnd.Next(2) == 0 ? "Submitted" : "PendingApproval")); continue; }
                decimal acc = rnd.Next(0, (int)received + 1), rej = rnd.Next(0, (int)(received - acc) + 1), hold = received - acc - rej;
                var resolved = rnd.Next(2) == 0;
                decimal ha = resolved ? rnd.Next(0, (int)hold + 1) : 0;
                receipts.Add(Closed(received, acc, rej, hold, rnd.Next(2) == 0 ? null : IrnRejectionActions.CloseShort,
                    resolved ? ha : null, resolved ? hold - ha : null));
            }
            AssertInvariant(PoLineSupply.Compute(rnd.Next(1, 30), receipts));
        }
    }
}
