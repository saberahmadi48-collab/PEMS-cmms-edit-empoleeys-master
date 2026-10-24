using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0 / F-02 + F-03 (لایه‌ی کنترلر، موتور مجوز واقعی): انتخاب برنده و ساخت قرارداد از برنده Fail-Closed و ممیزی‌شده‌اند، و
/// سازنده‌ی قرارداد (از مناقصه) برای سیاست SoD «ایجادکننده ≠ تاییدکننده» دیده می‌شود. شرط Allowed صریح در گارد مشترک است.
/// </summary>
public class TenderControllerP0Tests
{
    private const string TenderOnlyRole = "TENDER_ONLY";

    private sealed record Arrange(
        TenderController Controller, PEMSDbContext Ctx, AccessControlService AccessControl, FakeCurrentUserService User, int TenderId, int ActorUserId);

    private static async Task<int> CreateUserWithRoleAsync(PEMSDbContext ctx, string roleName)
    {
        var user = new ApplicationUser { UserName = $"u_{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@pems.local", FullName = "Test User" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var role = ctx.Roles.First(r => r.Name == roleName);
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Arrange> CreateAsync(string db, string actorRole)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TenderP0_{db}");
        foreach (var roleName in SystemRoles.All.Append(TenderOnlyRole))
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();
        // نقشی که فقط Tender.Approve دارد (Contracts.Create ندارد) - برای اثبات اینکه ساخت قرارداد مجوز جداگانه می‌خواهد
        ctx.RolePermissions.Add(new RolePermission { RoleName = TenderOnlyRole, ModuleName = "Tender", CanView = true, CanApprove = true, CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        var actorUserId = await CreateUserWithRoleAsync(ctx, actorRole);
        var user = new FakeCurrentUserService { UserId = actorUserId, Roles = new[] { actorRole } };

        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();
        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = project.ProjectId, Title = "Pumps", IsActive = true };
        var vendor = new Vendor { Name = "Qualified Vendor", IsActive = true, QualificationStatus = "Qualified", QualificationExpiryDate = DateTime.Now.AddYears(1) };
        ctx.ProcurementPackages.Add(package);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var tenderService = new TenderService(ctx, new FakeNumberingService());
        var tenderId = await tenderService.CreateAsync(package.ProcurementPackageId, "Pump Tender", null, null, false, null, null, null, null);
        await tenderService.InviteVendorAsync(tenderId, vendor.VendorId);
        await tenderService.OpenForBiddingAsync(tenderId);
        var bid = await tenderService.SubmitBidAsync(tenderId, vendor.VendorId, 100000m, "USD", null, null);
        await tenderService.CloseBiddingAsync(tenderId);
        await tenderService.RecordBidEvaluationAsync(bid.BidId!.Value, "Technical", 1, "Engineering", "Passed", null);
        await tenderService.RecordBidEvaluationAsync(bid.BidId!.Value, "Commercial", 1, "Finance", "Passed", null);

        var controller = new TenderController(
            tenderService, new ProcurementPackageService(ctx), permissionService, user, ctx, accessControl).WithTempData();
        return new Arrange(controller, ctx, accessControl, user, tenderId, actorUserId) with { };
    }

    private static int BidIdOf(PEMSDbContext ctx) => ctx.Bids.Single().BidId;

    // ------------------------------------------------------------------ انتخاب برنده
    [Fact]
    public async Task SelectWinner_ByAuthorizedUser_Awards_AndWritesAnAllowAuditRowForTheTender()
    {
        var a = await CreateAsync(nameof(SelectWinner_ByAuthorizedUser_Awards_AndWritesAnAllowAuditRowForTheTender), SystemRoles.ContractManager);

        var result = await a.Controller.SelectWinner(a.TenderId, BidIdOf(a.Ctx));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Awarded", (await a.Ctx.Tenders.SingleAsync()).Status);
        Assert.Contains(a.Ctx.AuthorizationAuditLogs.ToList(), l =>
            l.UserId == a.ActorUserId && l.ResourceName == "Tender" && l.ActionName == "Approve"
            && l.ResourceId == a.TenderId.ToString() && l.Decision == nameof(AccessDecisionReason.Allow));
    }

    [Fact]
    public async Task SelectWinner_ByRoleWithoutApprove_IsForbidden_AndAwardsNothing()
    {
        var a = await CreateAsync(nameof(SelectWinner_ByRoleWithoutApprove_IsForbidden_AndAwardsNothing), SystemRoles.DisciplineEngineer);

        var result = await a.Controller.SelectWinner(a.TenderId, BidIdOf(a.Ctx));

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Closed", (await a.Ctx.Tenders.SingleAsync()).Status);
        Assert.Contains(a.Ctx.AuthorizationAuditLogs.ToList(), l => l.ResourceName == "Tender" && l.ActionName == "Approve" && l.Decision != nameof(AccessDecisionReason.Allow));
    }

    [Fact]
    public async Task SelectWinner_WithoutAnAuthenticatedUserId_IsFailClosed()
    {
        var a = await CreateAsync(nameof(SelectWinner_WithoutAnAuthenticatedUserId_IsFailClosed), SystemRoles.ContractManager);
        a.User.UserId = null;

        var result = await a.Controller.SelectWinner(a.TenderId, BidIdOf(a.Ctx));

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Closed", (await a.Ctx.Tenders.SingleAsync()).Status);
    }

    [Fact]
    public async Task SelectWinner_BySystemAdminWithoutAnExplicitGrant_IsForbidden()
    {
        var a = await CreateAsync(nameof(SelectWinner_BySystemAdminWithoutAnExplicitGrant_IsForbidden), SystemRoles.SystemAdmin);

        var result = await a.Controller.SelectWinner(a.TenderId, BidIdOf(a.Ctx));

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Closed", (await a.Ctx.Tenders.SingleAsync()).Status);
    }

    // ------------------------------------------------------------------ ساخت قرارداد از برنده
    private static async Task AwardAsync(Arrange a)
        => Assert.IsType<RedirectToActionResult>(await a.Controller.SelectWinner(a.TenderId, BidIdOf(a.Ctx)));

    [Fact]
    public async Task CreateContract_RecordsTheCreator_SoTheSameUserIsDeniedBySoDOnApproval()
    {
        var a = await CreateAsync(nameof(CreateContract_RecordsTheCreator_SoTheSameUserIsDeniedBySoDOnApproval), SystemRoles.ContractManager);
        await AwardAsync(a);

        var result = await a.Controller.CreateContract(a.TenderId, "CT-900", null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Contract", redirect.ControllerName);
        var contract = await a.Ctx.Contracts.SingleAsync();
        Assert.Equal(contract.ContractId, (await a.Ctx.Tenders.SingleAsync()).ContractId);

        // رویداد «ایجاد قرارداد» برای همین قرارداد ثبت شده است
        Assert.Contains(a.Ctx.AuthorizationAuditLogs.ToList(), l =>
            l.UserId == a.ActorUserId && l.ResourceName == "Contracts" && l.ActionName == "Create"
            && l.ResourceId == contract.ContractId.ToString() && l.Decision == nameof(AccessDecisionReason.Allow));

        // و همان کاربر برای تایید همین قرارداد با DeniedBySoD رد می‌شود (دقیقاً همان بررسی ContractWorkflowService.ApproveAsync)
        var approval = await a.AccessControl.AuthorizeAsync(new AccessControlRequest
        {
            UserId = a.ActorUserId, ResourceName = "Contracts", ActionName = "Approve", ResourceId = contract.ContractId.ToString()
        });
        Assert.False(approval.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, approval.Reason);
    }

    [Fact]
    public async Task CreateContract_WithTenderApproveButWithoutContractsCreate_IsForbidden_AndCreatesNothing()
    {
        var a = await CreateAsync(nameof(CreateContract_WithTenderApproveButWithoutContractsCreate_IsForbidden_AndCreatesNothing), TenderOnlyRole);
        await AwardAsync(a);                       // این نقش می‌تواند برنده را انتخاب کند (Tender.Approve)

        var result = await a.Controller.CreateContract(a.TenderId, "CT-901", null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, await a.Ctx.Contracts.CountAsync());
        Assert.Null((await a.Ctx.Tenders.SingleAsync()).ContractId);
    }

    [Fact]
    public async Task CreateContract_ByRoleWithoutTenderApprove_IsForbidden()
    {
        var a = await CreateAsync(nameof(CreateContract_ByRoleWithoutTenderApprove_IsForbidden), SystemRoles.DisciplineEngineer);

        var result = await a.Controller.CreateContract(a.TenderId, "CT-902", null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, await a.Ctx.Contracts.CountAsync());
    }

    [Fact]
    public async Task CreateContract_Retry_DoesNotDuplicateTheContractOrTheCreateAuditEvent()
    {
        var a = await CreateAsync(nameof(CreateContract_Retry_DoesNotDuplicateTheContractOrTheCreateAuditEvent), SystemRoles.ContractManager);
        await AwardAsync(a);

        await a.Controller.CreateContract(a.TenderId, "CT-903", null, null);
        var retry = await a.Controller.CreateContract(a.TenderId, "CT-904", null, null);

        Assert.IsType<RedirectToActionResult>(retry);
        Assert.NotNull(a.Controller.TempData["Error"]);
        Assert.Equal(1, await a.Ctx.Contracts.CountAsync());
        Assert.Equal(1, a.Ctx.AuthorizationAuditLogs.Count(l => l.ResourceName == "Contracts" && l.ActionName == "Create" && l.Decision == nameof(AccessDecisionReason.Allow)));
    }
}
