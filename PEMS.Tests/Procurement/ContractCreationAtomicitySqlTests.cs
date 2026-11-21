using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using Xunit.Abstractions;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0.3 - اثبات اتمیک‌بودن «ساخت قرارداد + AuditLog عمومی + رویداد Contracts.Create» روی SQL Server واقعیِ یک‌بارمصرف (PEMS_TEST_P03_*، هرگز PEMSDB).
/// هر تست دیتابیس خودش را با تمام Migrationهای واقعی می‌سازد و فقط همان را Drop می‌کند. شماره‌ی ذخیره‌ی فیزیکی در یک عملیات:
/// ۱ = A (قرارداد + اتصال مناقصه)، ۲ = B (AuditLog عمومیِ آن)، ۳ = C (ردیف رویداد)، ۴ = D (AuditLog عمومیِ رویداد). تمام ادعاها از ردیف‌های ذخیره‌شده‌ی
/// SQL با اتصال جداگانه خوانده می‌شوند. «Commit نامطمئن» اینجا شبیه‌سازی است (Interceptor پس از Commit واقعی خطا پرتاب می‌کند)، نه قطع واقعی شبکه.
/// نام تست‌ها T1..T14 را به سناریوهای مصوب نگاشت می‌کند.
/// </summary>
public sealed class ContractCreationAtomicitySqlTests : IAsyncLifetime
{
    private DisposableSqlDatabase _db = null!;
    private readonly FakeNumberingService _numbering = new();
    private readonly ITestOutputHelper _output;
    private int _seq;
    private int? _projectId;

    public ContractCreationAtomicitySqlTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await DisposableSqlDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // =============================================================== داده‌ی پایه
    private async Task<int> UserAsync(string userName)
    {
        await using var ctx = _db.NewContext();
        var user = new ApplicationUser { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), Email = $"{userName}@pems.local", FullName = userName };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>مناقصه‌ی Awarded تازه (بسته، فروشنده، پیشنهاد و ارزیابی‌های موفق)، با Context جدا و بدون Interceptor.</summary>
    private async Task<int> AwardedTenderAsync()
    {
        var n = Interlocked.Increment(ref _seq);
        await using var ctx = _db.NewContext();
        ctx.CurrentUserName = "seed.user";

        if (_projectId == null)
        {
            var project = new Project { ProjectCode = "P-SQL", ProjectName = "Project SQL", IsActive = true };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            _projectId = project.ProjectId;
        }

        var package = new ProcurementPackage { PackageNumber = $"PP-{n:000}", ProjectId = _projectId.Value, Title = $"Pumps {n}", IsActive = true };
        var vendor = new Vendor { Name = $"Qualified Vendor {n}", IsActive = true, QualificationStatus = "Qualified", QualificationExpiryDate = DateTime.Now.AddYears(1) };
        ctx.ProcurementPackages.Add(package);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var service = new TenderService(ctx, _numbering);
        var tenderId = await service.CreateAsync(package.ProcurementPackageId, $"Pump Tender {n}", null, null, false, null, null, null, null);
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

    private (TenderService Service, PEMSDbContext Ctx) NewService(string? userName, IEnumerable<IInterceptor>? interceptors = null,
        Func<Microsoft.EntityFrameworkCore.Storage.ExecutionStrategyDependencies, Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy>? strategy = null,
        int lockTimeoutMs = 15000)
    {
        var ctx = _db.NewContext(interceptors, strategy);
        ctx.CurrentUserName = userName;
        return (new TenderService(ctx, _numbering, null, lockTimeoutMs), ctx);
    }

    // =============================================================== خواندن وضعیت ذخیره‌شده (اتصال جداگانه، SQL خام)
    private Task<int> ContractsAsync(string? contractNo = null)
        => contractNo == null
            ? _db.ScalarIntAsync("SELECT COUNT(*) FROM Contracts")
            : _db.ScalarIntAsync("SELECT COUNT(*) FROM Contracts WHERE ContractNo = @n", ("@n", contractNo));

    private async Task<int?> LinkedAsync(int tenderId)
        => (await _db.QueryAsync("SELECT ContractId FROM Tenders WHERE TenderId = @t", r => r.IsDBNull(0) ? (int?)null : r.GetInt32(0), ("@t", tenderId))).Single();

    private Task<List<(int? UserId, string ResourceId)>> EventsAsync(int? contractId = null)
        => _db.QueryAsync(
            "SELECT UserId, ResourceId FROM AuthorizationAuditLogs WHERE ResourceName='Contracts' AND ActionName='Create' AND Decision='Allow'"
            + (contractId.HasValue ? " AND ResourceId = @c" : ""),
            r => (r.IsDBNull(0) ? (int?)null : r.GetInt32(0), r.GetString(1)),
            contractId.HasValue ? new[] { ("@c", (object)contractId.Value.ToString()) } : Array.Empty<(string, object)>());

    private Task<int> GenericAuditAsync(string entityName, string? action = null)
        => _db.ScalarIntAsync("SELECT COUNT(*) FROM AuditLogs WHERE EntityName = @e" + (action != null ? " AND Action = @a" : ""),
            action != null ? new[] { ("@e", (object)entityName), ("@a", (object)action) } : new[] { ("@e", (object)entityName) });

    private Task<int> TenderLinkAuditAsync()
        => _db.ScalarIntAsync("SELECT COUNT(*) FROM AuditLogs WHERE EntityName='Tender' AND ChangedFieldsSummary LIKE '%ContractId%'");

    private async Task AssertNothingPersistedAsync(int tenderId)
    {
        Assert.Equal(0, await ContractsAsync());
        Assert.Null(await LinkedAsync(tenderId));
        Assert.Empty(await EventsAsync());
        Assert.Equal(0, await GenericAuditAsync("Contract"));
        Assert.Equal(0, await GenericAuditAsync("AuthorizationAuditLog"));
        Assert.Equal(0, await TenderLinkAuditAsync());
    }

    private async Task<int> AssertFullyCommittedOnceAsync(int tenderId, int creatorUserId)
    {
        Assert.Equal(1, await ContractsAsync());
        var linked = await LinkedAsync(tenderId);
        Assert.NotNull(linked);
        var ev = Assert.Single(await EventsAsync());
        Assert.Equal(creatorUserId, ev.UserId);
        Assert.Equal(linked!.Value.ToString(), ev.ResourceId);
        Assert.Equal(1, await GenericAuditAsync("Contract", "Insert"));                // B
        Assert.Equal(1, await GenericAuditAsync("AuthorizationAuditLog", "Insert"));   // D
        Assert.Equal(1, await TenderLinkAuditAsync());
        return linked.Value;
    }

    // =============================================================== T1 : مسیر موفق
    [Fact]
    public async Task T1_HappyPath_CommitsContractLink_BothGenericAudits_AndTheCreatorEvent_Together()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t1");
        var (service, _) = NewService("creator.t1");

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T1", new DateTime(2026, 1, 1), null, creator, "10.0.0.1");

        Assert.True(result.Success);
        Assert.True(result.Committed);
        Assert.False(result.OutcomeUnknown);
        Assert.False(result.RecoveredAfterUncertainCommit);
        Assert.Equal(ContractCreatorEventStatus.Recorded, result.CreatorEvent);
        var contractId = await AssertFullyCommittedOnceAsync(tenderId, creator);
        Assert.Equal(result.ContractId, contractId);
        Assert.Equal("creator.t1", (await _db.QueryAsync("SELECT CreatedBy FROM Contracts", r => r.GetString(0))).Single());
    }

