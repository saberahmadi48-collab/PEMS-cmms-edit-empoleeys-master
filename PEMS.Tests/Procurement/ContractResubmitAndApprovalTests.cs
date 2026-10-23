using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0 / ChecklistB1+B2: (۱) ارسال مجدد قرارداد بعد از Reject (نهایی یا رشته‌ای) کار می‌کند و ردیف بازبینی تکراری نمی‌سازد؛
/// (۲) تایید نهایی Fail-Closed است: فقط «Allowed صریح» عبور می‌کند، حتی اگر نقش‌های پارامتر درخواست درست جعل شده باشند.
/// موتور واقعی گردش‌کار (WorkflowRuntimeService) و موتور مجوز واقعی - نه Fake.
/// </summary>
public class ContractResubmitAndApprovalTests
{
    private static readonly (string Discipline, string Role)[] AllDisciplines =
    {
        ("Engineering", SystemRoles.EngineeringManager),
        ("TechnicalInspection", SystemRoles.QAManager),
        ("Legal", SystemRoles.Legal),
        ("Finance", SystemRoles.Finance),
        ("Procurement", SystemRoles.ContractManager),
        ("HSE", SystemRoles.HSE)
    };

    private static async Task<(PEMSDbContext Ctx, ContractWorkflowService Workflow, int ContractId)> CreateAsync(string db)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"ContractP0_{db}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await ContractWorkflowSeeder.SeedAsync(provider);

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        var vendor = new Vendor { Name = "Vendor A", IsActive = true };
        ctx.Projects.Add(project);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();
        var contract = new Contract { ContractNo = "CT-001", ProjectId = project.ProjectId, VendorId = vendor.VendorId, ContractValue = 100000m, Status = "Draft", IsActive = true };
        ctx.Contracts.Add(contract);
        await ctx.SaveChangesAsync();

