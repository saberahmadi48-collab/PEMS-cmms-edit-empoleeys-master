using Microsoft.AspNetCore.Identity;
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

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0.4 / H-1 (بدون SQL، دیتابیس InMemory) - مسیر دستی ساخت قرارداد: مجوز Fail-Closed پیش از هر درج، رویداد ایجادکننده برای کاربر واقعی، مسیر قدیمی بدون کاربر،
/// و نگاشت نتیجه‌ها در کنترلر. InMemory تراکنش/Rollback/قفل/قید یکتا ندارد؛ هیچ ادعایی درباره‌ی اتمیک‌بودن اینجا نیست - آن در
/// ManualContractCreationSqlTests روی SQL Server یک‌بارمصرف اثبات می‌شود. «مجوز پیش از درج» روی هر Provider برقرار است و همین‌جا سنجیده می‌شود.
/// </summary>
public class ManualContractCreationTests
{
    private sealed record Rig(PEMSDbContext Ctx, ContractService Service, AccessControlService AccessControl, PermissionService Permissions,
        ContractController Controller, FakeCurrentUserService CurrentUser, int ManagerId, int ViewerId, int NoRoleUserId, int ProjectId, int VendorId);

    private static async Task<Rig> BuildAsync(string name, IAccessControlService? serviceAccessControlOverride = null, bool passAccessControlToService = true)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"ManualContract_{name}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, accessControl);
        await permissions.SeedDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var provider = services.BuildServiceProvider();
        await AccessControlSeeder.SeedFromLegacyAsync(provider);
        await AccessControlSeeder.SeedPhase5GovernanceAsync(provider);

