using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Inventory;

/// <summary>SCRUM-472: warehouse intake of work-order materials (request, source from a picked warehouse, supply code, guards).</summary>
public class WarehouseIntakeServiceTests
{
    private sealed class Permissions : IPermissionService
    {
        public HashSet<string> Granted { get; } = new() { "WorkOrder.Edit", "StoreRequisition.Create" };
        public Task<List<RolePermissionDto>> GetMatrixAsync() => Task.FromResult(new List<RolePermissionDto>());
        public Task SetPermissionsAsync(string roleName, IEnumerable<ModulePermissionInput> permissions) => Task.CompletedTask;
        public Task<bool> HasPermissionAsync(IEnumerable<string> userRoles, string moduleName, string action) => Task.FromResult(Granted.Contains($"{moduleName}.{action}"));
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private sealed class Numbers : INumberingService
    {
        private int _n;
        public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null) => Task.FromResult($"{documentType}-{++_n}");
    }

    private sealed class Env
    {
        public required PEMSDbContext Db { get; init; }
        public required WarehouseIntakeService Service { get; init; }
        public required FakeWorkflowRuntime Runtime { get; init; }
        public required Permissions Perms { get; init; }
        public int Wh, Wh2, InactiveWh, Item, Item2, Item3;
    }

    private static readonly WarehouseIntakeActor Planner = new(10, new[] { "MaintenancePlanner" }, "planner");
    private static readonly WarehouseIntakeActor Storekeeper = new(20, new[] { "WH.Storekeeper" }, "keeper");

    private static async Task<Env> NewAsync()
    {
        var options = new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase("whi_" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new PEMSDbContext(options);
        var each = new MaterialUOM { Code = "EA", Name = "Each" };
        var wh = new Warehouse { Code = "WH-1", Name = "Main" };
        var wh2 = new Warehouse { Code = "WH-2", Name = "Site" };
        var off = new Warehouse { Code = "WH-X", Name = "Closed", IsActive = false };
        db.AddRange(each, wh, wh2, off);
        await db.SaveChangesAsync();
        var i1 = new MaterialItem { MaterialCode = "M-1", Description = "Bearing", UomId = each.MaterialUOMId };
        var i2 = new MaterialItem { MaterialCode = "M-2", Description = "Seal", UomId = each.MaterialUOMId };
        var i3 = new MaterialItem { MaterialCode = "M-3", Description = "Gasket", UomId = each.MaterialUOMId };
        db.AddRange(i1, i2, i3);
        await db.SaveChangesAsync();
        var runtime = new FakeWorkflowRuntime();
        var perms = new Permissions();
        return new Env
        {
            Db = db, Runtime = runtime, Perms = perms, Service = new WarehouseIntakeService(db, runtime, new Numbers(), perms),
            Wh = wh.WarehouseId, Wh2 = wh2.WarehouseId, InactiveWh = off.WarehouseId, Item = i1.MaterialItemId, Item2 = i2.MaterialItemId, Item3 = i3.MaterialItemId
        };
    }

    private static async Task<(WorkOrder Wo, int Line1, int Line2)> WoAsync(Env e, string status = "Assigned", string supply2 = WorkOrderMaterialSupplyCodes.Stock)
    {
        var wo = new WorkOrder { EquipmentId = 1, WorkOrderNumber = "WO-" + Guid.NewGuid().ToString("N")[..6], WorkOrderType = "Corrective", Priority = "Low", Status = status };
        e.Db.WorkOrders.Add(wo);
        await e.Db.SaveChangesAsync();
        var a = new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item, RequiredQuantity = 8 };
        var b = new WorkOrderMaterial { WorkOrderId = wo.WorkOrderId, MaterialItemId = e.Item2, RequiredQuantity = 3, SupplyCode = supply2 };
        e.Db.WorkOrderMaterials.AddRange(a, b);
        await e.Db.SaveChangesAsync();
        e.Db.ChangeTracker.Clear();
        return (wo, a.WorkOrderMaterialId, b.WorkOrderMaterialId);
    }

    // ------------------------------------------------------------ request

    [Fact]
    public async Task Requesting_materials_puts_one_task_in_the_warehouse_queue_and_audits_it()
    {
        var e = await NewAsync();
        var (wo, _, _) = await WoAsync(e);

        var r = await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        Assert.True(r.Success, r.Error);
        var instance = await e.Runtime.GetInstanceAsync(WarehouseIntakeWorkflowSeeder.BusinessObjectType, wo.WorkOrderId);
        Assert.Equal("Running", instance!.Status);
        Assert.Equal(WarehouseIntakeWorkflowSeeder.Module, instance.Module);
        Assert.Single(e.Db.AuditLogs.Where(a => a.Action == "RequestMaterials" && a.EntityKeyValue == wo.WorkOrderId.ToString()));
        Assert.True((await e.Service.GetAsync(wo.WorkOrderId))!.IsOpen);
    }

