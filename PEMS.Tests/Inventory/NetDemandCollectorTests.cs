using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Inventory;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Inventory;

/// <summary>
/// Phase 2 / P3 - جمع‌آوری تقاضا از SR line و WorkOrderMaterial. وضعیت‌ها از StoreRequisitionStatuses و WorkOrderLifecycleService.LegalTransitions
/// گرفته شده‌اند (هیچ وضعیت ساختگی نیست). منابع MR/PM/Part Catalogue عمداً جمع نمی‌شوند.
/// </summary>
public class NetDemandCollectorTests
{
    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required NetDemandCollector Collector { get; init; }
        public int Each, Box, Item, Item2, Wh, Wh2;
        public int NextSr = 1, NextWo = 1;
    }

    private static async Task<Env> NewAsync()
    {
        var db = PemsDbContextFactory.CreateInMemory("collector_" + Guid.NewGuid().ToString("N"));
        var each = new MaterialUOM { Code = "EA", Name = "Each" };
        var box = new MaterialUOM { Code = "BX", Name = "Box" };
        var wh = new Warehouse { Code = "WH-1", Name = "Main" };
        var wh2 = new Warehouse { Code = "WH-2", Name = "Second" };
        db.AddRange(each, box, wh, wh2);
        await db.SaveChangesAsync();
        var i1 = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", UomId = each.MaterialUOMId };
        var i2 = new MaterialItem { MaterialCode = "M-2", Description = "Seal", UomId = each.MaterialUOMId };
        db.AddRange(i1, i2);
        await db.SaveChangesAsync();
        return new Env
        {
            Db = db, Collector = new NetDemandCollector(db), Each = each.MaterialUOMId, Box = box.MaterialUOMId,
            Item = i1.MaterialItemId, Item2 = i2.MaterialItemId, Wh = wh.WarehouseId, Wh2 = wh2.WarehouseId
        };
    }

    private static async Task<StoreRequisition> SrAsync(Env e, string status, int? workOrderId = null, int? warehouse = null, bool active = true,
        params (int Item, decimal Requested, decimal? Approved, decimal Issued, int? Uom, bool Active)[] lines)
    {
        var sr = new StoreRequisition
        {
            RequisitionNumber = "SR-" + e.NextSr++, Status = status, WarehouseId = warehouse ?? e.Wh, RequestedById = 1, WorkOrderId = workOrderId, IsActive = active
        };
        foreach (var l in lines)
            sr.Lines.Add(new StoreRequisitionLine { MaterialItemId = l.Item, RequestedQuantity = l.Requested, ApprovedQuantity = l.Approved, IssuedQuantity = l.Issued, UomId = l.Uom, IsActive = l.Active });
        e.Db.StoreRequisitions.Add(sr);
        await e.Db.SaveChangesAsync();
        return sr;
    }

    private static (int Item, decimal Requested, decimal? Approved, decimal Issued, int? Uom, bool Active) L(int item, decimal requested, decimal? approved = null, decimal issued = 0, int? uom = null, bool active = true)
        => (item, requested, approved, issued, uom, active);

    private static async Task<WorkOrder> WoAsync(Env e, string status, bool active = true)
    {
        var wo = new WorkOrder { EquipmentId = 1, WorkOrderNumber = "WO-" + e.NextWo++, WorkOrderType = "Corrective", Priority = "Low", Status = status, IsActive = active };
        e.Db.WorkOrders.Add(wo);
        await e.Db.SaveChangesAsync();
        return wo;
    }

    private static async Task WoMaterialAsync(Env e, WorkOrder wo, int item, decimal required, int? uom = null)
    {
        e.Db.WorkOrderMaterials.Add(new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = item, RequiredQuantity = required, UomId = uom });
        await e.Db.SaveChangesAsync();
    }

    private static async Task ReserveAsync(Env e, StoreRequisition sr, StoreRequisitionLine? line, int item, decimal qty, decimal issued = 0, string status = "Active")
    {
        e.Db.MaterialReservations.Add(new MaterialReservation
        {
            MaterialItemId = item, WarehouseId = sr.WarehouseId, Quantity = qty, IssuedQuantity = issued, Status = status,
            StoreRequisitionId = sr.StoreRequisitionId, StoreRequisitionLineId = line?.StoreRequisitionLineId, ReservedById = 1
        });
        await e.Db.SaveChangesAsync();
    }

    private static async Task<DemandCollection> CollectAsync(Env e, NetDemandScope? scope = null)
    {
        e.Db.ChangeTracker.Clear();
        return await e.Collector.CollectAsync(scope ?? new NetDemandScope());
    }

    private static DemandLine Single(DemandCollection c, DemandSourceType source) => Assert.Single(c.Lines, l => l.Origin.Source == source);
    private static bool Excluded(DemandCollection c, NetDemandIssueCode code) => c.Excluded.Any(x => x.Reason == code);

    // ================================================================ Store Requisition
    [Fact]
    public async Task OnlyApprovedReservedAndPartiallyIssuedRequisitions_AreCommittedDemand()
    {
        var e = await NewAsync();
        foreach (var status in new[] { "Draft", "Rejected", "Cancelled", "Issued", "Closed" }) await SrAsync(e, status, null, null, true, L(e.Item, 5, 5));
        await SrAsync(e, "Submitted", null, null, true, L(e.Item, 7, 7));
        var approved = await SrAsync(e, "Approved", null, null, true, L(e.Item, 3, 3));
        var reserved = await SrAsync(e, "Reserved", null, null, true, L(e.Item, 4, 4));
        var partial = await SrAsync(e, "PartiallyIssued", null, null, true, L(e.Item, 6, 6, issued: 2));

        var c = await CollectAsync(e);

        Assert.Equal(new[] { approved.StoreRequisitionId, reserved.StoreRequisitionId, partial.StoreRequisitionId }.OrderBy(x => x), c.Lines.Select(l => l.Origin.ParentId).OrderBy(x => x));
        Assert.Equal(new[] { 3m, 4m, 4m }, c.Lines.OrderBy(l => l.Origin.ParentId).Select(l => l.GrossRemainingQuantity));   // Partially: 6 - 2
        var potential = Assert.Single(c.Excluded);
        Assert.Equal(NetDemandIssueCode.PotentialDemandNotCommitted, potential.Reason);   // Submitted: بالقوه، شمرده نمی‌شود
        Assert.Equal(7m, potential.Quantity);
    }

    [Fact]
    public async Task Quantity_UsesApprovedOtherwiseRequested_MinusIssued_AndSkipsFullyIssued()
    {
        var e = await NewAsync();
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 10, null), L(e.Item2, 10, 8, issued: 8));   // خط دوم کاملاً تحویل
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 10, 4));
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 10, 0));                                      // تأیید صفر

        var c = await CollectAsync(e);

        Assert.Equal(new[] { 4m, 10m }, c.Lines.Select(l => l.GrossRemainingQuantity).OrderBy(x => x));
        Assert.Empty(c.Excluded);
    }

    [Fact]
    public async Task NegativeQuantities_AreInvalid_NotSilentlyUsed()
    {
        var e = await NewAsync();
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 5, -2));

        var c = await CollectAsync(e);

        Assert.Empty(c.Lines);
        Assert.True(Excluded(c, NetDemandIssueCode.InvalidQuantity));
    }

    [Fact]
    public async Task InactiveRequisitionsAndLines_AreIgnored()
    {
        var e = await NewAsync();
        await SrAsync(e, "Approved", null, null, false, L(e.Item, 5, 5));
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 5, 5, active: false));

        Assert.Empty((await CollectAsync(e)).Lines);
    }

    [Fact]
    public async Task LineReservations_AreCoverage_ReleasedAndForeignLineReservationsAreNot()
    {
        var e = await NewAsync();
        var sr = await SrAsync(e, "PartiallyIssued", null, null, true, L(e.Item, 10, 10, issued: 4), L(e.Item, 3, 3));   // دو خط برای یک کالا
        var first = sr.Lines.OrderBy(l => l.StoreRequisitionLineId).First();
        var second = sr.Lines.OrderBy(l => l.StoreRequisitionLineId).Last();
        await ReserveAsync(e, sr, first, e.Item, 10, issued: 4);                       // باقی‌مانده‌ی رزرو 6
        await ReserveAsync(e, sr, first, e.Item, 5, status: "Released");               // رها شده: نادیده
        await ReserveAsync(e, sr, first, e.Item, 9, issued: 9, status: "FullyIssued"); // کاملاً تحویل: نادیده

        var c = await CollectAsync(e);

        var l1 = c.Lines.Single(l => l.Origin.SourceLineId == first.StoreRequisitionLineId);
        var l2 = c.Lines.Single(l => l.Origin.SourceLineId == second.StoreRequisitionLineId);
        Assert.Equal(6m, l1.GrossRemainingQuantity);
        Assert.Equal(6m, l1.CoveredByReservationQuantity);
        Assert.Equal(3m, l2.GrossRemainingQuantity);
        Assert.Equal(0m, l2.CoveredByReservationQuantity);     // رزرو خط اول به خط دوم نشت نمی‌کند (Reservation_IsPerLine_EvenForDuplicateMaterialLines)
    }

    [Fact]
    public async Task ReservationLargerThanRemainingDemand_IsCappedAndReportedAsNonBlocking()
    {
        var e = await NewAsync();
        var sr = await SrAsync(e, "Reserved", null, null, true, L(e.Item, 5, 5));
        await ReserveAsync(e, sr, sr.Lines.Single(), e.Item, 8);

        var c = await CollectAsync(e);

        var line = Single(c, DemandSourceType.StoreRequisitionLine);
        Assert.Equal(5m, line.CoveredByReservationQuantity);
        var note = Assert.Single(c.Excluded);
        Assert.Equal(NetDemandIssueCode.ReservationExceedsRemainingDemand, note.Reason);
        Assert.False(NetDemandIssues.IsBlocking(note.Reason));
    }

    [Fact]
    public async Task LegacyReservationWithoutLine_IsAttributedOnlyWhenExactlyOneLineMatches_OtherwiseAmbiguous()
    {
        var e = await NewAsync();
        var one = await SrAsync(e, "Reserved", null, null, true, L(e.Item, 5, 5));
        await ReserveAsync(e, one, null, e.Item, 5);
        var two = await SrAsync(e, "Reserved", null, null, true, L(e.Item, 5, 5), L(e.Item, 2, 2));
        await ReserveAsync(e, two, null, e.Item, 7);

        var c = await CollectAsync(e);

        var attributed = Assert.Single(c.Lines);                                    // فقط SR اول
        Assert.Equal(one.StoreRequisitionId, attributed.Origin.ParentId);
        Assert.Equal(5m, attributed.CoveredByReservationQuantity);
        Assert.Equal(2, c.Excluded.Count(x => x.Reason == NetDemandIssueCode.AmbiguousReservationAttribution));   // هر دو خط SR دوم
    }

    [Fact]
    public async Task Units_NullLineUnitIsBase_DifferentUnitIsIncompatible_MissingBaseIsUnknown()
    {
        var e = await NewAsync();
        var noBase = new MaterialItem { MaterialCode = "M-NB", Description = "x", UomId = null };
        e.Db.MaterialItems.Add(noBase);
        await e.Db.SaveChangesAsync();
        await SrAsync(e, "Approved", null, null, true, L(e.Item, 1, 1, uom: null), L(e.Item2, 1, 1, uom: e.Each), L(e.Item, 2, 2, uom: e.Box), L(noBase.MaterialItemId, 3, 3));

        var c = await CollectAsync(e);

        var byQty = c.Lines.OrderBy(l => l.GrossRemainingQuantity).ToList();
        Assert.Equal(2, byQty.Count(l => l.GrossRemainingQuantity == 1m && l.UnitStatus == DemandUnitStatus.Compatible));   // UomId خالی و UomId = پایه
        Assert.Equal(DemandUnitStatus.Incompatible, byQty.Single(l => l.GrossRemainingQuantity == 2m).UnitStatus);
        Assert.Equal(DemandUnitStatus.Unknown, byQty.Single(l => l.GrossRemainingQuantity == 3m).UnitStatus);
    }

    [Fact]
    public async Task MaterialProblems_AreExcludedWithAReason_NotSilentlyDropped()
    {
        var e = await NewAsync();
        var inactive = new MaterialItem { MaterialCode = "M-OFF", Description = "x", UomId = e.Each, IsActive = false };
        var nonStock = new MaterialItem { MaterialCode = "M-NS", Description = "x", UomId = e.Each, IsStockItem = false };
        e.Db.AddRange(inactive, nonStock);
        await e.Db.SaveChangesAsync();
        await SrAsync(e, "Approved", null, null, true, L(inactive.MaterialItemId, 2, 2), L(nonStock.MaterialItemId, 2, 2), L(999_999, 2, 2));

        var c = await CollectAsync(e);

        Assert.Empty(c.Lines);
        Assert.True(Excluded(c, NetDemandIssueCode.InactiveMaterial));
        Assert.True(Excluded(c, NetDemandIssueCode.NotStockItem));
        Assert.True(Excluded(c, NetDemandIssueCode.MaterialNotFound));
    }

    // ================================================================ Work Order
    [Fact]
    public async Task WorkOrderMaterials_AreCommittedOnlyForAssignedInProgressAndOnHold_AndHaveNoWarehouse()
    {
        var e = await NewAsync();
        foreach (var status in new[] { "Completed", "Closed", "Cancelled", "Rejected" }) await WoMaterialAsync(e, await WoAsync(e, status), e.Item, 9);
        var open = await WoAsync(e, "Open");
        await WoMaterialAsync(e, open, e.Item, 7);
        var weird = await WoAsync(e, "Weird");
        await WoMaterialAsync(e, weird, e.Item, 6);
        foreach (var status in new[] { "Assigned", "InProgress", "OnHold" }) await WoMaterialAsync(e, await WoAsync(e, status), e.Item, 2);
        await WoMaterialAsync(e, await WoAsync(e, "Assigned", active: false), e.Item, 50);

        var c = await CollectAsync(e);

        Assert.Equal(3, c.Lines.Count);
        Assert.All(c.Lines, l => { Assert.Null(l.WarehouseId); Assert.Equal(2m, l.GrossRemainingQuantity); Assert.Equal(0m, l.CoveredByReservationQuantity); });
        Assert.Contains(c.Excluded, x => x.Reason == NetDemandIssueCode.PotentialDemandNotCommitted && x.Quantity == 7m);
        Assert.Contains(c.Excluded, x => x.Reason == NetDemandIssueCode.UnknownLifecycleStatus && x.Quantity == 6m);
        Assert.Equal(2, c.Excluded.Count);
    }

    [Fact]
    public async Task WorkOrderRemaining_IsRequiredMinusNetSpareUsage_AndIgnoresInactiveUsage()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, wo, e.Item, 10);
        var full = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, full, e.Item, 3);
        e.Db.MaintenanceSpareUsages.AddRange(
            new MaintenanceSpareUsage { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item, Quantity = 5, ReturnedQuantity = 2 },
            new MaintenanceSpareUsage { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item, Quantity = 100, IsActive = false },
            new MaintenanceSpareUsage { WorkOrderId = full.WorkOrderId, MaterialItemId = e.Item, Quantity = 3 });
        await e.Db.SaveChangesAsync();

        var c = await CollectAsync(e);

        var line = Assert.Single(c.Lines);
        Assert.Equal(wo.WorkOrderId, line.Origin.ParentId);
        Assert.Equal(7m, line.GrossRemainingQuantity);          // 10 - (5 - 2)؛ دیگری کاملاً مصرف شده
    }

    [Fact]
    public async Task OverReturnedSpareUsage_IsInvalid_NotSilentlyUsed()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, wo, e.Item, 10);
        e.Db.MaintenanceSpareUsages.Add(new MaintenanceSpareUsage { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item, Quantity = 2, ReturnedQuantity = 5 });
        await e.Db.SaveChangesAsync();

        var c = await CollectAsync(e);

        Assert.Empty(c.Lines);
        Assert.True(Excluded(c, NetDemandIssueCode.InvalidQuantity));
    }

    // ================================================================ WO ↔ SR overlap
    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Submitted", true)]
    [InlineData("Approved", true)]
    [InlineData("Reserved", true)]
    [InlineData("PartiallyIssued", true)]
    [InlineData("Issued", true)]
    [InlineData("Closed", true)]
    [InlineData("Rejected", false)]
    [InlineData("Cancelled", false)]
    public async Task WorkOrderRequirement_IsAmbiguousWhenAStoreRequisitionExistsForTheSameWorkOrderAndMaterial(string srStatus, bool ambiguous)
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, wo, e.Item, 8);
        await SrAsync(e, srStatus, wo.WorkOrderId, null, true, L(e.Item, 5, 5));

        var c = await CollectAsync(e);

        Assert.Equal(ambiguous, Excluded(c, NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition));
        Assert.Equal(!ambiguous, c.Lines.Any(l => l.Origin.Source == DemandSourceType.WorkOrderMaterial));
    }

    [Fact]
    public async Task Overlap_RequiresTheSameMaterialAndTheSameWorkOrder_AndTheStoreRequisitionDemandStaysCounted()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        var other = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, wo, e.Item, 8);
        await SrAsync(e, "Approved", wo.WorkOrderId, null, true, L(e.Item2, 5, 5));      // کالای دیگر ⇒ هم‌پوشانی نیست
        await SrAsync(e, "Approved", other.WorkOrderId, null, true, L(e.Item, 5, 5));    // WO دیگر ⇒ هم‌پوشانی با WO اول نیست

        var c = await CollectAsync(e);

        var woLine = Assert.Single(c.Lines, l => l.Origin.Source == DemandSourceType.WorkOrderMaterial);
        Assert.Equal(8m, woLine.GrossRemainingQuantity);
        Assert.Equal(2, c.Lines.Count(l => l.Origin.Source == DemandSourceType.StoreRequisitionLine));
        Assert.False(Excluded(c, NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition));
    }

    [Fact]
    public async Task OverlapOnlyAppliesToTheOverlappedPair_OtherMaterialsOfTheSameWorkOrderStillCount()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        await WoMaterialAsync(e, wo, e.Item, 8);
        await WoMaterialAsync(e, wo, e.Item2, 3);
        await SrAsync(e, "Approved", wo.WorkOrderId, null, true, L(e.Item, 5, 5));

        var c = await CollectAsync(e);

        var woLine = Assert.Single(c.Lines, l => l.Origin.Source == DemandSourceType.WorkOrderMaterial);
        Assert.Equal(e.Item2, woLine.MaterialItemId);
        Assert.Single(c.Excluded, x => x.Reason == NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition && x.MaterialItemId == e.Item);
    }

    // ================================================================ SCRUM-472: warehouse intake link and supply code
    private static async Task<int> WoMaterialIdAsync(Env e, WorkOrder wo, int item, decimal required, string supply = WorkOrderMaterialSupplyCodes.Stock)
    {
        var m = new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = item, RequiredQuantity = required, SupplyCode = supply };
        e.Db.WorkOrderMaterials.Add(m);
        await e.Db.SaveChangesAsync();
        return m.WorkOrderMaterialId;
    }

    private static async Task LinkedSrAsync(Env e, string status, WorkOrder wo, int woLineId, int item, decimal qty, int? warehouse = null)
    {
        var sr = new StoreRequisition { RequisitionNumber = "SR-" + e.NextSr++, Status = status, WarehouseId = warehouse ?? e.Wh, RequestedById = 1, WorkOrderId = wo.WorkOrderId };
        sr.Lines.Add(new StoreRequisitionLine { MaterialItemId = item, RequestedQuantity = qty, WorkOrderMaterialId = woLineId });
        e.Db.StoreRequisitions.Add(sr);
        await e.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_linked_requisition_line_is_not_an_ambiguous_overlap_and_carries_the_warehouse()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        var woLine = await WoMaterialIdAsync(e, wo, e.Item, 8);
        await LinkedSrAsync(e, "Approved", wo, woLine, e.Item, 8, e.Wh2);

        var c = await CollectAsync(e);

        Assert.False(Excluded(c, NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition));
        Assert.DoesNotContain(c.Lines, l => l.Origin.Source == DemandSourceType.WorkOrderMaterial);   // fully sourced: no warehouse-less demand left
        var sr = Single(c, DemandSourceType.StoreRequisitionLine);
        Assert.Equal(e.Wh2, sr.WarehouseId);
        Assert.Equal(8m, sr.GrossRemainingQuantity);
    }

    [Fact]
    public async Task A_partly_sourced_work_order_line_keeps_only_the_unsourced_quantity()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "Assigned");
        var woLine = await WoMaterialIdAsync(e, wo, e.Item, 8);
        await LinkedSrAsync(e, "Draft", wo, woLine, e.Item, 5);    // a Draft requisition already counts as sourced (not dead)

        var c = await CollectAsync(e);

        var woDemand = Single(c, DemandSourceType.WorkOrderMaterial);
        Assert.Equal(3m, woDemand.GrossRemainingQuantity);
        Assert.Null(woDemand.WarehouseId);
    }

    [Fact]
    public async Task A_cancelled_linked_requisition_no_longer_sources_the_line()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        var woLine = await WoMaterialIdAsync(e, wo, e.Item, 8);
        await LinkedSrAsync(e, "Cancelled", wo, woLine, e.Item, 8);

        var c = await CollectAsync(e);

        Assert.Equal(8m, Single(c, DemandSourceType.WorkOrderMaterial).GrossRemainingQuantity);
    }

    [Fact]
    public async Task An_unlinked_requisition_for_the_same_work_order_and_material_is_still_ambiguous()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        var woLine = await WoMaterialIdAsync(e, wo, e.Item, 8);
        await LinkedSrAsync(e, "Approved", wo, woLine, e.Item, 3);
        await SrAsync(e, "Approved", wo.WorkOrderId, null, true, L(e.Item, 5, 5));   // raised by hand, no link

        var c = await CollectAsync(e);

        Assert.True(Excluded(c, NetDemandIssueCode.AmbiguousOverlapWithStoreRequisition));
    }

    [Fact]
    public async Task A_direct_purchase_line_is_never_stock_demand_and_does_not_block_the_result()
    {
        var e = await NewAsync();
        var wo = await WoAsync(e, "InProgress");
        await WoMaterialIdAsync(e, wo, e.Item, 4, WorkOrderMaterialSupplyCodes.DirectPurchase);

        var c = await CollectAsync(e);

        Assert.DoesNotContain(c.Lines, l => l.Origin.Source == DemandSourceType.WorkOrderMaterial);
        var x = Assert.Single(c.Excluded, x => x.Reason == NetDemandIssueCode.DirectPurchaseNotStockDemand);
        Assert.Equal(4m, x.Quantity);
        Assert.False(NetDemandIssues.IsBlocking(NetDemandIssueCode.DirectPurchaseNotStockDemand));
    }

    // ================================================================ Scope / exclusions / read-only
    [Fact]
    public async Task ScopeFilters_RestrictMaterialsAndWarehouses_ButNeverCreateDemand()
    {
        var e = await NewAsync();
        await SrAsync(e, "Approved", null, e.Wh, true, L(e.Item, 3, 3), L(e.Item2, 4, 4));
        await SrAsync(e, "Approved", null, e.Wh2, true, L(e.Item, 5, 5));

        Assert.Equal(3, (await CollectAsync(e)).Lines.Count);
        Assert.Equal(2, (await CollectAsync(e, new NetDemandScope(MaterialItemIds: new[] { e.Item }))).Lines.Count);
        Assert.Equal(2, (await CollectAsync(e, new NetDemandScope(WarehouseIds: new[] { e.Wh }))).Lines.Count);
        Assert.Single((await CollectAsync(e, new NetDemandScope(new[] { e.Item }, new[] { e.Wh2 }))).Lines);
        Assert.Empty((await CollectAsync(e, new NetDemandScope(MaterialItemIds: new[] { 999_999 }))).Lines);
    }

    [Fact]
    public async Task MaterialRequisitionsPartCatalogueRequestsAndPmPlans_AreNotDemandSources()
    {
        var e = await NewAsync();
        var mr = new MaterialRequisition { MRNumber = "MR-1", ProjectId = 1, DisciplineId = 1, Description = "d", Status = "Approved" };
        mr.Items.Add(new MaterialRequisitionItem { ItemDescription = "Bearing", PartNumber = "M-1", Quantity = 100, Unit = "EA" });
        e.Db.MaterialRequisitions.Add(mr);
        e.Db.PartCatalogueRequests.Add(new PartCatalogueRequest { RequesterEmployeeId = 1, Status = "Draft", FirstPurchaseQuantity = 50 });
        e.Db.JobPlanRequiredMaterials.Add(new JobPlanRequiredMaterial { JobPlanId = 1, MaterialItemId = e.Item, RequiredQuantity = 30 });
        await e.Db.SaveChangesAsync();

        var c = await CollectAsync(e);

        Assert.Empty(c.Lines);
        Assert.Empty(c.Excluded);
    }

    [Fact]
    public async Task Collection_IsReadOnlyAndDeterministic()
    {
        var e = await NewAsync();
        var sr = await SrAsync(e, "Reserved", null, null, true, L(e.Item, 5, 5));
        await ReserveAsync(e, sr, sr.Lines.Single(), e.Item, 5);
        await WoMaterialAsync(e, await WoAsync(e, "Assigned"), e.Item2, 4);
        string Snap() => string.Join("|", e.Db.StoreRequisitionLines.AsNoTracking().Select(l => $"{l.StoreRequisitionLineId}:{l.IssuedQuantity}").ToList())
            + "#" + e.Db.MaterialReservations.AsNoTracking().Sum(r => r.IssuedQuantity) + "#" + e.Db.InventoryTransactions.Count() + "#" + e.Db.MaterialStocks.Count();
        var before = Snap();

        var first = await CollectAsync(e);
        var second = await CollectAsync(e);

        Assert.False(e.Db.ChangeTracker.HasChanges());
        Assert.Empty(e.Db.ChangeTracker.Entries());
        Assert.Equal(before, Snap());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }
}
