using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// داشبورد مدیریتی KPI (/Kpi/Executive): فقط شاخص‌های دارای داده‌ی واقعی، با وضعیت نسبت به هدف قابل‌ویرایش. بدون داده‌ی کافی
/// Value=null (NoData) می‌ماند - هرگز صفر یا عدد نمونه‌ی جعلی نمایش داده نمی‌شود.
/// </summary>
public class ExecutiveKpiTests
{
    // ------------------------------------------------------------------ pure status logic

    [Theory]
    [InlineData("Higher", 96, 95, 90, KpiStatuses.Met)]
    [InlineData("Higher", 95, 95, 90, KpiStatuses.Met)]       // boundary: equal to target is met
    [InlineData("Higher", 92, 95, 90, KpiStatuses.Near)]
    [InlineData("Higher", 90, 95, 90, KpiStatuses.Near)]      // boundary: equal to warn is near
    [InlineData("Higher", 89.9, 95, 90, KpiStatuses.Missed)]
    [InlineData("Higher", 80, 95, null, KpiStatuses.Missed)]  // no warn threshold -> met / missed only
    [InlineData("Lower", 4, 5, 8, KpiStatuses.Met)]
    [InlineData("Lower", 5, 5, 8, KpiStatuses.Met)]
    [InlineData("Lower", 6, 5, 8, KpiStatuses.Near)]
    [InlineData("Lower", 8, 5, 8, KpiStatuses.Near)]
    [InlineData("Lower", 8.1, 5, 8, KpiStatuses.Missed)]
    [InlineData("Lower", 9, 5, null, KpiStatuses.Missed)]
    public void Status_IsComputedFromDirectionTargetAndWarn(string direction, double value, double target, object? warn, string expected)
    {
        // xUnit cannot convert an integer literal to double?, so the optional threshold arrives as object
        decimal? warnValue = warn is null ? null : Convert.ToDecimal(warn, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, KpiStatusCalculator.Evaluate(direction, (decimal)value, (decimal)target, warnValue));
    }

    [Fact]
    public void Status_WithoutAValue_IsNoData_EvenWhenATargetExists()
    {
        Assert.Equal(KpiStatuses.NoData, KpiStatusCalculator.Evaluate(KpiDirections.HigherIsBetter, null, 95m, 90m));
        Assert.Equal(KpiStatuses.NoData, KpiStatusCalculator.Evaluate(KpiDirections.LowerIsBetter, null, null, null));
    }

    [Fact]
    public void Status_WithAValueButNoTarget_IsNoTarget()
    {
        Assert.Equal(KpiStatuses.NoTarget, KpiStatusCalculator.Evaluate(KpiDirections.HigherIsBetter, 12m, null, null));
    }

    [Fact]
    public void Catalog_HasUniqueKeys_ValidDefaults_AndNoSampleDataCategories()
    {
        Assert.Equal(KpiCatalog.All.Count, KpiCatalog.All.Select(k => k.Key).Distinct().Count());
        Assert.All(KpiCatalog.All, k =>
        {
            Assert.Contains(k.Section, KpiSections.Ordered);
            Assert.Contains(k.Direction, new[] { KpiDirections.HigherIsBetter, KpiDirections.LowerIsBetter });
            if (k.DefaultTarget is null) { Assert.Null(k.DefaultWarn); return; }
            if (k.DefaultWarn is null) return;
            // the warn threshold sits on the "worse" side of the target
            if (k.Direction == KpiDirections.HigherIsBetter) Assert.True(k.DefaultWarn <= k.DefaultTarget, k.Key);
            else Assert.True(k.DefaultWarn >= k.DefaultTarget, k.Key);
        });
        // categories with no data source must never become tiles
        Assert.DoesNotContain(KpiCatalog.All, k => k.Key.Contains("oee") || k.Key.Contains("safety") || k.Key.Contains("energy") || k.Key.Contains("budget"));
    }

    // ------------------------------------------------------------------ targets