    [Fact]
    public async Task A_second_request_while_the_task_is_open_is_refused()
    {
        var e = await NewAsync();
        var (wo, _, _) = await WoAsync(e);
        await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        var r = await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        Assert.Equal("Whi_Err_AlreadyRequested", r.Error);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task Materials_are_requested_only_for_a_released_work_order(string status)
    {
        var e = await NewAsync();
        var (wo, _, _) = await WoAsync(e, status);

        var r = await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        Assert.Equal("Whi_Err_NotReleased", r.Error);
        Assert.Null(await e.Runtime.GetInstanceAsync(WarehouseIntakeWorkflowSeeder.BusinessObjectType, wo.WorkOrderId));
    }

    [Fact]
    public async Task Requesting_without_work_order_edit_permission_is_refused()
    {
        var e = await NewAsync();
        var (wo, _, _) = await WoAsync(e);
        e.Perms.Granted.Remove("WorkOrder.Edit");

        Assert.Equal("Whi_Err_Forbidden", (await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner)).Error);
    }

    [Fact]
    public async Task Direct_purchase_lines_alone_give_nothing_to_request()
    {
        var e = await NewAsync();
        var (wo, line1, _) = await WoAsync(e, supply2: WorkOrderMaterialSupplyCodes.DirectPurchase);
        Assert.True((await e.Service.SetSupplyCodeAsync(line1, WorkOrderMaterialSupplyCodes.DirectPurchase, Planner)).Success);

        Assert.Equal("Whi_Err_NothingToRequest", (await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner)).Error);
    }

    // ------------------------------------------------------------ source

    [Fact]
    public async Task Sourcing_raises_a_draft_requisition_in_the_picked_warehouse_with_linked_lines()
    {
        var e = await NewAsync();
        var (wo, line1, line2) = await WoAsync(e);
        await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        var r = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh2, new Dictionary<int, decimal> { [line1] = 8, [line2] = 3 }, Storekeeper);

        Assert.True(r.Success, r.Error);
        Assert.True(r.IntakeCompleted);
        var sr = await e.Db.StoreRequisitions.Include(x => x.Lines).SingleAsync(x => x.StoreRequisitionId == r.StoreRequisitionId);
        Assert.Equal(StoreRequisitionStatuses.Draft, sr.Status);
        Assert.Equal(e.Wh2, sr.WarehouseId);
        Assert.Equal(wo.WorkOrderId, sr.WorkOrderId);
        Assert.Equal(20, sr.RequestedById);
        Assert.Equal("StoreRequisition-1", sr.RequisitionNumber);
        Assert.Contains(sr.Lines, l => l.WorkOrderMaterialId == line1 && l.MaterialItemId == e.Item && l.RequestedQuantity == 8);
        Assert.Contains(sr.Lines, l => l.WorkOrderMaterialId == line2 && l.MaterialItemId == e.Item2 && l.RequestedQuantity == 3);
        Assert.Equal("Completed", (await e.Runtime.GetInstanceAsync(WarehouseIntakeWorkflowSeeder.BusinessObjectType, wo.WorkOrderId))!.Status);
        Assert.Equal("Complete", e.Runtime.LastExecutedActionType);
        Assert.Single(e.Db.AuditLogs.Where(a => a.Action == "WarehouseIntakeSource"));
    }

    [Fact]
    public async Task Partial_sourcing_keeps_the_task_open_and_the_rest_can_come_from_another_warehouse()
    {
        var e = await NewAsync();
        var (wo, line1, line2) = await WoAsync(e);
        await e.Service.RequestMaterialsAsync(wo.WorkOrderId, Planner);

        var first = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, new Dictionary<int, decimal> { [line1] = 5 }, Storekeeper);
        Assert.True(first.Success, first.Error);
        Assert.False(first.IntakeCompleted);
        var mid = (await e.Service.GetAsync(wo.WorkOrderId))!;
        Assert.True(mid.IsOpen);
        Assert.Equal(3m, mid.Lines.Single(l => l.WorkOrderMaterialId == line1).UnsourcedQuantity);

        var second = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh2, new Dictionary<int, decimal> { [line1] = 3, [line2] = 3 }, Storekeeper);
        Assert.True(second.IntakeCompleted);
        var done = (await e.Service.GetAsync(wo.WorkOrderId))!;
        Assert.All(done.Lines, l => Assert.Equal(0m, l.UnsourcedQuantity));
        Assert.Equal(2, done.Lines.Single(l => l.WorkOrderMaterialId == line1).Requisitions.Count);
    }

    [Fact]
    public async Task More_than_the_unsourced_quantity_is_refused_and_nothing_is_written()
    {
        var e = await NewAsync();
        var (wo, line1, _) = await WoAsync(e);

        var r = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, new Dictionary<int, decimal> { [line1] = 9 }, Storekeeper);

        Assert.Equal("Whi_Err_Quantity", r.Error);
        Assert.Empty(e.Db.StoreRequisitions);
    }

    [Fact]
    public async Task A_direct_purchase_line_cannot_be_sourced_from_a_warehouse()
    {
        var e = await NewAsync();
        var (wo, _, line2) = await WoAsync(e, supply2: WorkOrderMaterialSupplyCodes.DirectPurchase);

        var r = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, new Dictionary<int, decimal> { [line2] = 1 }, Storekeeper);

        Assert.Equal("Whi_Err_NotStockLine", r.Error);
    }

    [Fact]
    public async Task An_inactive_warehouse_or_a_missing_permission_or_employee_is_refused()
    {
        var e = await NewAsync();
        var (wo, line1, _) = await WoAsync(e);
        var q = new Dictionary<int, decimal> { [line1] = 1 };

        Assert.Equal("Whi_Err_Warehouse", (await e.Service.SourceAsync(wo.WorkOrderId, e.InactiveWh, q, Storekeeper)).Error);
        Assert.Equal("Whi_Err_NoEmployee", (await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, q, Storekeeper with { EmployeeId = null })).Error);
        e.Perms.Granted.Remove("StoreRequisition.Create");
        Assert.Equal("Whi_Err_Forbidden", (await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, q, Storekeeper)).Error);
        Assert.Empty(e.Db.StoreRequisitions);
    }

    // ------------------------------------------------------------ supply code and removal guard

    [Fact]
    public async Task A_sourced_line_keeps_its_supply_code_and_cannot_be_removed_until_its_requisition_is_cancelled()
    {
        var e = await NewAsync();
        var (wo, line1, _) = await WoAsync(e);
        var r = await e.Service.SourceAsync(wo.WorkOrderId, e.Wh, new Dictionary<int, decimal> { [line1] = 2 }, Storekeeper);

        Assert.Equal("Whi_Err_AlreadySourced", (await e.Service.SetSupplyCodeAsync(line1, WorkOrderMaterialSupplyCodes.DirectPurchase, Planner)).Error);
        Assert.False(await e.Service.CanRemoveMaterialAsync(line1));

        var sr = await e.Db.StoreRequisitions.SingleAsync(x => x.StoreRequisitionId == r.StoreRequisitionId);
        sr.Status = StoreRequisitionStatuses.Cancelled;
        await e.Db.SaveChangesAsync();

        Assert.True(await e.Service.CanRemoveMaterialAsync(line1));
        Assert.True((await e.Service.SetSupplyCodeAsync(line1, WorkOrderMaterialSupplyCodes.DirectPurchase, Planner)).Success);
        Assert.Single(e.Db.AuditLogs.Where(a => a.Action == "SetSupplyCode"));
    }

    [Fact]
    public async Task An_unknown_supply_code_is_refused()
    {
        var e = await NewAsync();
        var (_, line1, _) = await WoAsync(e);

        Assert.Equal("Whi_Err_SupplyCode", (await e.Service.SetSupplyCodeAsync(line1, "Magic", Planner)).Error);
    }

    // ------------------------------------------------------------ procurement side

    [Fact]
    public async Task Direct_purchase_demand_lists_only_lines_of_released_work_orders()
    {
        var e = await NewAsync();
        var (released, _, dp) = await WoAsync(e, "InProgress", WorkOrderMaterialSupplyCodes.DirectPurchase);
        await WoAsync(e, "Open", WorkOrderMaterialSupplyCodes.DirectPurchase);

        var list = await e.Service.GetDirectPurchaseDemandAsync();

        var only = Assert.Single(list);
        Assert.Equal(dp, only.WorkOrderMaterialId);
        Assert.Equal(released.WorkOrderId, only.WorkOrderId);
        Assert.Equal("M-2", only.MaterialCode);
        Assert.Equal(3m, only.RequiredQuantity);
        Assert.Equal("Each", only.UnitName);
    }
}
