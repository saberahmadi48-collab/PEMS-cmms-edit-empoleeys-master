using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0.3 - این کلاس رفتار «بدون تراکنش» (InMemory) را می‌سنجد: روی SQL Server کل عملیات (قرارداد، هر دو AuditLog عمومی، رویداد) اتمیک است و
/// شکست هر ذخیره همه را Rollback می‌کند؛ اتمیک‌بودن و Rollback فقط در PEMS.Tests.Procurement.ContractCreationAtomicitySqlTests (SQL Server یک‌بارمصرف)
/// اثبات می‌شود. InMemory نه تراکنش دارد نه Rollback، پس اینجا هر ذخیره‌ی موفق ماندگار است و طبقه‌بندی نتیجه از وضعیت واقعی دیتابیس خوانده می‌شود.
/// P0.1 / R-01 - رویداد «Contracts.Create» برای قرارداد ساخته‌شده از مناقصه. PEMSDbContext.SaveChangesAsync برای هر ذخیره دو ذخیره‌ی فیزیکی
/// جدا دارد (داده‌ی کسب‌وکار، سپس AuditLog عمومی)، و رویداد خودش هم یک ذخیره‌ی جداست. این تست‌ها هر پنجره‌ی شکست را جدا تزریق می‌کنند و
/// «وضعیت ذخیره‌شده و تعداد رویدادها» را می‌سنجند (نه فقط کد وضعیت). توجه: دیتابیس InMemory تراکنش، RowVersion و قید یکتا را اعمال نمی‌کند؛
/// پس این تست‌ها رفتار برنامه را اثبات می‌کنند، نه تضمین‌های SQL Server یا هم‌زمانی واقعی را.
/// شماره ذخیره‌ی فیزیکی در یک عملیات: ۱ = قرارداد+اتصال مناقصه، ۲ = AuditLog عمومیِ آن، ۳ = ردیف رویداد، ۴ = AuditLog عمومیِ رویداد.
/// </summary>
public class TenderContractCreatorEventTests
{
    private sealed class FailOnNthSave : SaveChangesInterceptor
    {
        public int Calls;
        public int FailOn;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailOn > 0 && Calls == FailOn) throw new DbUpdateException("injected failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public void Arm(int failOn) { Calls = 0; FailOn = failOn; }
        public void Disarm() { Calls = 0; FailOn = 0; }
    }

    private static PEMSDbContext NewContext(string db, FailOnNthSave? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase(db)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return new PEMSDbContext(builder.Options);
    }

    private const string CreatorName = "creator.a";
    private const string RetrierName = "retrier.b";

    private sealed record Arrange(string Db, PEMSDbContext Ctx, FailOnNthSave Faults, TenderService Service, int TenderId, int CreatorId, int RetrierId);

    private static async Task<Arrange> AwardedAsync(string name)
    {
        var db = $"TenderEvt_{name}";
        var faults = new FailOnNthSave();
        var ctx = NewContext(db, faults);

        var creator = new ApplicationUser { UserName = CreatorName, Email = "a@pems.local", FullName = "Creator A" };
        var retrier = new ApplicationUser { UserName = RetrierName, Email = "b@pems.local", FullName = "Retrier B" };
        ctx.Users.Add(creator);
        ctx.Users.Add(retrier);
        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();

        var package = new ProcurementPackage { PackageNumber = "PP-001", ProjectId = project.ProjectId, Title = "Pumps", IsActive = true };
        var vendor = new Vendor { Name = "Qualified Vendor", IsActive = true, QualificationStatus = "Qualified", QualificationExpiryDate = DateTime.Now.AddYears(1) };
        ctx.ProcurementPackages.Add(package);
        ctx.Vendors.Add(vendor);
        await ctx.SaveChangesAsync();

        var service = new TenderService(ctx, new FakeNumberingService());
        var tenderId = await service.CreateAsync(package.ProcurementPackageId, "Pump Tender", null, null, false, null, null, null, null);
        await service.InviteVendorAsync(tenderId, vendor.VendorId);
        await service.OpenForBiddingAsync(tenderId);
        var bid = await service.SubmitBidAsync(tenderId, vendor.VendorId, 100000m, "USD", null, null);
        await service.CloseBiddingAsync(tenderId);
        await service.RecordBidEvaluationAsync(bid.BidId!.Value, "Technical", 1, "Engineering", "Passed", null);
        await service.RecordBidEvaluationAsync(bid.BidId!.Value, "Commercial", 1, "Finance", "Passed", null);
        Assert.True((await service.SelectWinnerAsync(tenderId, bid.BidId!.Value, 1)).Success);

        faults.Disarm();
        return new Arrange(db, ctx, faults, service, tenderId, creator.Id, retrier.Id);
    }

