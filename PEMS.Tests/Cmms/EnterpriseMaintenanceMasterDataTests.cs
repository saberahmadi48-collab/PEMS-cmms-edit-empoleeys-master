using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// طبق «PEMS — Enterprise Maintenance Master Data &amp; Standards Implementation» بند ۲۰: تست‌های
/// Seed (پیش‌فرض‌ها موجودند، Idempotent)، رابطه (Activity→Type/Group، Activity→EquipmentClass/
/// StandardReference)، و یکپارچگی داده (کد تکراری رد می‌شود، Orphan Activity ممنوع).
/// </summary>
public class EnterpriseMaintenanceMasterDataTests
{
    private static async Task<PEMS.Persistence.Context.PEMSDbContext> SeedFullBaselineAsync(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"EntMMD_{dbName}");
        var services = BuildProvider(ctx);

        await DisciplineSeeder.SeedAsync(services);
        await MaintenanceGroupSeeder.SeedAsync(services);
        await MaintenanceTypeSeeder.SeedAsync(services);
        await MaintenanceStrategyTypeSeeder.SeedAsync(services);
        await EquipmentClassSeeder.SeedAsync(services);
        await StandardReferenceSeeder.SeedAsync(services);
        await MaintenanceActivitySeeder.SeedAsync(services);

