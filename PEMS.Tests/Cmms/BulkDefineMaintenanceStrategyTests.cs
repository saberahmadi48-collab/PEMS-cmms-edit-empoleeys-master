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
/// طبق «Bulk Define Maintenance Strategy — Location and Tag Selection» ممیزی: تست‌های رفتار
/// جدید فیلتر (چندانتخابی Discipline/EquipmentType، فیلتر Functional Location، جستجوی Tag) و
/// شکاف اعتبارسنجی که در Commit رفع شد (تجهیز غیرفعال). هیچ تستی برای رفتار پشتیبانی‌نشده
/// (مثلاً بسط والد→فرزند Functional Location) اضافه نشد - آن رفتار عمداً پیاده‌سازی نشد.
/// </summary>
public class BulkDefineMaintenanceStrategyTests
{
    private static async Task<(MaintenanceStrategyController Controller, PEMS.Persistence.Context.PEMSDbContext Ctx)> CreateController(
        string dbName, string? role = SystemRoles.EngineeringSupervisor)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"BulkStrategy_{dbName}");
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
        var strategyService = new MaintenanceStrategyService(ctx);
        var functionalLocationService = new FunctionalLocationService(ctx);

        var controller = new MaintenanceStrategyController(ctx, strategyService,
            new MaintenanceStrategyCommandService(ctx, new FakeWorkflowRuntime()), new StrategyAssignmentService(ctx),
            new FailureModeCatalogService(ctx), new StrategyCoverageService(ctx), functionalLocationService, currentUser, permissionService).WithTempData();
        return (controller, ctx);
    }

    private static List<Equipment> GetViewEquipments(IActionResult result)
    {
        var viewResult = Assert.IsType<ViewResult>(result);
        return (List<Equipment>)viewResult.ViewData["Equipments"]!;
    }

    [Fact]
    public async Task BulkCreate_Get_MultipleDisciplineIds_ReturnsUnionOfBothDisciplines()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_MultipleDisciplineIds_ReturnsUnionOfBothDisciplines));
        ctx.Equipments.Add(new Equipment { TagNumber = "M-1", Name = "M-1", EquipmentType = "Pump", DisciplineId = 1, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "M-2", Name = "M-2", EquipmentType = "Pump", DisciplineId = 2, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "M-3", Name = "M-3", EquipmentType = "Pump", DisciplineId = 3, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(new List<int> { 1, 2 }, null, null, null);

        var equipments = GetViewEquipments(result);
        Assert.Equal(2, equipments.Count);
        Assert.Contains(equipments, e => e.TagNumber == "M-1");
        Assert.Contains(equipments, e => e.TagNumber == "M-2");
        Assert.DoesNotContain(equipments, e => e.TagNumber == "M-3");
    }

    [Fact]
    public async Task BulkCreate_Get_MultipleEquipmentTypes_ReturnsUnionOfBothTypes()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_MultipleEquipmentTypes_ReturnsUnionOfBothTypes));
        ctx.Equipments.Add(new Equipment { TagNumber = "T-1", Name = "T-1", EquipmentType = "Pump", IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "T-2", Name = "T-2", EquipmentType = "Valve", IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "T-3", Name = "T-3", EquipmentType = "Motor", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, new List<string> { "Pump", "Valve" }, null, null);

        var equipments = GetViewEquipments(result);
        Assert.Equal(2, equipments.Count);
        Assert.DoesNotContain(equipments, e => e.TagNumber == "T-3");
    }

    [Fact]
    public async Task BulkCreate_Get_TagSearch_MatchesPartialTagNumber()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagSearch_MatchesPartialTagNumber));
        ctx.Equipments.Add(new Equipment { TagNumber = "P-101-A", Name = "Pump A", EquipmentType = "Pump", IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "P-102-B", Name = "Pump B", EquipmentType = "Pump", IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "V-201-C", Name = "Valve C", EquipmentType = "Valve", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, "P-10");

        var equipments = GetViewEquipments(result);
        Assert.Equal(2, equipments.Count);
        Assert.All(equipments, e => Assert.StartsWith("P-10", e.TagNumber));
    }

    [Fact]
    public async Task BulkCreate_Get_TagSearchAlone_CountsAsAFilter_DoesNotTriggerNoFilterApplied()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagSearchAlone_CountsAsAFilter_DoesNotTriggerNoFilterApplied));
        ctx.Equipments.Add(new Equipment { TagNumber = "P-999", Name = "Pump", EquipmentType = "Pump", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, "P-999");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False((bool)viewResult.ViewData["NoFilterApplied"]!);
    }

    [Fact]
    public async Task BulkCreate_Get_FunctionalLocationFilter_MatchesOnlyEquipmentWithThatLocation()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_FunctionalLocationFilter_MatchesOnlyEquipmentWithThatLocation));
        var fl = new FunctionalLocation { Code = "85A07", Name = "FINISHING", IsActive = true };
        ctx.FunctionalLocations.Add(fl);
        await ctx.SaveChangesAsync();

        ctx.Equipments.Add(new Equipment { TagNumber = "FL-1", Name = "FL-1", EquipmentType = "Pump", FunctionalLocationId = fl.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "FL-2", Name = "FL-2", EquipmentType = "Pump", FunctionalLocationId = null, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, new List<int> { fl.FunctionalLocationId }, null);

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("FL-1", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_FunctionalLocationPickerList_IsPopulated_EvenWithNoEquipmentLinked()
    {
        // طبق بند ۸ ممیزی: نبود داده‌ی پیوند Equipment نباید باعث شود خودِ Picker خالی باشد -
        // این دو مشکل جدا هستند (Problem A در برابر Problem B).
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_FunctionalLocationPickerList_IsPopulated_EvenWithNoEquipmentLinked));
        ctx.FunctionalLocations.Add(new FunctionalLocation { Code = "85A07", Name = "FINISHING", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var pickerList = (List<PEMS.Application.DTOs.FunctionalLocationPickerItemDto>)viewResult.ViewData["FunctionalLocations"]!;
        Assert.Single(pickerList);
        var equipmentWithFlCount = (int)viewResult.ViewData["EquipmentWithFunctionalLocationCount"]!;
        Assert.Equal(0, equipmentWithFlCount);
    }

    private static async Task<(int StrategyId, int ClassId)> SeedApprovedStrategyAsync(PEMS.Persistence.Context.PEMSDbContext ctx)
    {
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(ctx, "BULK");
        var s = new MaintenanceStrategy
        {
            EquipmentClassId = classId, FailureModeId = modeId, FailureModeCode = "BULK_LEAK", StrategyType = "Preventive",
            ConsequenceCategory = "Operational", Status = "Active", IsActive = true
        };
        ctx.MaintenanceStrategies.Add(s);
        await ctx.SaveChangesAsync();
        return (s.MaintenanceStrategyId, classId);
    }

    [Fact]
    public async Task BulkCreate_Post_InactiveEquipment_SkippedAndNotAssigned()
    {
        // POST دوباره تایید می‌کند که تجهیز هنوز فعال است
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Post_InactiveEquipment_SkippedAndNotAssigned));
        var (strategyId, classId) = await SeedApprovedStrategyAsync(ctx);
        var active = await RcmTestFactory.AddEquipmentAsync(ctx, "A-1", classId);
        var inactive = await RcmTestFactory.AddEquipmentAsync(ctx, "A-2", classId, active: false);

        var result = await controller.BulkCreate(new List<int> { active.EquipmentId, inactive.EquipmentId }, strategyId);

        Assert.IsType<RedirectToActionResult>(result);
        var created = ctx.MaintenanceStrategyAssignments.ToList();
        Assert.Single(created);
        Assert.Equal(active.EquipmentId, created[0].EquipmentId);
    }

    [Fact]
    public async Task BulkCreate_Post_DuplicateEquipmentIdInSameSubmission_OnlyAssignsOnce()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Post_DuplicateEquipmentIdInSameSubmission_OnlyAssignsOnce));
        var (strategyId, classId) = await SeedApprovedStrategyAsync(ctx);
        var equipment = await RcmTestFactory.AddEquipmentAsync(ctx, "D-1", classId);

        var result = await controller.BulkCreate(new List<int> { equipment.EquipmentId, equipment.EquipmentId }, strategyId);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(ctx.MaintenanceStrategyAssignments);
        Assert.Single(ctx.MaintenanceStrategies); // هیچ استراتژی تک‌تگی جدیدی ساخته نمی‌شود
    }

    [Fact]
    public async Task BulkCreate_Get_NoFilterAtAll_ReturnsEmptyList_NotFullFleet()
    {
        // طبق بند ۲۴ فاز ۷.۶: این محافظت عمدی است و در این ممیزی دست‌نخورده باقی ماند
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_NoFilterAtAll_ReturnsEmptyList_NotFullFleet));
        ctx.Equipments.Add(new Equipment { TagNumber = "N-1", Name = "N-1", EquipmentType = "Pump", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True((bool)viewResult.ViewData["NoFilterApplied"]!);
        var equipments = (List<Equipment>)viewResult.ViewData["Equipments"]!;
        Assert.Empty(equipments);
    }

    [Fact]
    public async Task BulkCreate_Get_ResultCapRemainsAt200()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_ResultCapRemainsAt200));
        for (var i = 0; i < 250; i++)
            ctx.Equipments.Add(new Equipment { TagNumber = $"CAP-{i:D3}", Name = $"CAP-{i:D3}", EquipmentType = "Pump", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, new List<string> { "Pump" }, null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var equipments = (List<Equipment>)viewResult.ViewData["Equipments"]!;
        Assert.Equal(200, equipments.Count);
        Assert.Equal(250, (int)viewResult.ViewData["MatchingCount"]!);
    }

    // ==============================================================================
    // طبق «PEMS CMMS — Bulk Define Maintenance Strategy Scope & Location/Equipment
    // Resolution»: تست‌های ابعاد جدید (Plant/Area/Unit/FL Exact-Descendants/Tag List) که
    // روی همان BulkCreate GET بالا افزوده شدند - بدون تغییر رفتار تست‌های موجود بالا.
    // ==============================================================================

    [Fact]
    public async Task BulkCreate_Get_PlantFilter_MatchesOnlyEquipmentUnderThatPlant()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_PlantFilter_MatchesOnlyEquipmentUnderThatPlant));
        var flA = new FunctionalLocation { Code = "PL-A", Name = "PL-A", Plant = "Plant1", IsActive = true };
        var flB = new FunctionalLocation { Code = "PL-B", Name = "PL-B", Plant = "Plant2", IsActive = true };
        ctx.FunctionalLocations.AddRange(flA, flB);
        await ctx.SaveChangesAsync();
        ctx.Equipments.Add(new Equipment { TagNumber = "PLE-1", Name = "PLE-1", EquipmentType = "Pump", FunctionalLocationId = flA.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "PLE-2", Name = "PLE-2", EquipmentType = "Pump", FunctionalLocationId = flB.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null, new List<string> { "Plant1" });

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("PLE-1", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_AreaFilter_MatchesOnlyEquipmentUnderThatArea()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_AreaFilter_MatchesOnlyEquipmentUnderThatArea));
        var flA = new FunctionalLocation { Code = "AR-A", Name = "AR-A", Area = "AreaX", IsActive = true };
        var flB = new FunctionalLocation { Code = "AR-B", Name = "AR-B", Area = "AreaY", IsActive = true };
        ctx.FunctionalLocations.AddRange(flA, flB);
        await ctx.SaveChangesAsync();
        ctx.Equipments.Add(new Equipment { TagNumber = "ARE-1", Name = "ARE-1", EquipmentType = "Pump", FunctionalLocationId = flA.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "ARE-2", Name = "ARE-2", EquipmentType = "Pump", FunctionalLocationId = flB.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null, null, new List<string> { "AreaX" });

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("ARE-1", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_UnitFilter_MatchesOnlyEquipmentUnderThatUnit()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_UnitFilter_MatchesOnlyEquipmentUnderThatUnit));
        var flA = new FunctionalLocation { Code = "UN-A", Name = "UN-A", Unit = "U100", IsActive = true };
        var flB = new FunctionalLocation { Code = "UN-B", Name = "UN-B", Unit = "U200", IsActive = true };
        ctx.FunctionalLocations.AddRange(flA, flB);
        await ctx.SaveChangesAsync();
        ctx.Equipments.Add(new Equipment { TagNumber = "UNE-1", Name = "UNE-1", EquipmentType = "Pump", FunctionalLocationId = flA.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "UNE-2", Name = "UNE-2", EquipmentType = "Pump", FunctionalLocationId = flB.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null, null, null, new List<string> { "U100" });

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("UNE-1", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_FunctionalLocationExactMode_DoesNotIncludeChildEquipment()
    {
        // پیش‌فرض/Exact صریح: فقط خودِ FL انتخاب‌شده - طبق رفتار قبلی فاز ۷.۶ (بدون تغییر)
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_FunctionalLocationExactMode_DoesNotIncludeChildEquipment));
        var parent = new FunctionalLocation { Code = "EX-P", Name = "EX-P", IsActive = true };
        ctx.FunctionalLocations.Add(parent);
        await ctx.SaveChangesAsync();
        var child = new FunctionalLocation { Code = "EX-C", Name = "EX-C", ParentId = parent.FunctionalLocationId, IsActive = true };
        ctx.FunctionalLocations.Add(child);
        await ctx.SaveChangesAsync();

        ctx.Equipments.Add(new Equipment { TagNumber = "EXE-PARENT", Name = "EXE-PARENT", EquipmentType = "Pump", FunctionalLocationId = parent.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "EXE-CHILD", Name = "EXE-CHILD", EquipmentType = "Pump", FunctionalLocationId = child.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, new List<int> { parent.FunctionalLocationId }, null,
            null, null, null, "Exact");

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("EXE-PARENT", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_FunctionalLocationDescendantsMode_IncludesMultiLevelChildEquipment()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_FunctionalLocationDescendantsMode_IncludesMultiLevelChildEquipment));
        var root = new FunctionalLocation { Code = "DS-ROOT", Name = "DS-ROOT", IsActive = true };
        ctx.FunctionalLocations.Add(root);
        await ctx.SaveChangesAsync();
        var mid = new FunctionalLocation { Code = "DS-MID", Name = "DS-MID", ParentId = root.FunctionalLocationId, IsActive = true };
        ctx.FunctionalLocations.Add(mid);
        await ctx.SaveChangesAsync();
        var leaf = new FunctionalLocation { Code = "DS-LEAF", Name = "DS-LEAF", ParentId = mid.FunctionalLocationId, IsActive = true };
        ctx.FunctionalLocations.Add(leaf);
        await ctx.SaveChangesAsync();

        ctx.Equipments.Add(new Equipment { TagNumber = "DSE-ROOT", Name = "DSE-ROOT", EquipmentType = "Pump", FunctionalLocationId = root.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "DSE-MID", Name = "DSE-MID", EquipmentType = "Pump", FunctionalLocationId = mid.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "DSE-LEAF", Name = "DSE-LEAF", EquipmentType = "Pump", FunctionalLocationId = leaf.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "DSE-UNRELATED", Name = "DSE-UNRELATED", EquipmentType = "Pump", FunctionalLocationId = null, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, new List<int> { root.FunctionalLocationId }, null,
            null, null, null, "Descendants");

        var equipments = GetViewEquipments(result);
        Assert.Equal(3, equipments.Count);
        Assert.Contains(equipments, e => e.TagNumber == "DSE-ROOT");
        Assert.Contains(equipments, e => e.TagNumber == "DSE-MID");
        Assert.Contains(equipments, e => e.TagNumber == "DSE-LEAF");
        Assert.DoesNotContain(equipments, e => e.TagNumber == "DSE-UNRELATED");
    }

    [Fact]
    public async Task BulkCreate_Get_OverlappingFunctionalLocationSelections_DoesNotDuplicateEquipmentInResult()
    {
        // انتخاب هم‌زمان Parent (با Descendants) و یکی از فرزندانش - تجهیز مشترک فقط یک‌بار
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_OverlappingFunctionalLocationSelections_DoesNotDuplicateEquipmentInResult));
        var parent = new FunctionalLocation { Code = "OV-P", Name = "OV-P", IsActive = true };
        ctx.FunctionalLocations.Add(parent);
        await ctx.SaveChangesAsync();
        var child = new FunctionalLocation { Code = "OV-C", Name = "OV-C", ParentId = parent.FunctionalLocationId, IsActive = true };
        ctx.FunctionalLocations.Add(child);
        await ctx.SaveChangesAsync();

        ctx.Equipments.Add(new Equipment { TagNumber = "OVE-CHILD", Name = "OVE-CHILD", EquipmentType = "Pump", FunctionalLocationId = child.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null,
            new List<int> { parent.FunctionalLocationId, child.FunctionalLocationId }, null,
            null, null, null, "Descendants");

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments); // نه دو ردیف تکراری برای همان تجهیز
        Assert.Equal("OVE-CHILD", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_TagList_AddsExactMatchesEvenWhenTheyFailAnUnrelatedNarrowingFilter()
    {
        // طبق قاعده‌ی این فاز: Tag List یک انتخابگر صریح UNION است، نه یک فیلتر AND محدودکننده -
        // یک Tag که Discipline انتخاب‌شده را ندارد، باید همچنان در نتیجه ظاهر شود.
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagList_AddsExactMatchesEvenWhenTheyFailAnUnrelatedNarrowingFilter));
        ctx.Equipments.Add(new Equipment { TagNumber = "TAG-MATCH-DISC", Name = "InDiscipline", EquipmentType = "Pump", DisciplineId = 1, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "TAG-MATCH-OTHER", Name = "OutOfDiscipline", EquipmentType = "Pump", DisciplineId = 99, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(new List<int> { 1 }, null, null, null,
            null, null, null, null, "TAG-MATCH-OTHER");

        var equipments = GetViewEquipments(result);
        Assert.Equal(2, equipments.Count);
        Assert.Contains(equipments, e => e.TagNumber == "TAG-MATCH-DISC");
        Assert.Contains(equipments, e => e.TagNumber == "TAG-MATCH-OTHER");
    }

    [Fact]
    public async Task BulkCreate_Get_TagList_ExactMatchOnly_DoesNotPartialMatch()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagList_ExactMatchOnly_DoesNotPartialMatch));
        ctx.Equipments.Add(new Equipment { TagNumber = "TAG-100", Name = "Exact", EquipmentType = "Pump", IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "TAG-100-EXTRA", Name = "Longer", EquipmentType = "Pump", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null,
            null, null, null, null, "TAG-100");

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("TAG-100", equipments[0].TagNumber);
    }

    [Fact]
    public async Task BulkCreate_Get_TagList_DuplicateOverlapWithNarrowingFilter_CountsEquipmentOnceInMatchingCount()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagList_DuplicateOverlapWithNarrowingFilter_CountsEquipmentOnceInMatchingCount));
        ctx.Equipments.Add(new Equipment { TagNumber = "DUP-1", Name = "Dup1", EquipmentType = "Pump", DisciplineId = 1, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(new List<int> { 1 }, null, null, null,
            null, null, null, null, "DUP-1");

        var viewResult = Assert.IsType<ViewResult>(result);
        var equipments = (List<Equipment>)viewResult.ViewData["Equipments"]!;
        Assert.Single(equipments);
        Assert.Equal(1, (int)viewResult.ViewData["MatchingCount"]!);
    }

    [Fact]
    public async Task BulkCreate_Get_TagList_NonexistentTag_SafeZeroResult_NotAnError()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_TagList_NonexistentTag_SafeZeroResult_NotAnError));
        ctx.Equipments.Add(new Equipment { TagNumber = "REAL-TAG", Name = "Real", EquipmentType = "Pump", IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null,
            null, null, null, null, "DOES-NOT-EXIST");

        var viewResult = Assert.IsType<ViewResult>(result);
        var equipments = (List<Equipment>)viewResult.ViewData["Equipments"]!;
        Assert.Empty(equipments);
        Assert.False((bool)viewResult.ViewData["NoFilterApplied"]!); // یک Tag List خالی از نتیجه با «بدون فیلتر» یکی نیست
    }

    [Fact]
    public async Task BulkCreate_Get_PlantAreaUnitCombined_ActAsAndNarrowingDimensions()
    {
        var (controller, ctx) = await CreateController(nameof(BulkCreate_Get_PlantAreaUnitCombined_ActAsAndNarrowingDimensions));
        var flMatch = new FunctionalLocation { Code = "CMB-1", Name = "CMB-1", Plant = "P1", Area = "A1", Unit = "U1", IsActive = true };
        var flPartial = new FunctionalLocation { Code = "CMB-2", Name = "CMB-2", Plant = "P1", Area = "A1", Unit = "U2", IsActive = true };
        ctx.FunctionalLocations.AddRange(flMatch, flPartial);
        await ctx.SaveChangesAsync();
        ctx.Equipments.Add(new Equipment { TagNumber = "CMBE-1", Name = "CMBE-1", EquipmentType = "Pump", FunctionalLocationId = flMatch.FunctionalLocationId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "CMBE-2", Name = "CMBE-2", EquipmentType = "Pump", FunctionalLocationId = flPartial.FunctionalLocationId, IsActive = true });
        await ctx.SaveChangesAsync();

        var result = await controller.BulkCreate(null, null, null, null,
            new List<string> { "P1" }, new List<string> { "A1" }, new List<string> { "U1" });

        var equipments = GetViewEquipments(result);
        Assert.Single(equipments);
        Assert.Equal("CMBE-1", equipments[0].TagNumber);
    }
}
