using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0 / شناسه‌ها (بدون Migration): شماره‌ی PO / قرارداد / بسته‌ی تدارکات دستی بودند و هیچ اعتبارسنجی یا یکتایی‌ای نداشتند. تا زمان
/// تصویب ایندکس‌های یکتای دیتابیس، بررسی در لایه‌ی سرویس انجام می‌شود (محافظ رقابت هم‌زمان دقیق نیست - محدودیت مستند).
/// </summary>
public class IdentifierValidationTests
{
    private static PEMSDbContext NewCtx(string name) => PemsDbContextFactory.CreateInMemory($"IdP0_{name}");

    // -------------------------------------------------------------- PO
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PurchaseOrder_BlankNumber_IsRejected(string number)
    {
        var ctx = NewCtx(nameof(PurchaseOrder_BlankNumber_IsRejected) + number.Length);
        var service = new PurchaseOrderService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(number, 1, 1, null, null, 10m));
        Assert.Equal(0, await ctx.PurchaseOrders.CountAsync());
    }

    [Fact]
    public async Task PurchaseOrder_DuplicateNumberInTheSameProject_IgnoringCaseAndSpaces_IsRejected()
    {
        var ctx = NewCtx(nameof(PurchaseOrder_DuplicateNumberInTheSameProject_IgnoringCaseAndSpaces_IsRejected));
        var service = new PurchaseOrderService(ctx);
        await service.CreateAsync("PO-100", 1, 1, null, null, 10m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("  po-100 ", 1, 2, null, null, 20m));

        Assert.Equal(1, await ctx.PurchaseOrders.CountAsync());
    }

    [Fact]
    public async Task PurchaseOrder_SameNumberInADifferentProject_IsAllowed_AndNegativeValueIsRejected()
    {
        var ctx = NewCtx(nameof(PurchaseOrder_SameNumberInADifferentProject_IsAllowed_AndNegativeValueIsRejected));
        var service = new PurchaseOrderService(ctx);
        await service.CreateAsync("PO-100", 1, 1, null, null, 10m);

        await service.CreateAsync("PO-100", 2, 1, null, null, 10m);                         // پروژه‌ی دیگر: مجاز (همان معنای مسیر Import)
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("PO-200", 1, 1, null, null, -1m));

        Assert.Equal(2, await ctx.PurchaseOrders.CountAsync());
        Assert.All(ctx.PurchaseOrders, po => Assert.Equal("Draft", po.Status));            // رفتار Draft قبلی دست‌نخورده
    }

    [Fact]
    public async Task PurchaseOrderController_Create_Duplicate_RedirectsToCreateWithAnError_NoSecondPo()
    {
        var ctx = NewCtx(nameof(PurchaseOrderController_Create_Duplicate_RedirectsToCreateWithAnError_NoSecondPo));
        var service = new PurchaseOrderService(ctx);
        await service.CreateAsync("PO-100", 1, 1, null, null, 10m);
        var access = new AccessControlService(ctx);
        var controller = new PurchaseOrderController(service, new FakePermissionService(), new FakeCurrentUserService { UserId = 1, Roles = new[] { "ContractManager" } }, ctx, access).WithTempData();

        var result = await controller.Create("PO-100", 1, 1, null, null, 10m);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrderController.Create), redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Equal(1, await ctx.PurchaseOrders.CountAsync());
    }

    // -------------------------------------------------------------- Contract
    [Fact]
    public async Task Contract_BlankDuplicateNegativeValueAndInvertedDates_AreRejected()
    {
        var ctx = NewCtx(nameof(Contract_BlankDuplicateNegativeValueAndInvertedDates_AreRejected));
        var service = new ContractService(ctx);
        await service.CreateAsync("CT-1", 1, 1, null, 100m, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("  ", 1, 1, null, 1m, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(" CT-1 ", 1, 1, null, 1m, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("CT-2", 1, 1, null, -5m, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("CT-3", 1, 1, null, 5m, DateTime.Today, DateTime.Today.AddDays(-1)));

        Assert.Equal(1, await ctx.Contracts.CountAsync());
    }

    [Fact]
    public async Task Contract_ValidCreate_StillCreatesADraft_WithATrimmedNumber()
    {
        var ctx = NewCtx(nameof(Contract_ValidCreate_StillCreatesADraft_WithATrimmedNumber));
        var service = new ContractService(ctx);

        var id = await service.CreateAsync(" CT-9 ", 1, 1, "scope", 500m, DateTime.Today, DateTime.Today.AddYears(1));

        var contract = await ctx.Contracts.SingleAsync(c => c.ContractId == id);
        Assert.Equal("CT-9", contract.ContractNo);
        Assert.Equal("Draft", contract.Status);
    }

    // -------------------------------------------------------------- Procurement package
    [Fact]
    public async Task Package_BlankAndDuplicateNumbers_AreRejected_ValidOneStillCreatesAnOpenPackage()
    {
        var ctx = NewCtx(nameof(Package_BlankAndDuplicateNumbers_AreRejected_ValidOneStillCreatesAnOpenPackage));
        var service = new ProcurementPackageService(ctx);
        await service.CreateAsync("PP-1", 1, null, "Pumps", null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("   ", 1, null, "x", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(" PP-1 ", 1, null, "y", null));

        var package = await ctx.ProcurementPackages.SingleAsync();
        Assert.Equal("Open", package.Status);
        Assert.Equal("MR", package.Stage);
    }
}
