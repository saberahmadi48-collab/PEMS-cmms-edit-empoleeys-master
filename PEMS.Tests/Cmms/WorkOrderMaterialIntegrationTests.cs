using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// B10: مسیر کامل متریال روی Work Order: ثبت مصرف → کسر موجودی (تراکنش Issue) → برگشت (تراکنش Return و کاهش هزینه) → بستن و هزینه‌ی نهایی در MaintenanceHistory.
/// حالت «Material Master خالی» در تست رندر (WorkOrderDetailsRenderTests) پوشش داده شده است.
/// </summary>
public class WorkOrderMaterialIntegrationTests : WorkOrderGovernanceTestBase
{
    [Fact]
    public async Task B10_issue_return_and_final_cost_flow_end_to_end()
    {
        var id = await CreateAsync("Corrective"); await RunToInProgressAsync(id);
        var emp = Executor.EmployeeId!.Value;
        Ctx.EmployeeLaborRates.Add(new EmployeeLaborRate { EmployeeId = emp, RateType = "Normal", Rate = 100, EffectiveFrom = DateTime.Today.AddYears(-1), IsActive = true });
        var item = new MaterialItem { MaterialCode = "SEAL-1", Description = "Mechanical seal", IsStockItem = true, IsActive = true };
        var wh = new Warehouse { Code = "WH-1", Name = "Main", IsActive = true };
        Ctx.AddRange(item, wh);
        Ctx.Users.Add(new ApplicationUser { Id = 90, UserName = "exec", NormalizedUserName = "EXEC", EmployeeId = emp, Email = "e@t.local" });
        await Ctx.SaveChangesAsync();
        Ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = item.MaterialItemId, WarehouseId = wh.WarehouseId, OnHandQuantity = 10, IsActive = true });
        await Ctx.SaveChangesAsync();

        var ctl = new WorkOrderController(Ctx, null!, new FakeCurrentUserService { UserId = 90, Roles = Executor.Roles.ToList() }, new PermissionService(Ctx, AccessControl),
            new WorkOrderLaborService(Ctx, AccessControl), Svc, new CrewService(Ctx), null!, null!, new WorkOrderSectionGuard(Ctx, AccessControl)).WithTempData();

        decimal OnHand() => Ctx.MaterialStocks.AsNoTracking().Single().OnHandQuantity;

        // 1) مصرف: موجودی کم می‌شود، تراکنش Issue و ردیف مصرف با هزینه ثبت می‌شود
        await ctl.AddSpareUsage(id, item.MaterialItemId, 4, 50, null);
        Assert.Equal(6, OnHand());
        var usage = await Ctx.MaintenanceSpareUsages.AsNoTracking().SingleAsync();
        Assert.Equal(200, usage.TotalCost);
        Assert.Single(Ctx.InventoryTransactions.Where(t => t.TransactionType == "Issue" && t.WorkOrderId == id));

        // 2) مصرف بیش از موجودی: رد با پیام مشخص، موجودی دست‌نخورده
        ctl.TempData.Clear();
        await ctl.AddSpareUsage(id, item.MaterialItemId, 50, 50, null);
        Assert.Contains("کافی", (string)ctl.TempData["Error"]!);
        Assert.Equal(6, OnHand());

        // 3) برگشت ۱ عدد: موجودی برمی‌گردد، هزینه‌ی خالص کم می‌شود
        await ctl.ReturnSpareUsage(usage.MaintenanceSpareUsageId, id, 1, "unused");
        Assert.Equal(7, OnHand());
        Assert.Equal(150, (await Ctx.MaintenanceSpareUsages.AsNoTracking().SingleAsync()).TotalCost);
        Assert.Single(Ctx.InventoryTransactions.Where(t => t.TransactionType == "Return"));

        // 4) برگشت بیش از مقدار مصرف‌شده رد می‌شود
        ctl.TempData.Clear();
        await ctl.ReturnSpareUsage(usage.MaintenanceSpareUsageId, id, 99, "x");
        Assert.NotNull(ctl.TempData["Error"]);
        Assert.Equal(7, OnHand());

        // 5) ساعت‌کار + بستن: هزینه‌ی نهایی = ساعت‌کار + قطعه‌ی خالص (۳×۵۰) و منجمد
        await ctl.AddLaborEntry(id, emp, null, DateTime.Today, 2, 0, null);
        await Do(id, "Approve", Executor, null, Data()); await Do(id, "Approve", Executor);
        await Do(id, "Approve", Verifier); await Do(id, "Approve", Approver);
        Assert.Equal("Closed", (await Wo(id)).Status);
        var history = await Ctx.MaintenanceHistories.AsNoTracking().SingleAsync(m => m.WorkOrderId == id);
        Assert.Equal(200 + 150, history.Cost);
    }
}