    // =============================================================== T2..T6 : شکست هر ذخیره‌ی فیزیکی = Rollback کامل، سپس تلاش مجدد
    private async Task FailureRollsEverythingBack_ThenRetrySucceeds(int failOn, string tag)
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator." + tag);
        var faults = new NthSaveFaultInterceptor();
        var (service, ctx) = NewService("creator." + tag, new IInterceptor[] { faults });
        faults.Arm(failOn);

        var failed = await service.CreateContractFromWinningBidAsync(tenderId, "CT-" + tag, null, null, creator, "10.0.0.1");

        Assert.False(failed.Success);
        Assert.False(failed.Committed);
        Assert.False(failed.OutcomeUnknown);
        Assert.Contains("هیچ تغییری ذخیره نشد", failed.Error);
        await AssertNothingPersistedAsync(tenderId);                                   // T2..T5 : هیچ چیز ماندگار نیست
        Assert.DoesNotContain(ctx.ChangeTracker.Entries(), e => e.State == EntityState.Added);   // Context پاک‌سازی شده است

        // T6: تلاش مجدد با همان Context و همان سرویس
        faults.Disarm();
        var retry = await service.CreateContractFromWinningBidAsync(tenderId, "CT-" + tag, null, null, creator, "10.0.0.1");
        Assert.True(retry.Success, retry.Error);
        Assert.Equal(ContractCreatorEventStatus.Recorded, retry.CreatorEvent);
        var contractId = await AssertFullyCommittedOnceAsync(tenderId, creator);
        Assert.Equal(retry.ContractId, contractId);

