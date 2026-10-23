using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0 / F-02 + F-03 (لایه‌ی سرویس): (۱) صلاحیت برنده نباید ردِ ارزیابی اجباری را با «Any(Passed)» نادیده بگیرد؛ (۲) ساخت قرارداد از
/// برنده اتمیک است (قرارداد یتیم/تکراری نمی‌سازد) و شماره‌ی قرارداد را اعتبارسنجی می‌کند.
/// </summary>
public class TenderAwardHardeningTests
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
    }

    private static PEMSDbContext NewContext(string db, FailOnNthSave? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase(db)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return new PEMSDbContext(builder.Options);
    }

    private sealed record Arrange(PEMSDbContext Ctx, TenderService Service, int TenderId, int BidId);

    /// <summary>مناقصه‌ی بسته‌شده با یک پیشنهاد؛ ارزیابی‌ها را هر تست خودش ثبت می‌کند.</summary>
    private static async Task<Arrange> ClosedTenderWithBidAsync(string db, FailOnNthSave? interceptor = null)
    {
        var ctx = NewContext(db, interceptor);
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
        return new Arrange(ctx, service, tenderId, bid.BidId!.Value);
    }

    private static Task Evaluate(Arrange a, string type, string decision)
        => a.Service.RecordBidEvaluationAsync(a.BidId, type, 1, type == "Technical" ? "Engineering" : "Finance", decision, null);

    private static async Task AssertNotAwardedAsync(Arrange a, (bool Success, string? Error) result)
    {
        Assert.False(result.Success);
        var tender = await a.Ctx.Tenders.SingleAsync();
        Assert.Equal("Closed", tender.Status);
        Assert.Null(tender.AwardedBidId);
        Assert.NotEqual("Won", (await a.Ctx.Bids.SingleAsync()).Status);
    }

    // ----------------------------------------------------------- F-02 : ماتریس صلاحیت برنده
    [Fact]
    public async Task Winner_PassPass_IsEligible()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_PassPass_IsEligible));
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Passed");

        var result = await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1);

        Assert.True(result.Success);
        Assert.Equal("Awarded", (await a.Ctx.Tenders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Winner_FailOnly_Technical_IsRejected_WithAFailureMessage()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_FailOnly_Technical_IsRejected_WithAFailureMessage));
        await Evaluate(a, "Technical", "Failed");
        await Evaluate(a, "Commercial", "Passed");

        var result = await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1);

        await AssertNotAwardedAsync(a, result);
        Assert.Contains("مردود", result.Error);
    }

    [Fact]
    public async Task Winner_TechnicalPassedThenFailed_IsRejected_TheFailureIsNotIgnored()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_TechnicalPassedThenFailed_IsRejected_TheFailureIsNotIgnored));
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Technical", "Failed");
        await Evaluate(a, "Commercial", "Passed");

        await AssertNotAwardedAsync(a, await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1));
    }

    [Fact]
    public async Task Winner_TechnicalFailedThenPassed_IsRejected_ConservativeRule()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_TechnicalFailedThenPassed_IsRejected_ConservativeRule));
        await Evaluate(a, "Technical", "Failed");
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Passed");

        await AssertNotAwardedAsync(a, await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1));
    }

    [Fact]
    public async Task Winner_CommercialPassedThenFailed_IsRejected()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_CommercialPassedThenFailed_IsRejected));
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Passed");
        await Evaluate(a, "Commercial", "Failed");

        await AssertNotAwardedAsync(a, await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1));
    }

    [Theory]
    [InlineData("Technical")]
    [InlineData("Commercial")]
    public async Task Winner_MissingMandatoryEvaluation_IsRejected(string passedOnly)
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_MissingMandatoryEvaluation_IsRejected) + passedOnly);
        await Evaluate(a, passedOnly, "Passed");

        await AssertNotAwardedAsync(a, await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1));
    }

    [Fact]
    public async Task Winner_PendingEvaluationIsNeverACountedPass()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_PendingEvaluationIsNeverACountedPass));
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Pending");

        await AssertNotAwardedAsync(a, await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1));
    }

    [Fact]
    public async Task Winner_InactiveFailedEvaluation_IsNotApplicable_AndDoesNotBlock()
    {
        var a = await ClosedTenderWithBidAsync(nameof(Winner_InactiveFailedEvaluation_IsNotApplicable_AndDoesNotBlock));
        await Evaluate(a, "Technical", "Failed");
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Passed");
        var failed = await a.Ctx.BidEvaluations.SingleAsync(x => x.EvaluationType == "Technical" && x.Decision == "Failed");
        failed.IsActive = false;                         // ارزیابی باطل‌شده‌ی مدیریتی: دیگر «قابل‌اعمال» نیست
        await a.Ctx.SaveChangesAsync();

        var result = await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1);

        Assert.True(result.Success);
    }

    // ----------------------------------------------------------- F-03 : ساخت قرارداد از برنده
    private static async Task<Arrange> AwardedAsync(string db, FailOnNthSave? interceptor = null)
    {
        var a = await ClosedTenderWithBidAsync(db, interceptor);
        await Evaluate(a, "Technical", "Passed");
        await Evaluate(a, "Commercial", "Passed");
        Assert.True((await a.Service.SelectWinnerAsync(a.TenderId, a.BidId, 1)).Success);
        return a;
    }

    [Fact]
    public async Task CreateContract_RetryAfterSuccess_IsRejected_AndNeverDuplicatesTheContract()
    {
        var a = await AwardedAsync(nameof(CreateContract_RetryAfterSuccess_IsRejected_AndNeverDuplicatesTheContract));

        var first = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-100", null, null);
        var retry = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-101", null, null);

        Assert.True(first.Success);
        Assert.False(retry.Success);
        Assert.Equal(1, await a.Ctx.Contracts.CountAsync());
        Assert.Equal(first.ContractId, (await a.Ctx.Tenders.SingleAsync()).ContractId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateContract_WithBlankNumber_IsRejected(string number)
    {
        var a = await AwardedAsync(nameof(CreateContract_WithBlankNumber_IsRejected) + number.Length);

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, number, null, null);

        Assert.False(result.Success);
        Assert.Equal(0, await a.Ctx.Contracts.CountAsync());
    }

    [Fact]
    public async Task CreateContract_WithAnExistingNumber_IsRejected()
    {
        var a = await AwardedAsync(nameof(CreateContract_WithAnExistingNumber_IsRejected));
        a.Ctx.Contracts.Add(new Contract { ContractNo = "CT-777", ProjectId = 1, VendorId = 1, Status = "Draft" });
        await a.Ctx.SaveChangesAsync();

        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, " CT-777 ", null, null);

        Assert.False(result.Success);
        Assert.Equal(1, await a.Ctx.Contracts.CountAsync());
        Assert.Null((await a.Ctx.Tenders.SingleAsync()).ContractId);
    }

    /// <summary>
    /// اتمیک‌بودن: هر ذخیره‌ای که شکست بخورد (اولین یا دومین SaveChanges)، نتیجه یا «هیچ‌کدام» یا «قرارداد + اتصال همراه هم» است؛
    /// هرگز قرارداد یتیم بدون اتصال به مناقصه (که پیاده‌سازی قدیمیِ دو-ذخیره‌ای در شکست ذخیره‌ی دوم می‌ساخت).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreateContract_WhenASaveFails_NeverLeavesAnOrphanContract(int failingSave)
    {
        var db = nameof(CreateContract_WhenASaveFails_NeverLeavesAnOrphanContract) + failingSave;
        var interceptor = new FailOnNthSave();
        var a = await AwardedAsync(db, interceptor);

        interceptor.Calls = 0;
        interceptor.FailOn = failingSave;
        var result = await a.Service.CreateContractFromWinningBidAsync(a.TenderId, "CT-ORPHAN", null, null);
        interceptor.FailOn = 0;

        // وضعیت پایدار را با Context تازه (بدون Interceptor و بدون Change Tracker قبلی) می‌خوانیم
        using var fresh = NewContext(db);
        var contracts = await fresh.Contracts.CountAsync();
        var tender = await fresh.Tenders.SingleAsync();
        Assert.Equal(tender.ContractId.HasValue ? 1 : 0, contracts);
        if (failingSave == 1) Assert.False(result.Success);
    }
}