        var workflow = new ContractWorkflowService(ctx, new WorkflowRuntimeService(ctx), accessControl, new FakeNumberingService());
        return (ctx, workflow, contract.ContractId);
    }

    private static async Task<int> CreateUserWithRoleAsync(PEMSDbContext ctx, string roleName)
    {
        var user = new ApplicationUser { UserName = $"u_{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@pems.local", FullName = "Test User" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = ctx.Roles.First(r => r.Name == roleName).Id });
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static readonly string[] Manager = { SystemRoles.ContractManager };

    // ------------------------------------------------------------------ ارسال مجدد بعد از Reject
    [Fact]
    public async Task Resubmit_AfterFinalReject_Works_WithoutDuplicateReviewRows()
    {
        var (ctx, workflow, contractId) = await CreateAsync(nameof(Resubmit_AfterFinalReject_Works_WithoutDuplicateReviewRows));
        Assert.True((await workflow.SubmitForReviewAsync(contractId, null, 1, Manager)).Success);
        Assert.True((await workflow.RejectAsync(contractId, "اصلاح شود", 1, Manager)).Success);
        Assert.Equal("Draft", (await ctx.Contracts.SingleAsync()).Status);

        var again = await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);

        Assert.True(again.Success, again.Error);
        Assert.Equal("UnderReview", (await ctx.Contracts.SingleAsync()).Status);
        var reviews = await ctx.ContractDisciplineReviews.Where(x => x.ContractId == contractId).ToListAsync();
        Assert.Equal(6, reviews.Count);                                    // بدون ردیف تکراری
        Assert.Equal(6, reviews.Select(r => r.DisciplineRole).Distinct().Count());
        Assert.All(reviews, r => { Assert.Equal("Pending", r.Decision); Assert.True(r.IsRequired); });
        Assert.Contains(ctx.WorkflowInstances, i => i.BusinessObjectId == contractId && i.Status == "Running");
    }

    [Fact]
    public async Task Resubmit_AfterFinalReject_CanBeCarriedToApproval()
    {
        var (ctx, workflow, contractId) = await CreateAsync(nameof(Resubmit_AfterFinalReject_CanBeCarriedToApproval));
        await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);
        await workflow.RejectAsync(contractId, "اصلاح شود", 1, Manager);
        await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);
        foreach (var (discipline, role) in AllDisciplines)
            Assert.True((await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role })).Success);

        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var result = await workflow.ApproveAsync(contractId, null, approver, 1, Manager, null);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Approved", (await ctx.Contracts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resubmit_AfterADisciplineRejects_IsNoLongerADeadEnd()
    {
        var (ctx, workflow, contractId) = await CreateAsync(nameof(Resubmit_AfterADisciplineRejects_IsNoLongerADeadEnd));
        await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);
        // ردِ رشته‌ای وقتی «Rejected» می‌شود که موتور بعد از تصمیم همه‌ی رشته‌ها Instance را Rejected کند
        foreach (var (discipline, role) in AllDisciplines)
            Assert.True((await workflow.RecordDisciplineReviewAsync(
                contractId, discipline, discipline == "Legal" ? "Reject" : "Approve", discipline == "Legal" ? "بند ۷ مشکل دارد" : null, 1, new[] { role })).Success);
        Assert.Equal("Rejected", (await ctx.Contracts.SingleAsync()).Status);       // پیش‌شرط: وضعیت بن‌بست قبلی

        var again = await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);

        Assert.True(again.Success, again.Error);
        Assert.Equal("UnderReview", (await ctx.Contracts.SingleAsync()).Status);
        Assert.Equal(6, await ctx.ContractDisciplineReviews.CountAsync(x => x.ContractId == contractId));
        Assert.All(ctx.ContractDisciplineReviews.Where(x => x.ContractId == contractId), r => Assert.Equal("Pending", r.Decision));
    }

    [Fact]
    public async Task Resubmit_WithASmallerDisciplineSet_DoesNotLetStaleRowsBlockApproval()
    {
        var (ctx, workflow, contractId) = await CreateAsync(nameof(Resubmit_WithASmallerDisciplineSet_DoesNotLetStaleRowsBlockApproval));
        // «Procurement» (نقش ContractManager) در هر دو مجموعه هست تا همان نقش بتواند Reject را اجرا کند
        await workflow.SubmitForReviewAsync(contractId, new[] { "Legal", "Finance", "Procurement" }, 1, Manager);
        await workflow.RejectAsync(contractId, "اصلاح شود", 1, Manager);

        var again = await workflow.SubmitForReviewAsync(contractId, new[] { "Legal", "Procurement" }, 1, Manager);

        Assert.True(again.Success, again.Error);
        var finance = await ctx.ContractDisciplineReviews.SingleAsync(x => x.ContractId == contractId && x.DisciplineRole == "Finance");
        Assert.False(finance.IsRequired);                                       // رشته‌ی حذف‌شده دیگر لازم نیست
        Assert.True((await workflow.RecordDisciplineReviewAsync(contractId, "Legal", "Approve", null, 1, new[] { SystemRoles.Legal })).Success);
        Assert.True((await workflow.RecordDisciplineReviewAsync(contractId, "Procurement", "Approve", null, 1, new[] { SystemRoles.ContractManager })).Success);

        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var result = await workflow.ApproveAsync(contractId, null, approver, 1, Manager, null);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task SubmitForReview_FromUnderReview_StillFails()
    {
        var (_, workflow, contractId) = await CreateAsync(nameof(SubmitForReview_FromUnderReview_StillFails));
        await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);

        Assert.False((await workflow.SubmitForReviewAsync(contractId, null, 1, Manager)).Success);
    }

    // ------------------------------------------------------------------ تایید نهایی Fail-Closed
    private static async Task<(PEMSDbContext Ctx, ContractWorkflowService Workflow, int ContractId)> ReadyForApprovalAsync(string db)
    {
        var (ctx, workflow, contractId) = await CreateAsync(db);
        await workflow.SubmitForReviewAsync(contractId, null, 1, Manager);
        foreach (var (discipline, role) in AllDisciplines)
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });
        return (ctx, workflow, contractId);
    }

    [Fact]
    public async Task Approve_ByAUserWithoutContractsApprove_IsDenied_EvenWithForgedRoleParameters()
    {
        var (ctx, workflow, contractId) = await ReadyForApprovalAsync(nameof(Approve_ByAUserWithoutContractsApprove_IsDenied_EvenWithForgedRoleParameters));
        var viewer = await CreateUserWithRoleAsync(ctx, SystemRoles.Viewer);

        // نقش‌های پارامتر (که کنترلر از کوکی می‌گیرد) جعل/کهنه‌اند؛ تصمیم کاربر-آگاه از نقش‌های واقعی در دیتابیس باید رد کند
        var result = await workflow.ApproveAsync(contractId, null, viewer, 1, Manager, null);

        Assert.False(result.Success);
        Assert.Equal("UnderReview", (await ctx.Contracts.SingleAsync()).Status);
        Assert.Null((await ctx.Contracts.SingleAsync()).ApprovedDate);
    }

    [Fact]
    public async Task Approve_BySystemAdminWithoutAnExplicitGrant_IsDenied_WithoutAnException()
    {
        var (ctx, workflow, contractId) = await ReadyForApprovalAsync(nameof(Approve_BySystemAdminWithoutAnExplicitGrant_IsDenied_WithoutAnException));
        var admin = await CreateUserWithRoleAsync(ctx, SystemRoles.SystemAdmin);

        var result = await workflow.ApproveAsync(contractId, null, admin, 1, new[] { SystemRoles.SystemAdmin }, null);

        Assert.False(result.Success);
        Assert.Equal("UnderReview", (await ctx.Contracts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Approve_ByAnUnknownUserId_IsDenied()
    {
        var (ctx, workflow, contractId) = await ReadyForApprovalAsync(nameof(Approve_ByAnUnknownUserId_IsDenied));

        var result = await workflow.ApproveAsync(contractId, null, 987654, 1, Manager, null);

        Assert.False(result.Success);
        Assert.Equal("UnderReview", (await ctx.Contracts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Approve_ByAnAuthorizedDifferentUser_StillSucceeds()
    {
        var (ctx, workflow, contractId) = await ReadyForApprovalAsync(nameof(Approve_ByAnAuthorizedDifferentUser_StillSucceeds));
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, Manager, null);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Approved", (await ctx.Contracts.SingleAsync()).Status);
    }
}
