using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Authorization;

/// <summary>
/// Phase 1 hardening: مسیرهای ساخت PO محدود و شناخته‌شده می‌مانند. FindOrCreateByNumberAsync (Import/Reconciliation،
/// وضعیت Issued بدون Approve/SoD) هیچ فراخوان تولیدی ندارد و هر فراخوان یا مسیر ساخت PO تازه باید آگاهانه بازبینی شود
/// (این تست‌ها را همراه بازبینی به‌روز کنید - نه برعکس).
/// </summary>
public class PurchaseOrderCreationPathTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> ProductionSources()
    {
        var root = Root();
        foreach (var project in new[] { "PEMS.Web", "PEMS.Persistence", "PEMS.Application", "PEMS.Domain", "PEMS.WorkflowPlatform", "PEMS.AI", "PEMS.Infrastructure", "PEMS.Shared" })
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = file[root.Length..].Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/Migrations/")) continue;
                yield return rel;
            }
        }
    }

    private static List<string> FilesContaining(string needle)
    {
        var root = Root();
        return ProductionSources().Where(f => File.ReadAllText(root + f).Contains(needle, StringComparison.Ordinal)).OrderBy(f => f).ToList();
    }

    [Fact]
    public void FindOrCreateByNumberAsync_HasNoProductionCallers_OnlyDeclarationAndImplementation()
    {
        var files = FilesContaining("FindOrCreateByNumberAsync");

        Assert.Equal(new[] { "/PEMS.Application/Interfaces/IPurchaseOrderService.cs", "/PEMS.Persistence/Servises/PurchaseOrderService.cs" }, files);
    }

    [Fact]
    public void PurchaseOrderEntities_AreOnlyConstructed_ByTheServiceAndTheVdrImport()
    {
        // SCRUM-470: an exact match, so "new PurchaseOrderLine" / "new PurchaseOrderAuthorityRule" do not count as building a PO
        var root = Root();
        var files = ProductionSources()
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(root + f), @"new PurchaseOrder\s*[({]"))
            .OrderBy(f => f).ToList();

        Assert.Equal(new[] { "/PEMS.Persistence/Servises/PurchaseOrderService.cs", "/PEMS.Persistence/Servises/VDRImportService.cs" }, files);
    }

    [Fact]
    public async Task ManualCreation_IsDraft_WhileTheImportReconciliationHelperStaysIssued()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("PoCreationPath_" + Guid.NewGuid().ToString("N"));
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "P", IsActive = true });
        ctx.Vendors.Add(new Vendor { VendorId = 1, Name = "V", IsActive = true });
        await ctx.SaveChangesAsync();
        var service = new PurchaseOrderService(ctx);

        var manual = await service.CreateAsync("PO-MANUAL", 1, 1, null, null, 1m);
        var imported = await service.FindOrCreateByNumberAsync("PO-IMPORTED", 1, 1);
        var again = await service.FindOrCreateByNumberAsync(" po-imported ", 1, 1);   // idempotent، بدون ساخت تکراری

        Assert.Equal("Draft", ctx.PurchaseOrders.Single(x => x.PoId == manual).Status);
        Assert.Equal("Issued", ctx.PurchaseOrders.Single(x => x.PoId == imported).Status);
        Assert.Equal(imported, again);
        Assert.Equal(2, ctx.PurchaseOrders.Count());
    }
}
