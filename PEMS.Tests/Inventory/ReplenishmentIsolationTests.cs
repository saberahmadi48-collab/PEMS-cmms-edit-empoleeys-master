namespace PEMS.Tests.Inventory;

/// <summary>
/// Phase 2 / P5 - تضمین ساختاری «فقط توصیه»: سرویس و کنترلر P5 هیچ وابستگی/DbSet ای برای نوشتن موجودی، دفتر کل، رزرو، درخواست انبار، MR، PO یا بسته‌ی
/// تدارکات ندارند و متد سیستمی «محاسبه‌ی بدون پوشاندن» فقط توسط سرویس P5 صدا زده می‌شود (هرگز مستقیم به Controller/View نمی‌رسد).
/// هر تغییر آگاهانه‌ی این مرز باید همراه بازبینی این تست انجام شود (نه برعکس).
/// </summary>
public class ReplenishmentIsolationTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static readonly string[] P5Sources =
    {
        "PEMS.Persistence/Servises/ReplenishmentRecommendationService.cs",
        "PEMS.Web/Services/ReplenishmentOperations.cs",
        "PEMS.Application/Replenishment/ReplenishmentRules.cs",
        "PEMS.Application/Replenishment/ReplenishmentModels.cs",
        "PEMS.Application/Interfaces/IReplenishmentRecommendationService.cs"
    };

    private static readonly string[] Forbidden =
    {
        "IMaterialStockService", "MaterialStockService", "StockRowResolver", "IStoreRequisitionLifecycleService", "StoreRequisitionLifecycleService",
        "IPurchaseOrderService", "PurchaseOrderService", "IProcurementPackageService", "IMaterialRequisition", "IProductionRunService",
        ".InventoryTransactions", ".MaterialReservations", ".PurchaseOrders", ".MaterialRequisitions", ".MaterialRequisitionItems",
        ".ProcurementPackages", ".StoreRequisitions.Add", ".WorkOrders.Add", "StoreRequisitionStatuses", "ExecuteSql", "ExecuteUpdate", "ExecuteDelete"
    };

    [Fact]
    public void P5Sources_ReferenceNoStockLedgerReservationRequisitionOrderOrProcurementWriter()
    {
        var root = Root();
        foreach (var rel in P5Sources)
        {
            var text = File.ReadAllText(Path.Combine(root, rel));
            foreach (var token in Forbidden)
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{rel} must not reference '{token}' (P5 is advisory only).");
        }
    }

    [Fact]
    public void InputTables_AreOnlyEverReadAsNoTracking_NeverTrackedForUpdate()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Persistence/Servises/ReplenishmentRecommendationService.cs"));

        foreach (var set in new[] { "MaterialStocks", "StoreRequisitionStatusHistories", "StoreRequisitionLines", "WorkOrderMaterials", "MaterialItems", "Warehouses", "Employees" })
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"_ctx\." + set + @"(?!\w)(.{0,20})", System.Text.RegularExpressions.RegexOptions.Singleline))
                Assert.StartsWith(".AsNoTracking()", m.Groups[1].Value);
    }

    [Fact]
    public void P5Service_WritesOnlyItsOwnTables()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Persistence/Servises/ReplenishmentRecommendationService.cs"));
        var allowedSets = new[] { "NetDemandRuns", "ReplenishmentRecommendations", "ReplenishmentRecommendationSources", "ReplenishmentRecommendationHistories" };

        foreach (var m in System.Text.RegularExpressions.Regex.Matches(text, @"_ctx\.(\w+)\.(Add|AddRange|Remove|RemoveRange|Update|Attach)\(").Cast<System.Text.RegularExpressions.Match>())
            Assert.Contains(m.Groups[1].Value, allowedSets);
    }

    [Fact]
    public void UnmaskedSystemCalculation_IsCalledOnlyByTheP5Service()
    {
        var root = Root();
        var callers = new List<string>();
        foreach (var project in new[] { "PEMS.Web", "PEMS.Persistence", "PEMS.Application", "PEMS.WorkflowPlatform", "PEMS.AI", "PEMS.Infrastructure" })
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml")))
            {
                var rel = file[root.Length..].Replace('\\', '/').TrimStart('/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                if (File.ReadAllText(file).Contains("CalculateUnmaskedForSystemAsync", StringComparison.Ordinal)) callers.Add(rel);
            }
        }

        Assert.Equal(new[]
        {
            "PEMS.Application/Interfaces/INetDemandServices.cs",
            "PEMS.Persistence/Servises/NetDemandService.cs",
            "PEMS.Persistence/Servises/ReplenishmentRecommendationService.cs"
        }, callers.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Statuses_ConvertedAndSuperseded_AreNeverWrittenInP5()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Persistence/Servises/ReplenishmentRecommendationService.cs"));

        Assert.DoesNotContain("ReplenishmentStatuses.Converted", text);
        Assert.DoesNotContain("ReplenishmentStatuses.Superseded", text);
    }
}
