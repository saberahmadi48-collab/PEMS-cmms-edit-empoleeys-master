using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS Phase 6B — Step 11: Permission Seed Hardening». همون الگوی هر-نقش-جدا که فاز ۵C
/// فقط برای EngineeringDocuments پیاده کرد، حالا برای ۷ ماژول باقی‌مانده (VDR, Contracts,
/// Vendors, MaterialRequisition, Equipment, DisciplineWorkspace, Correspondence) هم اعمال شده -
/// از طریق یک متد داخلی مشترک (SeedMissingRolesAsync)، نه کپی/پیست. این تست‌ها دقیقاً همون چهار
/// سناریوی C-7 فاز ۵C را برای هر ۷ ماژول تکرار می‌کنند: خالی، کامل (بدون تکرار در اجرای دوباره)،
/// جزئی (نقش موجود دست‌نخورده می‌ماند)، و ماژول نامرتبط (WorkOrder) بدون تغییر.
/// </summary>
public class PermissionSeedHardeningTests
{
    // ماژول -> نقش‌های Authored (دقیقاً همانی که در PermissionService.SeedDefaultsAsync نوشته شده)
    public static readonly TheoryData<string, string[]> HardenedModules = new()
    {
        // طبق «PEMS — Global Engineering Management UI/UX, Navigation & RBAC Hardening» بند ۱۰:
        // Viewer به VDR اضافه شد تا لینک VDR Import زیر Administration > Import Center برای این
        // نقش (که روی MDR/EquipmentImport/MrsImport از قبل دید فقط‌خواندنی دارد) به 403 ختم نشود.
        { "VDR", new[] { SystemRoles.EngineeringManager, SystemRoles.EngineeringSupervisor, SystemRoles.DocumentController, SystemRoles.DisciplineEngineer, SystemRoles.Viewer } },   // SCRUM-559: EngineeringSupervisor holds and releases VDR items
        // طبق ممیزی Package Procurement Contract Finalization (§F، Phase 2.6): QAManager/Legal/
        // Finance/HSE اضافه شدند تا هر رشته بتواند تصمیم بازبینی چند-رشته‌ای خودش را در
        // ContractDisciplineReview ثبت کند (کنترل دقیق‌تر این‌که هر کاربر فقط رشته‌ی خودش را
        // می‌تواند ثبت کند، داخل ContractWorkflowService.RecordDisciplineReviewAsync است).
        { "Contracts", new[] { SystemRoles.ContractManager, SystemRoles.EngineeringManager, SystemRoles.QAManager, SystemRoles.Legal, SystemRoles.Finance, SystemRoles.HSE } },
        { "Vendors", new[] { SystemRoles.ContractManager } },
        { "MaterialRequisition", new[] { SystemRoles.EngineeringManager, SystemRoles.DisciplineEngineer } },
        { "Equipment", new[] { SystemRoles.MaintenanceEngineer } },
        // طبق «PEMS — P1 Organization & Master Data Authorization Hardening»: این ماژول از فاز
        // ۶B فقط Seed شده بود ولی DisciplineController هرگز HasPermissionAsync را صدا نمی‌زد.
        // با سیم‌کشی‌کردنش، EngineeringManager/DocumentController/Viewer هم اضافه شدند تا لینک
        // سایدبار «Disciplines» (که از قبل به این سه نقش هم نشان داده می‌شود) به 403 ختم نشود.
        { "DisciplineWorkspace", new[] { SystemRoles.EngineeringSupervisor, SystemRoles.DisciplineEngineer, SystemRoles.EngineeringManager, SystemRoles.DocumentController, SystemRoles.Viewer } },
        { "Correspondence", new[] { SystemRoles.DocumentController, SystemRoles.EngineeringManager, SystemRoles.EngineeringSupervisor, SystemRoles.DisciplineEngineer, SystemRoles.ContractManager } },
    };

    private static PermissionService CreateService(string dbName, out PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        ctx = PemsDbContextFactory.CreateInMemory($"PermSeedHardening_{dbName}");
        return new PermissionService(ctx, new AccessControlService(ctx));
    }

    // ==== Scenario 1: Empty ====
    [Theory]
    [MemberData(nameof(HardenedModules))]
    public async Task Scenario1_EmptyDatabase_SeedsAllAuthoredRoles(string module, string[] expectedRoles)
    {
        var service = CreateService($"S1_{module}", out var ctx);

        await service.SeedDefaultsAsync();

        var seededRoles = ctx.RolePermissions.Where(x => x.ModuleName == module).Select(x => x.RoleName).ToList();

        foreach (var role in expectedRoles)
            Assert.Contains(role, seededRoles);
        Assert.Equal(expectedRoles.Length, seededRoles.Count);
    }

