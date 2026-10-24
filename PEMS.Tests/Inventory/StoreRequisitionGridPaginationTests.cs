using PEMS.Web.Areas.Warehouse.Controllers;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Inventory;

/// <summary>
/// طبق «PEMS Phase 6B — Wave A: StoreRequisition Grid». قبلاً کل جدول فیلترشده (با ۲ Include)
/// با ToListAsync (بدون Skip/Take) بارگذاری می‌شد. از P0-09 (D-11، ۲۰۲۶-۱۰-۱۴) Index مجوز StoreRequisition.View می‌خواهد.
/// </summary>
public class StoreRequisitionGridPaginationTests
{
    private static (StoreRequisitionController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx) CreateController(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"StoreReqGrid_{dbName}");
        ctx.Warehouses.Add(new Warehouse { WarehouseId = 1, Code = "WH-01", Name = "Main Warehouse", IsActive = true });
        ctx.Departments.Add(new Department { DepartmentId = 1, Name = "Maintenance" });
        ctx.Employees.Add(new Employee { EmployeeId = 1, FirstName = "Test", LastName = "User", DepartmentId = 1, IsActive = true });
        ctx.SaveChanges();

        var currentUser = new FakeCurrentUserService();
        // P0-09 (D-11): Index حالا StoreRequisition.View می‌خواهد؛ این تست صفحه‌بندی را می‌سنجد، پس کاربر مجاز است
        // (رد بدون View در ProcurementP0ReadAndSodDecisionTests پوشش داده شده).
        var controller = new StoreRequisitionController(ctx, null!, currentUser, new FakePermissionService(), null!);
        return (controller, ctx);
    }

    private static int _nextId = 1;

    private static StoreRequisition NewStoreRequisition(DateTime requestedDate, string status = "Draft") => new()
    {
        StoreRequisitionId = _nextId++,
        RequisitionNumber = $"SR-{_nextId:D4}",
        Status = status,
        WarehouseId = 1,
        RequestedById = 1,
        RequestedDate = requestedDate,
        IsActive = true
    };

    private static async Task<PagedResult<StoreRequisitionListDto>> RunIndex(
        StoreRequisitionController controller, string? status = null, int page = 1, int? pageSize = null) =>
        Assert.IsType<PagedResult<StoreRequisitionListDto>>(
            ((ViewResult)await controller.Index(status, page, pageSize)).Model);

    [Fact]
    public async Task Index_DefaultPageSize_Is50()
    {
        var (controller, ctx) = CreateController(nameof(Index_DefaultPageSize_Is50));
        for (var i = 0; i < 60; i++)
            ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today.AddMinutes(-i)));
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller, page: 1, pageSize: 999);

        Assert.Equal(50, result.PageSize);
        Assert.Equal(50, result.Items.Count);
        Assert.Equal(60, result.TotalCount);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Index_AllowedPageSizes_ReturnExactlyThatCount(int pageSize)
    {
        var (controller, ctx) = CreateController($"{nameof(Index_AllowedPageSizes_ReturnExactlyThatCount)}_{pageSize}");
        for (var i = 0; i < 120; i++)
            ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today.AddMinutes(-i)));
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller, page: 1, pageSize: pageSize);

        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(Math.Min(pageSize, 120), result.Items.Count);
    }

    [Fact]
    public async Task Index_InvalidPageSize_FallsBackTo50()
    {
        var (controller, ctx) = CreateController(nameof(Index_InvalidPageSize_FallsBackTo50));
        ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today));
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller, page: 1, pageSize: 13);

        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task Index_Page1AndPage2_AreStableAndDisjoint()
    {
        var (controller, ctx) = CreateController(nameof(Index_Page1AndPage2_AreStableAndDisjoint));
        for (var i = 0; i < 25; i++)
            ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today.AddMinutes(-i)));
        await ctx.SaveChangesAsync();

        var page1 = await RunIndex(controller, page: 1, pageSize: 20);
        var page2 = await RunIndex(controller, page: 2, pageSize: 20);

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(5, page2.Items.Count);

        var allIds = page1.Items.Concat(page2.Items).Select(x => x.StoreRequisitionId).ToList();
        Assert.Equal(25, allIds.Distinct().Count());
    }

    [Fact]
    public async Task Index_DeterministicOrdering_SameRequestedDate_SameOrderTwice()
    {
        var (controller, ctx) = CreateController(nameof(Index_DeterministicOrdering_SameRequestedDate_SameOrderTwice));
        var sameDate = DateTime.Today;
        for (var i = 0; i < 10; i++)
            ctx.StoreRequisitions.Add(NewStoreRequisition(sameDate));
        await ctx.SaveChangesAsync();

        var first = await RunIndex(controller, page: 1, pageSize: 20);
        var second = await RunIndex(controller, page: 1, pageSize: 20);

        Assert.Equal(first.Items.Select(x => x.StoreRequisitionId), second.Items.Select(x => x.StoreRequisitionId));
    }

    [Fact]
    public async Task Index_StatusFilter_StillWorksAfterPageSizeChange()
    {
        var (controller, ctx) = CreateController(nameof(Index_StatusFilter_StillWorksAfterPageSizeChange));
        ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today, status: "Draft"));
        ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today, status: "Approved"));
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller, status: "Draft");

        Assert.Single(result.Items);
        Assert.Equal("Draft", result.Items[0].Status);
    }

    [Fact]
    public async Task Index_InactiveRequisitions_AreExcluded()
    {
        var (controller, ctx) = CreateController(nameof(Index_InactiveRequisitions_AreExcluded));
        var active = NewStoreRequisition(DateTime.Today);
        var inactive = NewStoreRequisition(DateTime.Today);
        inactive.IsActive = false;
        ctx.StoreRequisitions.Add(active);
        ctx.StoreRequisitions.Add(inactive);
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller);

        Assert.Single(result.Items);
        Assert.Equal(active.StoreRequisitionId, result.Items[0].StoreRequisitionId);
    }

    [Fact]
    public async Task Index_EmptyDataset_ReturnsEmptyPagedResult()
    {
        var (controller, _) = CreateController(nameof(Index_EmptyDataset_ReturnsEmptyPagedResult));

        var result = await RunIndex(controller);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Index_ReturnsWarehouseCodeAndRequestedByName_ViaNavigation()
    {
        var (controller, ctx) = CreateController(nameof(Index_ReturnsWarehouseCodeAndRequestedByName_ViaNavigation));
        ctx.StoreRequisitions.Add(NewStoreRequisition(DateTime.Today));
        await ctx.SaveChangesAsync();

        var result = await RunIndex(controller);

        Assert.Single(result.Items);
        Assert.Equal("WH-01", result.Items[0].WarehouseCode);
        Assert.Equal("Test User", result.Items[0].RequestedByName);
    }
}
