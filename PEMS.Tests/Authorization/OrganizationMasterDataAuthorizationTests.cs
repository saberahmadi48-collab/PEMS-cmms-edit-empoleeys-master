using PEMS.Web.Areas.Warehouse.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS — P1 Organization &amp; Master Data Authorization Hardening»: TeamController/
/// CrewController/DisciplineController/InventoryMasterDataController/MaintenanceMasterDataController
/// هیچ‌کدام بررسی مجوزی نداشتند - CreateTeam/AddMember/RemoveMember/MoveMember/AddTask/
/// SetTaskStatus/Create*/Toggle*Active برای هر کاربر احرازهویت‌شده‌ای باز بودند. همون الگوی
/// ImportPipelineAuthorizationTests.cs/AIPipelineAuthorizationTests.cs.
/// </summary>
public class OrganizationMasterDataAuthorizationTests
{
    private static async Task<(PEMSDbContext Ctx, IPermissionService Permissions, ICurrentUserService CurrentUser)>
        CreateInfra(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"OrgMasterDataAuth_{dbName}");
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
        return (ctx, permissionService, currentUser);
    }

    private class FakeTeamService : ITeamService
    {
        public bool AddOrReactivateCalled;
        public bool RemoveCalled;
        public bool MoveCalled;
        public Task AddOrReactivateMemberAsync(int teamId, int employeeId, int roleId) { AddOrReactivateCalled = true; return Task.CompletedTask; }
        public Task RemoveMemberAsync(int teamMemberId) { RemoveCalled = true; return Task.CompletedTask; }
        public Task MoveMemberAsync(int employeeId, int fromTeamId, int toTeamId, int roleId) { MoveCalled = true; return Task.CompletedTask; }
    }

    private class FakeCrewService : ICrewService
    {
        public bool CreateCalled;
        public bool AddOrReactivateCalled;
        public bool RemoveCalled;
        public Task<(bool Success, string? Error, int? CrewId)> CreateAsync(string code, string name, int? workOrderId)
        { CreateCalled = true; return Task.FromResult<(bool, string?, int?)>((true, null, 1)); }
        public Task AddOrReactivateMemberAsync(int crewId, int employeeId, int roleId) { AddOrReactivateCalled = true; return Task.CompletedTask; }
        public Task RemoveMemberAsync(int crewMemberId) { RemoveCalled = true; return Task.CompletedTask; }
    }

    private class FakeDisciplineWorkspaceService : IDisciplineWorkspaceService
    {
        public bool AddTaskCalled;
        public bool SetTaskStatusCalled;
        public Task<List<DisciplineSummaryDto>> GetSummaryListAsync() => Task.FromResult(new List<DisciplineSummaryDto>());
        public Task<DisciplineWorkspaceDto?> GetWorkspaceAsync(int disciplineId, int? projectId = null) => Task.FromResult<DisciplineWorkspaceDto?>(null);
        public Task<int> AddTaskAsync(int disciplineId, int? projectId, string title, string? description, string type, int? responsibleEmployeeId, DateTime? dueDate)
        { AddTaskCalled = true; return Task.FromResult(1); }
        public Task SetTaskStatusAsync(int taskId, string status) { SetTaskStatusCalled = true; return Task.CompletedTask; }
    }

    // ================= Team =================

    [Fact]
    public async Task Team_GetTeamMembers_WithoutViewPermission_ReturnsForbid()
    {
        // ContractManager/QAManager هیچ Grant ای روی "Team" ندارند - حتی View
        var (ctx, perms, user) = await CreateInfra(nameof(Team_GetTeamMembers_WithoutViewPermission_ReturnsForbid), role: SystemRoles.QAManager);
        var controller = new TeamController(ctx, new FakeTeamService(), new OrganizationStructureService(ctx), user, perms).WithTempData();

        var result = await controller.GetTeamMembers(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Team_GetTeamMembers_DisciplineEngineer_HasViewOnly_CanRead()
    {
        // DisciplineEngineer باید بتواند این AJAX را برای بازبینی WorkOrder فراخوانی کند (View)،
        // حتی اگرچه اجازه‌ی مدیریت ترکیب تیم (Edit) را ندارد.
        var (ctx, perms, user) = await CreateInfra(nameof(Team_GetTeamMembers_DisciplineEngineer_HasViewOnly_CanRead), role: SystemRoles.DisciplineEngineer);
        var controller = new TeamController(ctx, new FakeTeamService(), new OrganizationStructureService(ctx), user, perms).WithTempData();

        var result = await controller.GetTeamMembers(1);

        Assert.IsType<JsonResult>(result);
    }

    // ================= Crew =================

    // ================= Inventory Master Data =================

    // ================= Maintenance Master Data =================

    [Fact]
    public async Task MaintenanceMasterData_CreateGroup_WithPermission_Creates()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(MaintenanceMasterData_CreateGroup_WithPermission_Creates), role: SystemRoles.EngineeringSupervisor);
        var ops = new PEMS.Web.Services.MaintenanceMasterDataOperations(ctx, new OrganizationStructureService(ctx));

        var result = await ops.CreateGroupAsync("G1", "Group 1", 1, null);

        Assert.True(result.Success);
        Assert.Single(ctx.MaintenanceGroups);
    }

    // ================= Dynamic Maintenance Organization (Team nesting / Position / Employee Assignment) =================
    // طبق «PEMS Dynamic Maintenance Organization»: همون الگوی دقیق بالا - فقط Permission، نه
    // Scope دیسیپلینی (که با SystemAdmin دور زده می‌شود؛ Scope واقعی تست نمی‌شود این‌جا).

    [Fact]
    public async Task Team_OrganizationTree_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Team_OrganizationTree_WithoutViewPermission_ReturnsForbid), role: null);
        var controller = new TeamController(ctx, new FakeTeamService(), new OrganizationStructureService(ctx), user, perms).WithTempData();

        var result = await controller.OrganizationTree(null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Team_OrganizationChart_WithoutViewPermission_ReturnsForbid()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Team_OrganizationChart_WithoutViewPermission_ReturnsForbid), role: null);
        var controller = new TeamController(ctx, new FakeTeamService(), new OrganizationStructureService(ctx), user, perms).WithTempData();

        var result = await controller.OrganizationChart(null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Team_OrganizationChart_WithViewPermission_ReturnsView()
    {
        var (ctx, perms, user) = await CreateInfra(nameof(Team_OrganizationChart_WithViewPermission_ReturnsView), role: SystemRoles.SystemAdmin);
        var controller = new TeamController(ctx, new FakeTeamService(), new OrganizationStructureService(ctx), user, perms).WithTempData();

        var result = await controller.OrganizationChart(null);

        Assert.IsType<ViewResult>(result);
    }

    // ================= Phase 6B: Team Details / Workforce Management =================

    private static async Task<(Discipline Discipline, MaintenanceGroup Group, Team Team)> SeedTeamAsync(PEMSDbContext ctx)
    {
        var discipline = new Discipline { Code = "MECH", Name = "Mechanical", IsActive = true, CreatedDate = DateTime.Now };
        ctx.Disciplines.Add(discipline);
        await ctx.SaveChangesAsync();
        var group = new MaintenanceGroup { Code = "G1", Name = "Group 1", DisciplineId = discipline.DisciplineId, IsActive = true, CreatedDate = DateTime.Now };
        ctx.MaintenanceGroups.Add(group);
        await ctx.SaveChangesAsync();
        var team = new Team { Code = "T1", Name = "Team 1", MaintenanceGroupId = group.MaintenanceGroupId, IsActive = true, CreatedDate = DateTime.Now };
        ctx.Teams.Add(team);
        await ctx.SaveChangesAsync();
        return (discipline, group, team);
    }

}