    // ==== Scenario 2: Fully seeded — repeat seed produces no duplicates ====
    [Theory]
    [MemberData(nameof(HardenedModules))]
    public async Task Scenario2_FullySeeded_RepeatSeed_NoDuplicates(string module, string[] expectedRoles)
    {
        var service = CreateService($"S2_{module}", out var ctx);

        await service.SeedDefaultsAsync();
        var countAfterFirst = ctx.RolePermissions.Count(x => x.ModuleName == module);
        Assert.Equal(expectedRoles.Length, countAfterFirst);

        await service.SeedDefaultsAsync();
        var countAfterSecond = ctx.RolePermissions.Count(x => x.ModuleName == module);

        Assert.Equal(countAfterFirst, countAfterSecond);
        foreach (var role in expectedRoles)
            Assert.Equal(1, ctx.RolePermissions.Count(x => x.ModuleName == module && x.RoleName == role));
    }

    // ==== Scenario 3: Partial — one role pre-exists (with custom values), others get completed ====
    [Theory]
    [MemberData(nameof(HardenedModules))]
    public async Task Scenario3_PartiallySeeded_OnlyMissingRolesAreCompleted_ExistingRolePreserved(string module, string[] expectedRoles)
    {
        var service = CreateService($"S3_{module}", out var ctx);

        // اولین نقش Authored را از قبل با مقادیر عمداً متفاوت (شبیه‌سازی SetPermissionsAsync
        // دستی از پنل Admin) اضافه می‌کنیم - این ردیف نباید Overwrite شود.
        var preExisting = new RolePermission
        {
            RoleName = expectedRoles[0],
            ModuleName = module,
            CanView = true,
            CanCreate = false, // مقدار دلخواه متفاوت - برای اثبات عدم Overwrite
            CreatedDate = DateTime.Now
        };
        ctx.RolePermissions.Add(preExisting);
        await ctx.SaveChangesAsync();

        await service.SeedDefaultsAsync();

        var rows = ctx.RolePermissions.Where(x => x.ModuleName == module).ToList();

        Assert.Equal(expectedRoles.Length, rows.Count);

        var preservedRow = rows.Single(x => x.RoleName == expectedRoles[0]);
        Assert.False(preservedRow.CanCreate); // دست‌نخورده ماند
        Assert.Equal(preExisting.RolePermissionId, preservedRow.RolePermissionId);

        foreach (var role in expectedRoles.Skip(1))
            Assert.Contains(rows, x => x.RoleName == role);
    }

    // ==== Scenario 4: Existing unrelated module (WorkOrder) stays unchanged, hardened modules still reconciled ====
    [Fact]
    public async Task Scenario4_ExistingWorkOrderSeed_WorkOrderUnchanged_AllHardenedModulesReconciled()
    {
        var service = CreateService(nameof(Scenario4_ExistingWorkOrderSeed_WorkOrderUnchanged_AllHardenedModulesReconciled), out var ctx);

        await service.SeedDefaultsAsync();
        var workOrderCountAfterFirst = ctx.RolePermissions.Count(x => x.ModuleName == "WorkOrder");
        Assert.True(workOrderCountAfterFirst > 0);

        await service.SeedDefaultsAsync();
        var workOrderCountAfterSecond = ctx.RolePermissions.Count(x => x.ModuleName == "WorkOrder");
        Assert.Equal(workOrderCountAfterFirst, workOrderCountAfterSecond);

        foreach (var row in HardenedModules)
        {
            var module = (string)row[0]!;
            var expectedRoles = (string[])row[1]!;
            var seededRoles = ctx.RolePermissions.Where(x => x.ModuleName == module).Select(x => x.RoleName).ToList();
            Assert.Equal(expectedRoles.Length, seededRoles.Count);
        }
    }

    // بند بسیار مهم فاز ۵C/۶B: RolePermission (Definition) و RolePermissionGrant (Grant واقعی)
    // دو چیز جدا هستند - SeedDefaultsAsync فقط RolePermission می‌سازد؛ AccessControlSeeder
    // (مسیر جدا، دست‌نخورده) مسئول تبدیل آن به RolePermissionGrant است.
    [Fact]
    public async Task AllHardenedModules_SeedDefaultsAsync_OnlyCreatesRolePermissions_NotGrants()
    {
        var service = CreateService(nameof(AllHardenedModules_SeedDefaultsAsync_OnlyCreatesRolePermissions_NotGrants), out var ctx);

        await service.SeedDefaultsAsync();

        Assert.Equal(0, ctx.RolePermissionGrants.Count());
        Assert.True(ctx.RolePermissions.Count(x => x.ModuleName == "VDR") > 0);
    }
}
