using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-470: PO lines from the pre-procurement path and approval by the PO authority table: lines trace back to MR items,
/// the line rules (stock lines catalogued; free text needs a commodity group and a reason), routing by currency and amount,
/// "responsible not set", segregation of duties, reject back to Draft, issue only after approval.
/// </summary>
public class PurchaseOrderApprovalServiceTests
{
    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required PurchaseOrderApprovalService Service { get; init; }
        public required PurchaseOrderService Orders { get; init; }
        public required FakeWorkflowRuntime Runtime { get; init; }
        public required FakePermissionService Perms { get; init; }
        public int Project, Vendor, Mr, Group, CataloguedItem, FreeItem, Warehouse;
    }

    private static readonly PoActor Buyer = new(30, new[] { "PRC.Buyer" }, "buyer");
    private static readonly PoActor Manager = new(40, new[] { "PRC.Manager" }, "manager");
    private static readonly PoActor Director = new(50, new[] { "DIR" }, "director");

    private static async Task<Env> NewAsync(bool catalogueOnly = false)
    {
        var db = new PEMSDbContext(new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase("po_" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var project = new Project { ProjectCode = "P-1", ProjectName = "Plant", IsActive = true };
        var discipline = new Discipline { Code = "ME", Name = "Mechanical", IsActive = true };
        var vendor = new Vendor { Name = "Acme", IsActive = true };
        var group = new PartCatalogueCommodityGroup { Code = "SRV", Description = "Services", IsActive = true };
        var material = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", IsStockItem = true };
        var warehouse = new Warehouse { Code = "WH-1", Name = "Main", IsActive = true };
        db.AddRange(project, discipline, vendor, group, material, warehouse);
        db.Roles.AddRange(new ApplicationRole("PRC.Manager") { NormalizedName = "PRC.MANAGER" }, new ApplicationRole("DIR") { NormalizedName = "DIR" });
        await db.SaveChangesAsync();
        var mr = new MaterialRequisition { MRNumber = "MR-1", ProjectId = project.ProjectId, DisciplineId = discipline.DisciplineId, Description = "Pump spares", Status = "Issued to Procurement" };
        mr.Items.Add(new MaterialRequisitionItem { ItemDescription = "Bearing", PartNumber = "M-1", MaterialItemId = material.MaterialItemId, Quantity = 4, Unit = "EA" });
        mr.Items.Add(new MaterialRequisitionItem { ItemDescription = "Alignment service", Quantity = 1, Unit = "LOT" });
        db.MaterialRequisitions.Add(mr);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var perms = new FakePermissionService();
        var runtime = new FakeWorkflowRuntime();
        var orders = new PurchaseOrderService(db);
        var service = new PurchaseOrderApprovalService(db, runtime, perms, orders, Options.Create(new ProcurementOptions { CatalogueOnlyLines = catalogueOnly }));
        return new Env
        {
            Db = db, Service = service, Orders = orders, Runtime = runtime, Perms = perms, Project = project.ProjectId, Vendor = vendor.VendorId,
            Mr = mr.MRId, Group = group.CommodityGroupId, Warehouse = warehouse.WarehouseId,
            CataloguedItem = mr.Items.First().MRItemId, FreeItem = mr.Items.Last().MRItemId
        };
    }

    private static async Task RulesAsync(Env e)
    {
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, 1000, "IRR", "PRC.Manager", "up to 1000", true), Buyer)).Success);
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 2, 1000.01m, null, "IRR", "DIR", "above", true), Buyer)).Success);
    }

    private static async Task<int> PoAsync(Env e)
    {
        var r = await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "irr", "PO-1", Buyer);
        Assert.True(r.Success, r.Error);
        // SCRUM-473: a stock line needs a delivery warehouse to be submitted
        foreach (var line in e.Db.PurchaseOrderLines.Where(l => l.PoId == r.Id)) line.WarehouseId = e.Warehouse;
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();
        return r.Id!.Value;
    }

    /// <summary>Prices both lines and completes the free-text line so the PO can be submitted.</summary>
    private static async Task<int> ReadyPoAsync(Env e, decimal bearingPrice = 100m, decimal servicePrice = 200m)
    {
        var poId = await PoAsync(e);
        var lines = await e.Db.PurchaseOrderLines.AsNoTracking().Where(l => l.PoId == poId).OrderBy(l => l.LineNo).ToListAsync();
        Assert.True((await e.Service.UpdateLineAsync(lines[0].PurchaseOrderLineId, new PoLineInput(4, bearingPrice, null, true, null, null), Buyer)).Success);
        Assert.True((await e.Service.UpdateLineAsync(lines[1].PurchaseOrderLineId, new PoLineInput(1, servicePrice, null, false, e.Group, "one-time service"), Buyer)).Success);
        e.Db.ChangeTracker.Clear();
        return poId;
    }

    // ------------------------------------------------------------ lines from the MR

    [Fact]
    public async Task A_PO_from_an_MR_has_one_line_per_item_tracing_back_to_it_with_the_registered_part()
    {
        var e = await NewAsync();

        var poId = await PoAsync(e);

        var po = await e.Db.PurchaseOrders.Include(p => p.Lines).SingleAsync(p => p.PoId == poId);
        Assert.Equal((PurchaseOrderStatuses.Draft, (int?)e.Mr, "IRR", e.Project), (po.Status, po.MaterialRequisitionId, po.Currency, po.ProjectId));
        var bearing = po.Lines.Single(l => l.MaterialRequisitionItemId == e.CataloguedItem);
        Assert.Equal((1, (int?)e.Db.MaterialItems.Single().MaterialItemId, 4m, true, true), (bearing.LineNo, bearing.MaterialItemId, bearing.Quantity, bearing.IsCatalogued, bearing.IsStockLine));
        var service = po.Lines.Single(l => l.MaterialRequisitionItemId == e.FreeItem);
        Assert.Equal((2, false, false), (service.LineNo, service.IsCatalogued, service.IsStockLine));   // free text starts as non-stock
    }

    [Fact]
    public async Task An_MR_item_is_ordered_once()
    {
        var e = await NewAsync();
        await PoAsync(e);

        var second = await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "IRR", "PO-2", Buyer);

        Assert.Equal("Po_Err_NoFreeItems", second.Error);
    }

    [Fact]
    public async Task An_MR_still_open_in_the_procurement_cartable_or_not_issued_cannot_feed_a_PO()
    {
        var e = await NewAsync();
        e.Db.ProcurementDemands.Add(new ProcurementDemand { SourceType = ProcurementDemandSources.MaterialRequisition, SourceId = e.Mr, Description = "x", Status = ProcurementDemandStatuses.Open });
        await e.Db.SaveChangesAsync();

        Assert.Equal("Po_Err_MrNotEligible", (await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "IRR", null, Buyer)).Error);

        var d = await e.Db.ProcurementDemands.SingleAsync();
        d.Status = ProcurementDemandStatuses.Accepted;
        var mr = await e.Db.MaterialRequisitions.SingleAsync();
        mr.Status = "Approved";
        await e.Db.SaveChangesAsync();
        Assert.Equal("Po_Err_MrNotEligible", (await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "IRR", null, Buyer)).Error);
    }

    // ------------------------------------------------------------ line rules

    [Fact]
    public async Task A_stock_line_that_is_not_catalogued_cannot_be_submitted()
    {
        var e = await NewAsync();
        var poId = await PoAsync(e);
        var free = await e.Db.PurchaseOrderLines.SingleAsync(l => l.MaterialRequisitionItemId == e.FreeItem);
        await e.Service.UpdateLineAsync(free.PurchaseOrderLineId, new PoLineInput(1, 5, null, true, e.Group, "x"), Buyer);

        Assert.Equal("Po_Err_StockLineNotCatalogued", (await e.Service.SubmitAsync(poId, Buyer)).Error);
    }

    [Fact]
    public async Task SCRUM473_The_delivery_warehouse_comes_from_the_pre_procurement_demand()
    {
        var e = await NewAsync();
        var demand = new ProcurementDemand { SourceType = ProcurementDemandSources.Replenishment, SourceId = 1, Description = "Bearing", Quantity = 4, WarehouseId = e.Warehouse, Status = ProcurementDemandStatuses.Converted };
        e.Db.ProcurementDemands.Add(demand);
        await e.Db.SaveChangesAsync();
        var item = await e.Db.MaterialRequisitionItems.SingleAsync(i => i.MRItemId == e.CataloguedItem);
        item.ProcurementDemandId = demand.ProcurementDemandId;
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        var r = await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "IRR", "PO-W", Buyer);

        Assert.True(r.Success, r.Error);
        var lines = await e.Db.PurchaseOrderLines.AsNoTracking().Where(l => l.PoId == r.Id).ToListAsync();
        Assert.Equal(e.Warehouse, lines.Single(l => l.MaterialRequisitionItemId == e.CataloguedItem).WarehouseId);
        Assert.Null(lines.Single(l => l.MaterialRequisitionItemId == e.FreeItem).WarehouseId);
    }

    [Fact]
    public async Task SCRUM473_A_stock_line_needs_a_delivery_warehouse_and_a_blank_field_keeps_it()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        var stock = await e.Db.PurchaseOrderLines.SingleAsync(l => l.MaterialRequisitionItemId == e.CataloguedItem);
        Assert.Equal(e.Warehouse, stock.WarehouseId);   // the update without a warehouse kept it
        stock.WarehouseId = null;
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        Assert.Equal("Po_Err_LineWarehouse", (await e.Service.SubmitAsync(poId, Buyer)).Error);
        Assert.Equal("Po_Err_Warehouse", (await e.Service.UpdateLineAsync(stock.PurchaseOrderLineId, new PoLineInput(4, 100, null, true, null, null, 999), Buyer)).Error);

        Assert.True((await e.Service.UpdateLineAsync(stock.PurchaseOrderLineId, new PoLineInput(4, 100, null, true, null, null, e.Warehouse), Buyer)).Success);
        Assert.True((await e.Service.SubmitAsync(poId, Buyer)).Success);
    }

    [Fact]
    public async Task A_free_text_line_needs_a_commodity_group_and_a_reason()
    {
        var e = await NewAsync();
        var poId = await PoAsync(e);

        Assert.Equal("Po_Err_FreeTextIncomplete", (await e.Service.SubmitAsync(poId, Buyer)).Error);
    }

    [Fact]
    public async Task The_catalogue_only_setting_refuses_every_free_text_line()
    {
        var e = await NewAsync(catalogueOnly: true);
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);

        Assert.Equal("Po_Err_CatalogueOnly", (await e.Service.SubmitAsync(poId, Buyer)).Error);
    }

    // ------------------------------------------------------------ routing by the authority table

    [Theory]
    [InlineData(100, 200, "PRC.Manager", 1)]     // 600 IRR -> level 1
    [InlineData(1000, 200, "DIR", 2)]            // 4200 IRR -> level 2
    public async Task Submitting_routes_the_PO_to_the_role_of_the_matching_level(decimal bearing, decimal service, string role, int level)
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e, bearing, service);

        var r = await e.Service.SubmitAsync(poId, Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Null(r.Error);
        var view = (await e.Service.GetAsync(poId))!;
        Assert.Equal((PurchaseOrderStatuses.Submitted, role, (int?)level, 4 * bearing + service), (view.Status, view.ApproverRole, view.AuthorityRuleLevel, view.Total));
        Assert.Equal(4 * bearing + service, (await e.Db.PurchaseOrders.SingleAsync()).Value);
        Assert.Equal("Running", (await e.Runtime.GetInstanceAsync(PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType, poId))!.Status);
    }

    [Fact]
    public async Task Without_a_matching_rule_the_PO_waits_as_responsible_not_set_and_routes_once_a_rule_exists()
    {
        var e = await NewAsync();
        var poId = await ReadyPoAsync(e);

        var submitted = await e.Service.SubmitAsync(poId, Buyer);

        Assert.True(submitted.Success);
        Assert.Equal("Po_ResponsibleNotSet", submitted.Error);
        Assert.True((await e.Service.GetAsync(poId))!.ResponsibleNotSet);
        Assert.Null(await e.Runtime.GetInstanceAsync(PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType, poId));
        Assert.Equal("Po_ResponsibleNotSet", (await e.Service.ApproveAsync(poId, Manager)).Error);

        await RulesAsync(e);
        var routed = await e.Service.RouteAsync(poId, Buyer);

        Assert.True(routed.Success, routed.Error);
        Assert.Equal("PRC.Manager", (await e.Service.GetAsync(poId))!.ApproverRole);
        Assert.Equal("Po_Err_AlreadyRouted", (await e.Service.RouteAsync(poId, Buyer)).Error);
    }

    [Fact]
    public async Task A_rule_of_another_currency_or_an_inactive_rule_is_never_used()
    {
        var e = await NewAsync();
        await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "USD", "PRC.Manager", null, true), Buyer);
        await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "IRR", "DIR", null, false), Buyer);
        var poId = await ReadyPoAsync(e);

        Assert.Equal("Po_ResponsibleNotSet", (await e.Service.SubmitAsync(poId, Buyer)).Error);
    }

    // ------------------------------------------------------------ approve / reject / SoD / issue

    [Fact]
    public async Task Only_the_routed_role_approves_and_never_the_creator_or_the_submitter()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        var po = await e.Db.PurchaseOrders.SingleAsync();
        po.CreatedBy = "manager";                                   // the manager created it
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();
        await e.Service.SubmitAsync(poId, Buyer);

        Assert.Equal("Po_Err_NotApprover", (await e.Service.ApproveAsync(poId, Director)).Error);
        Assert.Equal("Po_Err_SoD", (await e.Service.ApproveAsync(poId, Manager)).Error);
        var otherManager = new PoActor(41, new[] { "PRC.Manager" }, "manager2");
        var buyerWithRole = new PoActor(30, new[] { "PRC.Manager" }, "buyer");
        Assert.Equal("Po_Err_SoD", (await e.Service.ApproveAsync(poId, buyerWithRole)).Error);    // the submitter

        var r = await e.Service.ApproveAsync(poId, otherManager);

        Assert.True(r.Success, r.Error);
        var view = (await e.Service.GetAsync(poId))!;
        Assert.Equal((PurchaseOrderStatuses.Approved, "manager2"), (view.Status, view.ApprovedBy));
        Assert.Equal("Completed", (await e.Runtime.GetInstanceAsync(PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType, poId))!.Status);
    }

    [Fact]
    public async Task Rejecting_returns_the_PO_to_draft_with_the_reason_and_it_can_be_submitted_again()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        await e.Service.SubmitAsync(poId, Buyer);

        Assert.Equal("Po_Err_ReasonRequired", (await e.Service.RejectAsync(poId, " ", Manager)).Error);
        Assert.True((await e.Service.RejectAsync(poId, "price too high", Manager)).Success);

        var view = (await e.Service.GetAsync(poId))!;
        Assert.Equal((PurchaseOrderStatuses.Draft, "price too high"), (view.Status, view.RejectionReason));
        e.Db.ChangeTracker.Clear();
        Assert.True((await e.Service.SubmitAsync(poId, Buyer)).Success);
        Assert.Null((await e.Service.GetAsync(poId))!.RejectionReason);
    }

    [Fact]
    public async Task Lines_cannot_change_after_submission_and_only_an_approved_PO_is_issued()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        await e.Service.SubmitAsync(poId, Buyer);
        var line = await e.Db.PurchaseOrderLines.AsNoTracking().FirstAsync();

        Assert.Equal("Po_Err_NotDraft", (await e.Service.UpdateLineAsync(line.PurchaseOrderLineId, new PoLineInput(9, 1, null, true, null, null), Buyer)).Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Orders.IssueAsync(poId));   // Submitted is not issuable

        await e.Service.ApproveAsync(poId, new PoActor(41, new[] { "PRC.Manager" }, "manager2"));
        e.Db.ChangeTracker.Clear();
        await e.Orders.IssueAsync(poId);

        Assert.Equal(PurchaseOrderStatuses.Issued, (await e.Db.PurchaseOrders.SingleAsync()).Status);
    }

    // ------------------------------------------------------------ authority table

    [Fact]
    public async Task Rules_need_an_existing_role_and_a_valid_range()
    {
        var e = await NewAsync();

        Assert.Equal("Po_Err_RuleRole", (await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "IRR", "NoSuchRole", null, true), Buyer)).Error);
        Assert.Equal("Po_Err_RuleRange", (await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 500, 100, "IRR", "DIR", null, true), Buyer)).Error);
        Assert.Equal("Po_Err_RuleIncomplete", (await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "", "DIR", null, true), Buyer)).Error);
        Assert.Empty(await e.Service.GetRulesAsync());
    }

    [Fact]
    public async Task Creating_and_editing_lines_need_the_purchase_order_permissions()
    {
        var e = await NewAsync();
        e.Perms.Denied.Add(("PurchaseOrder", "Create"));
        Assert.Equal("Po_Err_Forbidden", (await e.Service.CreateFromRequisitionAsync(e.Mr, e.Vendor, "IRR", null, Buyer)).Error);
        e.Perms.Denied.Clear();
        var poId = await PoAsync(e);
        e.Perms.Denied.Add(("PurchaseOrder", "Edit"));
        Assert.Equal("Po_Err_Forbidden", (await e.Service.SubmitAsync(poId, Buyer)).Error);
    }

    // ------------------------------------------------------------ SCRUM-546: authority rules and re-routing

    [Fact]
    public async Task SCRUM546_A_rule_refuses_a_role_without_purchase_order_approve_and_the_approver_list_leaves_it_out()
    {
        var e = await NewAsync();
        e.Perms.DeniedForRole.Add(("DIR", "PurchaseOrder", "Approve"));

        Assert.Equal("Po_Err_RuleRoleNoApprove", (await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "IRR", "DIR", null, true), Buyer)).Error);
        Assert.Empty(await e.Service.GetRulesAsync());
        Assert.Equal(new[] { "PRC.Manager" }, await e.Service.GetApproverRolesAsync());

        // an inactive rule routes nothing: the admin can still switch off an old rule whose role lost the permission
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "IRR", "DIR", null, false), Buyer)).Success);
    }

    [Fact]
    public async Task SCRUM546_An_active_rule_may_not_overlap_another_active_rule_of_the_same_currency()
    {
        var e = await NewAsync();
        await RulesAsync(e);   // level 1: 0 - 1000, level 2: 1000.01 - no limit (IRR)

        var overlap = await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 2, 1000.01m, null, "irr", "PRC.Manager", null, true), Buyer);
        Assert.Equal("Po_Err_RuleOverlap", overlap.Error);
        Assert.Equal(2, overlap.Args![0]);
        Assert.Equal("Po_Err_RuleOverlap", (await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 3, 500, 600, "IRR", "DIR", null, true), Buyer)).Error);
        Assert.Equal(2, (await e.Service.GetRulesAsync()).Count);

        // another currency, an inactive rule, and editing a rule in place are all fine
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 1, 0, null, "USD", "DIR", null, true), Buyer)).Success);
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(null, 3, 500, 600, "IRR", "DIR", null, false), Buyer)).Success);
        var level2 = (await e.Service.GetRulesAsync()).Single(r => r.Currency == "IRR" && r.Level == 2);
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(level2.Id, 2, 1000.01m, null, "IRR", "PRC.Manager", null, true), Buyer)).Success);
    }

    [Fact]
    public async Task SCRUM546_A_PO_whose_role_lost_the_approve_permission_is_routed_again_and_the_old_task_is_cancelled()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e, bearingPrice: 500m);   // 2,200 -> level 2 (DIR)
        Assert.True((await e.Service.SubmitAsync(poId, Buyer)).Success);
        Assert.False((await e.Service.GetAsync(poId))!.RouteInvalid);
        Assert.Equal("Po_Err_AlreadyRouted", (await e.Service.RouteAsync(poId, Buyer)).Error);   // a valid route stays
        var oldInstance = await e.Runtime.GetInstanceAsync(PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType, poId);

        // the admin moves level 2 to PRC.Manager: the PO still waits for DIR
        var level2 = (await e.Service.GetRulesAsync()).Single(r => r.Level == 2);
        Assert.True((await e.Service.SaveRuleAsync(new PoAuthorityRuleInput(level2.Id, 2, 1000.01m, null, "IRR", "PRC.Manager", null, true), Buyer)).Success);
        e.Db.ChangeTracker.Clear();
        Assert.True((await e.Service.GetAsync(poId))!.RouteInvalid);

        var r = await e.Service.RouteAsync(poId, Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Equal("Cancelled", oldInstance!.Status);
        var view = (await e.Service.GetAsync(poId))!;
        Assert.Equal(("PRC.Manager", false), (view.ApproverRole, view.RouteInvalid));
        Assert.Equal("Running", (await e.Runtime.GetInstanceAsync(PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType, poId))!.Status);
        Assert.Contains(e.Db.AuditLogs, a => a.EntityName == nameof(PurchaseOrder) && a.Action == "Reroute");
    }

    [Fact]
    public async Task SCRUM546_A_matching_rule_whose_role_lost_approve_leaves_the_responsible_not_set_and_the_buyer_may_take_the_PO_back()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);   // 600 -> level 1 (PRC.Manager)
        e.Perms.DeniedForRole.Add(("PRC.Manager", "PurchaseOrder", "Approve"));

        Assert.Equal("Po_ResponsibleNotSet", (await e.Service.SubmitAsync(poId, Buyer)).Error);
        Assert.True((await e.Service.RejectAsync(poId, "wrong authority", Buyer)).Success);
        Assert.Equal(PurchaseOrderStatuses.Draft, (await e.Service.GetAsync(poId))!.Status);
    }

    // ------------------------------------------------------------ SCRUM-547: a stock line needs its material record

    [Fact]
    public async Task SCRUM547_A_stock_line_of_a_catalogued_part_without_a_material_record_is_refused_naming_the_line_and_part()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        var line = await e.Db.PurchaseOrderLines.SingleAsync(l => l.PoId == poId && l.LineNo == 1);
        (line.MaterialItemId, line.PartCatalogueRequestId, line.PartNumber) = (null, 999, "LV-PN-0001");
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        var r = await e.Service.SubmitAsync(poId, Buyer);

        Assert.Equal("Po_Err_StockLineNoMaterial", r.Error);
        Assert.Equal(new object[] { 1, "LV-PN-0001" }, r.Args);
        Assert.Equal(PurchaseOrderStatuses.Draft, (await e.Service.GetAsync(poId))!.Status);
    }

    [Fact]
    public async Task SCRUM547_An_existing_material_record_with_the_part_number_is_linked_on_submit()
    {
        var e = await NewAsync();
        await RulesAsync(e);
        var poId = await ReadyPoAsync(e);
        var line = await e.Db.PurchaseOrderLines.SingleAsync(l => l.PoId == poId && l.LineNo == 1);
        (line.MaterialItemId, line.PartCatalogueRequestId, line.PartNumber) = (null, 999, "M-1");
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();

        var r = await e.Service.SubmitAsync(poId, Buyer);

        Assert.True(r.Success, r.Error);
        Assert.Equal(e.Db.MaterialItems.Single().MaterialItemId, (await e.Db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.PoId == poId && l.LineNo == 1)).MaterialItemId);
    }
}