        async Task<int> AddUser(string userName, string? role)
        {
            var user = new ApplicationUser { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), Email = $"{userName}@pems.local", FullName = userName };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            if (role != null)
            {
                ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = ctx.Roles.First(r => r.Name == role).Id });
                await ctx.SaveChangesAsync();
            }
            return user.Id;
        }

        var manager = await AddUser("manager.m", SystemRoles.ContractManager);
        var viewer = await AddUser("viewer.m", SystemRoles.Viewer);
        var noRole = await AddUser("norole.m", null);

        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        var vendor = new Vendor { Name = "Vendor One", IsActive = true };
        ctx.Projects.Add(project);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var service = new ContractService(ctx, passAccessControlToService ? (serviceAccessControlOverride ?? accessControl) : null);
        var currentUser = new FakeCurrentUserService { UserId = manager, Roles = new[] { SystemRoles.ContractManager } };
        var runtime = new FakeWorkflowRuntime();
        var workflow = new ContractWorkflowService(ctx, runtime, accessControl, new FakeNumberingService());
        var controller = new ContractController(service, workflow, runtime, accessControl, permissions, currentUser, ctx).WithTempData();
        ctx.CurrentUserName = "manager.m";
        return new Rig(ctx, service, accessControl, permissions, controller, currentUser, manager, viewer, noRole, project.ProjectId, vendor.VendorId);
    }

    private static Task<List<AuthorizationAuditLog>> CreateRowsAsync(PEMSDbContext ctx)
        => ctx.AuthorizationAuditLogs.AsNoTracking().Where(l => l.ResourceName == "Contracts" && l.ActionName == "Create").ToListAsync();

    private sealed class ThrowingAccessControl : IAccessControlService
    {
        public int Calls;
        public Task<AccessDecision> AuthorizeAsync(AccessControlRequest request) { Calls++; throw new InvalidOperationException("authorization store unavailable"); }
        public Task<AccessDecision> AuthorizeForRolesAsync(IEnumerable<string> roles, string resourceName, string actionName) => throw new NotSupportedException();
    }

    private sealed class FixedAccessControl : IAccessControlService
    {
        private readonly AccessDecision _decision;
        public int Calls;
        public FixedAccessControl(AccessDecision decision) => _decision = decision;
        public Task<AccessDecision> AuthorizeAsync(AccessControlRequest request) { Calls++; return Task.FromResult(_decision); }
        public Task<AccessDecision> AuthorizeForRolesAsync(IEnumerable<string> roles, string resourceName, string actionName) => throw new NotSupportedException();
    }

    // ------------------------------------------------------------------ سرویس: مسیر موفق
    [Fact]
    public async Task Service_HappyPath_CreatesTheContract_AndTheCreatorEventForTheActualCreator()
    {
        var r = await BuildAsync(nameof(Service_HappyPath_CreatesTheContract_AndTheCreatorEventForTheActualCreator));

        var result = await r.Service.CreateWithCreatorEventAsync(" CT-M1 ", r.ProjectId, r.VendorId, "scope", 100m, null, null, true, r.ManagerId, "10.0.0.9");

        Assert.True(result.Success, result.Error);
        Assert.True(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.Recorded, result.CreatorEvent);
        var contract = await r.Ctx.Contracts.AsNoTracking().SingleAsync();
        Assert.Equal("CT-M1", contract.ContractNo);
        Assert.Equal("Draft", contract.Status);
        Assert.True(contract.RequiresInspection);
        Assert.Equal("manager.m", contract.CreatedBy);

        var rows = await CreateRowsAsync(r.Ctx);
        var ev = Assert.Single(rows, x => x.ResourceId == contract.ContractId.ToString());      // رویداد ایجادکننده
        Assert.Equal(r.ManagerId, ev.UserId);
        Assert.Equal("Allow", ev.Decision);
        var preAuth = Assert.Single(rows, x => x.ResourceId == null);                            // ردیف پیش‌بررسی مجوز: متمایز از رویداد
        Assert.Equal("Allow", preAuth.Decision);
    }

    // ------------------------------------------------------------------ سرویس: مجوز پیش از هر درج
    [Fact]
    public async Task Service_DeniedUser_CreatesNoContract_AndNoCreatorEvent_ButTheDenialIsAudited()
    {
        var r = await BuildAsync(nameof(Service_DeniedUser_CreatesNoContract_AndNoCreatorEvent_ButTheDenialIsAudited));

        var result = await r.Service.CreateWithCreatorEventAsync("CT-M2", r.ProjectId, r.VendorId, null, null, null, null, false, r.ViewerId, null);

        Assert.False(result.Success);
        Assert.True(result.AuthorizationDenied);
        Assert.False(result.Committed);
        Assert.Empty(r.Ctx.Contracts);
        var rows = await CreateRowsAsync(r.Ctx);
        var denial = Assert.Single(rows);
        Assert.Equal("DeniedByRole", denial.Decision);
        Assert.Null(denial.ResourceId);
        Assert.Equal(r.ViewerId, denial.UserId);
    }

    [Fact]
    public async Task Service_UserWithNoRoles_IsDenied()
    {
        var r = await BuildAsync(nameof(Service_UserWithNoRoles_IsDenied));

        var result = await r.Service.CreateWithCreatorEventAsync("CT-M2B", r.ProjectId, r.VendorId, null, null, null, null, false, r.NoRoleUserId, null);

        Assert.True(result.AuthorizationDenied);
        Assert.Empty(r.Ctx.Contracts);
    }

    [Fact]
    public async Task Service_AuthorizationException_IsIndeterminate_AndFailsClosed()
    {
        var throwing = new ThrowingAccessControl();
        var r = await BuildAsync(nameof(Service_AuthorizationException_IsIndeterminate_AndFailsClosed), throwing);

        var result = await r.Service.CreateWithCreatorEventAsync("CT-M3", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null);

        Assert.Equal(1, throwing.Calls);
        Assert.False(result.Success);
        Assert.True(result.AuthorizationIndeterminate);
        Assert.False(result.AuthorizationDenied);
        Assert.Empty(r.Ctx.Contracts);
    }

    [Fact]
    public async Task Service_WithoutAnAccessControlService_FailsClosed()
    {
        var r = await BuildAsync(nameof(Service_WithoutAnAccessControlService_FailsClosed), passAccessControlToService: false);

        var result = await r.Service.CreateWithCreatorEventAsync("CT-M3B", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null);

        Assert.True(result.AuthorizationIndeterminate);
        Assert.Empty(r.Ctx.Contracts);
    }

    [Theory]
    [InlineData(false, AccessDecisionReason.Allow)]              // Allowed=false با دلیل Allow: ناسازگار
    [InlineData(true, AccessDecisionReason.DeniedByScope)]       // Allowed=true با دلیل رد: ناسازگار
    [InlineData(true, AccessDecisionReason.DeniedBySoD)]
    public async Task Service_InconsistentOrNonAllowDecisions_NeverCreateAContract(bool allowed, AccessDecisionReason reason)
    {
        var fixedDecision = new FixedAccessControl(new AccessDecision { Allowed = allowed, Reason = reason, Detail = "test" });
        var r = await BuildAsync($"{nameof(Service_InconsistentOrNonAllowDecisions_NeverCreateAContract)}_{allowed}_{reason}", fixedDecision);

        var result = await r.Service.CreateWithCreatorEventAsync("CT-M4", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null);

        Assert.False(result.Success);
        Assert.True(result.AuthorizationDenied);
        Assert.Empty(r.Ctx.Contracts);
    }

    [Fact]
    public async Task Service_ValidationFailures_DoNotEvenReachAuthorization()
    {
        var counting = new FixedAccessControl(AccessDecision.Allow("x"));
        var r = await BuildAsync(nameof(Service_ValidationFailures_DoNotEvenReachAuthorization), counting);

        var blank = await r.Service.CreateWithCreatorEventAsync("  ", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null);
        var negative = await r.Service.CreateWithCreatorEventAsync("CT-M5", r.ProjectId, r.VendorId, null, -1m, null, null, false, r.ManagerId, null);
        var dates = await r.Service.CreateWithCreatorEventAsync("CT-M5", r.ProjectId, r.VendorId, null, 1m, DateTime.Today, DateTime.Today.AddDays(-1), false, r.ManagerId, null);

        Assert.All(new[] { blank, negative, dates }, x => { Assert.False(x.Success); Assert.False(x.AuthorizationDenied); Assert.NotNull(x.Error); });
        Assert.Equal(0, counting.Calls);
        Assert.Empty(r.Ctx.Contracts);
    }

    [Fact]
    public async Task Service_DuplicateNumbers_AreRejected_IncludingCaseAndSpaceVariants()
    {
        var r = await BuildAsync(nameof(Service_DuplicateNumbers_AreRejected_IncludingCaseAndSpaceVariants));
        Assert.True((await r.Service.CreateWithCreatorEventAsync("CT-M6", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null)).Success);

        var again = await r.Service.CreateWithCreatorEventAsync("CT-M6", r.ProjectId, r.VendorId, null, null, null, null, false, r.ManagerId, null);

        Assert.False(again.Success);
        Assert.Contains("از قبل وجود دارد", again.Error);
        Assert.Equal(1, await r.Ctx.Contracts.CountAsync());
    }

    // ------------------------------------------------------------------ مسیر قدیمی بدون کاربر
    [Fact]
    public async Task LegacyCreateAsync_StillWorks_WithoutAuthorizationOrEvent_AndCanBeRepairedByTheSharedHelper()
    {
        var r = await BuildAsync(nameof(LegacyCreateAsync_StillWorks_WithoutAuthorizationOrEvent_AndCanBeRepairedByTheSharedHelper));

        var id = await r.Service.CreateAsync("CT-M7", r.ProjectId, r.VendorId, null, 5m, null, null);

        Assert.Empty(await CreateRowsAsync(r.Ctx));                                                // بدون ردیف مجوز و بدون رویداد
        var repaired = await r.Service.EnsureCreatorEventAsync(id);
        Assert.Equal(ContractCreatorEventStatus.Repaired, repaired);                               // برای CreatedBy = manager.m
        var ev = Assert.Single(await CreateRowsAsync(r.Ctx));
        Assert.Equal(r.ManagerId, ev.UserId);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, await r.Service.EnsureCreatorEventAsync(id));
    }

    // ------------------------------------------------------------------ کنترلر
    [Fact]
    public async Task Controller_Allow_RedirectsToDetails_AndRecordsTheEventForTheCurrentUser()
    {
        var r = await BuildAsync(nameof(Controller_Allow_RedirectsToDetails_AndRecordsTheEventForTheCurrentUser));

        var result = await r.Controller.Create("CT-M8", r.ProjectId, r.VendorId, null, null, null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        var id = (int)redirect.RouteValues!["id"]!;
        Assert.Contains(await CreateRowsAsync(r.Ctx), x => x.ResourceId == id.ToString() && x.UserId == r.ManagerId && x.Decision == "Allow");
        Assert.Equal("Contract created successfully", r.Controller.TempData["Message"]);
    }

    [Fact]
    public async Task Controller_RoleGateDenial_IsForbid_AndCreatesNothing()
    {
        var r = await BuildAsync(nameof(Controller_RoleGateDenial_IsForbid_AndCreatesNothing));
        r.CurrentUser.Roles = new[] { SystemRoles.Viewer };

        Assert.IsType<ForbidResult>(await r.Controller.Create("CT-M9", r.ProjectId, r.VendorId, null, null, null, null));
        Assert.Empty(r.Ctx.Contracts);
    }

    [Fact]
    public async Task Controller_UserAwareDenial_AfterThePassingRoleGate_IsForbid_AndCreatesNothing()
    {
        // نقش نشست (ContractManager) قدیمی را رد می‌کند ولی این کاربر در AspNetUserRoles هیچ نقشی ندارد → مسیر آگاه-از-کاربر رد می‌کند
        var r = await BuildAsync(nameof(Controller_UserAwareDenial_AfterThePassingRoleGate_IsForbid_AndCreatesNothing));
        r.CurrentUser.UserId = r.NoRoleUserId;

        Assert.IsType<ForbidResult>(await r.Controller.Create("CT-M10", r.ProjectId, r.VendorId, null, null, null, null));

        Assert.Empty(r.Ctx.Contracts);
        var denial = Assert.Single(await CreateRowsAsync(r.Ctx));
        Assert.Equal("DeniedByRole", denial.Decision);
    }

    [Fact]
    public async Task Controller_MissingIdentity_IsForbid_AuditedAndCreatesNothing()
    {
        var r = await BuildAsync(nameof(Controller_MissingIdentity_IsForbid_AuditedAndCreatesNothing));
        r.CurrentUser.UserId = null;

        Assert.IsType<ForbidResult>(await r.Controller.Create("CT-M11", r.ProjectId, r.VendorId, null, null, null, null));

        Assert.Empty(r.Ctx.Contracts);
        var denial = Assert.Single(await CreateRowsAsync(r.Ctx));
        Assert.Null(denial.UserId);
        Assert.Equal("DeniedByPolicy", denial.Decision);
    }

    [Fact]
    public async Task Controller_ValidationFailure_ShowsTheMessage_AndRedirectsToCreate()
    {
        var r = await BuildAsync(nameof(Controller_ValidationFailure_ShowsTheMessage_AndRedirectsToCreate));

        var result = await r.Controller.Create("  ", r.ProjectId, r.VendorId, null, null, null, null);

        Assert.Equal("Create", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.NotNull(r.Controller.TempData["Error"]);
        Assert.Empty(r.Ctx.Contracts);
    }

    private sealed class FakeContractService : IContractService
    {
        public ContractCreateResult Result { get; set; } = new(true, null, 1);
        public Task<ContractCreateResult> CreateWithCreatorEventAsync(string contractNo, int projectId, int vendorId, string? scope, decimal? value,
            DateTime? startDate, DateTime? endDate, bool requiresInspection, int creatorUserId, string? ipAddress) => Task.FromResult(Result);
        public Task<List<ContractDto>> GetAllAsync() => throw new NotSupportedException();
        public Task<PagedResult<ContractDto>> GetPagedAsync(int page, int pageSize) => throw new NotSupportedException();
        public Task<ContractDto?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<int> CreateAsync(string contractNo, int projectId, int vendorId, string? scope, decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection = false) => throw new NotSupportedException();
        public Task ChangeStatusAsync(int contractId, string status) => throw new NotSupportedException();
        public Task<ContractCreatorEventStatus> EnsureCreatorEventAsync(int contractId, string? ipAddress = null) => throw new NotSupportedException();
    }

    private static async Task<(ContractController Controller, FakeContractService Fake)> ControllerWithFakeServiceAsync(string name)
    {
        var r = await BuildAsync(name);
        var fake = new FakeContractService();
        var runtime = new FakeWorkflowRuntime();
        var workflow = new ContractWorkflowService(r.Ctx, runtime, r.AccessControl, new FakeNumberingService());
        var controller = new ContractController(fake, workflow, runtime, r.AccessControl, r.Permissions, r.CurrentUser, r.Ctx).WithTempData();
        return (controller, fake);
    }

    [Fact]
    public async Task Controller_OutcomeUnknown_NeverSaysNotCreated()
    {
        var (controller, fake) = await ControllerWithFakeServiceAsync(nameof(Controller_OutcomeUnknown_NeverSaysNotCreated));
        fake.Result = new ContractCreateResult(false, "نتیجه‌ی ساخت قرارداد نامطمئن است", null, OutcomeUnknown: true, CreatorEvent: ContractCreatorEventStatus.OutcomeUnknown);

        var result = await controller.Create("CT-M12", 1, 1, null, null, null, null);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var message = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.Contains("نامطمئن", message);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", message);
        Assert.Null(controller.TempData["Message"]);
    }

    [Fact]
    public async Task Controller_RecoveredAfterUncertainCommit_IsSuccess_WithAnExplicitMessage()
    {
        var (controller, fake) = await ControllerWithFakeServiceAsync(nameof(Controller_RecoveredAfterUncertainCommit_IsSuccess_WithAnExplicitMessage));
        fake.Result = new ContractCreateResult(true, null, 77, Committed: true, CreatorEvent: ContractCreatorEventStatus.Recorded, RecoveredAfterUncertainCommit: true);

        var result = await controller.Create("CT-M13", 1, 1, null, null, null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(77, redirect.RouteValues!["id"]);
        Assert.Contains("uncertain", Assert.IsType<string>(controller.TempData["Message"]));
    }

    [Fact]
    public async Task Controller_Indeterminate_And_Denied_And_NothingSaved_AreAllNonSuccess()
    {
        var (controller, fake) = await ControllerWithFakeServiceAsync(nameof(Controller_Indeterminate_And_Denied_And_NothingSaved_AreAllNonSuccess));

        fake.Result = new ContractCreateResult(false, "مجوز قابل‌ارزیابی نبود", null, AuthorizationIndeterminate: true);
        var indeterminate = await controller.Create("CT-M14", 1, 1, null, null, null, null);
        Assert.Equal("Create", Assert.IsType<RedirectToActionResult>(indeterminate).ActionName);
        Assert.NotNull(controller.TempData["Error"]);

        fake.Result = new ContractCreateResult(false, "شما مجاز نیستید", null, AuthorizationDenied: true);
        Assert.IsType<ForbidResult>(await controller.Create("CT-M14", 1, 1, null, null, null, null));

        fake.Result = new ContractCreateResult(false, "ساخت قرارداد انجام نشد؛ هیچ تغییری ذخیره نشد.", null);
        var nothingSaved = await controller.Create("CT-M14", 1, 1, null, null, null, null);
        Assert.Equal("Create", Assert.IsType<RedirectToActionResult>(nothingSaved).ActionName);
    }
}
