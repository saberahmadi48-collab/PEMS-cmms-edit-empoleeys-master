using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§D/§F، Phase 2.7): گردش‌کار نهایی‌سازی
/// قرارداد (DisciplineReview → FinalApproval → Sign → Effective) روی موتور واقعی
/// (WorkflowRuntimeService، نه Fake) تا واقعاً ثابت شود AllApprovalsRequired/SoD کار می‌کند -
/// دقیقاً هم‌الگو با IrnDispositionServiceTests.
/// </summary>
public class ContractWorkflowServiceTests
{
    private static async Task<(PEMSDbContext Ctx, ContractWorkflowService Workflow, AccessControlService AccessControl, int ContractId)>
        CreateAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"ContractWorkflow_{dbName}");
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

        var contract = new Contract
        {
            ContractNo = "CT-001",
            ProjectId = project.ProjectId,
            VendorId = vendor.VendorId,
            ContractValue = 100000m,
            Status = "Draft",
            IsActive = true
        };
        ctx.Contracts.Add(contract);
        await ctx.SaveChangesAsync();

        var runtime = new WorkflowRuntimeService(ctx);
        var workflow = new ContractWorkflowService(ctx, runtime, accessControl, new FakeNumberingService());

        return (ctx, workflow, accessControl, contract.ContractId);
    }

    // طبق AccessControlScopeAndSoDTests: AccessControlService.AuthorizeAsync (مسیر SoD-آگاه) نقش
    // کاربر را از AspNetUserRoles واقعی می‌خواند، نه از یک پارامتر actorRoles - بدون ردیف واقعی
    // UserRoles، اولین بررسی نقش/مجوز با DeniedByRole رد می‌شود و اصلاً به بررسی SoD نمی‌رسد.
    private static async Task<int> CreateUserWithRoleAsync(PEMSDbContext ctx, string roleName)
    {
        var user = new ApplicationUser { UserName = $"u_{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@pems.local", FullName = "Test User" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var role = ctx.Roles.First(r => r.Name == roleName);
        ctx.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await ctx.SaveChangesAsync();

        return user.Id;
    }

    private static readonly (string Discipline, string Role)[] AllDisciplines =
    {
        ("Engineering", SystemRoles.EngineeringManager),
        ("TechnicalInspection", SystemRoles.QAManager),
        ("Legal", SystemRoles.Legal),
        ("Finance", SystemRoles.Finance),
        ("Procurement", SystemRoles.ContractManager),
        ("HSE", SystemRoles.HSE)
    };

    [Fact]
    public async Task SubmitForReview_FromDraft_CreatesOneReviewRowPerRequiredDiscipline()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(SubmitForReview_FromDraft_CreatesOneReviewRowPerRequiredDiscipline));

        var result = await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        Assert.True(result.Success);
        Assert.Equal("UnderReview", ctx.Contracts.Single().Status);
        Assert.Equal(6, ctx.ContractDisciplineReviews.Count(x => x.ContractId == contractId));
        Assert.All(ctx.ContractDisciplineReviews.Where(x => x.ContractId == contractId), r => Assert.Equal("Pending", r.Decision));
    }

    [Fact]
    public async Task SubmitForReview_WhenNotDraft_Fails()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(SubmitForReview_WhenNotDraft_Fails));
        ctx.Contracts.Single().Status = "UnderReview";
        await ctx.SaveChangesAsync();

        var result = await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RecordDisciplineReview_ByWrongRole_IsRejected()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(RecordDisciplineReview_ByWrongRole_IsRejected));
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        // فقط Legal مجاز است رشته‌ی Legal را ثبت کند
        var result = await workflow.RecordDisciplineReviewAsync(
            contractId, "Legal", "Approve", null, 2, new[] { SystemRoles.Finance });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approve_WhilePendingDisciplinesRemain_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(Approve_WhilePendingDisciplinesRemain_Fails));
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        // فقط ۵ از ۶ رشته تصمیم می‌گیرند
        foreach (var (discipline, role) in AllDisciplines.Take(5))
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });

        var result = await workflow.ApproveAsync(contractId, null, 99, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approve_AfterAllDisciplinesApprove_Succeeds_ByDifferentUserThanCreator()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_AfterAllDisciplinesApprove_Succeeds_ByDifferentUserThanCreator));
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        foreach (var (discipline, role) in AllDisciplines)
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });

        // کاربری با نقش ContractManager که خودش این Contract را ایجاد نکرده (هیچ Create.Allow ای
        // برایش Audit نشده) قرارداد را تایید می‌کند
        var approverUserId = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var result = await workflow.ApproveAsync(contractId, null, approverUserId, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
        Assert.Equal("Approved", ctx.Contracts.Single().Status);
        Assert.NotNull(ctx.Contracts.Single().ApprovedDate);
    }

    [Fact]
    public async Task Approve_BySameUserWhoCreatedTheContract_IsDeniedBySoD()
    {
        var (ctx, workflow, accessControl, contractId) = await CreateAsync(nameof(Approve_BySameUserWhoCreatedTheContract_IsDeniedBySoD));

        var creatorUserId = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        // شبیه‌سازی همان چیزی که ContractController.Create بعد از ساخت قرارداد ثبت می‌کند
        var createAudit = await accessControl.AuthorizeAsync(new AccessControlRequest
        {
            UserId = creatorUserId,
            ResourceName = "Contracts",
            ActionName = "Create",
            ResourceId = contractId.ToString()
        });
        Assert.True(createAudit.Allowed); // پیش‌شرط: اگر این خودش Allow نشود، تست SoD معنا ندارد

        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });
        foreach (var (discipline, role) in AllDisciplines)
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });

        var result = await workflow.ApproveAsync(contractId, null, creatorUserId, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
        Assert.Equal("UnderReview", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Reject_FromUnderReview_ReturnsToDraft_AndClearsReviewDecisions()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Reject_FromUnderReview_ReturnsToDraft_AndClearsReviewDecisions));
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });
        await workflow.RecordDisciplineReviewAsync(contractId, "Legal", "Approve", null, 1, new[] { SystemRoles.Legal });

        var result = await workflow.RejectAsync(contractId, "نیاز به اصلاح دارد", 1, new[] { SystemRoles.ContractManager });

        Assert.True(result.Success);
        Assert.Equal("Draft", ctx.Contracts.Single().Status);
        Assert.All(ctx.ContractDisciplineReviews.Where(x => x.ContractId == contractId), r => Assert.Equal("Pending", r.Decision));
    }

    [Fact]
    public async Task Sign_BeforeApproved_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(Sign_BeforeApproved_Fails));

        var result = await workflow.SignAsync(contractId, 1, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Sign_AfterApproved_SetsSignedStatus_AndInitializesCurrentValue()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Sign_AfterApproved_SetsSignedStatus_AndInitializesCurrentValue));
        ctx.Contracts.Single().Status = "Approved";
        await ctx.SaveChangesAsync();

        var result = await workflow.SignAsync(contractId, 1, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
        var contract = ctx.Contracts.Single();
        Assert.Equal("Signed", contract.Status);
        Assert.NotNull(contract.SignedDate);
        Assert.Equal(100000m, contract.CurrentValue);
    }

    [Fact]
    public async Task MarkEffective_BeforeSigned_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(MarkEffective_BeforeSigned_Fails));

        var result = await workflow.MarkEffectiveAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RequestRevision_ThenApprove_UpdatesCurrentValue_ButPreservesOriginalContractValue()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(RequestRevision_ThenApprove_UpdatesCurrentValue_ButPreservesOriginalContractValue));
        var contract = ctx.Contracts.Single();
        contract.Status = "Signed";
        contract.CurrentValue = 100000m;
        await ctx.SaveChangesAsync();

        var revisionResult = await workflow.RequestRevisionAsync(contractId, "Variation", 115000m, "Scope increase", "Added instrumentation package", 1);
        Assert.True(revisionResult.Success);

        var approveResult = await workflow.ApproveRevisionAsync(revisionResult.RevisionId!.Value, 2, new[] { SystemRoles.ContractManager });
        Assert.True(approveResult.Success);

        var updated = ctx.Contracts.Single();
        Assert.Equal(100000m, updated.ContractValue); // اصلی هرگز بازنویسی نمی‌شود
        Assert.Equal(115000m, updated.CurrentValue);   // فقط جاری تغییر می‌کند

        var revision = ctx.ContractRevisions.Single();
        Assert.Equal(100000m, revision.PreviousValue);
        Assert.Equal(115000m, revision.NewValue);
        Assert.Equal("Approved", revision.Status);
    }

    [Fact]
    public async Task IssueNtp_BeforeSignedOrEffective_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(IssueNtp_BeforeSignedOrEffective_Fails));

        var result = await workflow.IssueNtpAsync(contractId, null, null, 1);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task IssueNtp_AfterSigned_Succeeds_AndBlocksASecondActiveNtp()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(IssueNtp_AfterSigned_Succeeds_AndBlocksASecondActiveNtp));
        ctx.Contracts.Single().Status = "Signed";
        await ctx.SaveChangesAsync();

        var first = await workflow.IssueNtpAsync(contractId, "Ref-1", DateTime.Today, 1);
        Assert.True(first.Success);
        Assert.Single(ctx.ContractNTPs);

        var second = await workflow.IssueNtpAsync(contractId, "Ref-2", DateTime.Today, 1);
        Assert.False(second.Success);
        Assert.Single(ctx.ContractNTPs);
    }

    [Fact]
    public async Task ScheduleKickoff_BeforeSignedOrEffective_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(ScheduleKickoff_BeforeSignedOrEffective_Fails));

        var result = await workflow.ScheduleKickoffMeetingAsync(contractId, DateTime.Today.AddDays(3), "Site Office", "Ops", null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ScheduleKickoff_AfterEffective_CreatesMeeting_LinkedToContract()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(ScheduleKickoff_AfterEffective_CreatesMeeting_LinkedToContract));
        ctx.Contracts.Single().Status = "Effective";
        await ctx.SaveChangesAsync();

        var result = await workflow.ScheduleKickoffMeetingAsync(contractId, DateTime.Today.AddDays(3), "Site Office", "Ops", "Kick-off agenda");

        Assert.True(result.Success);
        var meeting = ctx.Meetings.Single();
        Assert.Equal(contractId, meeting.ContractId);
        Assert.Equal("KickOff", meeting.MeetingType);
    }

    // ================= Final Hardening Gap #1: Contract completion/termination lifecycle =================

    [Fact]
    public async Task Complete_FromNonEffectiveStatus_Fails()
    {
        var (_, workflow, _, contractId) = await CreateAsync(nameof(Complete_FromNonEffectiveStatus_Fails));

        var result = await workflow.CompleteAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Complete_RequiresInspection_WithNoLinkedItp_IsBlocked()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Complete_RequiresInspection_WithNoLinkedItp_IsBlocked));
        var contract = ctx.Contracts.Single();
        contract.Status = "Effective";
        contract.RequiresInspection = true;
        await ctx.SaveChangesAsync();

        var result = await workflow.CompleteAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
        Assert.Equal("Effective", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Complete_RequiresInspection_WithOpenItp_IsBlocked()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Complete_RequiresInspection_WithOpenItp_IsBlocked));
        var contract = ctx.Contracts.Single();
        contract.Status = "Effective";
        contract.RequiresInspection = true;
        ctx.InspectionTestPlans.Add(new InspectionTestPlan
        {
            ITPNumber = "ITP-001", ProjectId = contract.ProjectId, ContractId = contractId, QCDisciplineId = 1, Title = "T", Status = "Active"
        });
        await ctx.SaveChangesAsync();

        var result = await workflow.CompleteAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
        Assert.Equal("Effective", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Complete_RequiresInspection_WithAllItpsClosed_Succeeds()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Complete_RequiresInspection_WithAllItpsClosed_Succeeds));
        var contract = ctx.Contracts.Single();
        contract.Status = "Effective";
        contract.RequiresInspection = true;
        ctx.InspectionTestPlans.Add(new InspectionTestPlan
        {
            ITPNumber = "ITP-001", ProjectId = contract.ProjectId, ContractId = contractId, QCDisciplineId = 1, Title = "T", Status = "Closed"
        });
        await ctx.SaveChangesAsync();

        var result = await workflow.CompleteAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.True(result.Success);
        var updated = ctx.Contracts.Single();
        Assert.Equal("Completed", updated.Status);
        Assert.NotNull(updated.CompletedDate);
    }

    [Fact]
    public async Task Complete_DoesNotRequireInspection_SucceedsWithNoItp()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Complete_DoesNotRequireInspection_SucceedsWithNoItp));
        ctx.Contracts.Single().Status = "Effective";
        await ctx.SaveChangesAsync();

        var result = await workflow.CompleteAsync(contractId, new[] { SystemRoles.ContractManager });

        Assert.True(result.Success);
        Assert.Equal("Completed", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Terminate_FromDraft_Succeeds_AndRecordsReason()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Terminate_FromDraft_Succeeds_AndRecordsReason));

        var result = await workflow.TerminateAsync(contractId, "Vendor withdrew", new[] { SystemRoles.ContractManager });

        Assert.True(result.Success);
        var updated = ctx.Contracts.Single();
        Assert.Equal("Terminated", updated.Status);
        Assert.Equal("Vendor withdrew", updated.TerminationReason);
        Assert.NotNull(updated.TerminatedDate);
    }

    [Fact]
    public async Task Terminate_FromCompleted_Fails()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Terminate_FromCompleted_Fails));
        ctx.Contracts.Single().Status = "Completed";
        await ctx.SaveChangesAsync();

        var result = await workflow.TerminateAsync(contractId, null, new[] { SystemRoles.ContractManager });

        Assert.False(result.Success);
    }

    // ================= Final Hardening Gap #3: Approval-Matrix Gate (previously untested) =================

    private static async Task<int> GetToUnderReviewFullyApprovedAsync(PEMSDbContext ctx, ContractWorkflowService workflow, int contractId)
    {
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });
        foreach (var (discipline, role) in AllDisciplines)
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });
        return contractId;
    }

    [Fact]
    public async Task Approve_WithNoConfiguredRules_UsesExistingRoleGate_Unchanged()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_WithNoConfiguredRules_UsesExistingRoleGate_Unchanged));
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Approve_WithConfiguredRules_NoMatch_IsBlockedFailClosed()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_WithConfiguredRules_NoMatch_IsBlockedFailClosed));
        // قاعده‌ای که فقط بازه‌ی ۰..۵۰۰۰۰ را پوشش می‌دهد - قرارداد این تست ارزش ۱۰۰۰۰۰ دارد
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = 0m, MaxValue = 50000m, RequiredApproverRole = SystemRoles.ContractManager, IsActive = true, Priority = 0
        });
        await ctx.SaveChangesAsync();
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approve_WithConfiguredRules_MatchButWrongRole_IsBlocked()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_WithConfiguredRules_MatchButWrongRole_IsBlocked));
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = null, MaxValue = null, RequiredApproverRole = SystemRoles.EngineeringManager, IsActive = true, Priority = 0
        });
        await ctx.SaveChangesAsync();
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approve_WithConfiguredRules_MatchAndCorrectRole_Succeeds()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_WithConfiguredRules_MatchAndCorrectRole_Succeeds));
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = null, MaxValue = null, RequiredApproverRole = SystemRoles.ContractManager, IsActive = true, Priority = 0
        });
        await ctx.SaveChangesAsync();
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Approve_WithTwoMatchingRules_LowerPriorityWins()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_WithTwoMatchingRules_LowerPriorityWins));
        // هر دو قاعده روی همین قرارداد مطابقت دارند - Priority=0 باید برنده شود، نه Priority=5
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = null, MaxValue = null, RequiredApproverRole = SystemRoles.ContractManager, IsActive = true, Priority = 0
        });
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = null, MaxValue = null, RequiredApproverRole = SystemRoles.EngineeringManager, IsActive = true, Priority = 5
        });
        await ctx.SaveChangesAsync();
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        // اگر Priority=5 (EngineeringManager) اشتباهاً برنده شود، این تایید رد می‌شد
        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
    }

    // ================= Procurement Lifecycle WP-B: SystemAdmin must not silently substitute =================

    [Fact]
    public async Task RecordDisciplineReview_BySystemAdmin_WithoutTheDisciplineRole_IsRejected()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(RecordDisciplineReview_BySystemAdmin_WithoutTheDisciplineRole_IsRejected));
        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager });

        var result = await workflow.RecordDisciplineReviewAsync(
            contractId, "Legal", "Approve", null, 2, new[] { SystemRoles.SystemAdmin });

        Assert.False(result.Success);
        Assert.Equal("Pending", ctx.ContractDisciplineReviews.Single(x => x.ContractId == contractId && x.DisciplineRole == "Legal").Decision);
    }

    [Fact]
    public async Task Approve_BySystemAdmin_WithConfiguredAuthorityRule_IsBlocked_WithoutTheRequiredRole()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_BySystemAdmin_WithConfiguredAuthorityRule_IsBlocked_WithoutTheRequiredRole));
        ctx.ContractApprovalAuthorityRules.Add(new ContractApprovalAuthorityRule
        {
            MinValue = null, MaxValue = null, RequiredApproverRole = SystemRoles.ContractManager, IsActive = true, Priority = 0
        });
        await ctx.SaveChangesAsync();
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var admin = await CreateUserWithRoleAsync(ctx, SystemRoles.SystemAdmin);

        var result = await workflow.ApproveAsync(contractId, null, admin, 1, new[] { SystemRoles.SystemAdmin }, null);

        Assert.False(result.Success);
        Assert.NotEqual("Approved", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task Approve_BySystemAdmin_WithoutConfiguredRules_IsNotApproved_ByTheWorkflowRoleGate()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(Approve_BySystemAdmin_WithoutConfiguredRules_IsNotApproved_ByTheWorkflowRoleGate));
        await GetToUnderReviewFullyApprovedAsync(ctx, workflow, contractId);
        var admin = await CreateUserWithRoleAsync(ctx, SystemRoles.SystemAdmin);

        // P0 (Fail-Closed): SystemAdmin برای Contracts.Approve بدون Grant صریح مجاز نیست (RequiresExplicitGrant). قبلاً ردِ
        // کاربر-آگاه نادیده گرفته می‌شد و فقط نقش مرحله‌ی FinalApproval در موتور گردش‌کار استثنا می‌داد؛ حالا تایید زودتر و
        // بدون استثنا رد می‌شود (و ContractController هم استثنای موتور را به پیام دوستانه تبدیل می‌کند).
        var result = await workflow.ApproveAsync(contractId, null, admin, 1, new[] { SystemRoles.SystemAdmin }, null);

        Assert.False(result.Success);
        Assert.NotEqual("Approved", ctx.Contracts.Single().Status);
    }

    // ==============================================================================
    // P0-10 / D-OPEN-05 (تصمیم مالک ۲۰۲۶-۱۰-۱۴): ویرایشگر قرارداد ≠ تأییدکننده‌ی آن
    // ==============================================================================

    private static async Task SubmitAndReviewAllAsync(ContractWorkflowService workflow, int contractId, int? editorUserId)
    {
        var submitted = await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager }, editorUserId);
        Assert.True(submitted.Success);
        foreach (var (discipline, role) in AllDisciplines)
            await workflow.RecordDisciplineReviewAsync(contractId, discipline, "Approve", null, 1, new[] { role });
    }

    [Fact]
    public async Task P010_SubmitForReview_RecordsTheEditorEvent_InTheSameSave()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_SubmitForReview_RecordsTheEditorEvent_InTheSameSave));
        var editor = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager }, editor, "10.0.0.1");

        var ev = Assert.Single(ctx.AuthorizationAuditLogs.Where(a => a.ResourceName == "Contracts" && a.ActionName == "Edit"));
        Assert.Equal(editor, ev.UserId);
        Assert.Equal(contractId.ToString(), ev.ResourceId);
        Assert.Equal("Allow", ev.Decision);
    }

    [Fact]
    public async Task P010_FailedSubmit_RecordsNoEditorEvent()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_FailedSubmit_RecordsNoEditorEvent));
        ctx.Contracts.Single().Status = "Approved";
        await ctx.SaveChangesAsync();
        var editor = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);

        var result = await workflow.SubmitForReviewAsync(contractId, null, 1, new[] { SystemRoles.ContractManager }, editor);

        Assert.False(result.Success);
        Assert.False(ctx.AuthorizationAuditLogs.Any(a => a.ResourceName == "Contracts" && a.ActionName == "Edit"));
    }

    [Fact]
    public async Task P010_Approve_ByTheUserWhoSubmittedTheContract_IsDeniedBySoD()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_Approve_ByTheUserWhoSubmittedTheContract_IsDeniedBySoD));
        var editor = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        await SubmitAndReviewAllAsync(workflow, contractId, editor);

        var result = await workflow.ApproveAsync(contractId, null, editor, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.False(result.Success);
        Assert.Contains("تفکیک وظایف", result.Error);
        Assert.Equal("UnderReview", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task P010_Approve_ByAnotherUser_AfterSomeoneElseEdited_Succeeds()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_Approve_ByAnotherUser_AfterSomeoneElseEdited_Succeeds));
        var editor = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        await SubmitAndReviewAllAsync(workflow, contractId, editor);

        var result = await workflow.ApproveAsync(contractId, null, approver, 1, new[] { SystemRoles.ContractManager }, null);

        Assert.True(result.Success);
        Assert.Equal("Approved", ctx.Contracts.Single().Status);
    }

    [Fact]
    public async Task P010_DisciplineReview_IsNotAnEdit_SoTheReviewerMayStillGiveFinalApproval()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_DisciplineReview_IsNotAnEdit_SoTheReviewerMayStillGiveFinalApproval));
        var editor = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        await SubmitAndReviewAllAsync(workflow, contractId, editor);

        Assert.Single(ctx.AuthorizationAuditLogs.Where(a => a.ResourceName == "Contracts" && a.ActionName == "Edit"));
    }

    private static async Task<int> SignedContractWithRevisionAsync(PEMSDbContext ctx, ContractWorkflowService workflow, int contractId, int? requesterUserId)
    {
        ctx.Contracts.Single().Status = "Signed";
        await ctx.SaveChangesAsync();
        var (ok, _, revisionId) = await workflow.RequestRevisionAsync(contractId, "Value", 120000m, "scope change", null, 1, requesterUserId);
        Assert.True(ok);
        return revisionId!.Value;
    }

    [Fact]
    public async Task P010_ApproveRevision_ByTheRequester_IsDeniedBySoD_AndValueUnchanged()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_ApproveRevision_ByTheRequester_IsDeniedBySoD_AndValueUnchanged));
        var requester = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var revisionId = await SignedContractWithRevisionAsync(ctx, workflow, contractId, requester);

        var result = await workflow.ApproveRevisionAsync(revisionId, 1, new[] { SystemRoles.ContractManager }, requester);

        Assert.False(result.Success);
        Assert.Equal("Pending", ctx.ContractRevisions.Single().Status);
        Assert.Null(ctx.Contracts.Single().CurrentValue);
    }

    [Fact]
    public async Task P010_ApproveRevision_ByAnotherAuthorizedUser_Succeeds()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_ApproveRevision_ByAnotherAuthorizedUser_Succeeds));
        var requester = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var approver = await CreateUserWithRoleAsync(ctx, SystemRoles.ContractManager);
        var revisionId = await SignedContractWithRevisionAsync(ctx, workflow, contractId, requester);

        var result = await workflow.ApproveRevisionAsync(revisionId, 1, new[] { SystemRoles.ContractManager }, approver);

        Assert.True(result.Success);
        Assert.Equal(120000m, ctx.Contracts.Single().CurrentValue);
    }

    [Fact]
    public async Task P010_ApproveRevision_ByUserWithoutApprovePermission_IsDenied()
    {
        var (ctx, workflow, _, contractId) = await CreateAsync(nameof(P010_ApproveRevision_ByUserWithoutApprovePermission_IsDenied));
        var revisionId = await SignedContractWithRevisionAsync(ctx, workflow, contractId, null);
        var legal = await CreateUserWithRoleAsync(ctx, SystemRoles.Legal);   // Contracts.Edit فقط

        var result = await workflow.ApproveRevisionAsync(revisionId, 1, new[] { SystemRoles.Legal }, legal);

        Assert.False(result.Success);
        Assert.Equal("Pending", ctx.ContractRevisions.Single().Status);
    }
}
