using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.PartCatalogue;

/// <summary>
/// تنظیمات سراسری کاتالوگ قطعه (روش تولید شماره‌ی قطعه + قالب): پیش‌فرض MANUAL (رفتار فعلی)، فقط PartCatalogue.Admin می‌تواند
/// ذخیره کند، اعتبارسنجی روش/قالب، و تضمین یک‌ردیفی بودن در مدل (ایندکس یکتا + قید CHECK).
/// </summary>
public class PartCatalogueSettingsServiceTests
{
    private sealed class Actions : IRoleModuleActionService
    {
        public Task<bool> HasActionAsync(IEnumerable<string> userRoles, string module, string action) =>
            Task.FromResult(module == "PartCatalogue" && action == "Admin" && userRoles.Contains("Admin"));
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private static readonly PcActor Admin = new(1, 101, new[] { "Admin" });
    private static readonly PcActor Regular = new(2, 102, new[] { "PC" });

    private static (PEMSDbContext ctx, PartCatalogueSettingsService svc) New(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("pcs_" + name);
        return (ctx, new PartCatalogueSettingsService(ctx, new Actions()));
    }

    [Fact]
    public async Task Get_WithoutARow_ReturnsTheManualDefault()
    {
        var (_, svc) = New(nameof(Get_WithoutARow_ReturnsTheManualDefault));

        var s = await svc.GetAsync();

        Assert.Equal(PartNoGenerationMethods.Manual, s.PartNoGenerationMethod);
        Assert.Null(s.PartNoFormat);
        Assert.False(s.IsPersisted);
    }

    [Fact]
    public async Task Save_AsAdmin_PersistsAndIsReadBack_UpdatingTheSameSingleRow()
    {
        var (ctx, svc) = New(nameof(Save_AsAdmin_PersistsAndIsReadBack_UpdatingTheSameSingleRow));

        Assert.True((await svc.SaveAsync("api", "  PCR-YYYY-NNNN  ", null, Admin)).Success);
        var first = await svc.GetAsync();
        Assert.Equal("API", first.PartNoGenerationMethod);
        Assert.Equal("PCR-YYYY-NNNN", first.PartNoFormat);   // normalised: trimmed, method upper-cased
        Assert.True(first.IsPersisted);
        Assert.NotNull(first.ModifiedDate);

        Assert.True((await svc.SaveAsync("MANUAL", null, first.RowVersion, Admin)).Success);
        var second = await svc.GetAsync();
        Assert.Equal("MANUAL", second.PartNoGenerationMethod);
        Assert.Null(second.PartNoFormat);
        Assert.Equal(1, await ctx.PartCatalogueSettings.CountAsync());
    }

    [Fact]
    public async Task Save_ByANonAdmin_IsDenied_AndPersistsNothing()
    {
        var (ctx, svc) = New(nameof(Save_ByANonAdmin_IsDenied_AndPersistsNothing));

        var r = await svc.SaveAsync("API", "PCR-NNNN", null, Regular);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, x => x.Key == "Pc_Err_NotAuthorized");
        Assert.False(await ctx.PartCatalogueSettings.AnyAsync());
    }

    [Theory]
    [InlineData(null, null, "Pc_Err_PartNoMethodInvalid")]
    [InlineData("", null, "Pc_Err_PartNoMethodInvalid")]
    [InlineData("AUTO", null, "Pc_Err_PartNoMethodInvalid")]
    [InlineData("API", null, "Pc_Err_PartNoFormatRequired")]
    [InlineData("API", "   ", "Pc_Err_PartNoFormatRequired")]
    [InlineData("API", "PCR-YYYY", "Pc_Err_PartNoFormatNeedsSequence")]
    [InlineData("API", "PCR-NN", "Pc_Err_PartNoFormatNeedsSequence")]
    [InlineData("MANUAL", "PCR YYYY NNNN", "Pc_Err_PartNoFormatInvalid")]
    [InlineData("MANUAL", "PCR-<NNNN>", "Pc_Err_PartNoFormatInvalid")]
    public async Task Save_InvalidInput_IsRejected_AndPersistsNothing(string? method, string? format, string expectedError)
    {
        var (ctx, svc) = New("invalid_" + (method ?? "null") + "_" + (format ?? "null").Length + expectedError);

        var r = await svc.SaveAsync(method, format, null, Admin);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, x => x.Key == expectedError);
        Assert.False(await ctx.PartCatalogueSettings.AnyAsync());
    }

    [Fact]
    public async Task Save_FormatLongerThanTheColumn_IsRejected()
    {
        var (_, svc) = New(nameof(Save_FormatLongerThanTheColumn_IsRejected));

        var r = await svc.SaveAsync("API", new string('A', 40) + "NNNNNNNNNNN", null, Admin);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, x => x.Key == "Pc_Err_TooLong");
    }

    [Fact]
    public async Task Save_ManualWithoutFormat_IsValid()
    {
        var (_, svc) = New(nameof(Save_ManualWithoutFormat_IsValid));

        Assert.True((await svc.SaveAsync("MANUAL", "", null, Admin)).Success);
    }

    [Fact]
    public void Model_EnforcesASingleRowAndAValidMethod()
    {
        var (ctx, _) = New(nameof(Model_EnforcesASingleRowAndAValidMethod));
        // ایندکس‌ها/قیدهای CHECK فقط در مدل Design-Time در دسترس‌اند (نه مدل بهینه‌ی زمان اجرا)
        var entity = ctx.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PartCatalogueSettings))!;

        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(PartCatalogueSettings.SingletonKey));
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_PartCatalogueSettings_Method");
        Assert.Equal(20, entity.FindProperty(nameof(PartCatalogueSettings.PartNoGenerationMethod))!.GetMaxLength());
        Assert.Equal(50, entity.FindProperty(nameof(PartCatalogueSettings.PartNoFormat))!.GetMaxLength());
    }
}