    private static (PEMSDbContext ctx, KpiTargetService svc) NewTargets(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("kpiT_" + name);
        return (ctx, new KpiTargetService(ctx));
    }

    [Fact]
    public async Task Targets_WithoutOverrides_ComeFromTheCatalogDefaults()
    {
        var (_, svc) = NewTargets(nameof(Targets_WithoutOverrides_ComeFromTheCatalogDefaults));

        var all = await svc.GetEffectiveAsync();

        Assert.Equal(KpiCatalog.All.Count, all.Count);
        var pm = all.Single(x => x.Key == "pm_compliance");
        Assert.Equal((95m, 90m, false), (pm.Target, pm.Warn, pm.IsCustom));
        Assert.Null(all.Single(x => x.Key == "cost_total").Target);   // no document target -> none
    }

    [Fact]
    public async Task Targets_SaveOverridesTheDefault_AndCanBeCleared_WithoutDeletingTheRow()
    {
        var (ctx, svc) = NewTargets(nameof(Targets_SaveOverridesTheDefault_AndCanBeCleared_WithoutDeletingTheRow));

        Assert.True((await svc.SaveAsync("pm_compliance", 85m, 80m)).Success);
        var custom = (await svc.GetEffectiveAsync()).Single(x => x.Key == "pm_compliance");
        Assert.Equal((85m, 80m, true), (custom.Target, custom.Warn, custom.IsCustom));

        Assert.True((await svc.SaveAsync("pm_compliance", null, 80m)).Success);   // clearing the target also drops the threshold
        var cleared = (await svc.GetEffectiveAsync()).Single(x => x.Key == "pm_compliance");
        Assert.Null(cleared.Target);
        Assert.Null(cleared.Warn);
        Assert.True(cleared.IsCustom);   // an explicit "no target", not the default again
        Assert.Equal(1, ctx.KpiTargets.Count(x => x.KpiKey == "pm_compliance"));

        Assert.True((await svc.SaveAsync("cost_total", 1_000_000m, null)).Success);   // a target for a KPI without a default
        Assert.Equal(1_000_000m, (await svc.GetEffectiveAsync()).Single(x => x.Key == "cost_total").Target);
    }

    [Theory]
    [InlineData("nope", 50, null, "Kpi_Err_UnknownKpi")]
    [InlineData("pm_compliance", 101, null, "Kpi_Err_TargetRange")]       // percentage above 100
    [InlineData("pm_compliance", -1, null, "Kpi_Err_TargetRange")]
    [InlineData("pm_compliance", 95, 120, "Kpi_Err_TargetRange")]
    [InlineData("pm_compliance", 90, 95, "Kpi_Err_WarnBelowTarget")]      // higher-is-better: warn must be <= target
    [InlineData("fleet_mttr", 5, 3, "Kpi_Err_WarnAboveTarget")]            // lower-is-better: warn must be >= target
    [InlineData("fleet_mttr", 2000000000, null, "Kpi_Err_TargetRange")]
    public async Task Targets_InvalidInput_IsRejected_AndNothingIsStored(string key, double target, object? warn, string expectedError)
    {
        var (ctx, svc) = NewTargets("invalid_" + key + "_" + target + "_" + warn);
        decimal? warnValue = warn is null ? null : Convert.ToDecimal(warn, System.Globalization.CultureInfo.InvariantCulture);

        var r = await svc.SaveAsync(key, (decimal)target, warnValue);

        Assert.False(r.Success);
        Assert.Equal(expectedError, r.Errors[0].Key);
        Assert.Empty(ctx.KpiTargets);
    }

