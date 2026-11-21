using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// ماتریس بحرانیت/خرابی/نگهداری (Reliability Dashboard) با استراتژی‌های RCM کلاس‌محور: الگوی جدید EquipmentId = Null دارد و
/// فقط از طریق MaintenanceStrategyAssignment به تگ‌ها وصل می‌شود. قبلاً ToDictionary روی EquipmentId؟ با کلید Null
/// می‌ترکید (ArgumentNullException: key)؛ اکنون نباید بترکد و پوشش واقعی را باید از Assignment فعال حساب کند.
/// </summary>
public class ReliabilityMatrixClassLevelStrategyTests
{
    private static async Task<PEMSDbContext> NewAsync(string name, int equipmentCount = 2)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RelClassLevel_" + name);
        for (var i = 1; i <= equipmentCount; i++)
        {
            ctx.Equipments.Add(new Equipment { EquipmentId = i, TagNumber = "E" + i, Name = "E" + i, EquipmentType = "Pump", ProjectId = 1, DisciplineId = 1, IsActive = true });
            ctx.EquipmentCriticalities.Add(new EquipmentCriticality { EquipmentCriticalityId = i, EquipmentId = i, CriticalityClass = "A", IsActive = true, CreatedDate = DateTime.Now });
        }
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static MaintenanceStrategy Template(int id, string type = "Preventive", bool active = true) =>
        new() { MaintenanceStrategyId = id, EquipmentId = null, EquipmentClassId = 5, StrategyType = type, IsActive = active, CreatedDate = DateTime.Now };

    private static MaintenanceStrategyAssignment Assign(int id, int strategyId, int equipmentId, string status = RcmCodes.AssignmentStatus.Active, bool active = true) =>
        new() { MaintenanceStrategyAssignmentId = id, MaintenanceStrategyId = strategyId, EquipmentId = equipmentId, Status = status, IsActive = active };

    [Fact]
    public async Task ClassLevelTemplate_WithoutAssignments_DoesNotThrow_AndCoversNothing()
    {
        var ctx = await NewAsync(nameof(ClassLevelTemplate_WithoutAssignments_DoesNotThrow_AndCoversNothing));
        ctx.MaintenanceStrategies.Add(Template(1));
        await ctx.SaveChangesAsync();

        var matrix = await new ReliabilityIntelligenceService(ctx).GetCriticalityFailureMaintenanceMatrixAsync();   // used to throw ArgumentNullException

        var row = matrix.Single(x => x.CriticalityClass == "A");
        Assert.Equal(2, row.EquipmentCount);
        Assert.Equal(0, row.StrategyCoveredEquipmentCount);
        Assert.Equal(0, row.StrategyCoveragePercent);
        Assert.Empty(row.StrategyTypeMix);
    }

    [Fact]
    public async Task ClassLevelTemplate_CoversOnlyTheEquipmentItIsAssignedTo()
    {
        var ctx = await NewAsync(nameof(ClassLevelTemplate_CoversOnlyTheEquipmentItIsAssignedTo));
        ctx.MaintenanceStrategies.Add(Template(1, "Predictive"));
        ctx.MaintenanceStrategyAssignments.Add(Assign(1, 1, equipmentId: 1));
        await ctx.SaveChangesAsync();

        var row = (await new ReliabilityIntelligenceService(ctx).GetCriticalityFailureMaintenanceMatrixAsync()).Single(x => x.CriticalityClass == "A");

        Assert.Equal(1, row.StrategyCoveredEquipmentCount);
        Assert.Equal(50, row.StrategyCoveragePercent);
        Assert.Equal(1, row.StrategyTypeMix["Predictive"]);
    }

    [Fact]
    public async Task LegacyTagStrategy_AndItsMigratedAssignment_AreCountedOnce()
    {
        var ctx = await NewAsync(nameof(LegacyTagStrategy_AndItsMigratedAssignment_AreCountedOnce), equipmentCount: 1);
        // الگوی مهاجرت RCM: استراتژی قدیمی تک‌تگی + یک Assignment برای همان جفت
        ctx.MaintenanceStrategies.Add(new MaintenanceStrategy { MaintenanceStrategyId = 1, EquipmentId = 1, StrategyType = "Calendar", IsActive = true, CreatedDate = DateTime.Now });
        ctx.MaintenanceStrategyAssignments.Add(Assign(1, 1, equipmentId: 1));
        await ctx.SaveChangesAsync();

        var row = (await new ReliabilityIntelligenceService(ctx).GetCriticalityFailureMaintenanceMatrixAsync()).Single(x => x.CriticalityClass == "A");

        Assert.Equal(1, row.StrategyCoveredEquipmentCount);
        Assert.Equal(1, row.StrategyTypeMix["Calendar"]);   // not 2
    }

    [Fact]
    public async Task LegacyStrategies_And_ClassLevelTemplates_CanBeMixed()
    {
        var ctx = await NewAsync(nameof(LegacyStrategies_And_ClassLevelTemplates_CanBeMixed));
        ctx.MaintenanceStrategies.Add(new MaintenanceStrategy { MaintenanceStrategyId = 1, EquipmentId = 1, StrategyType = "Calendar", IsActive = true, CreatedDate = DateTime.Now });
        ctx.MaintenanceStrategies.Add(Template(2, "Predictive"));
        ctx.MaintenanceStrategyAssignments.Add(Assign(1, 2, equipmentId: 2));
        await ctx.SaveChangesAsync();

        var row = (await new ReliabilityIntelligenceService(ctx).GetCriticalityFailureMaintenanceMatrixAsync()).Single(x => x.CriticalityClass == "A");

        Assert.Equal(2, row.StrategyCoveredEquipmentCount);
        Assert.Equal(100, row.StrategyCoveragePercent);
        Assert.Equal(1, row.StrategyTypeMix["Calendar"]);
        Assert.Equal(1, row.StrategyTypeMix["Predictive"]);
    }

    [Theory]
    [InlineData(RcmCodes.AssignmentStatus.Suspended, true, true)]
    [InlineData(RcmCodes.AssignmentStatus.Retired, true, true)]
    [InlineData(RcmCodes.AssignmentStatus.Active, false, true)]   // assignment row inactive
    [InlineData(RcmCodes.AssignmentStatus.Active, true, false)]   // the strategy itself is inactive
    public async Task InactiveOrNonActiveAssignments_DoNotCountAsCoverage(string status, bool assignmentActive, bool strategyActive)
    {
        var ctx = await NewAsync($"inactive_{status}_{assignmentActive}_{strategyActive}");
        ctx.MaintenanceStrategies.Add(Template(1, "Preventive", strategyActive));
        ctx.MaintenanceStrategyAssignments.Add(Assign(1, 1, equipmentId: 1, status, assignmentActive));
        await ctx.SaveChangesAsync();

        var row = (await new ReliabilityIntelligenceService(ctx).GetCriticalityFailureMaintenanceMatrixAsync()).Single(x => x.CriticalityClass == "A");

        Assert.Equal(0, row.StrategyCoveredEquipmentCount);
    }
}