    /// <summary>وضعیت پایدار را با Context تازه (بدون Interceptor و بدون Change Tracker) می‌خواند.</summary>
    private static async Task<(int Contracts, int? LinkedContractId, List<AuthorizationAuditLog> Events)> StoredAsync(string db)
    {
        using var fresh = NewContext(db);
        var events = await fresh.AuthorizationAuditLogs.AsNoTracking()
            .Where(l => l.ResourceName == "Contracts" && l.ActionName == "Create").ToListAsync();
        return (await fresh.Contracts.CountAsync(), (await fresh.Tenders.AsNoTracking().SingleAsync()).ContractId, events);
    }

    // --------------------------------------------------------------- 1 : موفقیت
    [Fact]
    public async Task Success_WritesExactlyOneCreatorEvent_ForTheActor()
    {
        var a = await AwardedAsync(nameof(Success_WritesExactlyOneCreatorEvent_ForTheActor));
        a.Ctx.CurrentUserName = CreatorName;

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-1", null, null, a.CreatorId, "10.0.0.1");

        Assert.True(result.Success);
        Assert.True(result.Committed);
        Assert.False(result.GenericAuditFailed);
        Assert.Equal(ContractCreatorEventStatus.Recorded, result.CreatorEvent);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.Equal(result.ContractId, stored.LinkedContractId);
        var ev = Assert.Single(stored.Events);
        Assert.Equal(a.CreatorId, ev.UserId);
        Assert.Equal(result.ContractId.ToString(), ev.ResourceId);
        Assert.Equal(nameof(AccessDecisionReason.Allow), ev.Decision);
    }

    // --------------------------------------------------------------- 2 : تفکیک شکست کسب‌وکار از شکست AuditLog عمومی
    [Fact]
    public async Task BusinessSaveFailure_IsReportedAsNothingSaved_AndNothingIsStored()
    {
        var a = await AwardedAsync(nameof(BusinessSaveFailure_IsReportedAsNothingSaved_AndNothingIsStored));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 1);

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-2", null, null, a.CreatorId, null);