    [Fact]
    public void TargetEntity_HasAUniqueKeyIndex()
    {
        var (ctx, _) = NewTargets(nameof(TargetEntity_HasAUniqueKeyIndex));
        var entity = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(ctx).Model.FindEntityType(typeof(KpiTarget))!;

        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(KpiTarget.KpiKey));
    }

    // ------------------------------------------------------------------ dashboard

    private static ExecutiveKpiService NewDashboard(PEMSDbContext ctx) =>
        new(ctx, new KpiService(ctx), new ReliabilityService(ctx), new ReliabilityIntelligenceService(ctx), new KpiTargetService(ctx));

    private static KpiTileDto Tile(ExecutiveKpiDashboardDto d, string key) => d.Tiles.Single(t => t.Key == key);

    private static readonly DateTime Today = DateTime.Today;

    [Fact]
    public async Task EmptyDatabase_ListsEveryKpiOnce_AndFabricatesNothing()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(EmptyDatabase_ListsEveryKpiOnce_AndFabricatesNothing));

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(KpiCatalog.All.Select(k => k.Key).OrderBy(x => x), d.Tiles.Select(t => t.Key).OrderBy(x => x));
        // only genuine zeros (a backlog that is empty, no repeat-failure groups) carry a value; everything else is "no data"
        Assert.Equal(new[] { "backlog_count", "repeat_failure_groups" }, d.Tiles.Where(t => t.Value != null).Select(t => t.Key).OrderBy(x => x));
        Assert.All(d.Tiles.Where(t => t.Value == null), t => Assert.Equal(KpiStatuses.NoData, t.Status));
        Assert.Empty(d.EquipmentByStatus);
        Assert.Empty(d.TopEquipmentByCost);
    }

    [Fact]
    public async Task Maintenance_ComputesComplianceBacklogAndCompletion_AgainstTheDefaultTargets()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Maintenance_ComputesComplianceBacklogAndCompletion_AgainstTheDefaultTargets));
        for (var i = 1; i <= 4; i++)
            ctx.MaintenancePlans.Add(new MaintenancePlan { MaintenancePlanId = i, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "P" + i, Title = "p", IsActive = true,
                NextDueDate = i == 1 ? Today.AddDays(-3) : Today.AddDays(10) });   // 1 of 4 overdue
        ctx.WorkOrders.AddRange(
            Wo(1, "Open", dueDate: Today.AddDays(-5)),     // backlog, past due
            Wo(2, "Assigned", dueDate: Today.AddDays(5)),   // backlog
            Wo(3, "InProgress", dueDate: null),             // backlog
            Wo(4, "Completed"), Wo(5, "Closed"));
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        var compliance = Tile(d, "pm_compliance");
        Assert.Equal(75.0m, compliance.Value);
        Assert.Equal(KpiStatuses.Missed, compliance.Status);        // 75 < warn 90
        Assert.Equal(1m, Tile(d, "pm_overdue").Value);
        Assert.Equal(3m, Tile(d, "backlog_count").Value);
        Assert.Equal(33.3m, Tile(d, "backlog_overdue_pct").Value);   // 1 of 3
        Assert.Equal(KpiStatuses.Missed, Tile(d, "backlog_overdue_pct").Status);
        Assert.Equal(40.0m, Tile(d, "wo_completion_rate").Value);    // 2 of 5 created in the period
        Assert.Equal(KpiStatuses.Missed, Tile(d, "wo_completion_rate").Status);
    }

    private static WorkOrder Wo(int id, string status, string type = "Corrective", DateTime? dueDate = null, DateTime? created = null) =>
        new() { WorkOrderId = id, EquipmentId = 1, WorkOrderNumber = "WO-" + id, WorkOrderType = type, Status = status, DueDate = dueDate, IsActive = true, CreatedDate = created ?? DateTime.Now };

    [Fact]
    public async Task CustomTarget_ChangesTheStatus_AndClearingItGivesNoTarget()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(CustomTarget_ChangesTheStatus_AndClearingItGivesNoTarget));
        for (var i = 1; i <= 4; i++)
            ctx.MaintenancePlans.Add(new MaintenancePlan { MaintenancePlanId = i, EquipmentId = 1, MaintenanceStrategyId = 1, PlanNumber = "P" + i, Title = "p", IsActive = true,
                NextDueDate = i == 1 ? Today.AddDays(-3) : Today.AddDays(10) });
        await ctx.SaveChangesAsync();
        var targets = new KpiTargetService(ctx);
        var dashboard = NewDashboard(ctx);

        Assert.Equal(KpiStatuses.Missed, Tile(await dashboard.GetAsync(Today.AddDays(-29), Today), "pm_compliance").Status);

        await targets.SaveAsync("pm_compliance", 70m, 60m);
        var met = Tile(await dashboard.GetAsync(Today.AddDays(-29), Today), "pm_compliance");
        Assert.Equal(KpiStatuses.Met, met.Status);
        Assert.Equal(70m, met.Target);

        await targets.SaveAsync("pm_compliance", null, null);
        Assert.Equal(KpiStatuses.NoTarget, Tile(await dashboard.GetAsync(Today.AddDays(-29), Today), "pm_compliance").Status);
    }

    [Fact]
    public async Task Period_OnlyCountsWorkOrdersCreatedInsideIt_AndSwappedDatesAreNormalised()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Period_OnlyCountsWorkOrdersCreatedInsideIt_AndSwappedDatesAreNormalised));
        ctx.WorkOrders.AddRange(
            Wo(1, "Completed"), Wo(2, "Open"), Wo(3, "Completed"));
        await ctx.SaveChangesAsync();
        // PEMSDbContext stamps CreatedDate on insert, so the age is set afterwards (a Modified row keeps its value)
        ctx.WorkOrders.Find(1)!.CreatedDate = Today.AddDays(-2);
        ctx.WorkOrders.Find(2)!.CreatedDate = Today.AddDays(-2);
        ctx.WorkOrders.Find(3)!.CreatedDate = Today.AddDays(-100);   // outside the 30-day period
        await ctx.SaveChangesAsync();
        var dashboard = NewDashboard(ctx);

        var d = await dashboard.GetAsync(Today.AddDays(-29), Today);
        var swapped = await dashboard.GetAsync(Today, Today.AddDays(-29));

        Assert.Equal(50.0m, Tile(d, "wo_completion_rate").Value);
        Assert.Equal(Tile(d, "wo_completion_rate").Value, Tile(swapped, "wo_completion_rate").Value);
        Assert.Equal(d.From, swapped.From);
        Assert.Equal(d.To, swapped.To);
    }

    [Fact]
    public async Task Cost_SharesAreRelativeToTheTotal_AndCorrectiveIncludesEmergency()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Cost_SharesAreRelativeToTheTotal_AndCorrectiveIncludesEmergency));
        ctx.Equipments.AddRange(Eq(1, "Active"), Eq(2, "Active"));
        ctx.WorkOrders.AddRange(Wo(1, "Closed", "Preventive"), Wo(2, "Closed", "Corrective"), Wo(3, "Closed", "Emergency"));
        ctx.MaintenanceHistories.AddRange(
            new MaintenanceHistory { MaintenanceHistoryId = 1, EquipmentId = 1, WorkOrderId = 1, ExecutionDate = Today.AddDays(-1), Cost = 500m, IsActive = true },
            new MaintenanceHistory { MaintenanceHistoryId = 2, EquipmentId = 1, WorkOrderId = 2, ExecutionDate = Today.AddDays(-1), Cost = 300m, IsActive = true },
            new MaintenanceHistory { MaintenanceHistoryId = 3, EquipmentId = 2, WorkOrderId = 3, ExecutionDate = Today.AddDays(-1), Cost = 200m, IsActive = true });
        ctx.WorkOrderLabors.Add(new WorkOrderLabor { WorkOrderLaborId = 1, WorkOrderId = 1, EmployeeId = 1, LaborCost = 400m, IsActive = true });
        ctx.MaintenanceSpareUsages.Add(new MaintenanceSpareUsage { MaintenanceSpareUsageId = 1, WorkOrderId = 2, MaterialItemId = 1, Quantity = 1, TotalCost = 100m, IsActive = true });
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(1000m, Tile(d, "cost_total").Value);
        Assert.Equal(40.0m, Tile(d, "cost_labor_share").Value);
        Assert.Equal(10.0m, Tile(d, "cost_material_share").Value);
        Assert.Equal(50.0m, Tile(d, "cost_corrective_share").Value);   // corrective 300 + emergency 200
        Assert.Equal(new[] { 1, 2 }, d.TopEquipmentByCost.Select(x => x.EquipmentId).OrderBy(x => x));
    }

    [Fact]
    public async Task Inventory_CountsItemsInStockLowAndOut_UsingTheStockRowsOfActiveItems()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Inventory_CountsItemsInStockLowAndOut_UsingTheStockRowsOfActiveItems));
        for (var i = 1; i <= 4; i++)
            ctx.MaterialItems.Add(new MaterialItem { MaterialItemId = i, MaterialCode = "M" + i, Description = "d", IsActive = i != 4 });
        ctx.MaterialStocks.AddRange(
            new MaterialStock { MaterialStockId = 1, MaterialItemId = 1, WarehouseId = 1, OnHandQuantity = 10, ReservedQuantity = 0, ReorderPoint = 5, IsActive = true },   // healthy
            new MaterialStock { MaterialStockId = 2, MaterialItemId = 2, WarehouseId = 1, OnHandQuantity = 4, ReservedQuantity = 0, ReorderPoint = 5, IsActive = true },    // low
            new MaterialStock { MaterialStockId = 3, MaterialItemId = 3, WarehouseId = 1, OnHandQuantity = 0, ReservedQuantity = 0, IsActive = true },                       // out
            new MaterialStock { MaterialStockId = 4, MaterialItemId = 4, WarehouseId = 1, OnHandQuantity = 0, ReservedQuantity = 0, IsActive = true });                      // inactive item: ignored
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(66.7m, Tile(d, "stock_in_rate").Value);   // 2 of 3 stocked items have stock
        Assert.Equal(1m, Tile(d, "low_stock_items").Value);
        Assert.Equal(1m, Tile(d, "out_of_stock_items").Value);
        Assert.Equal(KpiStatuses.Missed, Tile(d, "stock_in_rate").Status);   // < warn 90
    }

    [Fact]
    public async Task Production_CountsOnTimeOverdueAndDowntime_WithoutSummingQuantities()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Production_CountsOnTimeOverdueAndDowntime_WithoutSummingQuantities));
        ctx.ProductionOrders.AddRange(
            Order(1, "Completed", planned: Today.AddDays(-5), actual: Today.AddDays(-6)),   // on time
            Order(2, "Closed", planned: Today.AddDays(-5), actual: Today.AddDays(-2)),      // late
            Order(3, "Completed", planned: null, actual: Today.AddDays(-1)),                 // finished, but nothing to compare with
            Order(4, "InProgress", planned: Today.AddDays(-1), actual: null),                // open and overdue
            Order(5, "Released", planned: Today.AddDays(4), actual: null),                   // open, not overdue
            Order(6, "Draft", planned: Today.AddDays(-9), actual: null),                     // drafts are not open work
            Order(7, "Cancelled", planned: Today.AddDays(-9), actual: null));
        ctx.ProductionDowntimeEvents.AddRange(
            new ProductionDowntimeEvent { ProductionDowntimeEventId = 1, ProductionRunId = 1, ProductionWorkCenterId = 1, StartTime = Today.AddDays(-3), EndTime = Today.AddDays(-3).AddHours(2) },
            new ProductionDowntimeEvent { ProductionDowntimeEventId = 2, ProductionRunId = 1, ProductionWorkCenterId = 1, StartTime = Today.AddDays(-2), EndTime = Today.AddDays(-2).AddMinutes(90) },
            new ProductionDowntimeEvent { ProductionDowntimeEventId = 3, ProductionRunId = 1, ProductionWorkCenterId = 1, StartTime = Today.AddDays(-200), EndTime = Today.AddDays(-200).AddHours(50) });   // outside the period
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(3m, Tile(d, "po_completed").Value);
        Assert.Equal(50.0m, Tile(d, "po_on_time_rate").Value);   // 1 of the 2 orders that have both dates
        Assert.Equal(1m, Tile(d, "po_overdue_open").Value);
        Assert.Equal(3.5m, Tile(d, "production_downtime_hours").Value);
    }

    private static ProductionOrder Order(int id, string status, DateTime? planned, DateTime? actual) =>
        new() { ProductionOrderId = id, OrderNumber = "PO-" + id, MaterialItemId = 1, ProductionWorkCenterId = 1, PlannedQuantity = 100 + id, Status = status, PlannedFinishDate = planned, ActualFinishDate = actual, IsActive = true };

    [Fact]
    public async Task Documents_CountsTotalReviewAndRejected_ForActiveDocumentsOnly()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Documents_CountsTotalReviewAndRejected_ForActiveDocumentsOnly));
        ctx.EngineeringDocuments.AddRange(
            Doc(1, "Approved"), Doc(2, "Submitted"), Doc(3, "Under Review"), Doc(4, "Rejected"), Doc(5, "Rejected"), Doc(6, "Rejected", active: false));
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(5m, Tile(d, "docs_total").Value);
        Assert.Equal(2m, Tile(d, "docs_under_review").Value);
        Assert.Equal(2m, Tile(d, "docs_rejected").Value);
    }

    private static EngineeringDocument Doc(int id, string status, bool active = true) =>
        new() { DocumentId = id, DocumentNo = "D-" + id, Title = "t" + id, Status = status, IsActive = active };

    [Fact]
    public async Task Equipment_GroupsActiveEquipmentByTheStatusValuesActuallyInTheData()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Equipment_GroupsActiveEquipmentByTheStatusValuesActuallyInTheData));
        ctx.Equipments.AddRange(Eq(1, "Active"), Eq(2, "Active"), Eq(3, "UnderMaintenance"), Eq(4, "Active", active: false));
        await ctx.SaveChangesAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(3m, Tile(d, "equipment_total").Value);
        Assert.Equal(new[] { ("Active", 2), ("UnderMaintenance", 1) }, d.EquipmentByStatus.Select(x => (x.Name, x.Count)));
    }

    private static Equipment Eq(int id, string status, bool active = true) =>
        new() { EquipmentId = id, TagNumber = "E" + id, Name = "E" + id, EquipmentType = "Pump", ProjectId = 1, DisciplineId = 1, Status = status, IsActive = active };

    [Fact]
    public async Task Reliability_FleetMeansMatchTheReliabilityService_AndStateHowManyEquipmentWereUsed()
    {
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(Reliability_FleetMeansMatchTheReliabilityService_AndStateHowManyEquipmentWereUsed));
        ctx.Equipments.AddRange(Eq(1, "Active"), Eq(2, "Active"));
        foreach (var (id, equipmentId, daysAgo) in new[] { (1, 1, 300), (2, 1, 200), (3, 1, 100), (4, 2, 50) })
            ctx.FailureRecords.Add(new FailureRecord { FailureRecordId = id, EquipmentId = equipmentId, FailureDate = Today.AddDays(-daysAgo), FailureMode = "Leakage", IsActive = true });
        await ctx.SaveChangesAsync();
        var fleet = await new ReliabilityService(ctx).GetFleetReliabilityAsync();

        var d = await NewDashboard(ctx).GetAsync(Today.AddDays(-29), Today);

        Assert.Equal(4m, Tile(d, "failure_count").Value);
        var mtbfValues = fleet.Where(f => f.MTBFHours.HasValue).Select(f => f.MTBFHours!.Value).ToList();
        var mtbf = Tile(d, "fleet_mtbf");
        if (mtbfValues.Count == 0) { Assert.Null(mtbf.Value); Assert.Equal(KpiStatuses.NoData, mtbf.Status); }
        else
        {
            Assert.Equal(Math.Round((decimal)mtbfValues.Average(), 0), mtbf.Value);
            Assert.Contains($"n={mtbfValues.Count} of {fleet.Count}", mtbf.Detail);
        }
    }
}
