using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§F/§J): ContractController پیش از این
/// اصلاً پوشش تست مجوز نداشت (§J - Test Coverage Gap). این تست‌ها فقط دروازه‌ی مجوز/دیوارهای
/// [RequirePermission] و اتصال درست به IContractWorkflowService را می‌آزمایند - منطق عمیق
/// گردش‌کار/SoD در ContractWorkflowServiceTests پوشش داده شده.
/// </summary>
public class ContractControllerAuthorizationTests
{
    private static async Task<(ContractController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx, FakeWorkflowRuntime Runtime)> CreateController(
        string dbName, string? role, int userId = 1)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"ContractCtrlAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        // طبق AccessControlScopeAndSoDTests: AuthorizeAsync (SoD-آگاه) نقش را از AspNetUserRoles
        // واقعی می‌خواند، نه از FakeCurrentUserService.Roles
        var user = new ApplicationUser { UserName = $"user_{dbName}_{userId}", Email = $"{dbName}_{userId}@pems.local", FullName = "Test User" };
        user.Id = userId;
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        if (role != null)
        {
            var roleEntity = ctx.Roles.First(r => r.Name == role);
            ctx.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = user.Id, RoleId = roleEntity.Id });
            await ctx.SaveChangesAsync();
        }

        var runtime = new FakeWorkflowRuntime();
        var contractService = new ContractService(ctx, accessControl);
        var workflowService = new ContractWorkflowService(ctx, runtime, accessControl, new FakeNumberingService());
        var currentUser = new FakeCurrentUserService
        {
            UserId = userId,
            Roles = role != null ? new List<string> { role } : new List<string>()
        };

        var controller = new ContractController(contractService, workflowService, runtime, accessControl, permissionService, currentUser, ctx).WithTempData();
        return (controller, ctx, runtime);
    }

    [Fact]
    public async Task Create_WithoutCreatePermission_ReturnsForbid_DoesNotCreateContract()
    {
        var (controller, ctx, _) = await CreateController(nameof(Create_WithoutCreatePermission_ReturnsForbid_DoesNotCreateContract), role: SystemRoles.Viewer);
        SeedProjectAndVendor(ctx);

        var result = await controller.Create("CT-001", ctx.Projects.Single().ProjectId, ctx.Vendors.Single().VendorId, null, null, null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(ctx.Contracts);
    }

    [Fact]
    public async Task Create_WithCreatePermission_CreatesContract()
    {
        var (controller, ctx, _) = await CreateController(nameof(Create_WithCreatePermission_CreatesContract), role: SystemRoles.ContractManager);
        SeedProjectAndVendor(ctx);

        var result = await controller.Create("CT-001", ctx.Projects.Single().ProjectId, ctx.Vendors.Single().VendorId, null, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(ctx.Contracts);
        Assert.Equal("Draft", ctx.Contracts.Single().Status);
    }

    // SCRUM-437: Approve, Sign, Complete (and the other workflow steps) are action definitions over Contract operations. Their permission is declared on the service
    // method and checked by the operation catalog before the service is called (OperationTests proves the check); the transition rules stay in ContractWorkflowService.
    [Theory]
    [InlineData("Approve", "Contracts.Approve")]
    [InlineData("Reject", "Contracts.Approve")]
    [InlineData("Sign", "Contracts.Approve")]
    [InlineData("MarkEffective", "Contracts.Approve")]
    [InlineData("Complete", "Contracts.Approve")]
    [InlineData("Terminate", "Contracts.Approve")]
    [InlineData("ApproveRevision", "Contracts.Approve")]
    [InlineData("SubmitForReview", "Contracts.Edit")]
    [InlineData("RecordDisciplineReview", "Contracts.Edit")]
    [InlineData("RequestRevision", "Contracts.Edit")]
    public void The_workflow_operations_of_a_contract_require_the_same_permission_the_controller_checked(string action, string permission)
    {
        var catalog = PEMS.Web.Definitions.OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly);
        Assert.Equal(permission, catalog.Get("Contract", action)!.Permission);
    }

    private static void SeedProjectAndVendor(PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        ctx.Projects.Add(new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true });
        ctx.Vendors.Add(new Vendor { Name = "Vendor A", IsActive = true });
        ctx.SaveChanges();
    }

    private static Contract SeedDraftContract(PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        SeedProjectAndVendor(ctx);
        var contract = new Contract
        {
            ContractNo = "CT-001",
            ProjectId = ctx.Projects.Single().ProjectId,
            VendorId = ctx.Vendors.Single().VendorId,
            ContractValue = 50000m,
            Status = "Draft",
            IsActive = true
        };
        ctx.Contracts.Add(contract);
        ctx.SaveChanges();
        return contract;
    }
}