        Assert.False(result.Success);
        Assert.False(result.Committed);
        Assert.Contains("هیچ تغییری ذخیره نشد", result.Error);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(0, stored.Contracts);
        Assert.Null(stored.LinkedContractId);
        Assert.Empty(stored.Events);
    }

    [Fact]
    public async Task GenericAuditSaveFailure_WithoutATransaction_IsNeverReportedAsNothingSaved()
    {
        var a = await AwardedAsync(nameof(GenericAuditSaveFailure_WithoutATransaction_IsNeverReportedAsNothingSaved));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 2);                       // ذخیره‌ی دوم = AuditLog عمومیِ ایجاد قرارداد

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-3", null, null, a.CreatorId, null);

        // P0.3: بدون تراکنش، ذخیره‌ی اول ماندگار است. سرویس پس از شکست وضعیت واقعی را از دیتابیس می‌خواند: قرارداد Commit شده است، پس هرگز
        // «ذخیره نشد» گزارش نمی‌شود؛ رویدادِ ایجادکننده هنوز نوشته نشده (مرحله‌ی بعد اجرا نشد) و NotRecorded است و تلاش مجدد آن را ترمیم می‌کند.
        // (روی SQL Server همین شکست کل عملیات را Rollback می‌کند - SQL تست T3.)
        Assert.True(result.Success);
        Assert.True(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.NotRecorded, result.CreatorEvent);
        Assert.Null(result.Error);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.Equal(result.ContractId, stored.LinkedContractId);
        Assert.Empty(stored.Events);

        a.Faults.Disarm();
        a.Ctx.CurrentUserName = RetrierName;
        var retry = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-3", null, null, a.RetrierId, null);
        Assert.Equal(ContractCreatorEventStatus.Repaired, retry.CreatorEvent);
        var ev = Assert.Single((await StoredAsync(a.Db)).Events);
        Assert.Equal(a.CreatorId, ev.UserId);
    }

    // --------------------------------------------------------------- 3,4,7 : شکست ثبت رویداد بعد از Commit و ترمیم با تلاش مجدد
    [Fact]
    public async Task EventPersistenceFailure_AfterCommit_ReportsCommittedAndNotRecorded_NotNothingSaved()
    {
        var a = await AwardedAsync(nameof(EventPersistenceFailure_AfterCommit_ReportsCommittedAndNotRecorded_NotNothingSaved));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 3);                       // ذخیره‌ی سوم = ردیف رویداد

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-4", null, null, a.CreatorId, null);

        Assert.True(result.Success);
        Assert.True(result.Committed);
        Assert.Equal(ContractCreatorEventStatus.NotRecorded, result.CreatorEvent);
        Assert.Null(result.Error);                     // هیچ ادعای «ذخیره نشد» وجود ندارد
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.Equal(result.ContractId, stored.LinkedContractId);
        Assert.Empty(stored.Events);                   // رویداد واقعاً ثبت نشده است
    }

    [Fact]
    public async Task EventRowCommitted_ButItsGenericAuditSaveFails_IsStillReportedAsRecorded()
    {
        var a = await AwardedAsync(nameof(EventRowCommitted_ButItsGenericAuditSaveFails_IsStillReportedAsRecorded));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 4);                       // ردیف رویداد Commit می‌شود، AuditLog عمومیِ آن شکست می‌خورد

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-5", null, null, a.CreatorId, null);

        Assert.True(result.Success);
        Assert.Equal(ContractCreatorEventStatus.Recorded, result.CreatorEvent);
        Assert.Single((await StoredAsync(a.Db)).Events);
    }

    [Fact]
    public async Task Retry_AfterEventFailure_RepairsTheEvent_ForTheOriginalCreator_WithoutDuplicatingAnything()
    {
        var a = await AwardedAsync(nameof(Retry_AfterEventFailure_RepairsTheEvent_ForTheOriginalCreator_WithoutDuplicatingAnything));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 3);
        var first = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-6", null, null, a.CreatorId, null);
        Assert.Equal(ContractCreatorEventStatus.NotRecorded, first.CreatorEvent);
        a.Faults.Disarm();

        // کاربر دیگری (B) تلاش مجدد می‌کند؛ رویداد باید برای A نوشته شود، نه B
        a.Ctx.CurrentUserName = RetrierName;
        var retry = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-6", null, null, a.RetrierId, null);

        Assert.False(retry.Success);                   // قرارداد تکراری ساخته نمی‌شود
        Assert.True(retry.Committed);
        Assert.Equal(first.ContractId, retry.ContractId);
        Assert.Equal(ContractCreatorEventStatus.Repaired, retry.CreatorEvent);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        var ev = Assert.Single(stored.Events);
        Assert.Equal(a.CreatorId, ev.UserId);          // هویت ایجادکننده‌ی اصلی حفظ شد
        Assert.NotEqual(a.RetrierId, ev.UserId);
    }

    [Fact]
    public async Task RepairFailureOnRetry_ReportsTheExistingContract_NotNothingSaved()
    {
        var a = await AwardedAsync(nameof(RepairFailureOnRetry_ReportsTheExistingContract_NotNothingSaved));
        a.Ctx.CurrentUserName = CreatorName;
        var created = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-RF", null, null, creatorUserId: null);   // بی‌رویداد
        a.Ctx.CurrentUserName = RetrierName;
        a.Faults.Arm(failOn: 1);                       // ردیف رویداد ترمیم شکست می‌خورد

        var failed = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-RF", null, null, a.RetrierId, null);

        Assert.False(failed.Success);
        Assert.True(failed.Committed);
        Assert.Equal(created.ContractId, failed.ContractId);
        Assert.Equal(ContractCreatorEventStatus.NotRecorded, failed.CreatorEvent);
        Assert.DoesNotContain("هیچ تغییری ذخیره نشد", failed.Error);
        Assert.Empty((await StoredAsync(a.Db)).Events);
    }

    [Fact]
    public async Task RepeatedRetries_AfterRepair_NeverDuplicateTheEvent()
    {
        var a = await AwardedAsync(nameof(RepeatedRetries_AfterRepair_NeverDuplicateTheEvent));
        a.Ctx.CurrentUserName = CreatorName;
        a.Faults.Arm(failOn: 3);
        await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-7", null, null, a.CreatorId, null);
        a.Faults.Disarm();

        var r1 = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-7", null, null, a.RetrierId, null);
        var r2 = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-7", null, null, a.RetrierId, null);
        var r3 = await a.Service.EnsureContractCreatorEventAsync(r1.ContractId!.Value);

        Assert.Equal(ContractCreatorEventStatus.Repaired, r1.CreatorEvent);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, r2.CreatorEvent);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, r3);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.Single(stored.Events);
    }

    [Fact]
    public async Task Crash_AfterCommit_BeforeAnyEventIsWritten_RemainsRecoverable()
    {
        var a = await AwardedAsync(nameof(Crash_AfterCommit_BeforeAnyEventIsWritten_RemainsRecoverable));
        a.Ctx.CurrentUserName = CreatorName;
        // creatorUserId = null شبیه‌سازی فرآیندی است که بعد از Commit قرارداد، پیش از نوشتن رویداد از بین رفته است
        var created = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-8", null, null, creatorUserId: null);
        Assert.True(created.Success);
        Assert.Equal(ContractCreatorEventStatus.NotApplicable, created.CreatorEvent);
        Assert.Empty((await StoredAsync(a.Db)).Events);

        a.Ctx.CurrentUserName = RetrierName;
        var status = await a.Service.EnsureContractCreatorEventAsync(created.ContractId!.Value);

        Assert.Equal(ContractCreatorEventStatus.Repaired, status);
        var ev = Assert.Single((await StoredAsync(a.Db)).Events);
        Assert.Equal(a.CreatorId, ev.UserId);
    }

    // --------------------------------------------------------------- 5 : تلاش مجدد بعد از موفقیت
    [Fact]
    public async Task Retry_AfterSuccess_DoesNotDuplicateTheEvent()
    {
        var a = await AwardedAsync(nameof(Retry_AfterSuccess_DoesNotDuplicateTheEvent));
        a.Ctx.CurrentUserName = CreatorName;
        var first = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-9", null, null, a.CreatorId, null);

        var retry = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-9", null, null, a.CreatorId, null);

        Assert.Equal(ContractCreatorEventStatus.Recorded, first.CreatorEvent);
        Assert.False(retry.Success);
        Assert.Equal(ContractCreatorEventStatus.AlreadyRecorded, retry.CreatorEvent);
        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.Single(stored.Events);
    }

    // --------------------------------------------------------------- هویت ایجادکننده‌ی نامعلوم: به نام کسی نوشته نمی‌شود
    [Fact]
    public async Task UnidentifiableCreator_WritesNoEvent_AndIsReported()
    {
        var a = await AwardedAsync(nameof(UnidentifiableCreator_WritesNoEvent_AndIsReported));
        a.Ctx.CurrentUserName = null;                  // CreatedBy خالی می‌ماند
        var created = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-10", null, null, creatorUserId: null);

        a.Ctx.CurrentUserName = RetrierName;
        var status = await a.Service.EnsureContractCreatorEventAsync(created.ContractId!.Value);

        Assert.Equal(ContractCreatorEventStatus.CreatorUnidentified, status);
        Assert.Empty((await StoredAsync(a.Db)).Events);   // به نام کاربر تلاش مجدد (B) هم نوشته نشده است
    }

    [Fact]
    public async Task Ensure_ForAMissingContract_ReportsNotFound_AndWritesNothing()
    {
        var a = await AwardedAsync(nameof(Ensure_ForAMissingContract_ReportsNotFound_AndWritesNothing));

        Assert.Equal(ContractCreatorEventStatus.ContractNotFound, await a.Service.EnsureContractCreatorEventAsync(987654));
        Assert.Empty((await StoredAsync(a.Db)).Events);
    }

    // --------------------------------------------------------------- 10 : هم‌زمانی (فقط مدل InMemory؛ ناکافی برای تضمین SQL)
    [Fact]
    public async Task ConcurrentRepairs_InMemoryOnly_NeverDuplicateTheContract_AndAlwaysEndWithAnEvent()
    {
        // برچسب صریح: InMemory قید یکتا و تراکنش ندارد؛ بدون قید یکتای دیتابیس، تکرار بی‌ضررِ رویداد در اجرای هم‌زمان واقعی ممکن است.
        // این تست فقط ثابت می‌کند «قرارداد تکراری» ساخته نمی‌شود و هم‌زمانی رویداد را از بین نمی‌برد.
        var a = await AwardedAsync(nameof(ConcurrentRepairs_InMemoryOnly_NeverDuplicateTheContract_AndAlwaysEndWithAnEvent));
        a.Ctx.CurrentUserName = CreatorName;
        var created = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-11", null, null, creatorUserId: null);

        using var ctx2 = NewContext(a.Db);
        using var ctx3 = NewContext(a.Db);
        var s2 = new TenderService(ctx2, new FakeNumberingService());
        var s3 = new TenderService(ctx3, new FakeNumberingService());
        await Task.WhenAll(
            s2.EnsureContractCreatorEventAsync(created.ContractId!.Value),
            s3.EnsureContractCreatorEventAsync(created.ContractId!.Value));

        var stored = await StoredAsync(a.Db);
        Assert.Equal(1, stored.Contracts);
        Assert.InRange(stored.Events.Count, 1, 2);
        Assert.All(stored.Events, e => Assert.Equal(a.CreatorId, e.UserId));
    }
}
