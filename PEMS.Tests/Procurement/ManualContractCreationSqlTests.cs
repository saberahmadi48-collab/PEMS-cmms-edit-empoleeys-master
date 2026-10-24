using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
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
/// P0.4 / H-1 - اثبات روی SQL Server واقعیِ یک‌بارمصرف (PEMS_TEST_P03_*، هرگز PEMSDB) برای مسیر دستی ساخت قرارداد. هر تست دیتابیس خودش را با Migrationهای واقعی می‌سازد
/// و فقط همان را Drop می‌کند. تمام ادعاهای وضعیت ماندگار با SQL خام روی اتصال جدا خوانده می‌شوند. شماره‌ی ذخیره‌ی فیزیکی در یک فراخوانی سرویس:
/// ۱-۲ = پیش‌بررسی مجوز (ردیف AuthorizationAuditLog با ResourceId = null + AuditLog عمومی‌اش؛ خارج از تراکنش)، ۳ = A (قرارداد)، ۴ = B (AuditLog عمومیِ آن)،
/// ۵ = C (رویداد ایجادکننده)، ۶ = D (AuditLog عمومیِ رویداد). «Commit نامطمئن» در M7 شبیه‌سازی است؛ تست‌های G* قطع اتصال را «واقعاً» (KILL نشست روی دیتابیس
/// یک‌بارمصرف) اجرا می‌کنند - همان‌جا دقیقاً نوشته شده چه چیزی واقعی است و چه چیزی نه.
/// </summary>
public sealed class ManualContractCreationSqlTests : IAsyncLifetime
{
    private DisposableSqlDatabase _db = null!;
    private readonly FakeNumberingService _numbering = new();
    private int _seq;

    public async Task InitializeAsync() => _db = await DisposableSqlDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // =============================================================== داده‌ی پایه
    private sealed record Seed(int ManagerOne, int ManagerTwo, int Viewer, int ProjectId, int VendorId);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
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

