using System.Reflection;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Authorization;

/// <summary>
/// طبق «PEMS — Centralized Import Center»: مرکز Import فقط لایه‌ی ناوبری روی Importهای موجود است.
/// این تست‌ها ثابت می‌کنند (۱) هر Import کشف‌شده در ImportCatalog هست و به Controller/Action واقعی
/// اشاره می‌کند، (۲) Import جدیدی بدون ثبت در کاتالوگ به‌سادگی اضافه نمی‌شود، (۳) هیچ نقشی بیش از
/// قبل نمی‌بیند، (۴) مجوز Backend مرجع نهایی نمایش است، (۵) لینک‌های پراکنده‌ی قدیمی از سایدبار رفته‌اند
/// و (۶) هر ورودی کاتالوگ متن EN و FA دارد.
/// </summary>
public class ImportCenterNavigationTests
{
    private static ClaimsPrincipal UserWith(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    private sealed class SpyPermissionService : IPermissionService
    {
        public HashSet<string> Denied { get; } = new();
        public List<(string Module, string Action)> Asked { get; } = new();
        public Task<List<RolePermissionDto>> GetMatrixAsync() => throw new NotImplementedException();
        public Task SetPermissionsAsync(string roleName, IEnumerable<ModulePermissionInput> permissions) => throw new NotImplementedException();
        public Task SeedDefaultsAsync() => throw new NotImplementedException();
        public Task<bool> HasPermissionAsync(IEnumerable<string> userRoles, string moduleName, string action)
        {
            Asked.Add((moduleName, action));
            return Task.FromResult(!Denied.Contains(moduleName));
        }
    }

    // ---------- (1) catalog integrity ----------

    [Fact]
    public void Catalog_HasFourteen_UniqueImports()
    {
        // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring» فاز ۹:
        // یک ورودی تازه («ConditionReading») به کاتالوگ اضافه شد - ۱۱ → ۱۲.
        // Organization Hierarchy Phase 1: «OrganizationStructure» اضافه شد - ۱۲ → ۱۳.
        // RCM کلاس‌محور: «RcmStrategy» اضافه شد - ۱۳ → ۱۴.
        Assert.Equal(14, ImportCatalog.All.Count);
        Assert.Equal(ImportCatalog.All.Count, ImportCatalog.All.Select(e => e.Key).Distinct().Count());
    }

    // MRS Import و مشابه‌ها در Area (مثلاً Warehouse) هستند؛ Namespace از Area کاتالوگ ساخته می‌شود
    private static Type? ControllerType(System.Reflection.Assembly asm, ImportCatalog.ImportEntry e) =>
        asm.GetType(string.IsNullOrEmpty(e.Area)
            ? $"PEMS.Web.Controllers.{e.Controller}Controller"
            : $"PEMS.Web.Areas.{e.Area}.Controllers.{e.Controller}Controller");

    [Fact]
    public void EveryCatalogEntry_PointsToAnExistingControllerAction()
    {
        var asm = typeof(ImportCenterController).Assembly;
        foreach (var e in ImportCatalog.All)
        {
            var type = ControllerType(asm, e);
            Assert.True(type != null, $"Controller {e.Controller}Controller not found for import '{e.Key}'");
            Assert.True(type!.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == e.Action),
                $"Action {e.Controller}.{e.Action} not found for import '{e.Key}'");
        }
    }

    // ---------- (2) nothing left behind ----------

    // آپلودهای فایلی که Import داده نیستند (ضمیمه/عکس/فایل‌های کامنت یا FTP) - هرکدام با دلیل
    private static readonly Dictionary<string, string> KnownNonImportUploads = new()
    {
        ["DCC"] = "attachment on incoming correspondence",
        ["EngineeringDocuments"] = "document attachments",
        ["Letter"] = "letter attachments / editor images",
        ["LetterCompose"] = "draft attachments and the recipient list (CSV/text is only resolved to employees; nothing is imported)",
        ["MOC"] = "MOC attachments",
        ["TechnicalQuery"] = "TQ attachments",
        ["MyProfile"] = "profile photo",
        ["VDR"] = "comment sheet / reply sheet / FTP upload on an existing VDR item",
        ["PartCatalogueRequest"] = "request attachments (drawings/documents)",
        ["PartCatalogueAdmin"] = "IFS reference-data upsert (gated by PartCatalogue.Admin on its own admin page, not role-set based)",
    };

