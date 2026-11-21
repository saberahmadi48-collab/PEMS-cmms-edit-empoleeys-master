using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PEMS.Application.Interfaces;
using PEMS.Application.Replenishment;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-471: pre-procurement intake. Real net-demand and replenishment stack (InMemory) with the procurement pipeline plugged in,
/// so "sent to procurement" is proven not to be recommended twice and to come back when the buyer dismisses it.
/// </summary>
public class PreProcurementServiceTests
{
    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required PreProcurementService Service { get; init; }
        public required ReplenishmentRecommendationService Replenishment { get; init; }
        public required FakeWorkflowRuntime Runtime { get; init; }
        public required FakePermissionService Perms { get; init; }
        public int Item, Wh, Project, Discipline;
    }

    private static readonly PreProcurementActor Buyer = new(30, new[] { "PRC.Buyer" }, "buyer");
    private static readonly ReplenishmentActor Warehouse = new(1, 1, new[] { "WH.Manager" });

    private static async Task<Env> NewAsync()
    {
        var db = new PEMSDbContext(new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase("ppr_" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var uom = new MaterialUOM { Code = "EA", Name = "Each" };
        var wh = new Warehouse { Code = "WH-1", Name = "Main" };
        var project = new Project { ProjectCode = "P-1", ProjectName = "Plant", IsActive = true };
        var discipline = new Discipline { Code = "ME", Name = "Mechanical", IsActive = true };
        db.AddRange(uom, wh, project, discipline);
        await db.SaveChangesAsync();
        var item = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", UomId = uom.MaterialUOMId, IsStockItem = true };
        db.MaterialItems.Add(item);
        await db.SaveChangesAsync();
        db.MaterialStocks.Add(new MaterialStock { MaterialItemId = item.MaterialItemId, WarehouseId = wh.WarehouseId, OnHandQuantity = 3 });
        var sr = new StoreRequisition { RequisitionNumber = "SR-1", Status = "Approved", WarehouseId = wh.WarehouseId, RequestedById = 1 };
        sr.Lines.Add(new StoreRequisitionLine { MaterialItemId = item.MaterialItemId, RequestedQuantity = 8, ApprovedQuantity = 8 });   // shortage 5
        db.StoreRequisitions.Add(sr);
        await db.SaveChangesAsync();

        var perms = new FakePermissionService();
        var runtime = new FakeWorkflowRuntime();
        var service = new PreProcurementService(db, runtime, perms, new MaterialRequisitionService(db));
        var replenishment = new ReplenishmentRecommendationService(db,
            new NetDemandService(new NetDemandCollector(db), new UsableStockService(db), perms), perms, service);
        return new Env
        {
            Db = db, Service = service, Replenishment = replenishment, Runtime = runtime, Perms = perms,
            Item = item.MaterialItemId, Wh = wh.WarehouseId, Project = project.ProjectId, Discipline = discipline.DisciplineId
        };
    }

    private static async Task<ReplenishmentRunResult> RunAsync(Env e)
    {
        e.Db.ChangeTracker.Clear();
        var r = await e.Replenishment.RunAsync(new ReplenishmentRunRequest(), Warehouse);
        Assert.True(r.Success, r.Error);
        e.Db.ChangeTracker.Clear();
        return r;
    }

    private static async Task<int> OpenRecommendationAsync(Env e)
    {
        await RunAsync(e);
        return (await e.Db.ReplenishmentRecommendations.SingleAsync(r => r.Status == ReplenishmentStatuses.Open)).ReplenishmentRecommendationId;
    }

    private static async Task<int> SentDemandAsync(Env e)
    {
        var rec = await OpenRecommendationAsync(e);
        var r = await e.Service.SendRecommendationAsync(rec, Buyer);
        Assert.True(r.Success, r.Error);
        e.Db.ChangeTracker.Clear();
        return r.Id!.Value;
    }

    private static NewRequisitionInput NewMr(Env e, string number = "MR-100") => new(number, e.Project, e.Discipline, null, null);

    // ------------------------------------------------------------ recommendation -> queue

    [Fact]
    public async Task Sending_a_recommendation_raises_one_task_marks_it_Converted_and_keeps_its_history()
    {
        var e = await NewAsync();
        var recId = await OpenRecommendationAsync(e);

        var r = await e.Service.SendRecommendationAsync(recId, Buyer);

        Assert.True(r.Success, r.Error);
        var demand = await e.Db.ProcurementDemands.SingleAsync();
        Assert.Equal((ProcurementDemandSources.Replenishment, recId, e.Item, e.Wh, 5m, ProcurementDemandStatuses.Open),
            (demand.SourceType, demand.SourceId, demand.MaterialItemId, demand.WarehouseId, demand.Quantity, demand.Status));
        var rec = await e.Db.ReplenishmentRecommendations.Include(x => x.History).SingleAsync(x => x.ReplenishmentRecommendationId == recId);
        Assert.Equal(ReplenishmentStatuses.Converted, rec.Status);
        Assert.Contains(rec.History, h => h.Action == ReplenishmentHistoryActions.SentToProcurement && h.ToStatus == ReplenishmentStatuses.Converted);
        var instance = await e.Runtime.GetInstanceAsync(PreProcurementWorkflowSeeder.BusinessObjectType, demand.ProcurementDemandId);
        Assert.Equal("Running", instance!.Status);
        Assert.Single(e.Db.AuditLogs.Where(a => a.EntityName == nameof(ProcurementDemand) && a.Action == "Raise"));
    }

    [Fact]
    public async Task The_same_recommendation_cannot_be_sent_twice()
    {
        var e = await NewAsync();
        var recId = await OpenRecommendationAsync(e);
        Assert.True((await e.Service.SendRecommendationAsync(recId, Buyer)).Success);
        e.Db.ChangeTracker.Clear();

        var again = await e.Service.SendRecommendationAsync(recId, Buyer);

        Assert.False(again.Success);
        Assert.Single(e.Db.ProcurementDemands);
    }

    [Fact]
    public async Task Sending_needs_the_replenishment_edit_permission()
    {
        var e = await NewAsync();
        var recId = await OpenRecommendationAsync(e);
        e.Perms.AllowAll = false;

        Assert.Equal("Ppr_Err_Forbidden", (await e.Service.SendRecommendationAsync(recId, Buyer)).Error);
        Assert.Empty(e.Db.ProcurementDemands);
    }

    // ------------------------------------------------------------ planned supply: never recommended twice

    [Fact]
    public async Task While_procurement_has_the_shortage_the_next_run_does_not_recommend_it_again()
    {
        var e = await NewAsync();
        await SentDemandAsync(e);

        var run = await RunAsync(e);

        Assert.Equal(0, run.Created);
        Assert.DoesNotContain(e.Db.ReplenishmentRecommendations, r => r.Status == ReplenishmentStatuses.Open);
    }

    [Fact]
    public async Task A_dismissed_demand_lets_the_next_run_recommend_the_shortage_again()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);
        Assert.True((await e.Service.DismissAsync(demandId, "stock arrives from the other site", Buyer)).Success);
        Assert.Equal("Cancelled", (await e.Runtime.GetInstanceAsync(PreProcurementWorkflowSeeder.BusinessObjectType, demandId))!.Status);

        var run = await RunAsync(e);

        Assert.Equal(1, run.Created);
        Assert.Single(e.Db.ReplenishmentRecommendations, r => r.Status == ReplenishmentStatuses.Open);
    }

    [Fact]
    public async Task A_converted_demand_counts_until_its_MR_is_cancelled()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);
        var converted = await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer);
        Assert.True(converted.Success, converted.Error);

        Assert.Equal(0, (await RunAsync(e)).Created);   // MR in Draft: still in procurement

        var mr = await e.Db.MaterialRequisitions.SingleAsync(m => m.MRId == converted.Id);
        mr.Status = "Cancelled";
        await e.Db.SaveChangesAsync();

        Assert.Equal(1, (await RunAsync(e)).Created);
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft, 5)]
    [InlineData(PurchaseOrderStatuses.Approved, 5)]
    [InlineData(PurchaseOrderStatuses.Issued, 1)]
    [InlineData(PurchaseOrderStatuses.Closed, 1)]
    public async Task SCRUM473_Quantity_on_an_issued_PO_line_leaves_planned_supply(string poStatus, decimal expectedPlanned)
    {
        // once ordered on an issued PO, the quantity is incoming supply of the PO line; counting it here too would count it twice
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);
        var converted = await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer);
        Assert.True(converted.Success, converted.Error);
        var mrItem = await e.Db.MaterialRequisitionItems.SingleAsync(i => i.MRId == converted.Id);
        var po = new PurchaseOrder { PoNumber = "PO-473", ProjectId = e.Project, VendorId = 1, Status = poStatus, IsActive = true };
        po.Lines.Add(new PurchaseOrderLine { LineNo = 1, MaterialRequisitionItemId = mrItem.MRItemId, MaterialItemId = e.Item, Description = "x", Quantity = 4 });
        e.Db.PurchaseOrders.Add(po);
        await e.Db.SaveChangesAsync();

        var planned = await e.Service.GetPlannedSupplyAsync(null);

        Assert.Equal(expectedPlanned, Assert.Single(planned).Value);
    }

    // ------------------------------------------------------------ buyer decisions on a material demand

    [Fact]
    public async Task Converting_raises_a_new_MR_whose_line_links_back_to_the_demand_and_the_registered_part()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);

        var r = await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer);

        Assert.True(r.Success, r.Error);
        var mr = await e.Db.MaterialRequisitions.Include(m => m.Items).SingleAsync(m => m.MRId == r.Id);
        Assert.Equal(("MR-100", "Draft", e.Project, e.Discipline), (mr.MRNumber, mr.Status, mr.ProjectId, mr.DisciplineId));
        var line = Assert.Single(mr.Items);
        Assert.Equal((demandId, (int?)e.Item, 5d, "M-1", "Each"), (line.ProcurementDemandId!.Value, line.MaterialItemId, line.Quantity, line.PartNumber, line.Unit));
        var demand = await e.Db.ProcurementDemands.SingleAsync();
        Assert.Equal((ProcurementDemandStatuses.Converted, (int?)mr.MRId, "buyer"), (demand.Status, demand.MaterialRequisitionId, demand.DecidedByUserName));
        Assert.Equal("Completed", (await e.Runtime.GetInstanceAsync(PreProcurementWorkflowSeeder.BusinessObjectType, demandId))!.Status);
    }

    [Fact]
    public async Task Converting_into_an_existing_draft_MR_adds_a_line_and_a_non_draft_MR_is_refused()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);
        var draft = new MaterialRequisition { MRNumber = "MR-7", ProjectId = e.Project, DisciplineId = e.Discipline, Description = "Spares", Status = "Draft" };
        var submitted = new MaterialRequisition { MRNumber = "MR-8", ProjectId = e.Project, DisciplineId = e.Discipline, Description = "Other", Status = "Submitted" };
        e.Db.MaterialRequisitions.AddRange(draft, submitted);
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        Assert.Equal("Ppr_Err_MrNotDraft", (await e.Service.ConvertToRequisitionAsync(demandId, submitted.MRId, null, Buyer)).Error);
        var r = await e.Service.ConvertToRequisitionAsync(demandId, draft.MRId, null, Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Single(e.Db.MaterialRequisitionItems.Where(i => i.MRId == draft.MRId && i.ProcurementDemandId == demandId));
    }

    [Fact]
    public async Task A_decided_demand_cannot_be_decided_again()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);
        Assert.True((await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer)).Success);
        e.Db.ChangeTracker.Clear();

        Assert.Equal("Ppr_Err_AlreadyDecided", (await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e, "MR-101"), Buyer)).Error);
        Assert.Equal("Ppr_Err_AlreadyDecided", (await e.Service.DismissAsync(demandId, "late", Buyer)).Error);
        Assert.Single(e.Db.MaterialRequisitions);
    }

    [Fact]
    public async Task A_refused_new_MR_leaves_the_demand_open_and_writes_nothing()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);

        var r = await e.Service.ConvertToRequisitionAsync(demandId, null, new NewRequisitionInput("MR-X", 999, e.Discipline, null, null), Buyer);

        Assert.False(r.Success);
        Assert.Empty(e.Db.MaterialRequisitions);
        Assert.Equal(ProcurementDemandStatuses.Open, (await e.Db.ProcurementDemands.SingleAsync()).Status);
    }

    [Fact]
    public async Task Dismissing_needs_a_reason_and_deciding_needs_the_pre_procurement_permission()
    {
        var e = await NewAsync();
        var demandId = await SentDemandAsync(e);

        Assert.Equal("Ppr_Err_ReasonRequired", (await e.Service.DismissAsync(demandId, " ", Buyer)).Error);
        e.Perms.Denied.Add(("PreProcurement", "Edit"));
        Assert.Equal("Ppr_Err_Forbidden", (await e.Service.DismissAsync(demandId, "no", Buyer)).Error);
        Assert.Equal("Ppr_Err_Forbidden", (await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer)).Error);
    }

    // ------------------------------------------------------------ work-order direct purchase

    [Fact]
    public async Task Direct_purchase_lines_of_a_released_work_order_are_raised_once()
    {
        var e = await NewAsync();
        var wo = new WorkOrder { WorkOrderNumber = "WO-9", EquipmentId = 1, Status = "Assigned" };
        e.Db.WorkOrders.Add(wo);
        await e.Db.SaveChangesAsync();
        e.Db.WorkOrderMaterials.AddRange(
            new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item, RequiredQuantity = 2, SupplyCode = WorkOrderMaterialSupplyCodes.DirectPurchase },
            new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item + 1000, RequiredQuantity = 4 });   // a stock line: not raised here
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        var first = await e.Service.RaiseDirectPurchaseLinesAsync(wo.WorkOrderId, Buyer);
        var second = await e.Service.RaiseDirectPurchaseLinesAsync(wo.WorkOrderId, Buyer);

        Assert.Equal(1, first.Count);
        Assert.Equal(0, second.Count);
        var d = await e.Db.ProcurementDemands.SingleAsync();
        Assert.Equal((ProcurementDemandSources.WorkOrderDirectPurchase, (int?)wo.WorkOrderId, 2m), (d.SourceType, d.WorkOrderId, d.Quantity));
        var row = Assert.Single(await e.Service.GetQueueAsync(ProcurementDemandStatuses.Open, null));
        Assert.Equal("WO-9", row.SourceNumber);
    }

    [Fact]
    public async Task Direct_purchase_lines_of_an_unreleased_work_order_are_not_raised()
    {
        var e = await NewAsync();
        var wo = new WorkOrder { WorkOrderNumber = "WO-10", EquipmentId = 1, Status = "Open" };
        e.Db.WorkOrders.Add(wo);
        await e.Db.SaveChangesAsync();

        Assert.Equal("Whi_Err_NotReleased", (await e.Service.RaiseDirectPurchaseLinesAsync(wo.WorkOrderId, Buyer)).Error);
    }

    // ------------------------------------------------------------ engineering MR

    private static async Task<int> IssuedMrDemandAsync(Env e)
    {
        var mr = new MaterialRequisition { MRNumber = "MR-ENG", ProjectId = e.Project, DisciplineId = e.Discipline, Description = "Pump spares", Status = "Issued to Procurement" };
        e.Db.MaterialRequisitions.Add(mr);
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();
        var r = await e.Service.RaiseMaterialRequisitionAsync(mr.MRId, Buyer);
        Assert.True(r.Success, r.Error);
        e.Db.ChangeTracker.Clear();
        return r.Id!.Value;
    }

    [Fact]
    public async Task An_issued_engineering_MR_enters_the_queue_and_can_be_accepted()
    {
        var e = await NewAsync();
        var demandId = await IssuedMrDemandAsync(e);

        Assert.Equal("Ppr_Err_NotMaterialDemand", (await e.Service.ConvertToRequisitionAsync(demandId, null, NewMr(e), Buyer)).Error);
        var r = await e.Service.AcceptAsync(demandId, Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Equal(ProcurementDemandStatuses.Accepted, (await e.Db.ProcurementDemands.SingleAsync()).Status);
        Assert.Equal("Issued to Procurement", (await e.Db.MaterialRequisitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Returning_an_engineering_MR_puts_it_back_to_Approved_with_the_reason()
    {
        var e = await NewAsync();
        var demandId = await IssuedMrDemandAsync(e);

        var r = await e.Service.DismissAsync(demandId, "datasheet missing", Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Equal("Approved", (await e.Db.MaterialRequisitions.SingleAsync()).Status);
        var d = await e.Db.ProcurementDemands.SingleAsync();
        Assert.Equal((ProcurementDemandStatuses.Dismissed, "datasheet missing"), (d.Status, d.DecisionReason));
    }

    [Fact]
    public async Task Only_an_MR_issued_to_procurement_is_raised_and_only_once()
    {
        var e = await NewAsync();
        var draft = new MaterialRequisition { MRNumber = "MR-D", ProjectId = e.Project, DisciplineId = e.Discipline, Description = "x", Status = "Draft" };
        e.Db.MaterialRequisitions.Add(draft);
        await e.Db.SaveChangesAsync();

        Assert.Equal("Ppr_Err_MrNotIssued", (await e.Service.RaiseMaterialRequisitionAsync(draft.MRId, Buyer)).Error);
        var demandId = await IssuedMrDemandAsync(e);
        var mrId = (await e.Db.ProcurementDemands.SingleAsync(d => d.ProcurementDemandId == demandId)).SourceId;
        var again = await e.Service.RaiseMaterialRequisitionAsync(mrId, Buyer);
        Assert.Equal(0, again.Count);
        Assert.Single(e.Db.ProcurementDemands);
        Assert.Equal("Ppr_Err_NotMrDemand", (await e.Service.AcceptAsync(await SentDemandAsync(e), Buyer)).Error);
    }
}
