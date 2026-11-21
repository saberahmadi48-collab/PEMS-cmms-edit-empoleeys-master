using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Inventory;

/// <summary>
/// SCRUM-473: with an incoming-supply source the net-demand result becomes complete (Projected = Usable + Incoming - Unreserved),
/// and a key whose incoming could not be counted is not definitive.
/// </summary>
public class NetDemandIncomingSupplyTests
{
    private sealed class AllowAll : IPermissionService
    {
        public Task<bool> HasPermissionAsync(IEnumerable<string> userRoles, string moduleName, string action) => Task.FromResult(true);
        public Task<List<RolePermissionDto>> GetMatrixAsync() => throw new NotSupportedException();
        public Task SetPermissionsAsync(string roleName, IEnumerable<ModulePermissionInput> permissions) => throw new NotSupportedException();
        public Task SeedDefaultsAsync() => throw new NotSupportedException();
    }

    private sealed class FixedIncoming(IncomingSupplySnapshot snapshot) : IIncomingSupplyProvider
    {
        public Task<IncomingSupplySnapshot> GetIncomingSupplyAsync(IReadOnlyCollection<int>? materialItemIds, CancellationToken ct = default) => Task.FromResult(snapshot);
    }

    private static async Task<(PEMSDbContext Db, int Item, int Wh)> SeedAsync()
    {
        // OnHand 10, Reserved 6 (SR-A fully reserved), SR-B approved 5 without reservation => usable 4, unreserved 5 => stock-only shortage 1
        var db = PemsDbContextFactory.CreateInMemory("netinc_" + Guid.NewGuid().ToString("N"));
        var uom = new MaterialUOM { Code = "EA", Name = "Each" };
        var wh = new Warehouse { Code = "WH-1", Name = "Main" };
        db.AddRange(uom, wh);
        await db.SaveChangesAsync();
        var item = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", UomId = uom.MaterialUOMId };
        db.MaterialItems.Add(item);
        await db.SaveChangesAsync();
        db.MaterialStocks.Add(new MaterialStock { MaterialItemId = item.MaterialItemId, WarehouseId = wh.WarehouseId, OnHandQuantity = 10, ReservedQuantity = 6 });
        var a = new StoreRequisition { RequisitionNumber = "SR-1", Status = "Reserved", WarehouseId = wh.WarehouseId, RequestedById = 1 };
        a.Lines.Add(new StoreRequisitionLine { MaterialItemId = item.MaterialItemId, RequestedQuantity = 6, ApprovedQuantity = 6 });
        var b = new StoreRequisition { RequisitionNumber = "SR-2", Status = "Approved", WarehouseId = wh.WarehouseId, RequestedById = 1 };
        b.Lines.Add(new StoreRequisitionLine { MaterialItemId = item.MaterialItemId, RequestedQuantity = 5, ApprovedQuantity = 5 });
        db.StoreRequisitions.AddRange(a, b);
        await db.SaveChangesAsync();
        db.MaterialReservations.Add(new MaterialReservation
        {
            MaterialItemId = item.MaterialItemId, WarehouseId = wh.WarehouseId, Quantity = 6, Status = "Active", ReservedById = 1,
            StoreRequisitionId = a.StoreRequisitionId, StoreRequisitionLineId = a.Lines.Single().StoreRequisitionLineId
        });
        await db.SaveChangesAsync();
        return (db, item.MaterialItemId, wh.WarehouseId);
    }

    private static async Task<NetDemandResult> RunAsync(PEMSDbContext db, IIncomingSupplyProvider? incoming)
    {
        var svc = new NetDemandService(new NetDemandCollector(db), new UsableStockService(db), new AllowAll(), incoming);
        return Assert.Single((await svc.CalculateAsync(new NetDemandScope(), new NetDemandCaller(new[] { "ALL" }))).Results);
    }

    [Fact]
    public async Task WithoutProvider_TheResultStaysAStockOnlyEstimate()
    {
        var (db, _, _) = await SeedAsync();
        var r = await RunAsync(db, null);
        Assert.Equal(NetDemandResultKind.StockOnlyShortageEstimate, r.Kind);
        Assert.Equal(IncomingSupplyStatus.NotModeled, r.IncomingSupply);
    }

    [Fact]
    public async Task IncomingCoversTheShortage_ResultIsComplete()
    {
        var (db, item, wh) = await SeedAsync();
        var r = await RunAsync(db, new FixedIncoming(new IncomingSupplySnapshot(
            new Dictionary<StockKey, decimal> { [new StockKey(item, wh)] = 3m }, new Dictionary<StockKey, IReadOnlyList<NetDemandIssue>>())));

        Assert.Equal(NetDemandResultKind.CompleteNetDemand, r.Kind);
        Assert.Equal(IncomingSupplyStatus.Known, r.IncomingSupply);
        Assert.Equal(2m, r.ProjectedBalance);   // 4 + 3 - 5
        Assert.Equal(0m, r.CommittedShortage);
        Assert.Equal(1m, r.StockOnlyShortage);  // still reported for comparison
        Assert.True(r.IsDefinitive);
        Assert.DoesNotContain(r.Issues, i => i.Code == NetDemandIssueCode.IncomingSupplyNotModeled);
    }

    [Fact]
    public async Task NoOpenPoLines_IncomingIsKnownZero()
    {
        var (db, _, _) = await SeedAsync();
        var r = await RunAsync(db, new FixedIncoming(IncomingSupplySnapshot.Empty));

        Assert.Equal(NetDemandResultKind.CompleteNetDemand, r.Kind);
        Assert.Equal(1m, r.CommittedShortage);
    }

    [Fact]
    public async Task IncomingThatCannotBeCounted_MakesTheResultNotDefinitive()
    {
        var (db, item, _) = await SeedAsync();
        var r = await RunAsync(db, new FixedIncoming(new IncomingSupplySnapshot(
            new Dictionary<StockKey, decimal>(), new Dictionary<StockKey, IReadOnlyList<NetDemandIssue>>(),
            new Dictionary<int, IReadOnlyList<NetDemandIssue>> { [item] = new[] { new NetDemandIssue(NetDemandIssueCode.IncomingWarehouseUnknown, "PO line 1") } })));

        Assert.False(r.IsDefinitive);
        Assert.Contains(r.Issues, i => i.Code == NetDemandIssueCode.IncomingWarehouseUnknown);
    }
}
