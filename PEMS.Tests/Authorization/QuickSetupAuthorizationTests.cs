using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS — Quick Setup &amp; Domain Import Architecture Refactoring» بند ۱۰/۲۳/۲۴: Quick
/// Setup خودش یک لایه‌ی مجوز مستقل دارد (Setup/View) - داشتن این مجوز هرگز به معنای مجوز انجام
/// Import های دامنه‌ای (MDR/VDR/Equipment/MRS) نیست. این تست‌ها هم گیت خودِ QuickSetupController
/// و هم عدم‌دورزدن مجوزهای Import قبلاً سخت‌شده (Regression) را می‌سنجند.
/// </summary>
public class QuickSetupAuthorizationTests
{
    private static async Task<(PEMSDbContext Ctx, IPermissionService Permissions, ICurrentUserService CurrentUser)>
        CreateInfra(string dbName, string? role)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"QuickSetupAuth_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissionService = new PermissionService(ctx, accessControl);
        await permissionService.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        var currentUser = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>() };
        return (ctx, permissionService, currentUser);
    }

    private static ISetupService CreateSetupService(PEMSDbContext ctx)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:Url"] = "http://localhost:11434",
            ["Ollama:Model"] = "qwen2.5:3b"
        }).Build();

        return new SetupService(ctx, configuration);
    }

    private static IServiceProvider BuildProvider(PEMSDbContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }

    // ================= Regression: Quick Setup does not bypass domain import authorization =================

    [Fact]
    public async Task SetupPermission_DoesNotGrantEquipmentImportController_DirectAccessStillForbidden()
    {
        // اثبات صریح بند ۱۰: کاربری که Setup/View دارد ولی EquipmentImport ندارد، اگر مستقیماً
        // URL کنترلر Import را هم صدا بزند، باز رد می‌شود - Setup هیچ Bypass ای ایجاد نمی‌کند.
        var (ctx, perms, user) = await CreateInfra(nameof(SetupPermission_DoesNotGrantEquipmentImportController_DirectAccessStillForbidden), role: SystemRoles.QAManager);
        ctx.RolePermissions.Add(new PEMS.Domain.Entities.RolePermission
        {
            RoleName = SystemRoles.QAManager,
            ModuleName = "Setup",
            CanView = true,
            CreatedDate = DateTime.Now
        });
        await ctx.SaveChangesAsync();
        await AccessControlSeeder.SeedFromLegacyAsync(BuildProvider(ctx));

        var equipmentImportController = new EquipmentImportController(
            new FakeEquipmentImportService(), new FakeEquipmentPdfScanService(), ctx, user, perms).WithTempData();

        var result = await equipmentImportController.Index();

        Assert.IsType<ForbidResult>(result);
    }

    private class FakeEquipmentImportService : IEquipmentImportService
    {
        public Task<int> StageFromExcelAsync(Stream fileStream, string fileName, int projectId, int? importedById) => Task.FromResult(1);
        public Task<PEMS.Application.DTOs.EquipmentImportBatchDto?> GetBatchAsync(int batchId) => Task.FromResult<PEMS.Application.DTOs.EquipmentImportBatchDto?>(null);
        public Task<List<PEMS.Application.DTOs.EquipmentImportBatchDto>> GetRecentBatchesAsync() => Task.FromResult(new List<PEMS.Application.DTOs.EquipmentImportBatchDto>());
        public Task SetRowIncludeAsync(int rowId, bool include) => Task.CompletedTask;
        public Task<(int created, int updated, int skipped)> CommitBatchAsync(int batchId) => Task.FromResult((0, 0, 0));
        public Task DiscardBatchAsync(int batchId) => Task.CompletedTask;
    }

    private class FakeEquipmentPdfScanService : IEquipmentPdfScanService
    {
        public Task<List<string>> ScanForTagCandidatesAsync(string filePath) => Task.FromResult(new List<string>());
    }
}