    [Fact]
    public void EveryControllerWithFileUploadAction_IsInCatalog_OrExplicitlyExcluded()
    {
        var asm = typeof(ImportCenterController).Assembly;
        var catalogControllers = ImportCatalog.All.Select(e => e.Controller).ToHashSet();
        var offenders = new List<string>();

        foreach (var type in asm.GetTypes().Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var name = type.Name.Replace("Controller", "");
            var takesFile = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(IFormFile) || p.ParameterType == typeof(List<IFormFile>)));
            if (takesFile && !catalogControllers.Contains(name) && !KnownNonImportUploads.ContainsKey(name))
                offenders.Add(name);
        }

        Assert.True(offenders.Count == 0,
            "Controllers accepting files that are neither in ImportCatalog nor a documented non-import upload: " + string.Join(", ", offenders));
    }

    // ---------- (3) no role sees more than before ----------

    // Expected counts are derived from the legacy scattered sidebar gates (section roles AND MenuAccess.ImportCenter):
    // SystemAdmin all 12; EngineeringManager: MDR, VDR, Equipment, FL, EFLA, MaintHistory, ConditionReading, MRS, Instrument + Document Upload (AI roles) = 10;
    // Viewer: MDR, VDR; DocumentController: MDR, VDR + Document Upload; MaintenanceEngineer: Equipment, FL, EFLA, MaintHistory, ConditionReading, MRS, Instrument.
    // طبق فاز ۹: «ConditionReading» دقیقاً همون Sets(CmmsIntegration, ImportCenter) را دارد که
    // MaintenanceHistory دارد - پس هر جا MaintenanceHistory دیده می‌شد، حالا این‌جا هم دیده می‌شود.
    [Theory]
    [InlineData(SystemRoles.SystemAdmin, 14)]
    [InlineData(SystemRoles.EngineeringManager, 11)]
    [InlineData(SystemRoles.Viewer, 2)]
    [InlineData(SystemRoles.DocumentController, 3)]
    [InlineData(SystemRoles.MaintenanceEngineer, 8)]
    [InlineData(SystemRoles.QAManager, 0)]
    public void VisibleByRole_MatchesLegacySidebarGates(string role, int expectedCount)
    {
        Assert.Equal(expectedCount, ImportCatalog.VisibleByRole(UserWith(role)).Count);
    }

    [Fact]
    public void ContractManager_And_DisciplineEngineer_OnlySeeDocumentUpload_TheirLegacyAiLink()
    {
        foreach (var role in new[] { SystemRoles.ContractManager, SystemRoles.DisciplineEngineer })
        {
            var keys = ImportCatalog.VisibleByRole(UserWith(role)).Select(e => e.Key).ToList();
            Assert.Equal(new[] { "DocumentUpload" }, keys);
        }
    }

    [Fact]
    public void AnonymousOrRolelessUser_SeesNothing()
    {
        Assert.Empty(ImportCatalog.VisibleByRole(UserWith()));
    }

    [Fact]
    public void InlineDirectImports_DepartmentsAndEmployees_AreAdminOnly_LikeTheirLegacySidebarLink()
    {
        foreach (var role in SystemRoles.All.Where(r => r != SystemRoles.SystemAdmin))
        {
            var keys = ImportCatalog.VisibleByRole(UserWith(role)).Select(e => e.Key).ToList();
            Assert.DoesNotContain("Department", keys);
            Assert.DoesNotContain("Employee", keys);
        }
    }

    // ---------- (4) backend permission is authoritative ----------

    [Fact]
    public async Task VisibleAsync_DropsStagedImport_WhenBackendViewPermissionIsDenied_EvenForAdmin()
    {
        var spy = new SpyPermissionService();
        spy.Denied.Add("MDRImport");

        var result = await ImportCatalog.VisibleAsync(UserWith(SystemRoles.SystemAdmin), spy, new[] { SystemRoles.SystemAdmin });

        Assert.DoesNotContain(result, e => e.Key == "Mdr");
        Assert.Contains(result, e => e.Key == "Vdr");
        Assert.Contains(spy.Asked, a => a.Module == "MDRImport" && a.Action == "View");
    }

    [Fact]
    public async Task ImportCenterController_Index_ReturnsOnlyImportsTheUserMayOpen()
    {
        var spy = new SpyPermissionService();
        spy.Denied.Add("MrsImport");
        var controller = new ImportCenterController(spy, new FakeCurrentUserService { Roles = new List<string> { SystemRoles.SystemAdmin } })
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = UserWith(SystemRoles.SystemAdmin) } }
        };

        var view = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsAssignableFrom<IReadOnlyList<ImportCatalog.ImportEntry>>(view.Model);

        Assert.Equal(13, model.Count); // 14 در کاتالوگ منهای Mrs (رد مجوز)
        Assert.DoesNotContain(model, e => e.Key == "Mrs");
    }

    // ---------- (5) old scattered sidebar links are gone, one entry remains ----------

    // مسیر را از محل خودِ فایل تست پیدا می‌کند (نه از پوشه‌ی خروجی Build) تا از هر خروجی اجرا شود
    private static string RepoFile(string first, string second, string third, string? fourth = null,
        [System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var parts = new[] { first, second, third }.Concat(fourth != null ? new[] { fourth } : Array.Empty<string>()).ToArray();
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Fact]
    public void Sidebar_HasNoScatteredImportLinks_AndExactlyOneImportCenterEntry()
    {
        var sidebar = File.ReadAllText(RepoFile("PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml"));

        foreach (var e in ImportCatalog.All.Where(x => x.Mode == ImportCatalog.LaunchMode.Page))
            Assert.DoesNotContain($"asp-controller=\"{e.Controller}\"", sidebar);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sidebar, "asp-controller=\"ImportCenter\""));
        // Barcode is not an import and must keep its original (ImportCenter-role) gate
        Assert.Contains("asp-controller=\"Barcode\"", sidebar);
    }

    // ---------- (7) Import Center is the ONLY place an import is initiated ----------

    [Fact]
    public void NoModulePage_ContainsAnImportInitiationControl()
    {
        var viewsRoot = Path.GetDirectoryName(RepoFile("PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml"))!;
        viewsRoot = Directory.GetParent(viewsRoot)!.FullName; // .../PEMS.Web/Views

        var importPageFolders = ImportCatalog.All.Where(e => e.Mode == ImportCatalog.LaunchMode.Page)
            .Select(e => e.Controller).Append("ImportCenter").ToHashSet(StringComparer.OrdinalIgnoreCase);
        // dedicated import controllers only; Department/Employee/Instrument are ordinary module controllers (their import is guarded via asp-action="Import")
        var importControllers = ImportCatalog.All.Where(e => e.Mode == ImportCatalog.LaunchMode.Page).Select(e => e.Controller).ToArray();

        var forbidden = new[]
        {
            "#import", "importDeptModal", "importEmpModal", "importInstrumentModal", "Import (MDR)", "Start Import",
        };
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(viewsRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var folder = rel.Split('/')[0];
            if (importPageFolders.Contains(folder)) continue;               // the import pages themselves

            foreach (var line in File.ReadLines(file))
            {
                if (forbidden.Any(f => line.Contains(f, StringComparison.Ordinal)) && !line.TrimStart().StartsWith("@*"))
                    offenders.Add($"{rel}: {line.Trim()}");
                if (line.Contains("asp-action=\"Import\""))
                    offenders.Add($"{rel}: {line.Trim()}");
                // a link/form to an import controller (allowed only to REVIEW an existing batch/history)
                foreach (var c in importControllers)
                    if (line.Contains($"asp-controller=\"{c}\"") && !line.Contains("asp-action=\"Review\"") && !line.Contains("asp-action=\"ImportCenter\""))
                        offenders.Add($"{rel}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "Import initiation found outside the Import Center:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void DirectImportEntries_PostToTheExistingImportAction_FromTheImportCenterDialog()
    {
        foreach (var e in ImportCatalog.All.Where(x => x.Mode == ImportCatalog.LaunchMode.Dialog))
        {
            Assert.Equal("Import", e.Action);
            Assert.False(string.IsNullOrEmpty(e.Accept));
            var type = ControllerType(typeof(ImportCenterController).Assembly, e)!;
            var post = type.GetMethods().Single(m => m.Name == "Import" && m.GetCustomAttributes<HttpPostAttribute>().Any());
            Assert.Contains(post.GetParameters(), p => p.ParameterType == typeof(IFormFile));
        }
    }

    [Fact]
    public void EveryEntry_HasWorkflowType_AndStagedFlagMatchesIt()
    {
        foreach (var e in ImportCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.WorkflowType));
            Assert.Equal(e.WorkflowType == ImportCatalog.Workflow.StagedReview, e.Staged);
        }
    }

    [Fact]
    public void CanUse_MirrorsTheCardGate_ForControllersWithoutModulePermission()
    {
        Assert.True(ImportCatalog.CanUse("Instrument", UserWith(SystemRoles.MaintenanceEngineer)));
        Assert.False(ImportCatalog.CanUse("Instrument", UserWith(SystemRoles.QAManager)));
        Assert.True(ImportCatalog.CanUse("DocumentUpload", UserWith(SystemRoles.DocumentController)));
        Assert.False(ImportCatalog.CanUse("DocumentUpload", UserWith(SystemRoles.Viewer)));
    }

    // ---------- (6) localization ----------

    [Fact]
    public void EveryCatalogEntry_HasEnglishAndPersianText()
    {
        foreach (var file in new[] { "SharedResource.resx", "SharedResource.fa.resx" })
        {
            var keys = XDocument.Load(RepoFile("PEMS.Web", "Resources", file)).Root!
                .Elements("data").Select(d => d.Attribute("name")!.Value).ToHashSet();

            foreach (var e in ImportCatalog.All)
                foreach (var suffix in new[] { "Title", "Desc", "Flow" })
                    Assert.Contains($"IC_{e.Key}_{suffix}", keys);
            foreach (var e in ImportCatalog.All.Where(x => x.Mode == ImportCatalog.LaunchMode.Dialog))
                Assert.Contains($"IC_{e.Key}_Hint", keys);
            foreach (var c in ImportCatalog.CategoryOrder) Assert.Contains($"IC_Cat_{c}", keys);
            Assert.Contains("IC_Title", keys);
        }
    }
}