        return ctx;
    }

    private static IServiceProvider BuildProvider(PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        return services.BuildServiceProvider();
    }

    // ================= Seed: baseline exists =================

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultMaintenanceTypesExist()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultMaintenanceTypesExist));

        Assert.True(await ctx.MaintenanceTypes.CountAsync() >= 18);
        Assert.True(await ctx.MaintenanceTypes.AnyAsync(x => x.Code == "PREVENTIVE" && x.IsSystemDefault));
    }

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultStrategyTypesExist()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultStrategyTypesExist));

        Assert.True(await ctx.MaintenanceStrategyTypes.CountAsync() >= 10);
        Assert.True(await ctx.MaintenanceStrategyTypes.AnyAsync(x => x.Code == "RCM" && x.IsSystemDefault));
    }

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultEquipmentClassesExist()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultEquipmentClassesExist));

        Assert.True(await ctx.EquipmentClasses.AnyAsync(x => x.Code == "PUMP_CENT"));
    }

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultStandardReferencesExist_MetadataOnly()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultStandardReferencesExist_MetadataOnly));

        var iso14224 = await ctx.StandardReferences.SingleAsync(x => x.Code == "ISO-14224");
        Assert.Equal("ISO", iso14224.Organization);
        Assert.Equal("14224", iso14224.StandardNumber);
        // طبق بند ۲: هرگز متن دارای Copyright ذخیره نمی‌شود - فقط توضیح کاربردی کوتاه
        Assert.True(iso14224.Description!.Length < 500);
    }

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultMaintenanceGroupsExist_IncludingPreviouslyBrokenPipeAndCivilGroups()
    {
        // طبق ممیزی این پاس: کدهای دیسیپلین "PIPE"/"CIVL" قبلاً با DisciplineSeeder ("PIP"/"CIV")
        // یکی نبودند - این ۷ گروه هرگز Seed نمی‌شدند. این تست همون رفع را اثبات می‌کند.
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultMaintenanceGroupsExist_IncludingPreviouslyBrokenPipeAndCivilGroups));

        Assert.True(await ctx.MaintenanceGroups.AnyAsync(x => x.Code == "PIPE-LIN"));
        Assert.True(await ctx.MaintenanceGroups.AnyAsync(x => x.Code == "PIPE-FLG"));
        Assert.True(await ctx.MaintenanceGroups.AnyAsync(x => x.Code == "CIVL-STR"));
        Assert.True(await ctx.MaintenanceGroups.AnyAsync(x => x.Code == "CIVL-RD"));
    }

    [Fact]
    public async Task Seed_EmptyDatabase_NewMaintenanceDomainDisciplinesExist()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_NewMaintenanceDomainDisciplinesExist));

        foreach (var code in new[] { "UTIL", "FIRE", "INSP", "CORR", "REL" })
            Assert.True(await ctx.Disciplines.AnyAsync(x => x.Code == code), $"Discipline '{code}' was not seeded");
    }

    [Fact]
    public async Task Seed_EmptyDatabase_DefaultMaintenanceActivitiesExist()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_EmptyDatabase_DefaultMaintenanceActivitiesExist));

        Assert.True(await ctx.MaintenanceActivities.CountAsync() >= 40);
        Assert.True(await ctx.MaintenanceActivities.AnyAsync(x => x.Code == "ACT_VIBRATION_MEASUREMENT"));
    }

    // ================= Seed: idempotent =================

    [Fact]
    public async Task Seed_RunTwice_NoDuplicates()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Seed_RunTwice_NoDuplicates));
        var services = BuildProvider(ctx);

        var typeCountBefore = await ctx.MaintenanceTypes.CountAsync();
        var strategyCountBefore = await ctx.MaintenanceStrategyTypes.CountAsync();
        var classCountBefore = await ctx.EquipmentClasses.CountAsync();
        var standardCountBefore = await ctx.StandardReferences.CountAsync();
        var activityCountBefore = await ctx.MaintenanceActivities.CountAsync();
        var groupCountBefore = await ctx.MaintenanceGroups.CountAsync();
        var disciplineCountBefore = await ctx.Disciplines.CountAsync();

        // اجرای دوباره‌ی کامل همه‌ی Seederها - دقیقاً همون چیزی که هر Startup واقعی انجام می‌دهد
        await DisciplineSeeder.SeedAsync(services);
        await MaintenanceGroupSeeder.SeedAsync(services);
        await MaintenanceTypeSeeder.SeedAsync(services);
        await MaintenanceStrategyTypeSeeder.SeedAsync(services);
        await EquipmentClassSeeder.SeedAsync(services);
        await StandardReferenceSeeder.SeedAsync(services);
        await MaintenanceActivitySeeder.SeedAsync(services);

        Assert.Equal(typeCountBefore, await ctx.MaintenanceTypes.CountAsync());
        Assert.Equal(strategyCountBefore, await ctx.MaintenanceStrategyTypes.CountAsync());
        Assert.Equal(classCountBefore, await ctx.EquipmentClasses.CountAsync());
        Assert.Equal(standardCountBefore, await ctx.StandardReferences.CountAsync());
        Assert.Equal(activityCountBefore, await ctx.MaintenanceActivities.CountAsync());
        Assert.Equal(groupCountBefore, await ctx.MaintenanceGroups.CountAsync());
        Assert.Equal(disciplineCountBefore, await ctx.Disciplines.CountAsync());
    }

    [Fact]
    public async Task DisciplineSeeder_AlreadyPartiallySeededDatabase_StillAddsMissingMaintenanceDomains()
    {
        // طبق ممیزی این پاس: گارد قبلی («اگر هر Discipline ای هست کلاً رد شو») روی یک دیتابیس
        // از قبل Seed‌شده (تقریباً همه‌ی محیط‌های واقعی) این Seeder را برای همیشه بی‌اثر می‌کرد.
        var ctx = PemsDbContextFactory.CreateInMemory(nameof(DisciplineSeeder_AlreadyPartiallySeededDatabase_StillAddsMissingMaintenanceDomains));
        ctx.Disciplines.Add(new Discipline { Code = "MECH", Name = "Mechanical", IsActive = true, CreatedDate = DateTime.Now });
        await ctx.SaveChangesAsync();

        await DisciplineSeeder.SeedAsync(BuildProvider(ctx));

        Assert.True(await ctx.Disciplines.AnyAsync(x => x.Code == "UTIL"));
        Assert.True(await ctx.Disciplines.AnyAsync(x => x.Code == "REL"));
        // MECH باید همون رکورد اصلی بماند (بدون Duplicate)
        Assert.Equal(1, await ctx.Disciplines.CountAsync(x => x.Code == "MECH"));
    }

    // ================= Relationships =================

    [Fact]
    public async Task Relationship_ActivityToGroupAndType_AreResolvedCorrectly()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Relationship_ActivityToGroupAndType_AreResolvedCorrectly));

        var activity = await ctx.MaintenanceActivities
            .Include(x => x.MaintenanceGroup)
            .Include(x => x.MaintenanceType)
            .SingleAsync(x => x.Code == "ACT_VIBRATION_MEASUREMENT");

        Assert.Equal("MECH-ROT", activity.MaintenanceGroup!.Code);
        Assert.Equal("CONDITION_BASED", activity.MaintenanceType!.Code);
    }

    [Fact]
    public async Task Relationship_GroupToDiscipline_IsResolvedCorrectly()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Relationship_GroupToDiscipline_IsResolvedCorrectly));

        var group = await ctx.MaintenanceGroups.Include(x => x.Discipline).SingleAsync(x => x.Code == "PIPE-LIN");

        Assert.Equal("PIP", group.Discipline!.Code);
    }

    [Fact]
    public async Task Relationship_ActivityToEquipmentClass_IsPopulated()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Relationship_ActivityToEquipmentClass_IsPopulated));

        var activityId = await ctx.MaintenanceActivities.Where(x => x.Code == "ACT_VIBRATION_MEASUREMENT")
            .Select(x => x.MaintenanceActivityId).SingleAsync();

        var equipmentClassCodes = await ctx.MaintenanceActivityEquipmentClasses
            .Where(x => x.MaintenanceActivityId == activityId)
            .Join(ctx.EquipmentClasses, l => l.EquipmentClassId, r => r.EquipmentClassId, (l, r) => r.Code)
            .ToListAsync();

        Assert.Contains("PUMP_CENT", equipmentClassCodes);
        Assert.Contains("COMPRESSOR", equipmentClassCodes);
    }

    [Fact]
    public async Task Relationship_ActivityToStandardReference_IsPopulated()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Relationship_ActivityToStandardReference_IsPopulated));

        var activityId = await ctx.MaintenanceActivities.Where(x => x.Code == "ACT_THICKNESS_MEASUREMENT")
            .Select(x => x.MaintenanceActivityId).SingleAsync();

        var standardCodes = await ctx.MaintenanceActivityStandardReferences
            .Where(x => x.MaintenanceActivityId == activityId)
            .Join(ctx.StandardReferences, l => l.StandardReferenceId, r => r.StandardReferenceId, (l, r) => r.Code)
            .ToListAsync();

        Assert.Contains("API-510", standardCodes);
    }

    [Fact]
    public async Task Relationship_ActivityToStrategyType_IsPopulated()
    {
        var ctx = await SeedFullBaselineAsync(nameof(Relationship_ActivityToStrategyType_IsPopulated));

        var activityId = await ctx.MaintenanceActivities.Where(x => x.Code == "ACT_VIBRATION_MEASUREMENT")
            .Select(x => x.MaintenanceActivityId).SingleAsync();

        var strategyCodes = await ctx.MaintenanceActivityStrategyTypes
            .Where(x => x.MaintenanceActivityId == activityId)
            .Join(ctx.MaintenanceStrategyTypes, l => l.MaintenanceStrategyTypeId, r => r.MaintenanceStrategyTypeId, (l, r) => r.Code)
            .ToListAsync();

        Assert.Contains("PDM", strategyCodes);
        Assert.Contains("CBM", strategyCodes);
    }

    // ================= Integrity: duplicate codes rejected (controller-level, mutation-proof) =================

    private static PEMS.Web.Services.MaintenanceMasterDataOperations CreateController(PEMS.Persistence.Context.PEMSDbContext ctx, string role) => new(ctx, new OrganizationStructureService(ctx));

    [Fact]
    public async Task CreateMaintenanceType_DuplicateCode_Rejected_NoMutation()
    {
        var ctx = await SeedFullBaselineAsync(nameof(CreateMaintenanceType_DuplicateCode_Rejected_NoMutation));
        var countBefore = await ctx.MaintenanceTypes.CountAsync();
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        await controller.CreateMaintenanceTypeAsync("PREVENTIVE", "Duplicate Attempt", null, null);

        Assert.Equal(countBefore, await ctx.MaintenanceTypes.CountAsync());
    }

    [Fact]
    public async Task CreateEquipmentClass_DuplicateCode_Rejected_NoMutation()
    {
        var ctx = await SeedFullBaselineAsync(nameof(CreateEquipmentClass_DuplicateCode_Rejected_NoMutation));
        var countBefore = await ctx.EquipmentClasses.CountAsync();
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        await controller.CreateEquipmentClassAsync("PUMP_CENT", "Duplicate Attempt", null, null);

        Assert.Equal(countBefore, await ctx.EquipmentClasses.CountAsync());
    }

    [Fact]
    public async Task CreateStandardReference_DuplicateOrganizationAndNumber_Rejected_NoMutation()
    {
        // طبق بند ۱۹: «duplicate standard references are prohibited» - حتی با کد متفاوت، اگر
        // Organization+StandardNumber یکی باشد باید رد شود.
        var ctx = await SeedFullBaselineAsync(nameof(CreateStandardReference_DuplicateOrganizationAndNumber_Rejected_NoMutation));
        var countBefore = await ctx.StandardReferences.CountAsync();
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        await controller.CreateStandardReferenceAsync("ISO-14224-DUP", "ISO", "14224", "Duplicate Attempt", null, null);

        Assert.Equal(countBefore, await ctx.StandardReferences.CountAsync());
    }

    [Fact]
    public async Task CreateMaintenanceActivity_InvalidMaintenanceGroup_Rejected_NoOrphan()
    {
        // طبق بند ۱۹: «orphan Maintenance Activities are prohibited»
        var ctx = await SeedFullBaselineAsync(nameof(CreateMaintenanceActivity_InvalidMaintenanceGroup_Rejected_NoOrphan));
        var countBefore = await ctx.MaintenanceActivities.CountAsync();
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        await controller.CreateMaintenanceActivityAsync("ACT_ORPHAN_TEST", "Orphan Test", null, maintenanceGroupId: 999999, maintenanceTypeId: null, description: null);

        Assert.Equal(countBefore, await ctx.MaintenanceActivities.CountAsync());
    }

    [Fact]
    public async Task CreateMaintenanceActivity_ValidGroup_Succeeds()
    {
        var ctx = await SeedFullBaselineAsync(nameof(CreateMaintenanceActivity_ValidGroup_Succeeds));
        var groupId = await ctx.MaintenanceGroups.Where(x => x.Code == "MECH-ROT").Select(x => x.MaintenanceGroupId).SingleAsync();
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        var result = await controller.CreateMaintenanceActivityAsync("ACT_CUSTOM_TEST", "Custom Activity", null, groupId, null, null);

        Assert.True(result.Success);
        Assert.True(await ctx.MaintenanceActivities.AnyAsync(x => x.Code == "ACT_CUSTOM_TEST" && !x.IsSystemDefault));
    }

    // ================= Integrity: inactive behavior =================

    [Fact]
    public async Task ToggleMaintenanceTypeActive_SystemDefaultRecord_CanBeDeactivated_NotDeleted()
    {
        // طبق بند ۱۲: کاربر می‌تواند رکورد پیش‌فرض را غیرفعال کند (Deactivate) - اما هرگز
        // Hard-Delete نمی‌شود (این Controller از ابتدا هیچ اکشن Delete ای ندارد).
        var ctx = await SeedFullBaselineAsync(nameof(ToggleMaintenanceTypeActive_SystemDefaultRecord_CanBeDeactivated_NotDeleted));
        var type = await ctx.MaintenanceTypes.SingleAsync(x => x.Code == "PREVENTIVE");
        Assert.True(type.IsSystemDefault);
        var controller = CreateController(ctx, SystemRoles.MaintenanceEngineer);

        await controller.ToggleMaintenanceTypeActiveAsync(type.MaintenanceTypeId);

        var afterToggle = await ctx.MaintenanceTypes.SingleAsync(x => x.MaintenanceTypeId == type.MaintenanceTypeId);
        Assert.False(afterToggle.IsActive); // غیرفعال شد
        Assert.True(afterToggle.IsSystemDefault); // اما رکورد و پرچم System Default همچنان موجودند - حذف نشد
    }

    // ================= Permission (reusing the existing hardened "MaintenanceMasterData" module) =================

}
