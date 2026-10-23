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

    [Fact]
    public async Task ChangeStatus_WithArbitraryFreeTextStatus_IsRejected_DoesNotMutateContract()
    {
        // طبق §D (Phase 2.2): این دقیقاً همان آسیب‌پذیری‌ای است که ممیزی پیدا کرد -
        // ChangeStatus دیگر هر رشته‌ی آزاد را نمی‌پذیرد
        var (controller, ctx, _) = await CreateController(nameof(ChangeStatus_WithArbitraryFreeTextStatus_IsRejected_DoesNotMutateContract), role: SystemRoles.ContractManager);
        var contract = SeedDraftContract(ctx);

        var result = await controller.ChangeStatus(contract.ContractId, "Effective");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Draft", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task ChangeStatus_ToUnderReview_DelegatesToWorkflowService()
    {
        var (controller, ctx, _) = await CreateController(nameof(ChangeStatus_ToUnderReview_DelegatesToWorkflowService), role: SystemRoles.ContractManager);
        var contract = SeedDraftContract(ctx);

        var result = await controller.ChangeStatus(contract.ContractId, "UnderReview");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("UnderReview", ctx.Contracts.Single().Status);
        Assert.Equal(6, ctx.ContractDisciplineReviews.Count(x => x.ContractId == contract.ContractId));
    }

    [Fact]
    public async Task Approve_WithoutApprovePermission_ReturnsForbid()
    {
        var (controller, ctx, _) = await CreateController(nameof(Approve_WithoutApprovePermission_ReturnsForbid), role: SystemRoles.DisciplineEngineer);
        var contract = SeedDraftContract(ctx);

        var result = await controller.Approve(contract.ContractId, null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Sign_WithoutApprovePermission_ReturnsForbid_DoesNotSign()
    {
        var (controller, ctx, _) = await CreateController(nameof(Sign_WithoutApprovePermission_ReturnsForbid_DoesNotSign), role: SystemRoles.DisciplineEngineer);
        var contract = SeedDraftContract(ctx);
        contract.Status = "Approved";
        ctx.SaveChanges();

        var result = await controller.Sign(contract.ContractId);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Approved", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Complete_WithoutApprovePermission_ReturnsForbid_DoesNotComplete()
    {
        var (controller, ctx, _) = await CreateController(nameof(Complete_WithoutApprovePermission_ReturnsForbid_DoesNotComplete), role: SystemRoles.DisciplineEngineer);
        var contract = SeedDraftContract(ctx);
        contract.Status = "Effective";
        ctx.SaveChanges();

        var result = await controller.Complete(contract.ContractId);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Effective", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Complete_FromDraft_InvalidTransition_IsRejected()
    {
        var (controller, ctx, _) = await CreateController(nameof(Complete_FromDraft_InvalidTransition_IsRejected), role: SystemRoles.ContractManager);
        var contract = SeedDraftContract(ctx);

        var result = await controller.Complete(contract.ContractId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Draft", ctx.Contracts.Single().Status);
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
