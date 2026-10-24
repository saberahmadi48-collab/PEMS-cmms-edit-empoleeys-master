using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.SearchProviders;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Rcm;

/// <summary>کاتالوگ ISO 14224، گزارش پوشش بر پایه‌ی تخصیص، جستجوی تجهیز با خط‌تیره، و بررسی کلیدهای Resource دو فرهنگ.</summary>
public class RcmCatalogCoverageAndSearchTests
{
    // ---------- catalog ----------

    [Fact]
    public async Task Catalog_SearchIsFilteredByClassChain_IncludesParentAndGenericModes()
    {
        var env = await RcmTestFactory.CreateEnvAsync("Cat_Chain");
        var (parent, parentMode) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "PUMP");
        var (other, otherMode) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "VALVE");
        var child = new EquipmentClass { Code = "PUMP_CENT", Name = "Centrifugal", ParentEquipmentClassId = parent, IsActive = true };
        env.Ctx.EquipmentClasses.Add(child);
        env.Ctx.FailureModes.Add(new FailureMode { Code = "GENERIC_OTHER", Name = "Other", IsActive = true });
        await env.Ctx.SaveChangesAsync();
        var catalog = new FailureModeCatalogService(env.Ctx);

        var forChild = await catalog.SearchAsync(child.EquipmentClassId, null);

        Assert.Contains(forChild, m => m.FailureModeId == parentMode);
        Assert.Contains(forChild, m => m.Code == "GENERIC_OTHER");
        Assert.DoesNotContain(forChild, m => m.FailureModeId == otherMode);
        _ = other;
    }

    [Theory]
    [InlineData("pump no flow")]
    [InlineData("PUMP_NO_FLOW")]
    [InlineData("pump-no-flow")]
    [InlineData("no flow")]
    public async Task Catalog_SearchIgnoresSeparatorsAndCase(string query)
    {
        var env = await RcmTestFactory.CreateEnvAsync("Cat_Search_" + query.Length);
        var (classId, _) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "PUMP");
        env.Ctx.FailureModes.Add(new FailureMode { Code = "PUMP_NO_FLOW", Name = "No Flow / Failure to Start", EquipmentClassId = classId, IsActive = true });
        await env.Ctx.SaveChangesAsync();

        var found = await new FailureModeCatalogService(env.Ctx).SearchAsync(classId, query);

        Assert.Contains(found, m => m.Code == "PUMP_NO_FLOW");
    }

    [Fact]
    public async Task Catalog_AdminSave_ValidatesCodeAndUniqueness_AndSupportsDeactivation()
    {
        var env = await RcmTestFactory.CreateEnvAsync("Cat_Admin");
        var (classId, _) = await RcmTestFactory.SeedCatalogAsync(env.Ctx, "PUMP");
        var cause = new FailureCause { Code = "WEAR", Name = "Wear", IsActive = true };
        env.Ctx.FailureCauses.Add(cause);
        await env.Ctx.SaveChangesAsync();
        var catalog = new FailureModeCatalogService(env.Ctx);

        var invalid = await catalog.SaveAsync(new FailureModeEditDto { Code = "x", Name = "" });
        Assert.Contains(invalid.Errors, e => e.Code == "Rcm_Err_CatalogCodeInvalid");
        Assert.Contains(invalid.Errors, e => e.Code == "Rcm_Err_CatalogNameRequired");

        var saved = await catalog.SaveAsync(new FailureModeEditDto
        {
            Code = "pump seal leak", Name = "Seal leak", NameFa = "نشتی سیل", EquipmentClassId = classId, CauseIds = { cause.FailureCauseId }
        });
        Assert.True(saved.Success);
        var dto = await catalog.GetAsync(saved.Id!.Value);
        Assert.Equal("PUMP_SEAL_LEAK", dto!.Code);
        Assert.Equal(new[] { cause.FailureCauseId }, dto.CauseIds);
        Assert.Contains((await catalog.SaveAsync(new FailureModeEditDto { Code = "PUMP_SEAL_LEAK", Name = "Again" })).Errors, e => e.Code == "Rcm_Err_CatalogCodeDuplicate");

        Assert.True(await catalog.IsKnownCodeAsync("PUMP_SEAL_LEAK"));
        await catalog.SetActiveAsync(saved.Id.Value, false);
        Assert.False(await catalog.IsKnownCodeAsync("PUMP_SEAL_LEAK"));
        Assert.DoesNotContain(await catalog.SearchAsync(classId, "seal"), m => m.Code == "PUMP_SEAL_LEAK");
        Assert.False(await catalog.IsKnownCodeAsync("  "));
    }

    // ---------- coverage ----------

    [Fact]
    public async Task Coverage_IsAssignmentBased_ByCriticalityAndClass_AndListsUncoveredCriticalEquipment()
    {
        var env = await RcmTestFactory.CreateEnvAsync("Coverage");
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var coveredA = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "COV-A", classId);
        var uncoveredA = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "UNC-A", classId);
        var uncoveredB = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "UNC-B", classId);
        var unrated = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "UNR-1");
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, coveredA.EquipmentId, "A");
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, uncoveredA.EquipmentId, "A");
        await RcmTestFactory.AddCriticalityAsync(env.Ctx, uncoveredB.EquipmentId, "B");
        await env.Assignments.AssignAsync(id, new[] { coveredA.EquipmentId }, RcmTestFactory.Actor(5));
        // استراتژی قدیمی تک‌تگی بدون تخصیص، پوشش حساب نمی‌شود (گزارش بر پایه‌ی تخصیص است)
        env.Ctx.MaintenanceStrategies.Add(new MaintenanceStrategy { EquipmentId = unrated.EquipmentId, StrategyType = "Preventive", Status = "Active", IsActive = true });
        await env.Ctx.SaveChangesAsync();

        var report = await new StrategyCoverageService(env.Ctx).GetCoverageAsync();

        Assert.Equal((4, 1), (report.TotalEquipment, report.CoveredEquipment));
        var a = report.ByCriticality.Single(r => r.Key == "A");
        Assert.Equal((2, 1, 1), (a.TotalEquipment, a.WithApprovedStrategy, a.WithoutStrategy));
        Assert.Equal(1, report.ByCriticality.Single(r => r.Key == "Unrated").TotalEquipment);
        Assert.Equal(1, report.UnratedEquipment);
        Assert.Equal(1, report.UnclassifiedEquipment);
        Assert.Equal("UNC-A", Assert.Single(report.CriticalWithoutStrategy).Equipment.Tag);
        Assert.Equal(1, report.CriticalWithoutStrategyTotal);
        Assert.Equal(33.3, report.ByClass.Single(c => c.Key == "Test PUMP_T").CoveragePercent);
    }

    [Fact]
    public async Task Coverage_ReadinessCounters_RecognizeAssignmentsAsStrategyCoverage()
    {
        var env = await RcmTestFactory.CreateEnvAsync("Readiness");
        var (classId, modeId) = await RcmTestFactory.SeedCatalogAsync(env.Ctx);
        var id = await RcmTestFactory.CreateActiveStrategyAsync(env, classId, modeId);
        var tag = await RcmTestFactory.AddEquipmentAsync(env.Ctx, "RD-1", classId);
        await env.Assignments.AssignAsync(id, new[] { tag.EquipmentId }, RcmTestFactory.Actor(5));
        var intelligence = new EquipmentReadinessService(env.Ctx, new EquipmentClassCoverageService(env.Ctx));

        var summary = await intelligence.GetOperationalReadinessSummaryAsync();

        Assert.Equal(1, summary.StrategyReadyEquipment);
    }

    // ---------- equipment search (issue 22) ----------

    [Theory]
    [InlineData("FT-6001")]
    [InlineData("80-FT-6001")]
    [InlineData("FT 6001")]
    [InlineData("FT6001")]
    [InlineData("80.FT.6001")]
    [InlineData("6001")]
    public async Task EquipmentSearch_FindsHyphenatedTag_RegardlessOfSeparators(string query)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("Rcm_Search_" + query.Length + query.GetHashCode());
        // طبق یادداشت InMemory: Include روی FK اجباری بدون ردیف والد، ردیف را بی‌صدا حذف می‌کند → Discipline واقعی Seed می‌شود
        var d = new Discipline { Code = "INST", Name = "Instrumentation", IsActive = true };
        ctx.Disciplines.Add(d);
        await ctx.SaveChangesAsync();
        ctx.Equipments.Add(new Equipment { TagNumber = "80-FT-6001", Name = "Flow transmitter", EquipmentType = "FT", DisciplineId = d.DisciplineId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "80-PT-7002", Name = "Pressure transmitter", EquipmentType = "PT", DisciplineId = d.DisciplineId, IsActive = true });
        await ctx.SaveChangesAsync();

        var (items, total) = await new EquipmentSearchProvider(ctx).SearchAsync(query, 1, 10, null);

        Assert.Contains(items, i => i.Label == "80-FT-6001");
        Assert.DoesNotContain(items, i => i.Label == "80-PT-7002");
        Assert.True(total >= 1);
    }

    [Fact]
    public async Task EquipmentSearch_DoesNotPadResultsWithUnrelatedEquipment()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("Rcm_Search_NoPad");
        var d = new Discipline { Code = "INST", Name = "Instrumentation", IsActive = true };
        ctx.Disciplines.Add(d);
        await ctx.SaveChangesAsync();
        for (var i = 0; i < 30; i++)
            ctx.Equipments.Add(new Equipment { TagNumber = $"80-AA-{i:0000}", Name = "Other " + i, EquipmentType = "AA", DisciplineId = d.DisciplineId, IsActive = true });
        ctx.Equipments.Add(new Equipment { TagNumber = "80-FT-6001", Name = "Flow transmitter", EquipmentType = "FT", DisciplineId = d.DisciplineId, IsActive = true });
        await ctx.SaveChangesAsync();

        var (items, _) = await new EquipmentSearchProvider(ctx).SearchAsync("FT-6001", 1, 10, null);

        Assert.Equal("80-FT-6001", Assert.Single(items).Label);
    }

    // ---------- scripts stay in sync with the code ----------

    [Fact]
    public void StandaloneDryRunSqlFile_IsIdenticalToTheEmbeddedConstant()
    {
        var file = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "rcm", "rcm-dry-run-report.sql")).ReplaceLineEndings("\n").Trim();
        var embedded = PEMS.Persistence.DataMigrations.RcmDataMigrationSql.DryRunReportSql.ReplaceLineEndings("\n").Trim();
        Assert.Equal(embedded, file);
    }

    // ---------- localization completeness (issue 24) ----------

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (PEMS.slnx) not found.");
    }

    private static HashSet<string> ResxKeys(string file)
        => Regex.Matches(File.ReadAllText(file), "<data name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void EveryRcmResourceKeyUsedInCodeAndViews_ExistsInBothCultures()
    {
        var root = RepoRoot();
        var en = ResxKeys(Path.Combine(root, "PEMS.Web", "Resources", "SharedResource.resx"));
        var fa = ResxKeys(Path.Combine(root, "PEMS.Web", "Resources", "SharedResource.fa.resx"));

        var used = new HashSet<string>();
        foreach (var project in new[] { "PEMS.Web", "PEMS.Application", "PEMS.Persistence" })
        {
            var files = Directory.EnumerateFiles(Path.Combine(root, project), "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".cshtml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains("Migrations"));
            foreach (var f in files)
                foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"((?:Rcm|Error)_[A-Za-z_]*[A-Za-z])\""))
                    used.Add(m.Groups[1].Value);
        }

        // کلیدهای خانواده‌ی پویا (Rcm_<Group>_<Code>) از روی کدهای واقعی تولید می‌شوند
        void Family(string group, IEnumerable<string> codes) { foreach (var c in codes) used.Add($"Rcm_{group}_{c}"); }
        Family("StrategyType", RcmCodes.StrategyType.All);
        Family("Consequence", RcmCodes.Consequence.All);
        Family("Frequency", RcmCodes.Frequency.All);
        Family("Trigger", RcmCodes.Trigger.All);
        Family("MeterType", RcmCodes.MeterType.All);
        Family("TaskType", RcmCodes.TaskType.All);
        Family("Source", RcmCodes.Source.All);
        Family("Status", RcmCodes.Status.All);
        Family("Criticality", new[] { "A", "B", "C", "Unrated" });
        Family("ScheduleMode", new[] { "Fixed", "Floating" });
        Family("OverrideStatus", new[] { "None", "Pending", "Approved", "Rejected" });
        Family("HistoryAction", new[] { "Created", "Edited", "Submitted", "Approved", "Rejected", "Activated", "NewVersion", "Superseded",
            "Retired", "Deactivated", "Assigned", "Unassigned", "OverrideRequested", "OverrideApproved", "OverrideRejected" });
        foreach (var c in new[] { "AlreadyAssigned", "ConflictingStrategy", "CriticalityBlocks", "ClassMismatch" })
        { used.Add("Rcm_Bulk_" + c); used.Add("Rcm_Err_Bulk_" + c); }
        used.Add("Rcm_Bulk_NotFound");

        used.RemoveWhere(k => k is "Rcm_Err_" or "Rcm_Bulk_");
        Assert.NotEmpty(used);
        var missingEn = used.Where(k => !en.Contains(k)).OrderBy(k => k).ToList();
        var missingFa = used.Where(k => !fa.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(missingEn.Count == 0, "Missing in SharedResource.resx: " + string.Join(", ", missingEn));
        Assert.True(missingFa.Count == 0, "Missing in SharedResource.fa.resx: " + string.Join(", ", missingFa));
    }

    [Fact]
    public void RcmResources_HaveIdenticalKeySetsInBothCultures_AndPersianIsActuallyPersian()
    {
        var root = RepoRoot();
        var faText = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Resources", "SharedResource.fa.resx"));
        var en = ResxKeys(Path.Combine(root, "PEMS.Web", "Resources", "SharedResource.resx")).Where(k => k.StartsWith("Rcm_") || k.StartsWith("Error_")).ToHashSet();
        var fa = ResxKeys(Path.Combine(root, "PEMS.Web", "Resources", "SharedResource.fa.resx")).Where(k => k.StartsWith("Rcm_") || k.StartsWith("Error_")).ToHashSet();

        Assert.Empty(en.Except(fa));
        Assert.Empty(fa.Except(en));

        // هر مقدار فارسی Rcm_Err_* باید حداقل یک حرف فارسی داشته باشد (نه کپی انگلیسی)
        foreach (Match m in Regex.Matches(faText, "<data name=\"(Rcm_Err_[^\"]+)\"[^>]*><value>([^<]*)</value>"))
            Assert.True(Regex.IsMatch(m.Groups[2].Value, "[؀-ۿ]"), m.Groups[1].Value);
    }
}
