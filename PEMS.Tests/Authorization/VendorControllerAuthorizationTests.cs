using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Phase 2B»: VendorController پیش از این فاز هیچ بررسی مجوزی نداشت. داده‌ی
/// RolePermissions برای ماژول "Vendors" از قبل در PermissionService.SeedDefaultsAsync وجود
/// داشت (فقط ContractManager: Create/Edit) ولی هرگز به کنترلر وصل نشده بود. این تست‌ها ثابت
/// می‌کنند گیت جدید کار می‌کند - و طبق تصمیم صریح کاربر، Index/Details دست‌نخورده و برای همه‌ی
/// نقش‌ها در دسترس می‌مانند.
/// </summary>
public class VendorControllerAuthorizationTests
{
    private class SpyVendorService : IVendorService
    {
        public bool CreateCalled;
        public bool UpdateCalled;
        public bool ToggleActiveCalled;
        public Vendor? EntityToReturn;

        public Task<List<VendorDto>> GetAllAsync() => Task.FromResult(new List<VendorDto>());

        public Task<PagedResult<VendorDto>> GetPagedAsync(int page, int pageSize) =>
            Task.FromResult(new PagedResult<VendorDto> { Page = page, PageSize = pageSize });

        public Task<VendorDto?> GetByIdAsync(int id) => Task.FromResult<VendorDto?>(new VendorDto { VendorId = id });

        public Task<Vendor?> GetEntityByIdAsync(int id) => Task.FromResult(EntityToReturn);

        public Task<int> CreateAsync(Vendor vendor)
        {
            CreateCalled = true;
            return Task.FromResult(1);
        }

        public Task UpdateAsync(Vendor vendor)
        {
            UpdateCalled = true;
            return Task.CompletedTask;
        }

        public Task ToggleActiveAsync(int id)
        {
            ToggleActiveCalled = true;
            return Task.CompletedTask;
        }

        public Task<int> FindOrCreateByNameAsync(string name) => throw new NotImplementedException();
    }

    private class ThrowingVendorRiskService : IVendorRiskService
    {
        public Task RecalculateAsync(int vendorId) => throw new InvalidOperationException("Should not be called when unauthorized");
        public Task RecalculateAllAsync() => throw new NotImplementedException();
    }

    private static async Task<(VendorController Controller, SpyVendorService Vendor)> CreateController(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"VendorAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>() };

        var vendorService = new SpyVendorService();
        var qualificationService = new VendorQualificationService(ctx);
        var controller = new VendorController(vendorService, new ThrowingVendorRiskService(), qualificationService, permissionService, currentUser, ctx).WithTempData();

        return (controller, vendorService);
    }

    // ==============================================================================
    // Create — طبق سید موجود فقط ContractManager مجاز است
    // ==============================================================================

    [Fact]
    public async Task CreateGet_ViewerRole_Denied()
    {
        var (controller, vendor) = await CreateController(nameof(CreateGet_ViewerRole_Denied), SystemRoles.Viewer);

        var result = await controller.Create();

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task CreateGet_ContractManager_Authorized_ReturnsView()
    {
        var (controller, _) = await CreateController(nameof(CreateGet_ContractManager_Authorized_ReturnsView), SystemRoles.ContractManager);

        var result = await controller.Create();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task CreatePost_ViewerRole_Denied_NoVendorCreated()
    {
        var (controller, vendor) = await CreateController(nameof(CreatePost_ViewerRole_Denied_NoVendorCreated), SystemRoles.Viewer);

        var result = await controller.Create(new Vendor { Name = "Test Vendor" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(vendor.CreateCalled);
    }

    [Fact]
    public async Task CreatePost_ContractManager_Authorized_CreatesVendor()
    {
        var (controller, vendor) = await CreateController(nameof(CreatePost_ContractManager_Authorized_CreatesVendor), SystemRoles.ContractManager);

        var result = await controller.Create(new Vendor { Name = "Test Vendor" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(vendor.CreateCalled);
    }

    // ==============================================================================
    // Edit / ToggleActive / RecalculateRisk — طبق سید موجود، "Edit" برای ContractManager
    // ==============================================================================

    [Fact]
    public async Task EditGet_ViewerRole_Denied()
    {
        var (controller, vendor) = await CreateController(nameof(EditGet_ViewerRole_Denied), SystemRoles.Viewer);
        vendor.EntityToReturn = new Vendor { VendorId = 1, Name = "V1" };

        var result = await controller.Edit(1);

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task EditPost_ViewerRole_Denied_NoUpdate()
    {
        var (controller, vendor) = await CreateController(nameof(EditPost_ViewerRole_Denied_NoUpdate), SystemRoles.Viewer);

        var result = await controller.Edit(new Vendor { VendorId = 1, Name = "V1" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(vendor.UpdateCalled);
    }

    [Fact]
    public async Task EditPost_ContractManager_Authorized_UpdatesVendor()
    {
        var (controller, vendor) = await CreateController(nameof(EditPost_ContractManager_Authorized_UpdatesVendor), SystemRoles.ContractManager);

        var result = await controller.Edit(new Vendor { VendorId = 1, Name = "V1" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(vendor.UpdateCalled);
    }

    [Fact]
    public async Task ToggleActive_ViewerRole_Denied_NoToggle()
    {
        var (controller, vendor) = await CreateController(nameof(ToggleActive_ViewerRole_Denied_NoToggle), SystemRoles.Viewer);

        var result = await controller.ToggleActive(1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(vendor.ToggleActiveCalled);
    }

    [Fact]
    public async Task ToggleActive_ContractManager_Authorized_Toggles()
    {
        var (controller, vendor) = await CreateController(nameof(ToggleActive_ContractManager_Authorized_Toggles), SystemRoles.ContractManager);

        var result = await controller.ToggleActive(1);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(vendor.ToggleActiveCalled);
    }

    [Fact]
    public async Task RecalculateRisk_ViewerRole_Denied_RiskServiceNeverInvoked()
    {
        var (controller, _) = await CreateController(nameof(RecalculateRisk_ViewerRole_Denied_RiskServiceNeverInvoked), SystemRoles.Viewer);

        var result = await controller.RecalculateRisk(1);

        Assert.IsType<RedirectToActionResult>(result);
    }

    // ==============================================================================
    // Index/Details — P0-09 (D-OPEN-02، تصمیم مالک ۲۰۲۶-۱۰-۱۴): تصمیم قبلی «همه‌ی نقش‌ها» لغو شد؛ حالا Vendors.View لازم است.
    // Viewer هیچ ردیف Vendors ندارد (سید موجود فقط ContractManager)، پس رد می‌شود.
    // ==============================================================================

    [Fact]
    public async Task Index_ViewerRole_WithoutVendorsView_Forbidden()
    {
        var (controller, _) = await CreateController(nameof(Index_ViewerRole_WithoutVendorsView_Forbidden), SystemRoles.Viewer);

        var result = await controller.Index();

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Details_ViewerRole_WithoutVendorsView_Forbidden()
    {
        var (controller, _) = await CreateController(nameof(Details_ViewerRole_WithoutVendorsView_Forbidden), SystemRoles.Viewer);

        var result = await controller.Details(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Index_And_Details_ContractManager_WithVendorsView_Accessible()
    {
        var (controller, _) = await CreateController(nameof(Index_And_Details_ContractManager_WithVendorsView_Accessible), SystemRoles.ContractManager);

        Assert.IsType<ViewResult>(await controller.Index());
        Assert.IsType<ViewResult>(await controller.Details(1));
    }
}
