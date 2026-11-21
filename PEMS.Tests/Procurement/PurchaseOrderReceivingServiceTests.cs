using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-473: receiving against PO lines, the OS&amp;D report, the buyer's rejection decision (D6b), Hold resolution by the
/// Warehouse Manager (D7), inspection release and incoming supply for net demand (D6a, D8).
/// </summary>
public class PurchaseOrderReceivingServiceTests
{
    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required PurchaseOrderReceivingService Service { get; init; }
        public required IrnReceiptService Irn { get; init; }
        public required FakeWorkflowRuntime Runtime { get; init; }
        public required FakePermissionService Perms { get; init; }
        public int Po, Line1, Line2, FreeLine, Item1, Item2;
    }

    private const int Vendor = 1, OtherVendor = 2, Wh = 1, Wh2 = 2;
    private static readonly PoReceivingActor Clerk = new(1, "clerk", new[] { "WH.Storekeeper" });
    private static readonly PoReceivingActor Buyer = new(3, "buyer", new[] { RoleCatalog.ProcurementBuyer });
    private static readonly PoReceivingActor WhManager = new(4, "whm", new[] { RoleCatalog.WarehouseManager });

    private static async Task<Env> NewAsync(string poStatus = PurchaseOrderStatuses.Issued, string? line1Unit = null, int? line1Wh = Wh, bool requiresInspection = false)
    {
        var db = PemsDbContextFactory.CreateInMemory("rcv_" + Guid.NewGuid().ToString("N"));
        db.Vendors.AddRange(new Vendor { VendorId = Vendor, Name = "Acme", IsActive = true }, new Vendor { VendorId = OtherVendor, Name = "Other", IsActive = true });
        db.Warehouses.AddRange(new Warehouse { WarehouseId = Wh, Code = "WH-01", Name = "Main", IsActive = true },
            new Warehouse { WarehouseId = Wh2, Code = "WH-02", Name = "Site", IsActive = true });
        db.Departments.Add(new Department { DepartmentId = 1, Name = "Warehouse" });
        db.Employees.Add(new Employee { EmployeeId = 1, FirstName = "Receiving", LastName = "Clerk", DepartmentId = 1, IsActive = true });
        var uom = new MaterialUOM { Code = "EA", Name = "Each", IsActive = true };
        db.Add(uom);
        var project = new Project { ProjectCode = "P-1", ProjectName = "Plant", IsActive = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var item1 = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", IsActive = true, IsStockItem = true, UomId = uom.MaterialUOMId };
        var item2 = new MaterialItem { MaterialCode = "M-2", Description = "Seal", IsActive = true, IsStockItem = true, UomId = uom.MaterialUOMId };
        db.MaterialItems.AddRange(item1, item2);
        var package = new ProcurementPackage { PackageNumber = "PP-1", ProjectId = project.ProjectId, Title = "Pumps", IsActive = true, RequiresInspection = requiresInspection };
        db.ProcurementPackages.Add(package);
        await db.SaveChangesAsync();
        var tender = new Tender { ProcurementPackageId = package.ProcurementPackageId, IsActive = true };
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder { PoNumber = "PO-1", ProjectId = project.ProjectId, VendorId = Vendor, Currency = "IRR", Status = poStatus, IsActive = true };
        po.Lines.Add(new PurchaseOrderLine { LineNo = 1, MaterialItemId = item1.MaterialItemId, Description = "Bearing", Quantity = 20, Unit = line1Unit, WarehouseId = line1Wh, IsStockLine = true, TenderId = tender.TenderId });
        po.Lines.Add(new PurchaseOrderLine { LineNo = 2, MaterialItemId = item2.MaterialItemId, Description = "Seal", Quantity = 5, Unit = "EA", WarehouseId = Wh, IsStockLine = true });
        po.Lines.Add(new PurchaseOrderLine { LineNo = 3, Description = "Alignment service", Quantity = 1, IsStockLine = false });
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var perms = new FakePermissionService();
        var runtime = new FakeWorkflowRuntime();
        var irn = new IrnReceiptService(db);
        var lines = po.Lines.OrderBy(l => l.LineNo).ToList();
        return new Env
        {
            Db = db, Service = new PurchaseOrderReceivingService(db, perms, irn, runtime), Irn = irn, Runtime = runtime, Perms = perms,
            Po = po.PoId, Line1 = lines[0].PurchaseOrderLineId, Line2 = lines[1].PurchaseOrderLineId, FreeLine = lines[2].PurchaseOrderLineId,
            Item1 = item1.MaterialItemId, Item2 = item2.MaterialItemId
        };
    }

    private static async Task<int> DraftIrnAsync(Env e, int vendor = Vendor, int wh = Wh)
    {
        var irn = new IrnReceipt { IrnNumber = "IRN-" + Guid.NewGuid().ToString("N")[..6], Status = "Draft", VendorId = vendor, WarehouseId = wh, ReceivedById = 1, PurchaseOrderId = e.Po, IsActive = true };
        e.Db.IrnReceipts.Add(irn);
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();
        return irn.IrnReceiptId;
    }

    private static async Task<int> ReceiveAndSubmitAsync(Env e, decimal qty1, decimal qty2 = 0, int wh = Wh)
    {
        var irnId = await DraftIrnAsync(e, wh: wh);
        var lines = new List<IrnPoLineInput> { new(e.Line1, qty1) };
        if (qty2 > 0) lines.Add(new IrnPoLineInput(e.Line2, qty2));
        Assert.True((await e.Service.ReceiveAgainstPoAsync(irnId, lines, Clerk)).Success);
        Assert.True((await e.Irn.SubmitAsync(irnId, null, 1)).Success);
        e.Db.ChangeTracker.Clear();
        return irnId;
    }

    /// <summary>Records a disposition on the first line and approves it (Accepted posted, IRN Closed).</summary>
    private static async Task<int> DispositionAsync(Env e, int irnId, decimal accepted, decimal rejected, decimal hold)
    {
        var lines = await e.Db.IrnReceiptLines.AsNoTracking().Where(l => l.IrnReceiptId == irnId).OrderBy(l => l.IrnReceiptLineId).ToListAsync();
        var input = lines.Select((l, i) => i == 0 ? (l.IrnReceiptLineId, accepted, rejected, hold) : (l.IrnReceiptLineId, l.ReceivedQuantity, 0m, 0m)).ToList();
        Assert.True((await e.Irn.RecordDispositionAsync(irnId, input, null, 2)).Success);
        Assert.True((await e.Irn.CompleteDispositionAsync(irnId, true, 2)).Success);
        e.Db.ChangeTracker.Clear();
        return lines[0].IrnReceiptLineId;
    }

    // ================================================================== receiving

    [Fact]
    public async Task Receive_CreatesLinesLinkedToThePoLine_InTheMaterialUnit()
    {
        var e = await NewAsync();
        var irnId = await DraftIrnAsync(e);

        var r = await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 12), new IrnPoLineInput(e.Line2, 0) }, Clerk);

        Assert.True(r.Success, r.Error);
        Assert.Empty(r.Warnings);
        var line = Assert.Single(await e.Db.IrnReceiptLines.Where(l => l.IrnReceiptId == irnId).ToListAsync());
        Assert.Equal(e.Line1, line.PurchaseOrderLineId);
        Assert.Equal(e.Item1, line.MaterialItemId);
        Assert.Equal(12, line.ReceivedQuantity);
        Assert.NotNull(line.UomId);
    }

    [Fact]
    public async Task OverReceipt_IsAllowed_WithAWarning()
    {
        var e = await NewAsync();
        await ReceiveAndSubmitAsync(e, 15);
        var irnId = await DraftIrnAsync(e);

        var r = await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 6) }, Clerk);   // 5 open

        Assert.True(r.Success);
        Assert.Contains("Rcv_Warn_OverReceipt", r.Warnings);
    }

    [Fact]
    public async Task OtherWarehouse_AndUnreleasedInspection_AreWarnings()
    {
        var e = await NewAsync(requiresInspection: true);
        var irnId = await DraftIrnAsync(e, wh: Wh2);

        var r = await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk);

        Assert.True(r.Success);
        Assert.Contains("Rcv_Warn_OtherWarehouse", r.Warnings);
        Assert.Contains("Rcv_Warn_InspectionNotReleased", r.Warnings);
    }

    [Fact]
    public async Task ReleasedInspection_RemovesTheWarning()
    {
        var e = await NewAsync(requiresInspection: true);
        Assert.True((await e.Service.ReleaseInspectionAsync(e.Line1, null, new PoReceivingActor(2, "qa", new[] { "QA" }))).Success);
        Assert.Equal("Rcv_Err_AlreadyReleased", (await e.Service.ReleaseInspectionAsync(e.Line1, null, Clerk)).Error);
        var irnId = await DraftIrnAsync(e);

        var r = await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk);

        Assert.DoesNotContain("Rcv_Warn_InspectionNotReleased", r.Warnings);
        Assert.True((await e.Service.GetPoLinesAsync(e.Po)).Single(l => l.PurchaseOrderLineId == e.Line1).InspectionRequired);
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft, "Rcv_Err_PoNotIssued")]
    [InlineData(PurchaseOrderStatuses.Approved, "Rcv_Err_PoNotIssued")]
    public async Task Receive_RequiresAnIssuedPo(string status, string error)
    {
        var e = await NewAsync(status);
        var irnId = await DraftIrnAsync(e);
        Assert.Equal(error, (await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk)).Error);
    }

    [Fact]
    public async Task Receive_Refuses_OtherVendor_FreeTextLine_ForeignLine_AndNonDraft()
    {
        var e = await NewAsync();
        var otherVendorIrn = await DraftIrnAsync(e, vendor: OtherVendor);
        Assert.Equal("Rcv_Err_VendorMismatch", (await e.Service.ReceiveAgainstPoAsync(otherVendorIrn, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk)).Error);

        var irnId = await DraftIrnAsync(e);
        Assert.Equal("Rcv_Err_LineNotCatalogued", (await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.FreeLine, 1) }, Clerk)).Error);
        Assert.Equal("Rcv_Err_LineNotOnPo", (await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(99999, 1) }, Clerk)).Error);
        Assert.Equal("Rcv_Err_NoLines", (await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 0) }, Clerk)).Error);

        var submitted = await ReceiveAndSubmitAsync(e, 1);
        Assert.Equal("Rcv_Err_NotDraft", (await e.Service.ReceiveAgainstPoAsync(submitted, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk)).Error);

        e.Perms.Denied.Add(("IRN", "Create"));
        Assert.Equal("Rcv_Err_Forbidden", (await e.Service.ReceiveAgainstPoAsync(irnId, new[] { new IrnPoLineInput(e.Line1, 1) }, Clerk)).Error);
    }

    // ================================================================== incoming supply

    [Fact]
    public async Task IncomingSupply_IsOpenQuantityAtTheDeliveryWarehouse_PlusInQcAtTheReceivingWarehouse()
    {
        var e = await NewAsync();
        var before = await e.Service.GetIncomingSupplyAsync(null);
        Assert.Equal(20, before.QuantityFor(new StockKey(e.Item1, Wh)));
        Assert.Equal(5, before.QuantityFor(new StockKey(e.Item2, Wh)));

        await ReceiveAndSubmitAsync(e, 12, wh: Wh2);   // in QC at WH-02

        var after = await e.Service.GetIncomingSupplyAsync(new[] { e.Item1 });
        Assert.Equal(8, after.QuantityFor(new StockKey(e.Item1, Wh)));
        Assert.Equal(12, after.QuantityFor(new StockKey(e.Item1, Wh2)));
        Assert.Equal(0, after.QuantityFor(new StockKey(e.Item2, Wh)));   // filtered out
    }

    [Fact]
    public async Task IncomingSupply_AfterApproval_AcceptedIsStock_HoldIsNeither_RejectedReopens()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        await DispositionAsync(e, irnId, accepted: 9, rejected: 1, hold: 2);

        var s = await e.Service.GetIncomingSupplyAsync(null);

        Assert.Equal(9, s.QuantityFor(new StockKey(e.Item1, Wh)));   // 8 open + 1 reopened (replace is the default)
        Assert.Equal(9, (await e.Db.MaterialStocks.SingleAsync(m => m.MaterialItemId == e.Item1)).OnHandQuantity);
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft)]
    [InlineData(PurchaseOrderStatuses.Approved)]
    [InlineData(PurchaseOrderStatuses.Closed)]
    [InlineData(PurchaseOrderStatuses.Cancelled)]
    public async Task IncomingSupply_CountsOnlyIssuedPos(string status)
    {
        var e = await NewAsync(status);
        Assert.Empty((await e.Service.GetIncomingSupplyAsync(null)).Quantities);
    }

    [Fact]
    public async Task ImportedPoWithoutLines_IsLeftOut_D8()
    {
        var e = await NewAsync(PurchaseOrderStatuses.Cancelled);
        e.Db.PurchaseOrders.Add(new PurchaseOrder { PoNumber = "IMP-1", ProjectId = 1, VendorId = Vendor, Status = PurchaseOrderStatuses.Issued, Value = 1000, IsActive = true });
        await e.Db.SaveChangesAsync();

        var s = await e.Service.GetIncomingSupplyAsync(null);
        Assert.Empty(s.Quantities);
        Assert.Empty(s.Issues);
    }

    [Fact]
    public async Task OtherUnit_IsNotCounted_AndBlocksTheResult()
    {
        var e = await NewAsync(line1Unit: "BOX");
        var s = await e.Service.GetIncomingSupplyAsync(null);

        Assert.Equal(0, s.QuantityFor(new StockKey(e.Item1, Wh)));
        var issue = Assert.Single(s.IssuesFor(new StockKey(e.Item1, Wh)));
        Assert.Equal(NetDemandIssueCode.IncomingUnitIncompatible, issue.Code);
        Assert.True(issue.IsBlocking);
    }

    [Fact]
    public async Task OpenStockLineWithoutWarehouse_IsReportedForEveryWarehouseOfTheItem()
    {
        var e = await NewAsync(line1Wh: null);
        var s = await e.Service.GetIncomingSupplyAsync(null);

        Assert.Contains(s.IssuesFor(new StockKey(e.Item1, Wh2)), i => i.Code == NetDemandIssueCode.IncomingWarehouseUnknown);
        Assert.Equal(0, s.QuantityFor(new StockKey(e.Item1, Wh)));
    }

    // ================================================================== D6b rejection decision

    [Fact]
    public async Task CloseShort_RemovesTheRejectedQuantityFromIncoming()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        var lineId = await DispositionAsync(e, irnId, accepted: 9, rejected: 3, hold: 0);
        Assert.Equal(11, (await e.Service.GetIncomingSupplyAsync(null)).QuantityFor(new StockKey(e.Item1, Wh)));

        Assert.True((await e.Service.DecideRejectionAsync(lineId, IrnRejectionActions.CloseShort, Buyer)).Success);

        Assert.Equal(8, (await e.Service.GetIncomingSupplyAsync(null)).QuantityFor(new StockKey(e.Item1, Wh)));
        var saved = await e.Db.IrnReceiptLines.AsNoTracking().SingleAsync(l => l.IrnReceiptLineId == lineId);
        Assert.Equal("buyer", saved.RejectionDecidedByUserName);
    }

    [Fact]
    public async Task RejectionDecision_IsRefused_BeforeApproval_WithoutRejection_OrUnknownAction()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        var lineId = (await e.Db.IrnReceiptLines.AsNoTracking().FirstAsync(l => l.IrnReceiptId == irnId)).IrnReceiptLineId;
        Assert.Equal("Rcv_Err_NotDispositioned", (await e.Service.DecideRejectionAsync(lineId, IrnRejectionActions.CloseShort, Buyer)).Error);

        await DispositionAsync(e, irnId, accepted: 12, rejected: 0, hold: 0);
        Assert.Equal("Rcv_Err_NothingRejected", (await e.Service.DecideRejectionAsync(lineId, IrnRejectionActions.CloseShort, Buyer)).Error);
        Assert.Equal("Rcv_Err_Action", (await e.Service.DecideRejectionAsync(lineId, "Scrap", Buyer)).Error);
    }

    // ================================================================== D7 Hold resolution

    [Fact]
    public async Task Hold_BecomesATask_AndTheWarehouseManagerResolvesIt()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        var lineId = await DispositionAsync(e, irnId, accepted: 9, rejected: 1, hold: 2);

        Assert.True((await e.Service.StartHoldResolutionAsync(irnId, 2)).Success);
        var task = await e.Runtime.GetInstanceAsync(IrnHoldResolutionWorkflowSeeder.BusinessObjectType, irnId);
        Assert.Equal("Running", task?.Status);

        Assert.Equal("Rcv_Err_NotWarehouseManager", (await e.Service.ResolveHoldAsync(lineId, 2, 0, null, Clerk)).Error);
        Assert.Equal("Rcv_Err_HoldSplit", (await e.Service.ResolveHoldAsync(lineId, 1, 0, null, WhManager)).Error);

        var r = await e.Service.ResolveHoldAsync(lineId, 1.5m, 0.5m, "half bad", WhManager);

        Assert.True(r.Success, r.Error);
        Assert.Equal("Completed", task!.Status);
        Assert.Equal(10.5m, (await e.Db.MaterialStocks.SingleAsync(m => m.MaterialItemId == e.Item1)).OnHandQuantity);
        Assert.Equal(2, await e.Db.InventoryTransactions.CountAsync(t => t.ReferenceType == "IrnReceipt" && t.ReferenceId == irnId));
        // incoming: 8 open + 1 rejected + 0.5 rejected from Hold, both reopened (replace by default)
        Assert.Equal(9.5m, (await e.Service.GetIncomingSupplyAsync(null)).QuantityFor(new StockKey(e.Item1, Wh)));
        Assert.Equal("Rcv_Err_NoOpenHold", (await e.Service.ResolveHoldAsync(lineId, 1.5m, 0.5m, null, WhManager)).Error);
    }

    [Fact]
    public async Task NoHold_StartsNoTask()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        await DispositionAsync(e, irnId, accepted: 12, rejected: 0, hold: 0);

        Assert.True((await e.Service.StartHoldResolutionAsync(irnId, 2)).Success);
        Assert.Null(await e.Runtime.GetInstanceAsync(IrnHoldResolutionWorkflowSeeder.BusinessObjectType, irnId));
    }

    // ================================================================== OS&D and PO view

    [Fact]
    public async Task Osd_ShowsShortOverAndRejected_AgainstTheOpenQuantityBeforeThisReceipt()
    {
        var e = await NewAsync();
        var first = await ReceiveAndSubmitAsync(e, 12, qty2: 7);   // line 2 ordered 5: 2 over
        await DispositionAsync(e, first, accepted: 10, rejected: 2, hold: 0);
        var second = await ReceiveAndSubmitAsync(e, 6);             // open before: 8 + 2 reopened = 10 -> 4 short

        var report1 = await e.Service.GetOsdReportAsync(first);
        var l1 = report1!.Lines.Single(l => l.PoLineNo == 1);
        Assert.Equal(8, l1.Short);
        Assert.Equal(2, l1.Rejected);
        Assert.Equal(2, report1.Lines.Single(l => l.PoLineNo == 2).Over);
        Assert.True(report1.HasDiscrepancy);

        var l2 = (await e.Service.GetOsdReportAsync(second))!.Lines.Single();
        Assert.Equal(12, l2.ReceivedBefore);
        Assert.Equal(4, l2.Short);
        Assert.Equal(0, l2.Over);
    }

    [Fact]
    public async Task PoView_ShowsTheBucketsPerLine()
    {
        var e = await NewAsync();
        var irnId = await ReceiveAndSubmitAsync(e, 12);
        await DispositionAsync(e, irnId, accepted: 9, rejected: 1, hold: 2);

        var line = (await e.Service.GetPoLinesAsync(e.Po)).Single(l => l.LineNo == 1);

        Assert.Equal((20m, 12m, 0m, 9m, 2m, 1m, 9m), (line.Ordered, line.Received, line.InQc, line.Accepted, line.OnHold, line.RejectedReplaced, line.Outstanding));
        Assert.False((await e.Service.GetPoLinesAsync(e.Po)).Single(l => l.LineNo == 3).CanReceive);
    }
}
