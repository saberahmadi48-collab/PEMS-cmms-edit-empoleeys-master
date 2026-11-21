using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.DataMigrations;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Rcm;

/// <summary>
/// اثبات روی SQL Server واقعی (دیتابیس ایزوله‌ی PEMSDB_RegressionTest، نه دیتابیس مشترک توسعه): مهاجرت داده‌ی
/// Idempotent برچسب‌های متنی → کد پایدار و استراتژی تک‌تگی → الگو+تخصیص، ایندکس یکتای فیلترشده‌ی تخصیص،
/// و کنترل Concurrency با RowVersion واقعی.
/// </summary>
public class RcmRealSqlTests
{
    private static string U() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<(EquipmentClass Class, FailureMode Mode, Project Project, Discipline Discipline)> SeedBaseAsync(PEMSDbContext ctx)
    {
        var project = new Project { ProjectName = "RCM " + U(), ProjectCode = "R" + U() };
        var discipline = new Discipline { Name = "RCM disc " + U(), Code = "D" + U() };
        var cls = new EquipmentClass { Code = "CLS_" + U(), Name = "RCM test class", IsActive = true };
        ctx.Projects.Add(project);
        ctx.Disciplines.Add(discipline);
        ctx.EquipmentClasses.Add(cls);
        await ctx.SaveChangesAsync();
        var mode = new FailureMode { Code = "FM_" + U(), Name = "Legacy vibration " + U(), EquipmentClassId = cls.EquipmentClassId, IsActive = true };
        ctx.FailureModes.Add(mode);
        await ctx.SaveChangesAsync();
        return (cls, mode, project, discipline);
    }

    private static async Task<Equipment> TagAsync(PEMSDbContext ctx, Project p, Discipline d, EquipmentClass? cls)
    {
        var e = new Equipment
        {
            TagNumber = "RCM-" + U(), Name = "rcm tag", EquipmentType = "Pump", ProjectId = p.ProjectId, DisciplineId = d.DisciplineId,
            EquipmentClassId = cls?.EquipmentClassId, IsActive = true
        };
        ctx.Equipments.Add(e);
        await ctx.SaveChangesAsync();
        return e;
    }