        // و تلاش سوم هیچ چیز تکراری نمی‌سازد
        var third = await service.CreateContractFromWinningBidAsync(tenderId, "CT-" + tag, null, null, creator, "10.0.0.1");
        Assert.False(third.Success);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, third.CreatorEvent);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact] public Task T2_FailureAtBusinessSave_A_RollsBack_ThenRetrySucceeds() => FailureRollsEverythingBack_ThenRetrySucceeds(1, "t2");
    [Fact] public Task T3_FailureAtGenericAuditSave_B_RollsBackTheCommittedBusinessRows_ThenRetrySucceeds() => FailureRollsEverythingBack_ThenRetrySucceeds(2, "t3");
    [Fact] public Task T4_FailureAtCreatorEventSave_C_RollsBackTheContract_ThenRetrySucceeds() => FailureRollsEverythingBack_ThenRetrySucceeds(3, "t4");
    [Fact] public Task T5_FailureAtEventGenericAuditSave_D_RollsBackTheEventAndContract_ThenRetrySucceeds() => FailureRollsEverythingBack_ThenRetrySucceeds(4, "t5");

    [Fact]
    public async Task T6_TransientFailureInsideTheTransaction_IsRetriedByTheStrategy_WithoutDuplicates()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t6");
        var faults = new NthSaveFaultInterceptor();
        var (service, _) = NewService("creator.t6", new IInterceptor[] { faults }, d => new TestRetryingStrategy(d));
        faults.Arm(3, () => new InjectedTransientException("injected transient failure at the creator-event save"));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T6", null, null, creator, null);

        Assert.True(result.Success, result.Error);
        Assert.False(result.RecoveredAfterUncertainCommit);                 // خطا پیش از Commit بود؛ Rollback کامل و تلاش تازه
        Assert.True(faults.Calls > 4);                                      // تلاش دوم هم ذخیره‌ی فیزیکی انجام داد
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T4b_RepairFailureOnRetry_ReportsTheExistingContract_NeverNothingSaved_AndALaterRetryRepairs()
    {
        var tenderId = await AwardedTenderAsync();
        var original = await UserAsync("creator.t4b");
        var other = await UserAsync("other.t4b");
        var (seed, seedCtx) = NewService("creator.t4b");
        var legacy = await seed.CreateContractFromWinningBidAsync(tenderId, "CT-T4B", null, null);          // قرارداد بی‌رویداد
        await seedCtx.DisposeAsync();
        var contractId = legacy.ContractId!.Value;

        var faults = new NthSaveFaultInterceptor();
        var (service, _) = NewService("other.t4b", new IInterceptor[] { faults });
        faults.Arm(1);                                                                                      // ذخیره‌ی ردیف رویدادِ ترمیم شکست می‌خورد
        var failed = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T4B", null, null, other, null);

        Assert.False(failed.Success);
        Assert.True(failed.Committed);                                                                      // قرارداد وجود دارد
        Assert.Equal(contractId, failed.ContractId);
        Assert.Equal(ContractCreatorEventStatus.NotRecorded, failed.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", failed.Error);
        Assert.Empty(await EventsAsync(contractId));
        Assert.Equal(1, await ContractsAsync());

        faults.Disarm();
        var retry = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T4B", null, null, other, null);
        Assert.Equal(ContractCreatorEventStatus.Repaired, retry.CreatorEvent);
        var ev = Assert.Single(await EventsAsync(contractId));
        Assert.Equal(original, ev.UserId);                                                                  // ایجادکننده‌ی اصلی، نه کاربر تلاش‌کننده
    }

    // =============================================================== T7 : Commit نامطمئن (شبیه‌سازی‌شده)
    [Fact]
    public async Task T7a_CommitAckLost_AfterRealCommit_RetryConfirmsWithoutDuplicating()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t7a");
        var probe = new CommitProbe { ThrowAfterRealCommit = 1 };
        var (service, _) = NewService("creator.t7a", new IInterceptor[] { new CommitFaultInterceptor(probe) }, d => new TestRetryingStrategy(d));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T7A", null, null, creator, null);

        Assert.True(result.Success, result.Error);
        Assert.True(result.RecoveredAfterUncertainCommit);
        Assert.Equal(1, probe.RealCommits);                                  // فقط یک Commit واقعی؛ تلاش دوم چیزی ننوشت
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T7b_CommitAckLost_ThenRetriesExhausted_VerificationConfirmsCommittedState()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t7b");
        var probe = new CommitProbe { ThrowAfterRealCommit = 1, TransientReadFailuresAfterCommitFailure = 3 };   // سه تلاش بعدی (کل Retryها) شکست می‌خورند
        var (service, _) = NewService("creator.t7b", new IInterceptor[] { new CommitFaultInterceptor(probe), new ReadFaultInterceptor(probe) },
            d => new TestRetryingStrategy(d, 3));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T7B", null, null, creator, null);

        Assert.Equal(3, probe.ReadFailuresInjected);
        Assert.True(result.Success, result.Error);                           // بررسی تأییدی دیتابیس ثابت کرد Commit انجام شده بود
        Assert.True(result.RecoveredAfterUncertainCommit);
        Assert.False(result.OutcomeUnknown);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T7c_CommitAckLost_RetriesExhausted_AndVerificationFails_ReportsUnknown_NeverNotCreated()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t7c");
        var probe = new CommitProbe { ThrowAfterRealCommit = 1, TransientReadFailuresAfterCommitFailure = 3, FailReadsNonTransientAfterBudget = true };
        var (service, _) = NewService("creator.t7c", new IInterceptor[] { new CommitFaultInterceptor(probe), new ReadFaultInterceptor(probe) },
            d => new TestRetryingStrategy(d, 3));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T7C", null, null, creator, null);

        Assert.True(result.OutcomeUnknown);
        Assert.False(result.Success);
        Assert.False(result.Committed);                                      // «تأیید نشده» - نه «ساخته نشد»
        Assert.Equal(ContractCreatorEventStatus.OutcomeUnknown, result.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Contains("نامطمئن", result.Error);
        // واقعیت دیتابیس: Commit انجام شده بود
        await AssertFullyCommittedOnceAsync(tenderId, creator);

        // یک تلاش تازه (بدون خطا) قرارداد تکراری نمی‌سازد و وضعیت را روشن می‌کند
        var (fresh, _) = NewService("creator.t7c");
        var again = await fresh.CreateContractFromWinningBidAsync(tenderId, "CT-T7C", null, null, creator, null);
        Assert.False(again.Success);
        Assert.True(again.Committed);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, again.CreatorEvent);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T7d_CommitFailsBeforeReachingTheServer_RollbackAndRetryCreatesExactlyOne()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t7d");
        var probe = new CommitProbe { ThrowBeforeRealCommit = 1 };
        var (service, _) = NewService("creator.t7d", new IInterceptor[] { new CommitFaultInterceptor(probe) }, d => new TestRetryingStrategy(d));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T7D", null, null, creator, null);

        Assert.True(result.Success, result.Error);
        Assert.False(result.RecoveredAfterUncertainCommit);                  // تلاش اول واقعاً Commit نشد؛ تلاش دوم ساخت
        Assert.Equal(1, probe.RealCommits);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T7e_EveryCommitFailsBeforeTheServer_RetriesExhausted_IsVerifiedNotCreated_AndNothingPersists()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t7e");
        var probe = new CommitProbe { ThrowBeforeRealCommit = 4 };           // تلاش اولیه + سه Retry
        var (service, _) = NewService("creator.t7e", new IInterceptor[] { new CommitFaultInterceptor(probe) }, d => new TestRetryingStrategy(d, 3));

        var result = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T7E", null, null, creator, null);

        Assert.False(result.Success);
        Assert.False(result.OutcomeUnknown);                                 // خواندن تأییدی انجام شد و مناقصه به قراردادی متصل نبود
        Assert.False(result.Committed);
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        Assert.Equal(0, probe.RealCommits);
        await AssertNothingPersistedAsync(tenderId);
    }

    // =============================================================== T8 : درخواست‌های هم‌زمان برای یک مناقصه
    [Theory]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(6, true)]
    public async Task T8_ConcurrentRequestsForTheSameTender_CreateExactlyOneContractAndOneEvent(int parallel, bool differentNumbers)
    {
        const int iterations = 15;
        var users = new List<(int Id, string Name)>();
        for (var i = 0; i < parallel; i++) users.Add((await UserAsync($"user.t8.{parallel}.{differentNumbers}.{i}"), $"user.t8.{parallel}.{differentNumbers}.{i}"));

        for (var iter = 0; iter < iterations; iter++)
        {
            var tenderId = await AwardedTenderAsync();
            var gate = new TaskCompletionSource();
            var tasks = Enumerable.Range(0, parallel).Select(async i =>
            {
                var (service, ctx) = NewService(users[i].Name);
                await using var _ = ctx;
                await gate.Task;
                return await service.CreateContractFromWinningBidAsync(tenderId, differentNumbers ? $"CT-T8-{parallel}-{iter}-{i}" : $"CT-T8-{parallel}-{iter}", null, null, users[i].Id, null);
            }).ToList();
            gate.SetResult();
            var results = await Task.WhenAll(tasks);                         // هیچ استثنای مدیریت‌نشده‌ای نباید بیرون بزند

            var winners = results.Select((r, i) => (r, i)).Where(x => x.r.Success).ToList();
            Assert.True(winners.Count == 1, $"iteration {iter}: expected exactly one winner, got {winners.Count}. Errors: {string.Join(" | ", results.Select(r => r.Error))}");
            var winner = winners[0];
            foreach (var loser in results.Where(r => !r.Success))
            {
                Assert.True(loser.Committed, $"iteration {iter}: loser reported a non-'already created' failure: {loser.Error}");
                Assert.Equal(winner.r.ContractId, loser.ContractId);
            }

            var linked = await LinkedAsync(tenderId);
            Assert.Equal(winner.r.ContractId, linked);
            var ev = Assert.Single(await EventsAsync(linked));
            Assert.Equal(users[winner.i].Id, ev.UserId);                      // رویداد برای برنده‌ی واقعی است
        }

        // کل دیتابیس: هر تکرار دقیقاً یک قرارداد و یک رویداد
        Assert.Equal(iterations, await ContractsAsync());
        Assert.Equal(iterations, (await EventsAsync()).Count);
    }

    // =============================================================== T9 : ترمیم در برابر ساخت و ترمیم در برابر ترمیم
    [Fact]
    public async Task T9_RepairVersusCreate_AndRepairVersusRepair_WriteExactlyOneEvent_ForTheOriginalCreator()
    {
        var legacyCreator = await UserAsync("legacy.creator");
        var other = await UserAsync("other.user");
        const int iterations = 15;

        for (var iter = 0; iter < iterations; iter++)
        {
            var tenderId = await AwardedTenderAsync();
            // قرارداد «قدیمی»: ساخته شده بدون رویداد (creatorUserId = null)؛ CreatedBy = legacy.creator
            var (seedService, seedCtx) = NewService("legacy.creator");
            var created = await seedService.CreateContractFromWinningBidAsync(tenderId, $"CT-T9-{iter}", null, null);
            await seedCtx.DisposeAsync();
            Assert.True(created.Success);
            var contractId = created.ContractId!.Value;
            Assert.Empty(await EventsAsync(contractId));                      // بی‌رویداد

            var gate = new TaskCompletionSource();
            var tasks = new List<Task>();
            for (var k = 0; k < 3; k++)                                       // ترمیم در برابر ترمیم
                tasks.Add(Task.Run(async () =>
                {
                    var (s, c) = NewService("other.user"); await using var _ = c;
                    await gate.Task;
                    await s.EnsureContractCreatorEventAsync(contractId);
                }));
            for (var k = 0; k < 3; k++)                                       // ترمیم در برابر ساخت (مسیر «قرارداد از قبل هست»)
                tasks.Add(Task.Run(async () =>
                {
                    var (s, c) = NewService("other.user"); await using var _ = c;
                    await gate.Task;
                    await s.CreateContractFromWinningBidAsync(tenderId, $"CT-T9-{iter}", null, null, other, null);
                }));
            gate.SetResult();
            await Task.WhenAll(tasks);

            var ev = Assert.Single(await EventsAsync(contractId));
            Assert.Equal(legacyCreator, ev.UserId);                           // نه کاربر تلاش‌کننده
            Assert.Equal(1, (await _db.QueryAsync("SELECT COUNT(*) FROM Contracts WHERE ContractNo = @n", r => r.GetInt32(0), ("@n", $"CT-T9-{iter}"))).Single());
        }
    }

    // =============================================================== T10 : ContractNo یکسان روی دو مناقصه‌ی هم‌زمان
    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    public async Task T10_IdenticalContractNoAcrossTenders_ConcurrentlyCreatesExactlyOneContract(int tenders, int callersPerTender)
    {
        const int iterations = 12;
        var users = new List<(int Id, string Name)>();
        for (var i = 0; i < tenders * callersPerTender; i++) users.Add((await UserAsync($"user.t10.{tenders}.{callersPerTender}.{i}"), $"user.t10.{tenders}.{callersPerTender}.{i}"));
        var variants = new[] { "CT-T10-{0}", " ct-t10-{0} ", "Ct-T10-{0}", "CT-T10-{0}  " };       // حروف کوچک/بزرگ و فاصله: Collation دیتابیس CI_AS

        for (var iter = 0; iter < iterations; iter++)
        {
            var tenderIds = new List<int>();
            for (var t = 0; t < tenders; t++) tenderIds.Add(await AwardedTenderAsync());
            var gate = new TaskCompletionSource();
            var calls = new List<(int Tender, int User, Task<TenderContractResult> Task)>();
            for (var t = 0; t < tenders; t++)
                for (var c = 0; c < callersPerTender; c++)
                {
                    var userIndex = t * callersPerTender + c;
                    var no = string.Format(variants[t % variants.Length], iter);
                    var tenderId = tenderIds[t];
                    calls.Add((t, userIndex, Task.Run(async () =>
                    {
                        var (s, ctx) = NewService(users[userIndex].Name); await using var _ = ctx;
                        await gate.Task;
                        return await s.CreateContractFromWinningBidAsync(tenderId, no, null, null, users[userIndex].Id, null);
                    })));
                }
            gate.SetResult();
            var results = await Task.WhenAll(calls.Select(c => c.Task));

            var key = $"CT-T10-{iter}";
            Assert.Equal(1, await ContractsAsync(key));                       // دقیقاً یک قرارداد با این شماره (نادیده‌گرفتن حروف/فاصله‌ی انتهایی)
            var winners = results.Where(r => r.Success).ToList();
            Assert.Single(winners);
            foreach (var r in results.Where(r => !r.Success))
            {
                // یا «شماره‌ی تکراری» (مناقصه‌ی دیگر)، یا «قبلاً ساخته شده» (درخواست هم‌زمان همان مناقصه)؛ هرگز استثنا یا ذخیره‌ی ناقص
                Assert.True(r.Error!.Contains("از قبل وجود دارد") || (r.Committed && r.ContractId.HasValue), $"iteration {iter}: unexpected failure: {r.Error}");
            }
            var linkedTenders = 0;
            foreach (var id in tenderIds) if (await LinkedAsync(id) != null) linkedTenders++;
            Assert.Equal(1, linkedTenders);
        }
        Assert.Equal(iterations, await ContractsAsync());
    }

    // ---- T10c: نرمال‌سازی کلید قفل در برابر مقایسه‌ی واقعی Collation دیتابیس
    private async Task<bool> SqlConsidersEqualAsync(string a, string b)
        => (await _db.QueryAsync("SELECT CASE WHEN @a = @b THEN 1 ELSE 0 END", r => r.GetInt32(0), ("@a", a), ("@b", b))).Single() == 1;

    [Fact]
    public async Task T10c_LockKey_IsEqual_WheneverTheDatabaseCollationTreatsTwoNumbersAsEqual_ForTheSupportedVariantClasses()
    {
        // کلاس‌های پشتیبانی‌شده: حروف بزرگ/کوچک، فاصله‌ی انتهایی، عرض کامل (ASCII)، ترکیب/تجزیه‌ی یونیکد (Å)، و گسترش‌های ss=ß، ae=æ، oe=œ
        // (سه گسترش آخر را اجرای اول این تست روی SQL واقعی به‌عنوان «ریسک باقی‌مانده» نشان داد و نرمال‌سازی برایشان تکمیل شد)
        var supported = new (string A, string B)[]
        {
            ("CT-1", "ct-1"), ("CT-1", "CT-1  "), ("CT-A", "CT-\uFF21"), ("CT-1", "CT-\uFF11"), ("CT-1", "\uFF23\uFF34\uFF0D\uFF11"),
            ("\u00C5", "A\u030A"), ("a\u0301", "\u00E1"), ("Ab-1", "aB-1"),
            ("ss", "\u00DF"), ("SS", "\u00DF"), ("ae", "\u00E6"), ("AE", "\u00C6"), ("oe", "\u0153"), ("OE", "\u0152"),
        };
        foreach (var (a, b) in supported)
        {
            var sqlEqual = await SqlConsidersEqualAsync(a, b);
            var keyEqual = ContractCreationLocks.ContractNoResource(a) == ContractCreationLocks.ContractNoResource(b);
            _output.WriteLine($"supported  sql_equal={sqlEqual,-5} key_equal={keyEqual,-5} [{a}] vs [{b}]");
            if (sqlEqual) Assert.True(keyEqual, $"SQL treats [{a}] and [{b}] as equal but the lock keys differ");
        }

        // کلاس‌های مشخصه‌یابی‌شده (نه تضمین): اگر SQL دو شماره را برابر بداند ولی کلیدها فرق کنند، آن جفت یک ریسک باقی‌مانده است و فقط گزارش می‌شود
        var characterization = new (string A, string B)[]
        {
            ("fi", "\uFB01"), ("ffi", "\uFB03"), ("e", "\u00E9"), ("i", "\u0131"),
            ("i", "\u0130"), ("K", "\u212A"), ("A", "\u00C5"), ("O", "\u00D8"), ("d", "\u0111"), ("L", "\u0141"), ("a", "\u00AA"),
        };
        var residual = new List<string>();
        foreach (var (a, b) in characterization)
        {
            var sqlEqual = await SqlConsidersEqualAsync(a, b);
            var keyEqual = ContractCreationLocks.ContractNoResource(a) == ContractCreationLocks.ContractNoResource(b);
            _output.WriteLine($"characterize sql_equal={sqlEqual,-5} key_equal={keyEqual,-5} U+{string.Join("+", a.Select(c => ((int)c).ToString("X4")))} vs U+{string.Join("+", b.Select(c => ((int)c).ToString("X4")))}");
            if (sqlEqual && !keyEqual) residual.Add($"U+{string.Join("+", a.Select(c => ((int)c).ToString("X4")))} ~ U+{string.Join("+", b.Select(c => ((int)c).ToString("X4")))}");
        }
        _output.WriteLine("residual (SQL-equal but different lock keys): " + (residual.Count == 0 ? "none" : string.Join(", ", residual)));
    }

    // =============================================================== T11 : مهلت قفل کاربردی و بازیابی
    private static async Task<(SqlConnection Conn, SqlTransaction Tx)> HoldLockAsync(string connectionString, string resource)
    {
        var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;";
        cmd.Parameters.AddWithValue("@res", resource);
        var granted = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.True(granted >= 0, "the test could not take the application lock it wants to hold");
        return (conn, tx);
    }

    private async Task<bool> LockIsFreeAsync(string resource)
    {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @r;";
        cmd.Parameters.AddWithValue("@res", resource);
        var granted = (int)(await cmd.ExecuteScalarAsync())!;
        tx.Rollback();
        return granted >= 0;
    }

    [Fact]
    public async Task T11a_TenderLockHeldElsewhere_TimesOutWithoutWriting_AndRecoversAfterRelease()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t11a");
        var (service, _) = NewService("creator.t11a", lockTimeoutMs: 700);
        var (conn, tx) = await HoldLockAsync(_db.ConnectionString, ContractCreationLocks.TenderResource(tenderId));
        try
        {
            var sw = Stopwatch.StartNew();
            var busy = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T11A", null, null, creator, null);
            sw.Stop();

            Assert.False(busy.Success);
            Assert.False(busy.Committed);
            Assert.False(busy.OutcomeUnknown);
            Assert.Contains("در حال پردازش", busy.Error);
            Assert.InRange(sw.ElapsedMilliseconds, 500, 12000);               // واقعاً منتظر ماند، بی‌نهایت نه
            await AssertNothingPersistedAsync(tenderId);
        }
        finally { tx.Rollback(); await tx.DisposeAsync(); await conn.DisposeAsync(); }

        var ok = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T11A", null, null, creator, null);
        Assert.True(ok.Success, ok.Error);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T11b_ContractNoLockHeldElsewhere_TimesOut_ReleasesTheTenderLock_AndRecoversAfterRelease()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.t11b");
        var (service, _) = NewService("creator.t11b", lockTimeoutMs: 700);
        // قفل با قالب متفاوتِ همان شماره‌ی نرمال‌شده نگه داشته می‌شود: نشان می‌دهد نرمال‌سازی و نام منبع واقعاً بین اتصال‌ها مشترک است
        var (conn, tx) = await HoldLockAsync(_db.ConnectionString, ContractCreationLocks.ContractNoResource("  ct-t11b "));
        try
        {
            var busy = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T11B", null, null, creator, null);

            Assert.False(busy.Success);
            Assert.False(busy.Committed);
            Assert.Contains("در حال پردازش", busy.Error);
            await AssertNothingPersistedAsync(tenderId);
            Assert.True(await LockIsFreeAsync(ContractCreationLocks.TenderResource(tenderId)), "the tender lock must not leak after a failed attempt");
        }
        finally { tx.Rollback(); await tx.DisposeAsync(); await conn.DisposeAsync(); }

        var ok = await service.CreateContractFromWinningBidAsync(tenderId, "CT-T11B", null, null, creator, null);
        Assert.True(ok.Success, ok.Error);
        await AssertFullyCommittedOnceAsync(tenderId, creator);
    }

    [Fact]
    public async Task T11c_CreatorEventLockHeldElsewhere_RepairTimesOut_ReportsNotRecorded_AndRecoversAfterRelease()
    {
        var creator = await UserAsync("creator.t11c");
        var tenderId = await AwardedTenderAsync();
        var (seed, seedCtx) = NewService("creator.t11c");
        var created = await seed.CreateContractFromWinningBidAsync(tenderId, "CT-T11C", null, null);       // بی‌رویداد
        await seedCtx.DisposeAsync();
        var contractId = created.ContractId!.Value;
        var (service, _) = NewService("someone.else", lockTimeoutMs: 700);

        var (conn, tx) = await HoldLockAsync(_db.ConnectionString, ContractCreationLocks.CreatorEventResource(contractId));
        try
        {
            var status = await service.EnsureContractCreatorEventAsync(contractId);
            Assert.Equal(ContractCreatorEventStatus.NotRecorded, status);                                   // هرگز «نوشته شد» نیست
            Assert.Empty(await EventsAsync(contractId));
        }
        finally { tx.Rollback(); await tx.DisposeAsync(); await conn.DisposeAsync(); }

        Assert.Equal(ContractCreatorEventStatus.Repaired, await service.EnsureContractCreatorEventAsync(contractId));
        var ev = Assert.Single(await EventsAsync(contractId));
        Assert.Equal(creator, ev.UserId);
    }

    // =============================================================== T12 / T13 : لایه‌ی کنترلر و SoD روی SQL واقعی
    private const string ManagerOne = "manager.one";
    private const string ManagerTwo = "manager.two";

    private sealed record Rig(TenderController Controller, PEMSDbContext Ctx, FakeCurrentUserService User, AccessControlService AccessControl,
        NthSaveFaultInterceptor Faults, int ManagerOneId, int ManagerTwoId);

    private async Task<Rig> BuildRigAsync()
    {
        var faults = new NthSaveFaultInterceptor();
        var ctx = _db.NewContext(new IInterceptor[] { faults });
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

        async Task<int> AddManager(string name)
        {
            var user = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@pems.local", FullName = name };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = ctx.Roles.First(r => r.Name == SystemRoles.ContractManager).Id });
            await ctx.SaveChangesAsync();
            return user.Id;
        }
        var m1 = await AddManager(ManagerOne);
        var m2 = await AddManager(ManagerTwo);

        // SCRUM-498: a signed-in user always has a user name; without it the tender creator separation (P0-10) cannot verify
        // the approver and fails closed with Forbid
        var user2 = new FakeCurrentUserService { UserId = m1, UserName = ManagerOne, Roles = new[] { SystemRoles.ContractManager } };
        var tenderService = new TenderService(ctx, _numbering);
        var controller = new TenderController(tenderService, new ProcurementPackageService(ctx), permissionService, user2, ctx, accessControl).WithTempData();
        ctx.CurrentUserName = ManagerOne;
        return new Rig(controller, ctx, user2, accessControl, faults, m1, m2);
    }

    private Task<int> GuardRowsAsync(int tenderId)
        => _db.ScalarIntAsync("SELECT COUNT(*) FROM AuthorizationAuditLogs WHERE ResourceName='Tender' AND ActionName='Approve' AND Decision='Allow' AND ResourceId=@t", ("@t", tenderId.ToString()));

    [Fact]
    public async Task T12_GuardAuditRows_SurviveAContractCreationRollback_AndAreDistinctFromTheCreatorEvent()
    {
        var rig = await BuildRigAsync();
        var tenderId = await AwardedTenderAsync();
        Assert.Equal(0, await GuardRowsAsync(tenderId));

        // ۱-۲ = رکوردهای گارد Tender.Approve (خارج از تراکنش ساخت قرارداد)، ۳ = A، ۴ = B، ۵ = C ← شکست ردیف رویداد
        rig.Faults.Arm(5);
        var failed = await rig.Controller.CreateContract(tenderId, "CT-T12", null, null);

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(failed).ActionName);       // به قرارداد هدایت نمی‌شود: قرارداد ساخته نشده
        Assert.Equal(0, await ContractsAsync());
        Assert.Null(await LinkedAsync(tenderId));
        Assert.Empty(await EventsAsync());
        Assert.Equal(1, await GuardRowsAsync(tenderId));                                        // رکورد گارد باقی ماند
        var message = rig.Controller.TempData["Error"]?.ToString() ?? "";
        Assert.Contains("هیچ تغییری ذخیره نشد", message);
        Assert.DoesNotContain("ساخته و ذخیره شد", message);

        rig.Faults.Disarm();
        var ok = await rig.Controller.CreateContract(tenderId, "CT-T12", null, null);
        Assert.Equal("Contract", Assert.IsType<RedirectToActionResult>(ok).ControllerName);
        Assert.Equal(2, await GuardRowsAsync(tenderId));                                        // هر تلاش یک رکورد گارد جدا (بدون یکتایی سراسری)
        var linked = await LinkedAsync(tenderId);
        var ev = Assert.Single(await EventsAsync());                                            // و دقیقاً یک رویداد ایجادکننده
        Assert.Equal(rig.ManagerOneId, ev.UserId);
        Assert.Equal(linked!.Value.ToString(), ev.ResourceId);
    }

    [Fact]
    public async Task T13_CreatorSelfApproval_IsDeniedBySoD_AfterCreation_AndAfterLegacyRepair()
    {
        var rig = await BuildRigAsync();
        var tenderId = await AwardedTenderAsync();
        var created = await rig.Controller.CreateContract(tenderId, "CT-T13", null, null);
        Assert.Equal("Contract", Assert.IsType<RedirectToActionResult>(created).ControllerName);
        var contractId = (await LinkedAsync(tenderId))!.Value;

        var self = await rig.AccessControl.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest { UserId = rig.ManagerOneId, ResourceName = "Contracts", ActionName = "Approve", ResourceId = contractId.ToString() });
        Assert.False(self.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, self.Reason);
        var other = await rig.AccessControl.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest { UserId = rig.ManagerTwoId, ResourceName = "Contracts", ActionName = "Approve", ResourceId = contractId.ToString() });
        Assert.True(other.Allowed, "control: a different user with the same role must still be allowed");

        // قرارداد «قدیمی» بدون رویداد: SoD کور است تا ترمیم انجام شود
        var legacyTender = await AwardedTenderAsync();
        var (seed, seedCtx) = NewService(ManagerOne);
        var legacy = await seed.CreateContractFromWinningBidAsync(legacyTender, "CT-T13-LEGACY", null, null);
        await seedCtx.DisposeAsync();
        var legacyId = legacy.ContractId!.Value;
        Assert.Empty(await EventsAsync(legacyId));

        var blind = await rig.AccessControl.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest { UserId = rig.ManagerOneId, ResourceName = "Contracts", ActionName = "Approve", ResourceId = legacyId.ToString() });
        Assert.True(blind.Allowed, "documented gap: without the creator event SoD cannot see the creator");

        var (repairService, repairCtx) = NewService(ManagerTwo);
        Assert.Equal(ContractCreatorEventStatus.Repaired, await repairService.EnsureContractCreatorEventAsync(legacyId));
        await repairCtx.DisposeAsync();

        var afterRepair = await rig.AccessControl.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest { UserId = rig.ManagerOneId, ResourceName = "Contracts", ActionName = "Approve", ResourceId = legacyId.ToString() });
        Assert.False(afterRepair.Allowed);
        Assert.Equal(AccessDecisionReason.DeniedBySoD, afterRepair.Reason);
    }

    // =============================================================== T14 : Detection Query
    /// <summary>دقیقاً همان کوئری ثبت‌شده در docs/procurement/P0_HARDENING_GATE_EVIDENCE.md (فقط‌خواندنی).</summary>
    private const string DetectionQuery =
        "SELECT c.ContractId, c.ContractNo, c.CreatedBy FROM Contracts c JOIN Tenders t ON t.ContractId = c.ContractId " +
        "WHERE NOT EXISTS (SELECT 1 FROM AuthorizationAuditLogs l WHERE l.ResourceName='Contracts' AND l.ActionName='Create' " +
        "AND l.ResourceId = CONVERT(nvarchar(50), c.ContractId) AND l.Decision='Allow');";

    [Fact]
    public async Task T14_DetectionQuery_ReportsNoOrphans_AfterSuccessAndRepair_AndFindsADeliberatelySeededOrphan()
    {
        var creator = await UserAsync("creator.t14");
        var tenderOk = await AwardedTenderAsync();
        var (service, ctx) = NewService("creator.t14");
        Assert.True((await service.CreateContractFromWinningBidAsync(tenderOk, "CT-T14-OK", null, null, creator, null)).Success);
        await ctx.DisposeAsync();
        Assert.Empty(await _db.QueryAsync(DetectionQuery, r => r.GetInt32(0)));                             // موفق → بدون یتیم

        var tenderOrphan = await AwardedTenderAsync();
        var (seed, seedCtx) = NewService("creator.t14");
        var orphan = await seed.CreateContractFromWinningBidAsync(tenderOrphan, "CT-T14-ORPHAN", null, null);   // عمداً بی‌رویداد
        await seedCtx.DisposeAsync();
        var found = await _db.QueryAsync(DetectionQuery, r => (Id: r.GetInt32(0), No: r.GetString(1), By: r.IsDBNull(2) ? null : r.GetString(2)));
        var row = Assert.Single(found);
        Assert.Equal(orphan.ContractId, row.Id);
        Assert.Equal("CT-T14-ORPHAN", row.No);
        Assert.Equal("creator.t14", row.By);

        var (repairService, repairCtx) = NewService("operator");
        Assert.Equal(ContractCreatorEventStatus.Repaired, await repairService.EnsureContractCreatorEventAsync(orphan.ContractId!.Value));
        await repairCtx.DisposeAsync();
        Assert.Empty(await _db.QueryAsync(DetectionQuery, r => r.GetInt32(0)));                             // ترمیم → بدون یتیم
    }

    // =============================================================== پاک‌سازی فقط مدخل‌های همین عملیات (Context مشترک)
    [Fact]
    public async Task FailedCreation_DoesNotDetachOrClearUnrelatedTrackedEntities_OnASharedContext()
    {
        var tenderId = await AwardedTenderAsync();
        var creator = await UserAsync("creator.cleanup");
        var faults = new NthSaveFaultInterceptor();
        var (service, ctx) = NewService("creator.cleanup", new IInterceptor[] { faults });

        // موجودیت نامرتبطِ ردیابی‌شده (فراخواننده پیش از عملیات خوانده است)
        var project = await ctx.Projects.FirstAsync();
        var trackedBefore = ctx.ChangeTracker.Entries().Select(e => e.Entity).ToList();
        Assert.Contains(project, trackedBefore);

        faults.Arm(3);
        var failed = await service.CreateContractFromWinningBidAsync(tenderId, "CT-CLEANUP", null, null, creator, null);

        Assert.False(failed.Success);
        Assert.Contains(project, ctx.ChangeTracker.Entries().Select(e => e.Entity));                         // Clear() نشده است
        Assert.Equal(EntityState.Unchanged, ctx.Entry(project).State);
        Assert.DoesNotContain(ctx.ChangeTracker.Entries(), e => e.Entity is Contract or AuthorizationAuditLog or AuditLog);   // مدخل‌های خود عملیات Detach شده‌اند
        Assert.DoesNotContain(ctx.ChangeTracker.Entries(), e => e.Entity is Tender);
        await AssertNothingPersistedAsync(tenderId);
    }
}
