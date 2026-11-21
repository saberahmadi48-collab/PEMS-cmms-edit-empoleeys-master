using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// P0.3 (بدون SQL) - نام منابع قفل کاربردی و پاک‌سازی ردیابی Context مشترک. این تست‌ها هیچ ادعایی درباره‌ی اتمیک‌بودن یا هم‌زمانی SQL ندارند؛
/// آن‌ها در ContractCreationAtomicitySqlTests روی SQL Server یک‌بارمصرف اثبات می‌شوند.
/// </summary>
public class ContractCreationScopeTests
{
    [Fact]
    public void ContractNoLockResource_IsTheSame_ForCaseWhitespaceAndWidthVariants()
    {
        var baseKey = ContractCreationLocks.ContractNoResource("CT-100");

        Assert.Equal(baseKey, ContractCreationLocks.ContractNoResource("  ct-100 "));
        Assert.Equal(baseKey, ContractCreationLocks.ContractNoResource("Ct-100\t"));
        Assert.Equal(baseKey, ContractCreationLocks.ContractNoResource("ＣＴ-１００"));          // عرض کامل → FormKC
        Assert.Equal(ContractCreationLocks.ContractNoResource("ss-1"), ContractCreationLocks.ContractNoResource("\u00DF-1"));      // ss = ß (Collation)
        Assert.Equal(ContractCreationLocks.ContractNoResource("AE-1"), ContractCreationLocks.ContractNoResource("\u00E6-1"));      // ae = æ
        Assert.Equal(ContractCreationLocks.ContractNoResource("OE-1"), ContractCreationLocks.ContractNoResource("\u0152-1"));      // oe = Œ
        Assert.NotEqual(baseKey, ContractCreationLocks.ContractNoResource("CT-101"));
        Assert.NotEqual(baseKey, ContractCreationLocks.ContractNoResource("CT 100"));            // فاصله‌ی داخلی معنادار است (SQL هم برابر نمی‌داند)
    }

    [Fact]
    public void LockResourceNames_AreBoundedDeterministicAndDistinctPerKind()
    {
        var no = ContractCreationLocks.ContractNoResource(new string('x', 5000));
        Assert.True(no.Length < 255);
        Assert.Equal(no, ContractCreationLocks.ContractNoResource(new string('X', 5000)));
        Assert.StartsWith("PEMS:Contracts.No:", no);
        Assert.NotEqual(ContractCreationLocks.TenderResource(7), ContractCreationLocks.CreatorEventResource(7));
        Assert.NotEqual(ContractCreationLocks.TenderResource(7), ContractCreationLocks.TenderResource(8));
        Assert.Equal(ContractCreationLocks.ContractNoResource(null), ContractCreationLocks.ContractNoResource("  "));
    }

    [Fact]
    public void TenderService_StillResolvesFromDependencyInjection_WithTheOptionalLockTimeoutParameter()
    {
        var options = new DbContextOptionsBuilder<PEMSDbContext>().UseInMemoryDatabase(nameof(TenderService_StillResolvesFromDependencyInjection_WithTheOptionalLockTimeoutParameter)).Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new PEMSDbContext(options));
        services.AddSingleton<INumberingService, FakeNumberingService>();
        services.AddScoped<ITenderService, TenderService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.IsType<TenderService>(scope.ServiceProvider.GetRequiredService<ITenderService>());
    }

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

    [Fact]
    public async Task FailedCreation_RestoresAPreTrackedTender_AndLeavesUnrelatedTrackedEntitiesAlone()
    {
        var faults = new FailOnNthSave();
        var db = nameof(FailedCreation_RestoresAPreTrackedTender_AndLeavesUnrelatedTrackedEntitiesAlone);
        var options = new DbContextOptionsBuilder<PEMSDbContext>().UseInMemoryDatabase(db)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).AddInterceptors(faults).Options;
        await using var ctx = new PEMSDbContext(options);

        var user = new ApplicationUser { UserName = "creator.a", Email = "a@pems.local", FullName = "A" };
        var project = new Project { ProjectCode = "P1", ProjectName = "Project One", IsActive = true };
        ctx.Users.Add(user);
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

        // این Context، مناقصه و پروژه را از قبل ردیابی می‌کند (فراخواننده‌ی مشترک)
        var tender = ctx.Tenders.Local.Single();
        Assert.Same(project, ctx.Projects.Local.Single());

        faults.Calls = 0;
        faults.FailOn = 1;                                                                  // ذخیره‌ی A شکست می‌خورد، پس از این‌که tender.Contract ست شده بود
        var failed = await service.CreateContractFromWinningBidAsync(tenderId, "CT-S1", null, null, user.Id, null);

        Assert.False(failed.Success);
        Assert.Same(tender, ctx.Tenders.Local.Single());                                    // همان نمونه هنوز ردیابی می‌شود (Clear نشده)
        Assert.Equal(EntityState.Unchanged, ctx.Entry(tender).State);                       // و از دیتابیس بازخوانی شده است
        Assert.Null(tender.ContractId);
        Assert.Null(tender.Contract);
        Assert.Same(project, ctx.Projects.Local.Single());
        Assert.Empty(ctx.Contracts.Local);
        Assert.Empty(ctx.AuthorizationAuditLogs.Local);
    }
}
