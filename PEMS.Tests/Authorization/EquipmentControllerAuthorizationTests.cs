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
/// طبق «PEMS Phase 2B»: EquipmentController پیش از این فاز هیچ بررسی مجوزی نداشت. داده‌ی
/// RolePermissions برای ماژول "Equipment" از قبل در PermissionService.SeedDefaultsAsync وجود
/// داشت (فقط MaintenanceEngineer: Create/Edit) ولی هرگز به کنترلر وصل نشده بود. این تست‌ها
/// ثابت می‌کنند گیت جدید (Create + AssignFoundation) کار می‌کند - و طبق تصمیم صریح کاربر،
/// Index/Details دست‌نخورده و برای همه‌ی نقش‌ها در دسترس می‌مانند.
/// </summary>
public class EquipmentControllerAuthorizationTests
{
    private class SpyEquipmentService : IEquipmentService
    {
        public bool CreateCalled;
        public bool AssignFoundationCalled;

        public Task<List<EquipmentDto>> GetAllAsync(
            int? projectId = null, int? disciplineId = null, string? equipmentType = null,
            string? status = null, string? q = null) => Task.FromResult(new List<EquipmentDto>());

        public Task<PagedResult<EquipmentDto>> GetPagedAsync(
            int page = 1, int pageSize = 50, int? projectId = null, int? disciplineId = null,
            string? equipmentType = null, string? status = null, string? q = null) =>
            Task.FromResult(new PagedResult<EquipmentDto> { Page = page, PageSize = pageSize });

        public Task<EquipmentDashboardDto> GetDashboardAsync(int? projectId = null) => Task.FromResult(new EquipmentDashboardDto());

        public Task<int> CreateAsync(Equipment equipment)
        {
            CreateCalled = true;
            return Task.FromResult(1);
        }

        public Task<EquipmentPassportDto?> GetPassportAsync(int equipmentId) => throw new NotImplementedException();

        public Task<(bool Success, string? Error)> AssignFoundationAsync(
            int equipmentId, int? functionalLocationId, int? parentEquipmentId, DateTime? installationDate, string? currentUserName)
        {
            AssignFoundationCalled = true;
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private class ThrowingFunctionalLocationService : IFunctionalLocationService
    {
        public Task<List<FunctionalLocationListDto>> GetListAsync() => throw new NotImplementedException();
        public Task<PagedResult<FunctionalLocationListDto>> GetPagedListAsync(int page, int pageSize) => throw new NotImplementedException();
        public Task<List<FunctionalLocationPickerItemDto>> GetPickerListAsync() => throw new NotImplementedException();
        public Task<string?> GetBreadcrumbAsync(int? functionalLocationId) => throw new NotImplementedException();
        public Task<(bool Success, string? Error, int? Id)> CreateAsync(string code, string name, int? parentId, string? plant,
            string? area, string? unit, string? description, string? currentUserName) => throw new NotImplementedException();
        public Task<(bool Success, string? Error)> UpdateAsync(int id, string code, string name, int? parentId, string? plant,
            string? area, string? unit, string? description, string? currentUserName) => throw new NotImplementedException();
        public Task<TreeNodePage> GetTreeNodesAsync(int? parentId, string? query, int page, int pageSize) => throw new NotImplementedException();
        public Task<List<int>> GetDescendantIdsAsync(IEnumerable<int> functionalLocationIds, bool includeSelf) => throw new NotImplementedException();
    }

    private class ThrowingReliabilityService : IReliabilityService
    {
        public Task<List<EquipmentReliabilityDto>> GetFleetReliabilityAsync() => throw new NotImplementedException();
        public Task<EquipmentReliabilityDto?> GetEquipmentReliabilityAsync(int equipmentId) => throw new NotImplementedException();
    }

    private class ThrowingInspectionService : IInspectionService
    {
        public Task<PagedResult<InspectionListItemDto>> GetPagedFleetAsync(int page, int pageSize) => throw new NotImplementedException();
        public Task<List<InspectionListItemDto>> GetForEquipmentAsync(int equipmentId) => throw new NotImplementedException();
        public Task<(bool Success, string? Error, int? InspectionRecordId)> CreateAsync(int equipmentId, string inspectionType,
            DateTime inspectionDate, string? inspector, decimal? measurementValue, string? measurementUnit, decimal? lowerLimit,
            decimal? upperLimit, string? recommendation, DateTime? nextInspectionDate, string? description) => throw new NotImplementedException();
    }

    private class ThrowingEquipmentClassificationService : IEquipmentClassificationService
    {
        public Task<ClassificationDashboardDto> GetDashboardAsync() => throw new NotImplementedException();
        public Task<ReviewQueuePageDto> GetReviewQueueAsync(int page, int pageSize, string statusFilter = "PENDING") => throw new NotImplementedException();
        public Task<ReviewQueueItemDto?> GetPendingSuggestionForEquipmentAsync(int equipmentId) => throw new NotImplementedException();
        public Task<ClassificationExplanationDto?> ExplainClassificationAsync(int equipmentId) => throw new NotImplementedException();
        public Task<BackfillResultDto> BackfillAsync() => throw new NotImplementedException();
        public Task<bool> AcceptSuggestionAsync(int suggestionId, string reviewedBy, string? comment) => throw new NotImplementedException();
        public Task<bool> RejectSuggestionAsync(int suggestionId, string reviewedBy, string? comment) => throw new NotImplementedException();
    }

    private class ThrowingMaintenanceIntelligenceService : IMaintenanceIntelligenceService
    {
        public Task<EquipmentMaintenanceIntelligenceDto?> GetForEquipmentAsync(int equipmentId) => throw new NotImplementedException();
    }

    private static async Task<(EquipmentController Controller, SpyEquipmentService Equipment)> CreateController(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"EquipmentAuth_{dbName}");
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

        var equipmentService = new SpyEquipmentService();
        var controller = new EquipmentController(
            equipmentService, ctx, new ThrowingFunctionalLocationService(), currentUser,
            new ThrowingReliabilityService(), new ThrowingInspectionService(),
            new ThrowingEquipmentClassificationService(), new ThrowingMaintenanceIntelligenceService(),
            permissionService, new PEMS.Persistence.Services.BarcodeService(ctx)).WithTempData();

        return (controller, equipmentService);
    }