        async Task<int> AddUser(string userName, string role)
        {
            var user = new ApplicationUser { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), Email = $"{userName}@pems.local", FullName = userName };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = ctx.Roles.First(r => r.Name == role).Id });
            await ctx.SaveChangesAsync();
            return user.Id;
        }

        var m1 = await AddUser("manager.one", SystemRoles.ContractManager);
        var m2 = await AddUser("manager.two", SystemRoles.ContractManager);
        var viewer = await AddUser("viewer.one", SystemRoles.Viewer);

        var project = new Project { ProjectCode = "P-MAN", ProjectName = "Project Manual", IsActive = true };
        var vendor = new Vendor { Name = "Vendor Manual", IsActive = true };
        ctx.Projects.Add(project);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();
        return new Seed(m1, m2, viewer, project.ProjectId, vendor.VendorId);
    }

    private sealed record Env(PEMSDbContext Ctx, ContractService Service, AccessControlService AccessControl, ContractController Controller, FakeCurrentUserService CurrentUser);

    private Env NewEnv(string? userName, int? userId, string? role = SystemRoles.ContractManager, IEnumerable<IInterceptor>? interceptors = null,
        Func<ExecutionStrategyDependencies, IExecutionStrategy>? strategy = null, int lockTimeoutMs = 15000)
    {
        var ctx = _db.NewContext(interceptors, strategy);
        ctx.CurrentUserName = userName;
        var accessControl = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, accessControl);
        var service = new ContractService(ctx, accessControl, null, lockTimeoutMs);
        var currentUser = new FakeCurrentUserService { UserId = userId, Roles = role == null ? Array.Empty<string>() : new[] { role } };
        var runtime = new FakeWorkflowRuntime();
        var workflow = new ContractWorkflowService(ctx, runtime, accessControl, _numbering);
        var controller = new ContractController(service, workflow, runtime, accessControl, permissions, currentUser, ctx).WithTempData();
        return new Env(ctx, service, accessControl, controller, currentUser);
    }

    /// <summary>مناقصه‌ی Awarded تازه (برای تست‌های هم‌زمانی بین مسیر دستی و مسیر مناقصه).</summary>
    private async Task<int> AwardedTenderAsync(Seed seed)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var ctx = _db.NewContext();
        ctx.CurrentUserName = "seed.user";
        var package = new ProcurementPackage { PackageNumber = $"PP-M{n:000}", ProjectId = seed.ProjectId, Title = $"Pumps {n}", IsActive = true };
        var vendor = new Vendor { Name = $"Qualified Vendor M{n}", IsActive = true, QualificationStatus = "Qualified", QualificationExpiryDate = DateTime.Now.AddYears(1) };
        ctx.ProcurementPackages.Add(package);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var service = new TenderService(ctx, _numbering);
        var tenderId = await service.CreateAsync(package.ProcurementPackageId, $"Pump Tender M{n}", null, null, false, null, null, null, null);
        await service.InviteVendorAsync(tenderId, vendor.VendorId);
        await service.OpenForBiddingAsync(tenderId);
        var bid = await service.SubmitBidAsync(tenderId, vendor.VendorId, 100000m, "USD", null, null);
        await service.CloseBiddingAsync(tenderId);
        Assert.True((await service.RecordBidEvaluationAsync(bid.BidId!.Value, "Technical", null, "Engineering", "Passed", null)).Success);
        Assert.True((await service.RecordBidEvaluationAsync(bid.BidId!.Value, "Commercial", null, "Finance", "Passed", null)).Success);
        var award = await service.SelectWinnerAsync(tenderId, bid.BidId!.Value, null);
        Assert.True(award.Success, award.Error);
        return tenderId;
    }

    // =============================================================== خواندن وضعیت ماندگار (SQL خام، اتصال جدا)
    private Task<int> ContractsAsync(string? contractNo = null)
        => contractNo == null
            ? _db.ScalarIntAsync("SELECT COUNT(*) FROM Contracts")
            : _db.ScalarIntAsync("SELECT COUNT(*) FROM Contracts WHERE ContractNo = @n", ("@n", contractNo));

    /// <summary>رویدادهای ایجادکننده (ResourceId = شناسه‌ی قرارداد).</summary>
    private Task<List<(int? UserId, string ResourceId)>> EventsAsync(int? contractId = null)
        => _db.QueryAsync(
            "SELECT UserId, ResourceId FROM AuthorizationAuditLogs WHERE ResourceName='Contracts' AND ActionName='Create' AND Decision='Allow' AND ResourceId IS NOT NULL"
            + (contractId.HasValue ? " AND ResourceId = @c" : ""),
            r => (r.IsDBNull(0) ? (int?)null : r.GetInt32(0), r.GetString(1)),
            contractId.HasValue ? new[] { ("@c", (object)contractId.Value.ToString()) } : Array.Empty<(string, object)>());

    private Task<int> PreAuthRowsAsync(string decision)
        => _db.ScalarIntAsync("SELECT COUNT(*) FROM AuthorizationAuditLogs WHERE ResourceName='Contracts' AND ActionName='Create' AND ResourceId IS NULL AND Decision = @d", ("@d", decision));

    private Task<int> GenericAuditAsync(string entityName, string? action = null)
        => _db.ScalarIntAsync("SELECT COUNT(*) FROM AuditLogs WHERE EntityName = @e" + (action != null ? " AND Action = @a" : ""),
            action != null ? new[] { ("@e", (object)entityName), ("@a", (object)action) } : new[] { ("@e", (object)entityName) });

    private async Task<int?> ContractIdAsync(string contractNo)
        => (await _db.QueryAsync("SELECT ContractId FROM Contracts WHERE ContractNo = @n", r => (int?)r.GetInt32(0), ("@n", contractNo))).SingleOrDefault();

    private async Task<int?> LinkedAsync(int tenderId)
        => (await _db.QueryAsync("SELECT ContractId FROM Tenders WHERE TenderId = @t", r => r.IsDBNull(0) ? (int?)null : r.GetInt32(0), ("@t", tenderId))).Single();

    private async Task AssertCommittedOnceAsync(string contractNo, int creatorUserId, string expectedCreatedBy)
    {
        Assert.Equal(1, await ContractsAsync());
        var id = await ContractIdAsync(contractNo);
        Assert.NotNull(id);
        var ev = Assert.Single(await EventsAsync());
        Assert.Equal(creatorUserId, ev.UserId);
        Assert.Equal(id!.Value.ToString(), ev.ResourceId);
        Assert.Equal(1, await GenericAuditAsync("Contract", "Insert"));
        Assert.Equal(expectedCreatedBy, (await _db.QueryAsync("SELECT CreatedBy FROM Contracts", r => r.GetString(0))).Single());
    }

    private async Task AssertNoContractAsync()
    {
        Assert.Equal(0, await ContractsAsync());
        Assert.Empty(await EventsAsync());
        Assert.Equal(0, await GenericAuditAsync("Contract"));
    }

    // =============================================================== 1 : مسیر موفق
    [Fact]
    public async Task M1_HappyPath_CommitsContractGenericAudits_AndCreatorEventForTheActualCreator()
    {
        var seed = await SeedAsync();
        var env = NewEnv("manager.one", seed.ManagerOne);

        var result = await env.Service.CreateWithCreatorEventAsync(" CT-M1 ", seed.ProjectId, seed.VendorId, "scope", 100m, null, null, false, seed.ManagerOne, "10.0.0.1");

        Assert.True(result.Success, result.Error);
        Assert.True(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.Recorded, result.CreatorEvent);
        await AssertCommittedOnceAsync("CT-M1", seed.ManagerOne, "manager.one");
        Assert.Equal(1, await PreAuthRowsAsync("Allow"));                                   // پیش‌بررسی مجوز، متمایز از رویداد ایجادکننده
        Assert.Equal(2, await GenericAuditAsync("AuthorizationAuditLog", "Insert"));        // ردیف پیش‌بررسی + رویداد
    }

    // =============================================================== 2 : مجوز رد شده، پیش از هر Commit
    [Fact]
    public async Task M2_AuthorizationDenied_NeverCommitsAContract_ButTheDenialIsPersisted()
    {
        var seed = await SeedAsync();
        var env = NewEnv("viewer.one", seed.Viewer, role: SystemRoles.Viewer);

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M2", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.Viewer, null);

        Assert.True(result.AuthorizationDenied);
        Assert.False(result.Success);
        Assert.False(result.Committed);
        await AssertNoContractAsync();
        Assert.Equal(1, await PreAuthRowsAsync("DeniedByRole"));
        Assert.Equal(0, await PreAuthRowsAsync("Allow"));
    }

    // =============================================================== 3/4 : شکست هر ذخیره‌ی فیزیکی داخل تراکنش = Rollback کامل، سپس تلاش مجدد
    private async Task FailureRollsBack_ThenRetrySucceeds(int failOn, string tag)
    {
        var seed = await SeedAsync();
        var faults = new NthSaveFaultInterceptor();
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { faults });
        faults.Arm(failOn);

        var failed = await env.Service.CreateWithCreatorEventAsync("CT-" + tag, seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, "10.0.0.1");

        Assert.False(failed.Success);
        Assert.False(failed.Committed);
        Assert.False(failed.OutcomeUnknown);
        Assert.False(failed.AuthorizationDenied);
        Assert.Contains("هیچ تغییری ذخیره نشد", failed.Error);
        await AssertNoContractAsync();                                                       // قرارداد، رویداد و AuditLog قرارداد: صفر
        Assert.Equal(1, await PreAuthRowsAsync("Allow"));                                     // ردیف پیش‌بررسی (خارج از تراکنش) باقی می‌ماند
        Assert.Equal(1, await GenericAuditAsync("AuthorizationAuditLog", "Insert"));          // فقط AuditLog همان ردیف پیش‌بررسی
        Assert.DoesNotContain(env.Ctx.ChangeTracker.Entries(), e => e.State == EntityState.Added || e.Entity is Contract);

        faults.Disarm();
        var retry = await env.Service.CreateWithCreatorEventAsync("CT-" + tag, seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, "10.0.0.1");
        Assert.True(retry.Success, retry.Error);
        await AssertCommittedOnceAsync("CT-" + tag, seed.ManagerOne, "manager.one");
        Assert.Equal(2, await PreAuthRowsAsync("Allow"));                                     // هر تلاش یک ردیف پیش‌بررسی جدا

        var third = await env.Service.CreateWithCreatorEventAsync("CT-" + tag, seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, "10.0.0.1");
        Assert.False(third.Success);
        Assert.Contains("از قبل وجود دارد", third.Error);
        await AssertCommittedOnceAsync("CT-" + tag, seed.ManagerOne, "manager.one");
    }

    [Fact] public Task M3a_FailureAtBusinessSave_A_RollsBack_ThenRetrySucceeds() => FailureRollsBack_ThenRetrySucceeds(3, "m3a");
    [Fact] public Task M4a_FailureAtGenericAuditSave_B_RollsBackTheBusinessRow_ThenRetrySucceeds() => FailureRollsBack_ThenRetrySucceeds(4, "m4a");
    [Fact] public Task M3b_FailureAtCreatorEventSave_C_RollsBackTheContract_ThenRetrySucceeds() => FailureRollsBack_ThenRetrySucceeds(5, "m3b");
    [Fact] public Task M4b_FailureAtEventGenericAuditSave_D_RollsBackTheEventAndContract_ThenRetrySucceeds() => FailureRollsBack_ThenRetrySucceeds(6, "m4b");

    // =============================================================== 5 : هم‌زمانی ContractNo بین مسیر دستی و مسیر مناقصه
    [Theory]
    [InlineData(2, 0)]     // دو دستی
    [InlineData(1, 1)]     // یک دستی + یک مناقصه
    [InlineData(2, 2)]     // دو دستی + دو مناقصه (دو مناقصه‌ی مختلف)
    public async Task M5_SameContractNo_AcrossManualAndTenderPaths_CreatesExactlyOneContract(int manualCallers, int tenderCallers)
    {
        const int iterations = 12;
        var seed = await SeedAsync();
        var variants = new[] { "CT-M5-{0}", " ct-m5-{0} ", "Ct-M5-{0}  " };
        var users = new[] { (Id: seed.ManagerOne, Name: "manager.one"), (Id: seed.ManagerTwo, Name: "manager.two") };

        for (var iter = 0; iter < iterations; iter++)
        {
            var tenders = new List<int>();
            for (var t = 0; t < tenderCallers; t++) tenders.Add(await AwardedTenderAsync(seed));
            var gate = new TaskCompletionSource();
            var calls = new List<(string Kind, int UserId, int? TenderId, Task<(bool Success, string? Error)> Task)>();

            for (var m = 0; m < manualCallers; m++)
            {
                var u = users[m % 2]; var no = string.Format(variants[m % variants.Length], iter);
                calls.Add(("manual", u.Id, null, Task.Run(async () =>
                {
                    var env = NewEnv(u.Name, u.Id); await using var _ = env.Ctx;
                    await gate.Task;
                    var r = await env.Service.CreateWithCreatorEventAsync(no, seed.ProjectId, seed.VendorId, null, null, null, null, false, u.Id, null);
                    return (r.Success, r.Error);
                })));
            }
            for (var t = 0; t < tenderCallers; t++)
            {
                var u = users[(t + 1) % 2]; var no = string.Format(variants[(t + manualCallers) % variants.Length], iter); var tenderId = tenders[t];
                calls.Add(("tender", u.Id, tenderId, Task.Run(async () =>
                {
                    var ctx = _db.NewContext(); ctx.CurrentUserName = u.Name; await using var _ = ctx;
                    var service = new TenderService(ctx, _numbering);
                    await gate.Task;
                    var r = await service.CreateContractFromWinningBidAsync(tenderId, no, null, null, u.Id, null);
                    return (r.Success, r.Error);
                })));
            }

            gate.SetResult();
            var results = await Task.WhenAll(calls.Select(c => c.Task));

            var key = $"CT-M5-{iter}";
            Assert.Equal(1, await ContractsAsync(key));                                      // دقیقاً یک قرارداد با این شماره، نادیده‌گرفتن حروف/فاصله
            var winners = results.Select((r, i) => (r, i)).Where(x => x.r.Success).ToList();
            Assert.True(winners.Count == 1, $"iteration {iter}: expected one winner, got {winners.Count}: {string.Join(" | ", results.Select(r => r.Error))}");
            foreach (var loser in results.Where(r => !r.Success))
                Assert.Contains("از قبل وجود دارد", loser.Error);                            // هیچ استثنا/ذخیره‌ی ناقص؛ «شماره‌ی تکراری»
            var id = (await ContractIdAsync(key))!.Value;
            var ev = Assert.Single(await EventsAsync(id));
            Assert.Equal(calls[winners[0].i].UserId, ev.UserId);                             // رویداد برای برنده‌ی واقعی
            var linked = 0;
            foreach (var tid in tenders) if (await LinkedAsync(tid) != null) linked++;
            Assert.Equal(calls[winners[0].i].Kind == "tender" ? 1 : 0, linked);
        }
        Assert.Equal(iterations, await ContractsAsync());
        Assert.Equal(iterations, (await EventsAsync()).Count);
    }

    // =============================================================== 6 : ایجادکننده‌ی دستی نمی‌تواند خودش تأیید کند
    [Fact]
    public async Task M6_ManualCreator_CannotApproveTheirOwnContract_ButAnotherUserCan()
    {
        var seed = await SeedAsync();
        var env = NewEnv("manager.one", seed.ManagerOne);
        var created = await env.Service.CreateWithCreatorEventAsync("CT-M6", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.True(created.Success, created.Error);

        var self = await env.AccessControl.AuthorizeAsync(new AccessControlRequest { UserId = seed.ManagerOne, ResourceName = "Contracts", ActionName = "Approve", ResourceId = created.ContractId.ToString() });
        var other = await env.AccessControl.AuthorizeAsync(new AccessControlRequest { UserId = seed.ManagerTwo, ResourceName = "Contracts", ActionName = "Approve", ResourceId = created.ContractId.ToString() });

        Assert.False(self.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, self.Reason);
        Assert.True(other.Allowed, "control: a different user with the same role must be allowed");
    }

    // =============================================================== 7 : Commit نامطمئن (شبیه‌سازی‌شده؛ G* قطع واقعی را اجرا می‌کنند)
    [Fact]
    public async Task M7a_CommitAckLost_AfterRealCommit_RetryConfirmsWithoutDuplicating()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowAfterRealCommit = 1 };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M7A", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.True(result.Success, result.Error);
        Assert.True(result.RecoveredAfterUncertainCommit);
        Assert.Equal(1, probe.RealCommits);
        await AssertCommittedOnceAsync("CT-M7A", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task M7b_CommitAckLost_RetriesExhausted_VerificationConfirmsTheCommittedState()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowAfterRealCommit = 1, TransientReadFailuresAfterCommitFailure = 3 };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe), new ReadFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d, 3));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M7B", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.Equal(3, probe.ReadFailuresInjected);
        Assert.True(result.Success, result.Error);
        Assert.True(result.RecoveredAfterUncertainCommit);
        Assert.False(result.OutcomeUnknown);
        await AssertCommittedOnceAsync("CT-M7B", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task M7c_CommitAckLost_AndVerificationFails_ReportsUnknown_NeverNotCreated()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowAfterRealCommit = 1, TransientReadFailuresAfterCommitFailure = 3, FailReadsNonTransientAfterBudget = true };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe), new ReadFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d, 3));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M7C", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.True(result.OutcomeUnknown);
        Assert.False(result.Success);
        Assert.False(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.OutcomeUnknown, result.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Contains("نامطمئن", result.Error);
        await AssertCommittedOnceAsync("CT-M7C", seed.ManagerOne, "manager.one");            // واقعیت: Commit شده بود

        var fresh = NewEnv("manager.one", seed.ManagerOne);
        var again = await fresh.Service.CreateWithCreatorEventAsync("CT-M7C", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.False(again.Success);                                                          // قرارداد تکراری ساخته نمی‌شود
        Assert.Contains("از قبل وجود دارد", again.Error);
        await AssertCommittedOnceAsync("CT-M7C", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task M7d_CommitFailsBeforeReachingTheServer_RollbackAndRetryCreatesExactlyOne()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowBeforeRealCommit = 1 };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M7D", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.True(result.Success, result.Error);
        Assert.False(result.RecoveredAfterUncertainCommit);
        Assert.Equal(1, probe.RealCommits);
        await AssertCommittedOnceAsync("CT-M7D", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task M7e_EveryCommitFailsBeforeTheServer_RetriesExhausted_IsVerifiedNotCreated()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowBeforeRealCommit = 4 };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d, 3));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-M7E", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Equal(0, probe.RealCommits);
        await AssertNoContractAsync();
    }

    // =============================================================== 8 : کنترلر روی SQL واقعی (Allow / Deny / استثنایی)
    [Fact]
    public async Task M8a_Controller_Allow_CreatesTheContractAndRedirectsToDetails()
    {
        var seed = await SeedAsync();
        var env = NewEnv("manager.one", seed.ManagerOne);

        var result = await env.Controller.Create("CT-M8A", seed.ProjectId, seed.VendorId, null, null, null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        await AssertCommittedOnceAsync("CT-M8A", seed.ManagerOne, "manager.one");
        Assert.Equal(await ContractIdAsync("CT-M8A"), (int)redirect.RouteValues!["id"]!);
    }

    [Fact]
    public async Task M8b_Controller_Deny_UserAwareDenialAfterAPassingRoleGate_IsForbid_AndNothingIsCommitted()
    {
        var seed = await SeedAsync();
        // نقش نشست ContractManager است (دروازه‌ی قدیمی عبور می‌کند)، ولی این هویت (Viewer در AspNetUserRoles) در مسیر آگاه-از-کاربر مجوز ندارد
        var env = NewEnv("viewer.one", seed.Viewer, role: SystemRoles.ContractManager);

        Assert.IsType<ForbidResult>(await env.Controller.Create("CT-M8B", seed.ProjectId, seed.VendorId, null, null, null, null));

        await AssertNoContractAsync();
        Assert.Equal(1, await PreAuthRowsAsync("DeniedByRole"));
    }

    [Fact]
    public async Task M8c_Controller_Exceptional_SaveFailureInsideTheTransaction_ShowsNothingSaved_AndCommitsNothing()
    {
        var seed = await SeedAsync();
        var faults = new NthSaveFaultInterceptor();
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { faults });
        faults.Arm(5);                                                                       // C: ردیف رویداد ایجادکننده

        var result = await env.Controller.Create("CT-M8C", seed.ProjectId, seed.VendorId, null, null, null, null);

        Assert.Equal("Create", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("هیچ تغییری ذخیره نشد", Assert.IsType<string>(env.Controller.TempData["Error"]));
        await AssertNoContractAsync();
    }

    [Fact]
    public async Task M8d_Controller_UncertainCommit_UnknownOutcomeIsNeverReportedAsNotCreated()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowAfterRealCommit = 1, TransientReadFailuresAfterCommitFailure = 3, FailReadsNonTransientAfterBudget = true };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe), new ReadFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d, 3));

        var result = await env.Controller.Create("CT-M8D", seed.ProjectId, seed.VendorId, null, null, null, null);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var message = Assert.IsType<string>(env.Controller.TempData["Error"]);
        Assert.Contains("نامطمئن", message);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", message);
        await AssertCommittedOnceAsync("CT-M8D", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task M8e_Controller_RecoveredAfterUncertainCommit_IsSuccess_WithAnExplicitMessage()
    {
        var seed = await SeedAsync();
        var probe = new CommitProbe { ThrowAfterRealCommit = 1 };
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe) }, strategy: d => new TestRetryingStrategy(d));

        var result = await env.Controller.Create("CT-M8E", seed.ProjectId, seed.VendorId, null, null, null, null);

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("uncertain", Assert.IsType<string>(env.Controller.TempData["Message"]));
        await AssertCommittedOnceAsync("CT-M8E", seed.ManagerOne, "manager.one");
    }

    // =============================================================== 9 : مهلت قفل شماره‌ی قرارداد (قفل مشترک با مسیر مناقصه)
    private static async Task<(SqlConnection Conn, SqlTransaction Tx)> HoldLockAsync(string connectionString, string resource)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;";
        cmd.Parameters.AddWithValue("@res", resource);
        Assert.True((int)(await cmd.ExecuteScalarAsync())! >= 0, "the test could not take the application lock it wants to hold");
        return (conn, tx);
    }

    [Fact]
    public async Task M9_ContractNoLockHeldElsewhere_ManualCreationTimesOutWithoutCommitting_ThenRecovers()
    {
        var seed = await SeedAsync();
        var env = NewEnv("manager.one", seed.ManagerOne, lockTimeoutMs: 700);
        var (conn, tx) = await HoldLockAsync(_db.ConnectionString, ContractCreationLocks.ContractNoResource("  ct-m9 "));
        try
        {
            var sw = Stopwatch.StartNew();
            var busy = await env.Service.CreateWithCreatorEventAsync("CT-M9", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
            sw.Stop();

            Assert.False(busy.Success);
            Assert.False(busy.Committed);
            Assert.False(busy.OutcomeUnknown);
            Assert.Contains("در حال پردازش", busy.Error);
            Assert.InRange(sw.ElapsedMilliseconds, 500, 12000);
            await AssertNoContractAsync();

            // مسیر قدیمیِ بدون کاربر هم همان قفل را می‌گیرد (دیگر راه دورزدن نیست)
            await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.CreateAsync("CT-M9", seed.ProjectId, seed.VendorId, null, null, null, null));
            await AssertNoContractAsync();
        }
        finally { tx.Rollback(); await tx.DisposeAsync(); await conn.DisposeAsync(); }

        var ok = await env.Service.CreateWithCreatorEventAsync("CT-M9", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.True(ok.Success, ok.Error);
        await AssertCommittedOnceAsync("CT-M9", seed.ManagerOne, "manager.one");
    }

    // =============================================================== 10 : ترمیم و تشخیص قراردادهای بدون رویداد (دستی/قدیمی)
    /// <summary>نسخه‌ی «همه‌ی قراردادها» (بدون Join با Tenders) - قراردادهای دستی را هم می‌بیند؛ فقط‌خواندنی.</summary>
    private const string DetectionQueryAllContracts =
        "SELECT c.ContractId, c.ContractNo, c.CreatedBy FROM Contracts c " +
        "WHERE NOT EXISTS (SELECT 1 FROM AuthorizationAuditLogs l WHERE l.ResourceName = 'Contracts' AND l.ActionName = 'Create' " +
        "AND l.ResourceId = CONVERT(nvarchar(50), c.ContractId) AND l.Decision = 'Allow');";

    [Fact]
    public async Task M10_LegacyManualContractWithoutAnEvent_IsDetected_RepairedOnce_AndThenSoDProtected()
    {
        var seed = await SeedAsync();
        var legacy = NewEnv("manager.one", seed.ManagerOne);
        var id = await legacy.Service.CreateAsync("CT-M10", seed.ProjectId, seed.VendorId, null, null, null, null);       // مسیر قدیمی: بدون رویداد
        await legacy.Ctx.DisposeAsync();
        Assert.Empty(await EventsAsync(id));

        var found = Assert.Single(await _db.QueryAsync(DetectionQueryAllContracts, r => (Id: r.GetInt32(0), No: r.GetString(1), By: r.IsDBNull(2) ? null : r.GetString(2))));
        Assert.Equal(id, found.Id);
        Assert.Equal("manager.one", found.By);

        var blind = NewEnv("manager.two", seed.ManagerTwo);
        var before = await blind.AccessControl.AuthorizeAsync(new AccessControlRequest { UserId = seed.ManagerOne, ResourceName = "Contracts", ActionName = "Approve", ResourceId = id.ToString() });
        Assert.True(before.Allowed, "documented gap: without the creator event SoD cannot see the creator");

        // ترمیم هم‌زمان توسط چند کاربر دیگر: دقیقاً یک رویداد، برای ایجادکننده‌ی اصلی
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            var env = NewEnv("manager.two", seed.ManagerTwo); await using var __ = env.Ctx;
            await gate.Task;
            return await env.Service.EnsureCreatorEventAsync(id);
        })).ToList();
        gate.SetResult();
        var statuses = await Task.WhenAll(tasks);
        Assert.Contains(ContractCreatorEventStatus.Repaired, statuses);
        var ev = Assert.Single(await EventsAsync(id));
        Assert.Equal(seed.ManagerOne, ev.UserId);

        Assert.Empty(await _db.QueryAsync(DetectionQueryAllContracts, r => r.GetInt32(0)));
        var after = await blind.AccessControl.AuthorizeAsync(new AccessControlRequest { UserId = seed.ManagerOne, ResourceName = "Contracts", ActionName = "Approve", ResourceId = id.ToString() });
        Assert.False(after.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, after.Reason);
    }

    // =============================================================== G : قطع «واقعی» اتصال (KILL نشست روی دیتابیس یک‌بارمصرف، لحظه‌ی Commit)
    [Fact]
    public async Task G1_RealSessionKillAtCommit_ManualPath_RollsBackOnTheServer_AndTheRetryCreatesExactlyOne()
    {
        var seed = await SeedAsync();
        var master = new SqlConnectionStringBuilder(_db.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var killer = new KillSessionAtCommitInterceptor(master, _db.Name);
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { killer }, strategy: d => new RetryAnySqlExceptionStrategy(d));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-G1", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.True(killer.Kills == 1, $"the session was not killed; service result: success={result.Success} denied={result.AuthorizationDenied} indeterminate={result.AuthorizationIndeterminate} error={result.Error}");                                                       // نشست واقعاً کشته شد
        Assert.NotNull(killer.LastCommitFailure);                                            // خطای واقعیِ Commit از SqlClient
        Assert.StartsWith("Microsoft.Data.SqlClient.SqlException", killer.LastCommitFailure);
        Assert.True(result.Success, result.Error);
        Assert.False(result.RecoveredAfterUncertainCommit);                                  // سرور Commit را اعمال نکرد (Rollback)؛ تلاش دوم ساخت
        await AssertCommittedOnceAsync("CT-G1", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task G2_RealSessionKillAtCommit_ManualPath_WithoutRetries_IsVerifiedNotCreated_NotAServerError()
    {
        var seed = await SeedAsync();
        var master = new SqlConnectionStringBuilder(_db.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var killer = new KillSessionAtCommitInterceptor(master, _db.Name);
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { killer }, strategy: d => new NoRetryStrategy(d));

        var result = await env.Service.CreateWithCreatorEventAsync("CT-G2", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);

        Assert.True(killer.Kills == 1, $"the session was not killed; service result: success={result.Success} denied={result.AuthorizationDenied} indeterminate={result.AuthorizationIndeterminate} error={result.Error}");
        Assert.StartsWith("Microsoft.Data.SqlClient.SqlException", killer.LastCommitFailure);
        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);                                                 // خواندن تأییدی با اتصال تازه ممکن بود و «چیزی Commit نشده» را اثبات کرد
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        await AssertNoContractAsync();

        var retry = await NewEnv("manager.one", seed.ManagerOne).Service.CreateWithCreatorEventAsync("CT-G2", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.True(retry.Success, retry.Error);
        await AssertCommittedOnceAsync("CT-G2", seed.ManagerOne, "manager.one");
    }

    [Fact]
    public async Task G3_RealSessionKillAtCommit_TenderPath_WithoutRetries_IsVerifiedNotCreated_ThenRetrySucceeds()
    {
        var seed = await SeedAsync();
        var tenderId = await AwardedTenderAsync(seed);
        var master = new SqlConnectionStringBuilder(_db.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var killer = new KillSessionAtCommitInterceptor(master, _db.Name);
        var ctx = _db.NewContext(new IInterceptor[] { killer }, d => new NoRetryStrategy(d));
        ctx.CurrentUserName = "manager.one";
        var tender = new TenderService(ctx, _numbering);

        var result = await tender.CreateContractFromWinningBidAsync(tenderId, "CT-G3", null, null, seed.ManagerOne, null);

        Assert.True(killer.Kills == 1, $"the session was not killed; service result: success={result.Success} error={result.Error}");
        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Equal(0, await ContractsAsync());
        Assert.Null(await LinkedAsync(tenderId));
        Assert.Empty(await EventsAsync());

        var retry = await tender.CreateContractFromWinningBidAsync(tenderId, "CT-G3", null, null, seed.ManagerOne, null);
        Assert.True(retry.Success, retry.Error);
        Assert.Equal(1, await ContractsAsync());
        Assert.Single(await EventsAsync());
    }

    // =============================================================== P0.5 / H-1 : خواندن تأییدیِ امن با READ_COMMITTED_SNAPSHOT=ON
    private Contract NewUncommittedContract(Seed seed, string contractNo) => new()
    {
        ContractNo = contractNo, ProjectId = seed.ProjectId, VendorId = seed.VendorId, Status = "Draft", CreatedDate = DateTime.Now
    };

    private string MasterConnection => new SqlConnectionStringBuilder(_db.ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    [Fact]
    public async Task R1_TheDisposableDatabase_ReallyHasReadCommittedSnapshotOn_IndependentOfTheModelDatabase()
    {
        // ON صریح روی خود این دیتابیس (در CreateAsync تنظیم و تأیید می‌شود)؛ model سرور جدا و بی‌ارتباط است
        Assert.True(await _db.IsRcsiOnAsync());
        Assert.Equal(1, await _db.ScalarIntAsync("SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()"));
        Assert.StartsWith(DisposableSqlDatabase.Prefix, _db.Name);
    }

    [Fact]
    public async Task R2_PlainReadIsBlindToAnOpenTransaction_ButTheLockProtectedReadWaitsForIt_ThenSeesTheCommit()
    {
        var seed = await SeedAsync();
        const string no = "CT-R2";
        await using var holder = await InFlightHolder.StartAsync(_db, ContractCreationLocks.ContractNoResource(no), NewUncommittedContract(seed, no));

        // ۱) خواندن ساده زیر RCSI منتظر نمی‌ماند و قرارداد Commit‌نشده را نمی‌بیند: «چیزی پیدا نشد» دلیل «Commit نشد» نیست
        var sw = Stopwatch.StartNew();
        Assert.Equal(0, await ContractsAsync(no));
        Assert.True(sw.ElapsedMilliseconds < 5000, "an RCSI read must not block behind the open transaction");

        // ۲) خواندن زیر قفل نمی‌تواند تأیید کند (تراکنش اصلی هنوز باز است) و نتیجه‌ای نمی‌دهد
        await using var ctx = _db.NewContext();
        var scope = new ContractCreationScope(ctx, null, 700);
        var blocked = await scope.TryReadUnderLockAsync(ContractCreationLocks.ContractNoResource(no), () => ctx.Contracts.AsNoTracking().CountAsync(c => c.ContractNo == no));
        Assert.False(blocked.Verified);

        // ۳) بعد از Commit تراکنش اصلی، همان خواندن موفق است و Commit را می‌بیند
        await holder.CommitAsync();
        var after = await scope.TryReadUnderLockAsync(ContractCreationLocks.ContractNoResource(no), () => ctx.Contracts.AsNoTracking().CountAsync(c => c.ContractNo == no));
        Assert.True(after.Verified);
        Assert.Equal(1, after.Value);
    }

    [Fact]
    public async Task R3_LockProtectedRead_AfterTheOriginalSessionIsReallyKilled_ProvesItNeverCommitted()
    {
        var seed = await SeedAsync();
        const string no = "CT-R3";
        await using var holder = await InFlightHolder.StartAsync(_db, ContractCreationLocks.ContractNoResource(no), NewUncommittedContract(seed, no));
        await using var ctx = _db.NewContext();
        var scope = new ContractCreationScope(ctx, null, 10000);

        // خواندنِ تأییدی شروع می‌شود و پشت قفل منتظر می‌ماند؛ سپس نشست تراکنش اصلی «واقعاً» کشته می‌شود
        var sw = Stopwatch.StartNew();
        var pending = scope.TryReadUnderLockAsync(ContractCreationLocks.ContractNoResource(no), () => ctx.Contracts.AsNoTracking().CountAsync(c => c.ContractNo == no));
        await Task.Delay(600);
        Assert.False(pending.IsCompleted, "the verification must wait while the original transaction is still open");
        await holder.KillSessionAsync();

        var result = await pending;
        sw.Stop();
        Assert.True(result.Verified);
        Assert.Equal(0, result.Value);                                                         // Rollback سمت سرور: Commit نشده بود
        Assert.InRange(sw.ElapsedMilliseconds, 500, 9000);
    }

    private async Task<(ContractCreateResult Result, long PlainCountWhileOpen, InFlightHolder Holder)> RunManualWithInFlightOriginalAsync(
        Seed seed, string no, bool killHolderDuringVerification, int lockTimeoutMs)
    {
        var probe = new CommitProbe { ThrowBeforeRealCommit = 1 };          // درخواست Commit صادر می‌شود ولی شکست می‌خورد: سرنوشت تراکنش «نامعلوم» است
        InFlightHolder? holder = null;
        long plain = -1;
        var hook = new FirstReaderAfterCommitFailureHook(probe, async () =>
        {
            // دقیقاً قبل از خواندن تأییدی: «تراکنش اصلی» (نشست جدا) هنوز باز است، قفل شماره را دارد و قرارداد Commit‌نشده دارد
            holder = await InFlightHolder.StartAsync(_db, ContractCreationLocks.ContractNoResource(no), NewUncommittedContract(seed, no));
            plain = await ContractsAsync(no);
            if (killHolderDuringVerification)
                _ = Task.Run(async () => { await Task.Delay(600); await holder.KillSessionAsync(); });
        });
        var env = NewEnv("manager.one", seed.ManagerOne, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe), hook },
            strategy: d => new NoRetryStrategy(d), lockTimeoutMs: lockTimeoutMs);

        var result = await env.Service.CreateWithCreatorEventAsync(no, seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        return (result, plain, holder!);
    }

    [Fact]
    public async Task R4_Manual_CommitAttempted_WhileTheOriginalTransactionIsStillOpen_IsOutcomeUnknown_NeverNothingSaved()
    {
        var seed = await SeedAsync();
        var (result, plain, holder) = await RunManualWithInFlightOriginalAsync(seed, "CT-R4", killHolderDuringVerification: false, lockTimeoutMs: 700);
        await using var _ = holder;

        Assert.Equal(0, plain);                                          // یک SELECT ساده در همین لحظه «هیچ قراردادی» می‌دید (RCSI) - منطق قبلی اشتباهاً «ساخته نشد» می‌گفت
        Assert.True(result.OutcomeUnknown);
        Assert.False(result.Success);
        Assert.False(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.OutcomeUnknown, result.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", result.Error);

        // تراکنش اصلی در نهایت Commit می‌کند: قرارداد واقعاً وجود دارد؛ تلاش مجدد قرارداد تکراری نمی‌سازد
        await holder.CommitAsync();
        Assert.Equal(1, await ContractsAsync("CT-R4"));
        var again = await NewEnv("manager.one", seed.ManagerOne).Service.CreateWithCreatorEventAsync("CT-R4", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.False(again.Success);
        Assert.Contains("از قبل وجود دارد", again.Error);
        Assert.Equal(1, await ContractsAsync("CT-R4"));
        Assert.Empty(await EventsAsync());
    }

    [Fact]
    public async Task R5_Manual_WhenTheOriginalSessionIsKilledDuringVerification_NotCreatedIsProven_AndARetryCreatesExactlyOne()
    {
        var seed = await SeedAsync();
        var sw = Stopwatch.StartNew();
        var (result, plain, holder) = await RunManualWithInFlightOriginalAsync(seed, "CT-R5", killHolderDuringVerification: true, lockTimeoutMs: 9000);
        sw.Stop();
        await using var _ = holder;

        Assert.Equal(0, plain);
        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);                              // قفل پس از مرگ نشست گرفته شد؛ تراکنش اصلی هرگز Commit نکرد
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        Assert.InRange(sw.ElapsedMilliseconds, 500, 8500);                // واقعاً منتظر ماند
        Assert.Equal(0, await ContractsAsync("CT-R5"));
        Assert.Empty(await EventsAsync());

        var retry = await NewEnv("manager.one", seed.ManagerOne).Service.CreateWithCreatorEventAsync("CT-R5", seed.ProjectId, seed.VendorId, null, null, null, null, false, seed.ManagerOne, null);
        Assert.True(retry.Success, retry.Error);
        await AssertCommittedOnceAsync("CT-R5", seed.ManagerOne, "manager.one");
    }

    private async Task<(TenderContractResult Result, InFlightHolder Holder)> RunTenderWithInFlightOriginalAsync(
        Seed seed, int tenderId, string no, bool killHolderDuringVerification, int lockTimeoutMs)
    {
        var probe = new CommitProbe { ThrowBeforeRealCommit = 1 };
        InFlightHolder? holder = null;
        var hook = new FirstReaderAfterCommitFailureHook(probe, async () =>
        {
            holder = await InFlightHolder.StartAsync(_db, ContractCreationLocks.TenderResource(tenderId));
            if (killHolderDuringVerification)
                _ = Task.Run(async () => { await Task.Delay(600); await holder.KillSessionAsync(); });
        });
        var ctx = _db.NewContext(new IInterceptor[] { new CommitFaultInterceptor(probe), hook }, d => new NoRetryStrategy(d));
        ctx.CurrentUserName = "manager.one";
        var service = new TenderService(ctx, _numbering, null, lockTimeoutMs);
        var result = await service.CreateContractFromWinningBidAsync(tenderId, no, null, null, seed.ManagerOne, null);
        return (result, holder!);
    }

    [Fact]
    public async Task R6a_Tender_CommitAttempted_WhileTheOriginalTransactionIsStillOpen_IsOutcomeUnknown()
    {
        var seed = await SeedAsync();
        var tenderId = await AwardedTenderAsync(seed);
        var (result, holder) = await RunTenderWithInFlightOriginalAsync(seed, tenderId, "CT-R6A", killHolderDuringVerification: false, lockTimeoutMs: 700);
        await using var _ = holder;

        Assert.True(result.OutcomeUnknown);
        Assert.False(result.Success);
        Assert.Equal(ContractCreatorEventStatus.OutcomeUnknown, result.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", result.Error);

        await holder.KillSessionAsync();                                  // رها شدن تراکنش اصلی؛ سپس تلاش مجدد معمولی موفق است و فقط یک قرارداد می‌سازد
        var retry = await new TenderService(NewTenderContext("manager.one"), _numbering).CreateContractFromWinningBidAsync(tenderId, "CT-R6A", null, null, seed.ManagerOne, null);
        Assert.True(retry.Success, retry.Error);
        Assert.Equal(1, await ContractsAsync());
        Assert.Single(await EventsAsync());
    }

    [Fact]
    public async Task R6b_Tender_WhenTheOriginalSessionIsKilledDuringVerification_NotCreatedIsProven_ThenRetrySucceeds()
    {
        var seed = await SeedAsync();
        var tenderId = await AwardedTenderAsync(seed);
        var sw = Stopwatch.StartNew();
        var (result, holder) = await RunTenderWithInFlightOriginalAsync(seed, tenderId, "CT-R6B", killHolderDuringVerification: true, lockTimeoutMs: 9000);
        sw.Stop();
        await using var _ = holder;

        Assert.InRange(sw.ElapsedMilliseconds, 500, 8500);                // واقعاً پشت قفل منتظر ماند تا نشست کشته شد (خواندن ساده فوراً جواب می‌داد)
        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Equal(0, await ContractsAsync());
        Assert.Null(await LinkedAsync(tenderId));

        var retry = await new TenderService(NewTenderContext("manager.one"), _numbering).CreateContractFromWinningBidAsync(tenderId, "CT-R6B", null, null, seed.ManagerOne, null);
        Assert.True(retry.Success, retry.Error);
        Assert.Equal(1, await ContractsAsync());
        Assert.Single(await EventsAsync());
    }

    private PEMSDbContext NewTenderContext(string userName)
    {
        var ctx = _db.NewContext();
        ctx.CurrentUserName = userName;
        return ctx;
    }

    private async Task<(ContractCreatorEventStatus Status, InFlightHolder Holder)> RunRepairWithInFlightOriginalAsync(
        Seed seed, int contractId, bool killHolderDuringVerification, int lockTimeoutMs)
    {
        var probe = new CommitProbe { ThrowBeforeRealCommit = 1 };
        InFlightHolder? holder = null;
        var hook = new FirstReaderAfterCommitFailureHook(probe, async () =>
        {
            holder = await InFlightHolder.StartAsync(_db, ContractCreationLocks.CreatorEventResource(contractId));
            if (killHolderDuringVerification)
                _ = Task.Run(async () => { await Task.Delay(600); await holder.KillSessionAsync(); });
        });
        var env = NewEnv("manager.two", seed.ManagerTwo, interceptors: new IInterceptor[] { new CommitFaultInterceptor(probe), hook },
            strategy: d => new NoRetryStrategy(d), lockTimeoutMs: lockTimeoutMs);
        var status = await env.Service.EnsureCreatorEventAsync(contractId);
        return (status, holder!);
    }

    [Fact]
    public async Task R7_Repair_CommitAttempted_ReportsUnknownWhileInFlight_AndNotRecordedOnlyAfterTheOriginalIsProvenGone()
    {
        var seed = await SeedAsync();
        var legacy = NewEnv("manager.one", seed.ManagerOne);
        var contractId = await legacy.Service.CreateAsync("CT-R7", seed.ProjectId, seed.VendorId, null, null, null, null);   // بی‌رویداد

        var (unknown, holder1) = await RunRepairWithInFlightOriginalAsync(seed, contractId, killHolderDuringVerification: false, lockTimeoutMs: 700);
        await using (holder1)
        {
            Assert.Equal(ContractCreatorEventStatus.OutcomeUnknown, unknown);          // نه «ثبت نشد»
            Assert.Empty(await EventsAsync(contractId));
        }

        var (proven, holder2) = await RunRepairWithInFlightOriginalAsync(seed, contractId, killHolderDuringVerification: true, lockTimeoutMs: 9000);
        await using (holder2)
        {
            Assert.Equal(ContractCreatorEventStatus.NotRecorded, proven);              // پس از مرگ نشست: رویداد Commit نشده، ثبت‌نشده اثبات شد
            Assert.Empty(await EventsAsync(contractId));
        }

        var repaired = await NewEnv("manager.two", seed.ManagerTwo).Service.EnsureCreatorEventAsync(contractId);
        Assert.Equal(ContractCreatorEventStatus.Repaired, repaired);
        var ev = Assert.Single(await EventsAsync(contractId));
        Assert.Equal(seed.ManagerOne, ev.UserId);                                       // ایجادکننده‌ی اصلی، نه کاربر ترمیم‌کننده
    }
}
