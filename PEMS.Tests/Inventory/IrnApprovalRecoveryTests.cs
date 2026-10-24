using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Warehouse.Controllers;

namespace PEMS.Tests.Inventory;

/// <summary>
/// P0 / F-07 - تایید IRN: «گام گردش‌کار» و «Post موجودی» دو commit جدا هستند. این تست‌ها ثابت می‌کنند حالت نیمه‌کاره
/// (گردش‌کار انجام شده، Post شکست خورده) قابل بازیابی است، Post هرگز دوبار انجام نمی‌شود، Instance کهنه‌ی نوبت قبلی هرگز
/// Post جدید را تعیین نمی‌کند، و شکست شروع گردش‌کار بعد از Disposition به حالت قابل‌تکرار (Submitted) جبران می‌شود.
/// </summary>
public class IrnApprovalRecoveryTests
{
    private const string BusinessObjectType = "IrnReceiptDisposition";

    private static (IrnReceiptController Controller, PEMSDbContext Ctx, FakeWorkflowRuntime Runtime) CreateController(string db)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"IrnRecovery_{db}");
        ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "Test Vendor", IsActive = true });
        ctx.Warehouses.Add(new Warehouse { WarehouseId = 1, Code = "WH-01", Name = "Main", IsActive = true });
        ctx.Departments.Add(new Department { DepartmentId = 1, Name = "Warehouse" });
        ctx.Employees.Add(new Employee { EmployeeId = 1, FirstName = "Receiving", LastName = "Clerk", DepartmentId = 1, IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = 1, EmployeeId = 1, UserName = "clerk" });
        ctx.MaterialItems.Add(new MaterialItem { MaterialItemId = 1, MaterialCode = "MAT-001", Description = "Test Material", IsActive = true, IsStockItem = true });
        ctx.SaveChanges();

        var currentUser = new FakeCurrentUserService { UserId = 1, Roles = new[] { "QAManager" } };
        var runtime = new FakeWorkflowRuntime();
        var controller = new IrnReceiptController(ctx, new IrnReceiptService(ctx), currentUser, new FakePermissionService(), new FakeNumberingService(), runtime).WithTempData();
        return (controller, ctx, runtime);
    }

    private static async Task<(int ReceiptId, int LineId)> SeedSubmittedAsync(PEMSDbContext ctx, decimal qty = 10)
    {
        var receipt = new IrnReceipt { IrnNumber = "IRN-2026-0099", Status = "Submitted", VendorId = 1, WarehouseId = 1, ReceivedById = 1, IsActive = true };
        receipt.Lines.Add(new IrnReceiptLine { MaterialItemId = 1, ReceivedQuantity = qty, IsActive = true });
        ctx.IrnReceipts.Add(receipt);
        await ctx.SaveChangesAsync();
        return (receipt.IrnReceiptId, (await ctx.IrnReceiptLines.FirstAsync()).IrnReceiptLineId);
    }

    private static async Task<int> DispositionAsync(IrnReceiptController controller, PEMSDbContext ctx)
    {
        var (receiptId, lineId) = await SeedSubmittedAsync(ctx);
        Assert.IsType<RedirectToActionResult>(await controller.Disposition(receiptId, new[] { lineId }, new[] { 10m }, new[] { 0m }, new[] { 0m }, null));
        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);
        return receiptId;
    }

    /// <summary>شبیه‌سازی حالت نیمه‌کاره: گام گردش‌کار انجام شده (Instance تمام‌شده) ولی IRN هنوز PendingApproval است.</summary>
    private static async Task CompleteWorkflowOnlyAsync(FakeWorkflowRuntime runtime, int receiptId, string action)
    {
        var instance = await runtime.GetInstanceAsync(BusinessObjectType, receiptId);
        await runtime.ExecuteActionAsync(instance!.WorkflowInstanceId, action, 1, new[] { "QAManager" }, null, null, null);
    }

    // ------------------------------------------------------------------ بازیابی Post بعد از گام گردش‌کار
    [Fact]
    public async Task Approve_AfterTheWorkflowAlreadyCompleted_PostsTheStockOnce_AndCloses()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Approve_AfterTheWorkflowAlreadyCompleted_PostsTheStockOnce_AndCloses));
        var id = await DispositionAsync(controller, ctx);
        await CompleteWorkflowOnlyAsync(runtime, id, "Approve");

        var result = await controller.Approve(id, "Approve", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Closed", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal(10, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
        Assert.Equal(1, await ctx.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Approve_RetryAfterRecovery_NeverPostsTwice()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Approve_RetryAfterRecovery_NeverPostsTwice));
        var id = await DispositionAsync(controller, ctx);
        await CompleteWorkflowOnlyAsync(runtime, id, "Approve");
        await controller.Approve(id, "Approve", null);

        var again = await controller.Approve(id, "Approve", null);

        Assert.IsType<RedirectToActionResult>(again);
        Assert.Equal(10, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
        Assert.Equal(1, await ctx.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Approve_RecoveryFollowsTheRecordedWorkflowOutcome_NotTheRequestParameter()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Approve_RecoveryFollowsTheRecordedWorkflowOutcome_NotTheRequestParameter));
        var id = await DispositionAsync(controller, ctx);
        await CompleteWorkflowOnlyAsync(runtime, id, "Reject");          // گردش‌کار «رد» ثبت کرده است

        await controller.Approve(id, "Approve", null);                    // درخواست جدید ادعای «Approve» می‌کند

        Assert.Equal("Submitted", (await ctx.IrnReceipts.SingleAsync()).Status);     // رد، نه Post
        Assert.Equal(0, await ctx.MaterialStocks.CountAsync());
    }

    [Fact]
    public async Task Approve_WhenPostingFailsTheFirstTime_CanBeRetriedAfterTheCauseIsFixed()
    {
        var (controller, ctx, _) = CreateController(nameof(Approve_WhenPostingFailsTheFirstTime_CanBeRetriedAfterTheCauseIsFixed));
        var id = await DispositionAsync(controller, ctx);
        // علت شکست: دو ردیف موجودی بدون مکان برای یک ماده/انبار (مبهم) - Post با پیام صریح رد می‌شود
        ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = 1, WarehouseId = 1, OnHandQuantity = 0, IsActive = true });
        ctx.MaterialStocks.Add(new MaterialStock { MaterialItemId = 1, WarehouseId = 1, OnHandQuantity = 0, IsActive = true });
        await ctx.SaveChangesAsync();

        await controller.Approve(id, "Approve", null);

        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);       // نیمه‌کاره: گردش‌کار انجام شد، Post نه
        Assert.Equal(0, await ctx.InventoryTransactions.CountAsync());
        Assert.NotNull(controller.TempData["Error"]);

        ctx.MaterialStocks.Remove(await ctx.MaterialStocks.OrderByDescending(s => s.MaterialStockId).FirstAsync());
        await ctx.SaveChangesAsync();
        await controller.Approve(id, "Approve", null);                                       // بازیابی: فقط Post تکرار می‌شود

        Assert.Equal("Closed", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal(10, (await ctx.MaterialStocks.SingleAsync()).OnHandQuantity);
        Assert.Equal(1, await ctx.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Approve_WithAStaleInstanceFromAnEarlierCycle_IsNotUsedToPost()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Approve_WithAStaleInstanceFromAnEarlierCycle_IsNotUsedToPost));
        var id = await DispositionAsync(controller, ctx);
        await CompleteWorkflowOnlyAsync(runtime, id, "Approve");
        // نوبت جاری PendingApproval «بعد» از شروع Instance تمام‌شده بوده است ⇒ Instance متعلق به نوبت قبل است
        var history = await ctx.IrnReceiptStatusHistories.SingleAsync(h => h.ToStatus == "PendingApproval");
        history.ChangedDate = DateTime.Now.AddHours(1);
        await ctx.SaveChangesAsync();

        await controller.Approve(id, "Approve", null);

        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal(0, await ctx.MaterialStocks.CountAsync());
        Assert.Equal(0, await ctx.InventoryTransactions.CountAsync());
    }

    [Fact]
    public async Task Approve_WithoutPermission_IsStillForbidden_EvenForARecoverableIrn()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Approve_WithoutPermission_IsStillForbidden_EvenForARecoverableIrn));
        var id = await DispositionAsync(controller, ctx);
        await CompleteWorkflowOnlyAsync(runtime, id, "Approve");
        var denied = new IrnReceiptController(ctx, new IrnReceiptService(ctx), new FakeCurrentUserService { UserId = 1, Roles = new[] { "Viewer" } },
            new FakePermissionService { AllowAll = false }, new FakeNumberingService(), runtime).WithTempData();

        var result = await denied.Approve(id, "Approve", null);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal(0, await ctx.MaterialStocks.CountAsync());
    }

    // ------------------------------------------------------------------ شکست شروع گردش‌کار بعد از Disposition
    [Fact]
    public async Task Disposition_WhenTheWorkflowCannotStart_IsCompensated_AndCanBeRecordedAgain()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Disposition_WhenTheWorkflowCannotStart_IsCompensated_AndCanBeRecordedAgain));
        var (receiptId, lineId) = await SeedSubmittedAsync(ctx);
        runtime.ThrowOnStart = true;

        var result = await controller.Disposition(receiptId, new[] { lineId }, new[] { 10m }, new[] { 0m }, new[] { 0m }, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Submitted", (await ctx.IrnReceipts.SingleAsync()).Status);             // نه PendingApproval بدون Instance
        Assert.Equal(0, await ctx.MaterialStocks.CountAsync());
        Assert.Equal(0, await ctx.InventoryTransactions.CountAsync());
        Assert.NotNull(controller.TempData["Error"]);

        runtime.ThrowOnStart = false;
        await controller.Disposition(receiptId, new[] { lineId }, new[] { 10m }, new[] { 0m }, new[] { 0m }, null);
        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal("Running", (await runtime.GetInstanceAsync(BusinessObjectType, receiptId))!.Status);
    }

    [Fact]
    public async Task Disposition_ReusesAnOrphanRunningInstance_InsteadOfStartingASecond()
    {
        var (controller, ctx, runtime) = CreateController(nameof(Disposition_ReusesAnOrphanRunningInstance_InsteadOfStartingASecond));
        var (receiptId, lineId) = await SeedSubmittedAsync(ctx);
        var orphanId = await runtime.StartInstanceAsync("IRN", BusinessObjectType, receiptId, "Inventory", null, 1);

        await controller.Disposition(receiptId, new[] { lineId }, new[] { 10m }, new[] { 0m }, new[] { 0m }, null);

        Assert.Equal("PendingApproval", (await ctx.IrnReceipts.SingleAsync()).Status);
        Assert.Equal(orphanId, (await runtime.GetInstanceAsync(BusinessObjectType, receiptId))!.WorkflowInstanceId);
    }
}