    // ==============================================================================
    // Create — طبق سید موجود فقط MaintenanceEngineer مجاز است
    // ==============================================================================

    [Fact]
    public async Task CreateGet_ViewerRole_Denied()
    {
        var (controller, _) = await CreateController(nameof(CreateGet_ViewerRole_Denied), SystemRoles.Viewer);

        var result = await controller.Create();

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task CreateGet_MaintenanceEngineer_Authorized_ReturnsView()
    {
        var (controller, _) = await CreateController(nameof(CreateGet_MaintenanceEngineer_Authorized_ReturnsView), SystemRoles.MaintenanceEngineer);

        var result = await controller.Create();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task CreatePost_ViewerRole_Denied_NoEquipmentCreated()
    {
        var (controller, equipment) = await CreateController(nameof(CreatePost_ViewerRole_Denied_NoEquipmentCreated), SystemRoles.Viewer);

        var result = await controller.Create(new Equipment { TagNumber = "EQ-X", Name = "EQ-X", EquipmentType = "Pump" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(equipment.CreateCalled);
    }

    [Fact]
    public async Task CreatePost_MaintenanceEngineer_Authorized_CreatesEquipment()
    {
        var (controller, equipment) = await CreateController(nameof(CreatePost_MaintenanceEngineer_Authorized_CreatesEquipment), SystemRoles.MaintenanceEngineer);

        var result = await controller.Create(new Equipment { TagNumber = "EQ-X", Name = "EQ-X", EquipmentType = "Pump" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(equipment.CreateCalled);
    }

    // ==============================================================================
    // AssignFoundation — طبق سید موجود، عملیات "Edit"
    // ==============================================================================

    [Fact]
    public async Task AssignFoundation_ViewerRole_Denied_ServiceNeverInvoked()
    {
        var (controller, equipment) = await CreateController(nameof(AssignFoundation_ViewerRole_Denied_ServiceNeverInvoked), SystemRoles.Viewer);

        var result = await controller.AssignFoundation(1, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(equipment.AssignFoundationCalled);
    }

    [Fact]
    public async Task AssignFoundation_MaintenanceEngineer_Authorized_ServiceInvoked()
    {
        var (controller, equipment) = await CreateController(nameof(AssignFoundation_MaintenanceEngineer_Authorized_ServiceInvoked), SystemRoles.MaintenanceEngineer);

        var result = await controller.AssignFoundation(1, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(equipment.AssignFoundationCalled);
    }

    // ==============================================================================
    // Index — طبق تصمیم صریح کاربر: عمداً دست‌نخورده، برای همه‌ی نقش‌ها در دسترس
    // (Details از این پوشش مستثنی است چون به چند سرویس دیگر - Reliability/Inspection/
    // Classification/MaintenanceIntelligence - وابسته است که مستقیماً هدف این فاز نیستند)
    // ==============================================================================

    [Fact]
    public async Task Index_ViewerRole_StillAccessible_NoRegressionFromNewGates()
    {
        var (controller, _) = await CreateController(nameof(Index_ViewerRole_StillAccessible_NoRegressionFromNewGates), SystemRoles.Viewer);

        var result = await controller.Index(null, null, null, null, null);

        Assert.IsType<ViewResult>(result);
    }
}