    /// <summary>ردیف را دقیقاً به شکل قبل از مهاجرت درمی‌آورد (برچسب متنی، Status پیش‌فرض ستون، FamilyId خالی).</summary>
    private static async Task<MaintenanceStrategy> LegacyStrategyAsync(PEMSDbContext ctx, Equipment e, string type, string consequence,
        string? frequency, string? failureModeCode, string? justification, bool active = true)
    {
        var s = new MaintenanceStrategy
        {
            EquipmentId = e.EquipmentId, StrategyType = type, ConsequenceCategory = consequence, Frequency = frequency,
            FailureModeCode = failureModeCode, Justification = justification, IsActive = active
        };
        ctx.MaintenanceStrategies.Add(s);
        await ctx.SaveChangesAsync();
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE MaintenanceStrategies SET Status = N'Draft', FamilyId = '00000000-0000-0000-0000-000000000000' WHERE MaintenanceStrategyId = {0}",
            s.MaintenanceStrategyId);
        ctx.ChangeTracker.Clear();
        return s;
    }

    private static async Task RunMigrationAsync(PEMSDbContext ctx)
    {
        foreach (var sql in RcmDataMigrationSql.UpStatements)
            await ctx.Database.ExecuteSqlRawAsync(sql);
        ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task DataMigration_ConvertsPersianLabelsToCodes_AndTagStrategiesToTemplatePlusAssignment_Idempotently()
    {
        using var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();
        var (cls, mode, project, discipline) = await SeedBaseAsync(ctx);
        var e1 = await TagAsync(ctx, project, discipline, cls);
        var e2 = await TagAsync(ctx, project, discipline, cls);
        var e3 = await TagAsync(ctx, project, discipline, cls);
        var e4 = await TagAsync(ctx, project, discipline, null);

        // ۱) برچسب فارسی + حالت خرابی قابل‌نگاشت به کاتالوگ + توجیه کوتاه
        var s1 = await LegacyStrategyAsync(ctx, e1, "پیشگیرانه", "Operational", "روزانه", mode.Code, "کوتاه");
        // ۲) رکورد ناقض قاعده‌ی ایمنی: RTF + Safety، بدون حالت خرابی/توجیه (همان استراتژی ۹ دیتابیس)
        var s2 = await LegacyStrategyAsync(ctx, e2, "RunToFailure", "Safety", "روزانه", null, null);
        // ۳) غیرفعال با برچسب فارسی ایمنی
        var s3 = await LegacyStrategyAsync(ctx, e3, "اصلاحی", "ایمنی", null, null, null, active: false);
        // ۴) طبقه‌بندی‌نشده + Economic
        var s4 = await LegacyStrategyAsync(ctx, e4, "CBM", "Economic", "Monthly", "Vibration", "OEM manual vibration limits section 4.2.");

        var p1 = new MaintenancePlan { EquipmentId = e1.EquipmentId, MaintenanceStrategyId = s1.MaintenanceStrategyId, PlanNumber = "MP-" + U(), Title = "Plan on s1", IsActive = true };
        var pMismatch = new MaintenancePlan { EquipmentId = e2.EquipmentId, MaintenanceStrategyId = s1.MaintenanceStrategyId, PlanNumber = "MP-" + U(), Title = "Mismatched plan", IsActive = true };
        ctx.MaintenancePlans.AddRange(p1, pMismatch);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await RunMigrationAsync(ctx);
        var after = await ctx.MaintenanceStrategies.AsNoTracking()
            .Where(s => new[] { s1.MaintenanceStrategyId, s2.MaintenanceStrategyId, s3.MaintenanceStrategyId, s4.MaintenanceStrategyId }.Contains(s.MaintenanceStrategyId))
            .ToDictionaryAsync(s => s.MaintenanceStrategyId);

        var a1 = after[s1.MaintenanceStrategyId];
        Assert.Equal(("Preventive", "Operational", "Daily", 1), (a1.StrategyType, a1.ConsequenceCategory, a1.Frequency, a1.DefaultIntervalDays));
        Assert.Equal(cls.EquipmentClassId, a1.EquipmentClassId);            // کلاس الگو از کلاس تجهیز
        Assert.Equal(mode.FailureModeId, a1.FailureModeId);                  // نگاشت به کاتالوگ ISO 14224
        Assert.Equal("Active", a1.Status);
        Assert.NotNull(a1.ReviewDate);                                       // توجیه ضعیف → صف بازنگری فوری
        Assert.NotEqual(Guid.Empty, a1.FamilyId);

        var a2 = after[s2.MaintenanceStrategyId];
        Assert.Equal(("RunToFailure", "Safety", "Draft"), (a2.StrategyType, a2.ConsequenceCategory, a2.Status)); // ناقض قاعده → نیازمند بازبینی
        Assert.Null(a2.FailureModeId);

        var a3 = after[s3.MaintenanceStrategyId];
        Assert.Equal(("Corrective", "Safety", "Retired"), (a3.StrategyType, a3.ConsequenceCategory, a3.Status));

        var a4 = after[s4.MaintenanceStrategyId];
        Assert.Equal(("CBM", "NonOperational", "Monthly", "Active"), (a4.StrategyType, a4.ConsequenceCategory, a4.Frequency, a4.Status));
        Assert.Null(a4.EquipmentClassId);

        // شناسه‌ها حفظ شدند و هر استراتژی تک‌تگی دقیقاً یک Assignment گرفت
        var ids = new[] { s1.MaintenanceStrategyId, s2.MaintenanceStrategyId, s3.MaintenanceStrategyId, s4.MaintenanceStrategyId };
        var assignments = await ctx.MaintenanceStrategyAssignments.AsNoTracking().Where(a => ids.Contains(a.MaintenanceStrategyId)).ToListAsync();
        Assert.Equal(4, assignments.Count);
        Assert.False(assignments.Single(a => a.MaintenanceStrategyId == s3.MaintenanceStrategyId).IsActive);
        Assert.Equal(e1.EquipmentId, assignments.Single(a => a.MaintenanceStrategyId == s1.MaintenanceStrategyId).EquipmentId);

        // Plan مرجع به Assignment وصل شد؛ Plan با تجهیز ناهماهنگ (ریسک مسئله ۱۵) دست‌نخورده و بی‌اتصال ماند
        var planAfter = await ctx.MaintenancePlans.AsNoTracking().ToDictionaryAsync(p => p.MaintenancePlanId);
        Assert.Equal(assignments.Single(a => a.MaintenanceStrategyId == s1.MaintenanceStrategyId).MaintenanceStrategyAssignmentId,
            planAfter[p1.MaintenancePlanId].MaintenanceStrategyAssignmentId);
        Assert.Null(planAfter[pMismatch.MaintenancePlanId].MaintenanceStrategyAssignmentId);
        Assert.Equal(s1.MaintenanceStrategyId, planAfter[pMismatch.MaintenancePlanId].MaintenanceStrategyId);

        Assert.Equal(4, await ctx.MaintenanceStrategyHistories.CountAsync(h => ids.Contains(h.MaintenanceStrategyId) && h.Action == "Created"));

        // Idempotent: اجرای دوباره هیچ ردیف/تخصیص/تاریخچه‌ی اضافه نمی‌سازد و مقادیر را تغییر نمی‌دهد
        await RunMigrationAsync(ctx);
        Assert.Equal(4, await ctx.MaintenanceStrategyAssignments.CountAsync(a => ids.Contains(a.MaintenanceStrategyId)));
        Assert.Equal(4, await ctx.MaintenanceStrategyHistories.CountAsync(h => ids.Contains(h.MaintenanceStrategyId) && h.Action == "Created"));
        var again = await ctx.MaintenanceStrategies.AsNoTracking().SingleAsync(s => s.MaintenanceStrategyId == s2.MaintenanceStrategyId);
        Assert.Equal("Draft", again.Status);
    }

    [Fact]
    public async Task DryRunReportSql_IsReadOnly_AndExecutesAgainstTheRealSchema()
    {
        using var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();
        var before = await ctx.MaintenanceStrategies.CountAsync();

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = RcmDataMigrationSql.DryRunReportSql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync()) rows++;

        Assert.True(rows >= 10);
        await connection.CloseAsync();
        Assert.Equal(before, await ctx.MaintenanceStrategies.CountAsync());
    }

    [Fact]
    public async Task ActiveAssignmentUniqueness_IsEnforcedByTheDatabase_ButRetiredDuplicatesAreAllowed()
    {
        using var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();
        var (cls, mode, project, discipline) = await SeedBaseAsync(ctx);
        var tag = await TagAsync(ctx, project, discipline, cls);
        var s = new MaintenanceStrategy { EquipmentClassId = cls.EquipmentClassId, FailureModeId = mode.FailureModeId, FailureModeCode = mode.Code, StrategyType = "Preventive", ConsequenceCategory = "Operational", Status = "Active" };
        ctx.MaintenanceStrategies.Add(s);
        await ctx.SaveChangesAsync();

        ctx.MaintenanceStrategyAssignments.Add(new MaintenanceStrategyAssignment { MaintenanceStrategyId = s.MaintenanceStrategyId, EquipmentId = tag.EquipmentId });
        await ctx.SaveChangesAsync();

        ctx.MaintenanceStrategyAssignments.Add(new MaintenanceStrategyAssignment { MaintenanceStrategyId = s.MaintenanceStrategyId, EquipmentId = tag.EquipmentId, IsActive = false, Status = "Retired" });
        await ctx.SaveChangesAsync(); // رکورد بسته مجاز است

        ctx.MaintenanceStrategyAssignments.Add(new MaintenanceStrategyAssignment { MaintenanceStrategyId = s.MaintenanceStrategyId, EquipmentId = tag.EquipmentId });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrency_RealRowVersion_RejectsAStaleFormAfterAnotherUserSaved()
    {
        using var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();
        var (cls, mode, _, _) = await SeedBaseAsync(ctx);
        var service = new MaintenanceStrategyCommandService(ctx, new FakeWorkflowRuntime());
        var queries = new MaintenanceStrategyService(ctx);
        var input = RcmTestFactory.ValidInput(cls.EquipmentClassId, mode.FailureModeId);
        var id = (await service.CreateAsync(input, RcmTestFactory.Actor(1))).Id!.Value;
        ctx.ChangeTracker.Clear();

        var formOfUserA = (await queries.GetDetailAsync(id))!.Input;
        var formOfUserB = (await queries.GetDetailAsync(id))!.Input;
        Assert.NotNull(formOfUserA.RowVersion);

        formOfUserB.Justification = "User B documented a stronger justification with new evidence.";
        Assert.True((await service.UpdateAsync(id, formOfUserB, RcmTestFactory.Actor(2))).Success);
        ctx.ChangeTracker.Clear();

        formOfUserA.Frequency = "Weekly";
        var stale = await service.UpdateAsync(id, formOfUserA, RcmTestFactory.Actor(1));

        Assert.Contains(stale.Errors, e => e.Code == "Rcm_Err_ConcurrencyConflict");
        var final = await ctx.MaintenanceStrategies.AsNoTracking().SingleAsync(s => s.MaintenanceStrategyId == id);
        Assert.Equal("Monthly", final.Frequency);
        Assert.StartsWith("User B", final.Justification);
    }

    [Fact]
    public async Task ClassLevelBulkAssignment_OnRealSql_IsAtomic_AndHonorsTheUniqueIndex()
    {
        using var ctx = PemsDbContextFactory.CreateSqlServerOrThrow();
        var (cls, mode, project, discipline) = await SeedBaseAsync(ctx);
        var s = new MaintenanceStrategy { EquipmentClassId = cls.EquipmentClassId, FailureModeId = mode.FailureModeId, FailureModeCode = mode.Code, StrategyType = "Preventive", ConsequenceCategory = "Operational", Status = "Active" };
        ctx.MaintenanceStrategies.Add(s);
        var tags = new List<Equipment>();
        for (var i = 0; i < 25; i++) tags.Add(await TagAsync(ctx, project, discipline, cls));
        await ctx.SaveChangesAsync();
        var service = new StrategyAssignmentService(ctx);

        var first = await service.AssignAsync(s.MaintenanceStrategyId, tags.Select(t => t.EquipmentId).ToList(), RcmTestFactory.Actor(1));
        var second = await service.AssignAsync(s.MaintenanceStrategyId, tags.Select(t => t.EquipmentId).ToList(), RcmTestFactory.Actor(1));

        Assert.Equal(25, first.Assigned);
        Assert.Equal((0, 25), (second.Assigned, second.SkippedAlreadyAssigned));
        Assert.Equal(25, await ctx.MaintenanceStrategyAssignments.CountAsync(a => a.MaintenanceStrategyId == s.MaintenanceStrategyId));
    }
}
