using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Web.Controllers;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// ساخت یکجای پشته‌ی RCM کلاس‌محور برای تست‌ها (سرویس‌ها، کنترلرها، کاتالوگ و ورودی معتبر). همه‌ی تست‌ها روی
/// InMemory با نام یکتا اجرا می‌شوند؛ هیچ داده‌ای در دیتابیس مشترک توسعه نوشته نمی‌شود.
/// </summary>
public sealed class RcmEnv
{
    public PEMSDbContext Ctx { get; init; } = null!;
    public PermissionService Permissions { get; init; } = null!;
    public FakeCurrentUserService User { get; init; } = null!;
    public FakeWorkflowRuntime Runtime { get; init; } = new();

    public MaintenanceStrategyCommandService Commands => new(Ctx, Runtime);
    public StrategyAssignmentService Assignments => new(Ctx);
    public MaintenanceStrategyService Queries => new(Ctx);
    public MaintenancePlanCommandService Plans(INumberingService? numbering = null) => new(Ctx, numbering ?? new FakeNumberingService());

    public MaintenanceStrategyController StrategyController() => new MaintenanceStrategyController(
        Ctx, Queries, Commands, Assignments, new FailureModeCatalogService(Ctx), new StrategyCoverageService(Ctx),
        new FunctionalLocationService(Ctx), User, Permissions).WithTempData();

    public MaintenancePlanController PlanController(IPMGenerationService? pm = null) => new MaintenancePlanController(
        Ctx, new MaintenancePlanService(Ctx), Plans(), Assignments,
        pm ?? new PMGenerationService(Ctx, new FakeNumberingService()), User, Permissions).WithTempData();
}

public static class RcmTestFactory
{
    public static async Task<RcmEnv> CreateEnvAsync(string dbName, string? role = SystemRoles.EngineeringSupervisor)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"Rcm_{dbName}");
        foreach (var roleName in SystemRoles.All)
            ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();

        var accessControl = new AccessControlService(ctx);
        var permissions = new PermissionService(ctx, accessControl);
        await permissions.SeedDefaultsAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());

        return new RcmEnv
        {
            Ctx = ctx,
            Permissions = permissions,
            User = new FakeCurrentUserService { Roles = role != null ? new List<string> { role } : new List<string>(), UserName = "tester" }
        };
    }

    public static async Task<(int ClassId, int ModeId)> SeedCatalogAsync(PEMSDbContext ctx, string code = "PUMP_T")
    {
        var cls = new EquipmentClass { Code = code, Name = "Test " + code, IsActive = true };
        ctx.EquipmentClasses.Add(cls);
        await ctx.SaveChangesAsync();
        var mode = new FailureMode { Code = code + "_LEAK", Name = "Leakage " + code, EquipmentClassId = cls.EquipmentClassId, IsActive = true };
        ctx.FailureModes.Add(mode);
        await ctx.SaveChangesAsync();
        return (cls.EquipmentClassId, mode.FailureModeId);
    }

    public static async Task<FailureMode> AddModeAsync(PEMSDbContext ctx, int classId, string code)
    {
        var mode = new FailureMode { Code = code, Name = code, EquipmentClassId = classId, IsActive = true };
        ctx.FailureModes.Add(mode);
        await ctx.SaveChangesAsync();
        return mode;
    }

    public static async Task<Equipment> AddEquipmentAsync(PEMSDbContext ctx, string tag, int? classId = null, bool active = true)
    {
        var e = new Equipment { TagNumber = tag, Name = tag, EquipmentType = "Pump", EquipmentClassId = classId, IsActive = active };
        ctx.Equipments.Add(e);
        await ctx.SaveChangesAsync();
        return e;
    }

    public static async Task AddCriticalityAsync(PEMSDbContext ctx, int equipmentId, string cls)
    {
        ctx.EquipmentCriticalities.Add(new EquipmentCriticality { EquipmentId = equipmentId, CriticalityClass = cls, IsActive = true });
        await ctx.SaveChangesAsync();
    }

    public const string GoodJustification = "OEM manual section 7 requires periodic inspection of this component.";

    public static StrategyInputDto ValidInput(int classId, int modeId, string type = "Preventive", string consequence = "Operational") => new()
    {
        StrategyType = type,
        ConsequenceCategory = consequence,
        EquipmentClassId = classId,
        FailureModeId = modeId,
        Justification = GoodJustification,
        Frequency = "Monthly",
        RecommendedTriggerType = "Calendar",
        CbmParameter = type is "CBM" or "Predictive" ? "Vibration" : null,
        InspectionIntervalDays = type is "CBM" or "Predictive" ? 30 : null
    };

    public static StrategyActor Actor(int? employeeId, string name = "tester", params string[] roles)
        => new(employeeId, name, roles.Length > 0 ? roles : new[] { SystemRoles.EngineeringManager });

    /// <summary>ساخت استراتژی معتبر و عبور کامل از Draft → InReview → Approved → Active با دو بازیگر مستقل.</summary>
    public static async Task<int> CreateActiveStrategyAsync(RcmEnv env, int classId, int modeId, string type = "Preventive",
        string consequence = "Operational", bool activate = true)
    {
        var author = Actor(1, "author");
        var reviewer = Actor(2, "reviewer");
        var created = await env.Commands.CreateAsync(ValidInput(classId, modeId, type, consequence), author);
        if (!created.Success) throw new InvalidOperationException(string.Join(",", created.Errors.Select(e => e.Code)));
        await env.Commands.SubmitForReviewAsync(created.Id!.Value, author);
        await env.Commands.ReviewAsync(created.Id.Value, true, null, reviewer);
        if (activate) await env.Commands.ActivateAsync(created.Id.Value, true, reviewer);
        return created.Id.Value;
    }

    public static async Task<JobPlan> AddJobPlanAsync(PEMSDbContext ctx)
    {
        var jp = new JobPlan { JobPlanNumber = "JP-" + Guid.NewGuid().ToString("N")[..6], Title = "Test job plan", IsActive = true };
        ctx.JobPlans.Add(jp);
        await ctx.SaveChangesAsync();
        return jp;
    }
}
