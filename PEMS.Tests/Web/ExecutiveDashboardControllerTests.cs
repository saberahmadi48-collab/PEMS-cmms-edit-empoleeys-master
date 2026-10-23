using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Navigation;
using PEMS.Web.Resources;

namespace PEMS.Tests.Web;

/// <summary>داشبورد مدیریتی (وب): مجوز هر بخش، قاعده‌ی دیسیپلین، آدرس‌ها/متن‌های راه‌انداز، میزبان Razor، سایدبار و پوشش ترجمه‌ی رابط React.</summary>
public class ExecutiveDashboardControllerTests
{
    private static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    private static string Read(params string[] parts) => System.IO.File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static Dictionary<string, string> LoadResx(string file)
        => XDocument.Load(Path.Combine(RepoRoot(), "PEMS.Web", "Resources", file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "");

    private sealed class ResxLocalizer : IStringLocalizer<SharedResource>
    {
        private readonly Dictionary<string, string> _map;
        public ResxLocalizer(Dictionary<string, string> map) => _map = map;
        public LocalizedString this[string name] => _map.TryGetValue(name, out var v) ? new LocalizedString(name, v, false) : new LocalizedString(name, name, true);
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _map.Select(kv => new LocalizedString(kv.Key, kv.Value, false));
    }

    private sealed class FakeAccess : IAccessControlService
    {
        private readonly bool _all;
        public FakeAccess(bool all) => _all = all;
        public Task<AccessDecision> AuthorizeForRolesAsync(IEnumerable<string> roles, string resourceName, string actionName)
            => Task.FromResult(_all ? AccessDecision.Allow("Test") : AccessDecision.Deny(AccessDecisionReason.DeniedByRole, "test-denied"));
        public Task<AccessDecision> AuthorizeAsync(AccessControlRequest request) => throw new NotImplementedException();
    }

    private sealed record Rig(ExecutiveDashboardController Controller, PEMSDbContext Ctx, FakePermissionService Permissions);

    private static Rig NewRig(string name, bool viewAllDisciplines = true, string lang = "en", int? userId = LetterTestWorld.Author)
    {
        var ctx = LetterTestWorld.NewContext(name);
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "Main plant" });
        ctx.Disciplines.AddRange(new Discipline { DisciplineId = 1, Code = "ME", Name = "Mechanical", IsActive = true }, new Discipline { DisciplineId = 2, Code = "EL", Name = "Electrical", IsActive = true });
        ctx.SaveChanges();

        var permissions = new FakePermissionService { AllowAll = true };
        var user = new FakeCurrentUserService { UserId = userId, UserName = "u", FullName = "U", Roles = new[] { "Staff" } };
        var service = new ExecutiveDashboardService(ctx, new VDRReportService(ctx), new ProcurementReportService(ctx), new CmmsReportService(ctx),
            new CorrespondenceReportService(ctx), NullLogger<ExecutiveDashboardService>.Instance);
        var controller = new ExecutiveDashboardController(service, user, permissions, new FakeAccess(viewAllDisciplines), ctx,
            new ResxLocalizer(LoadResx(lang == "fa" ? "SharedResource.fa.resx" : "SharedResource.resx"))).WithTempData().WithUrlHelper();
        return new Rig(controller, ctx, permissions);
    }

    private static int _seq;
    private static EngineeringDocument Doc(int? discipline, string status = "Draft")
        => new() { DocumentNo = "X-" + (++_seq), Title = "t", DocumentType = "DWG", ProjectId = 1, DisciplineId = discipline, Status = status };

    private static JsonElement J(IActionResult r)
        => JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // ------------------------------------------------------------------ data endpoint

    [Fact]
    public async Task Data_AsksEachModuleForItsOwnViewPermission_AndDropsTheSectionsTheRoleCannotSee()
    {
        var rig = NewRig("perm");
        rig.Ctx.EngineeringDocuments.Add(Doc(1));
        await rig.Ctx.SaveChangesAsync();
        rig.Permissions.Denied.Add(("VDR", "View"));
        rig.Permissions.Denied.Add(("WorkOrder", "View"));
        rig.Permissions.Denied.Add(("Correspondence", "View"));

        var json = J(await rig.Controller.Data(null));

        Assert.Equal(1, json.GetProperty("engineering").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("vdr").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("operations").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("correspondence").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, json.GetProperty("procurement").ValueKind);
    }

    [Fact]
    public async Task Data_ProcurementIsVisibleWithEitherMaterialRequisitionOrPackagePermission()
    {
        var rig = NewRig("proc-perm");
        rig.Permissions.Denied.Add(("MaterialRequisition", "View"));
        Assert.NotEqual(JsonValueKind.Null, J(await rig.Controller.Data(null)).GetProperty("procurement").ValueKind);

        rig.Permissions.Denied.Add(("ProcurementPackage", "View"));
        Assert.Equal(JsonValueKind.Null, J(await rig.Controller.Data(null)).GetProperty("procurement").ValueKind);
    }

    [Fact]
    public async Task Data_WithoutViewAllDisciplines_LimitsTheEngineeringNumbersToTheEmployeesDiscipline()
    {
        var rig = NewRig("disc", viewAllDisciplines: false);
        // user 101 is employee 1; give them discipline 1
        rig.Ctx.Employees.Single(e => e.EmployeeId == 1).DisciplineId = 1;
        rig.Ctx.EngineeringDocuments.AddRange(Doc(1), Doc(1), Doc(2), Doc(null));
        await rig.Ctx.SaveChangesAsync();

        var restricted = J(await rig.Controller.Data(null)).GetProperty("engineering").GetProperty("total").GetInt32();
        var all = J(await NewRigWithDocs("disc-all", true)).GetProperty("engineering").GetProperty("total").GetInt32();

        Assert.Equal(3, restricted);   // own discipline + unassigned
        Assert.Equal(4, all);
    }

    private static async Task<IActionResult> NewRigWithDocs(string name, bool all)
    {
        var rig = NewRig(name, viewAllDisciplines: all);
        rig.Ctx.EngineeringDocuments.AddRange(Doc(1), Doc(1), Doc(2), Doc(null));
        await rig.Ctx.SaveChangesAsync();
        return await rig.Controller.Data(null);
    }

    [Fact]
    public async Task Data_PassesTheProjectFilterThrough_AndIsNeverCached()
    {
        var rig = NewRig("project");
        rig.Ctx.Projects.Add(new Project { ProjectId = 2, ProjectCode = "P2", ProjectName = "Other" });
        await rig.Ctx.SaveChangesAsync();
        var other = Doc(1); other.ProjectId = 2;
        rig.Ctx.EngineeringDocuments.AddRange(Doc(1), Doc(1), other);
        await rig.Ctx.SaveChangesAsync();

        var json = J(await rig.Controller.Data(2));

        Assert.Equal(1, json.GetProperty("engineering").GetProperty("total").GetInt32());
        Assert.Equal(2, json.GetProperty("projectId").GetInt32());
        var cache = typeof(ExecutiveDashboardController).GetMethod(nameof(ExecutiveDashboardController.Data))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>().Single();
        Assert.True(cache.NoStore);   // management numbers must be live
    }

    // ------------------------------------------------------------------ host page

    [Fact]
    public void Index_ShipsEveryExecString_AndTheLinksTheUiNeeds()
    {
        var rig = NewRig("index", lang: "fa");

        var result = Assert.IsType<ViewResult>(rig.Controller.Index());

        var i18n = (Dictionary<string, string>)rig.Controller.ViewBag.I18n;
        Assert.All(i18n.Keys, k => Assert.StartsWith("Exec_", k));
        Assert.Equal("داشبورد مدیریتی", i18n["Exec_Title"]);
        Assert.Equal("داشبورد مدیریتی", result.ViewData["Title"]);
        var links = (Dictionary<string, string?>)rig.Controller.ViewBag.Links;
        foreach (var key in new[] { "Engineering", "EngRejected", "EngInReview", "Vdr", "Procurement", "Cmms", "Correspondence" })
            Assert.True(links.ContainsKey(key), key);
        Assert.Equal("/ExecutiveDashboard/Data", (string)rig.Controller.ViewBag.DataUrl);
    }

    [Fact]
    public async Task HostView_RendersTheMountPoint_BootJson_AndScopedAssets()
    {
        var rig = NewRig("view");
        rig.Controller.Index();
        rig.Controller.ViewBag.DataUrl = "/ExecutiveDashboard/Data";

        var html = await RazorViewRenderer.RenderAsync(rig.Controller, new ViewResult(), "~/Views/ExecutiveDashboard/Index.cshtml");

        Assert.Contains("id=\"executive-root\"", html);
        Assert.Contains("bk-scope", html);
        var boot = Regex.Match(html, "<script type=\"application/json\" id=\"exec-boot\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.DoesNotContain("<", boot);
        var parsed = JsonDocument.Parse(boot).RootElement;
        Assert.Equal("/ExecutiveDashboard/Data", parsed.GetProperty("dataUrl").GetString());
        Assert.Equal("Executive Dashboard", parsed.GetProperty("i18n").GetProperty("Exec_Title").GetString());
        Assert.False(parsed.GetProperty("rtl").GetBoolean());
        // the script/style tags live in @section blocks, which the render harness skips: check the view source instead
        var source = Read("PEMS.Web", "Views", "ExecutiveDashboard", "Index.cshtml");
        Assert.Contains("dist/dashboards/dashboards.js", source);
        Assert.Contains("dist/dashboards/dashboards.css", source);
        Assert.Contains("type=\"module\"", source);
    }

    // ------------------------------------------------------------------ navigation + built assets

    [Fact]
    public void Navigation_HasTheDashboardRightAfterHome_InCatalogAndLegacySidebar()
    {
        var items = NavigationMenuDefaults.All.Where(i => i.ParentKey == null && i.Kind == NavigationMenuItemKinds.Link).OrderBy(i => i.SortOrder).Take(3).ToList();
        Assert.Equal(new[] { "home", "exec.dashboard", "mywork" }, items.Select(i => i.Key));
        Assert.Equal("ExecutiveDashboard", items[1].Controller);

        var legacy = Read("PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml");
        Assert.True(legacy.IndexOf("ExecutiveDashboard", StringComparison.Ordinal) > legacy.IndexOf("asp-controller=\"Home\"", StringComparison.Ordinal));
        Assert.True(legacy.IndexOf("ExecutiveDashboard", StringComparison.Ordinal) < legacy.IndexOf("asp-controller=\"MyWork\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBuiltBundleExists_SoTheServerNeverNeedsNode()
    {
        foreach (var file in new[] { "dashboards.js", "dashboards.css" })
        {
            var path = Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "dist", "dashboards", file);
            Assert.True(System.IO.File.Exists(path), path + " is missing - run `npm run build` in frontend/dashboards");
            Assert.True(new FileInfo(path).Length > 1000);
        }
    }

    [Fact]
    public void TheBuiltCss_IsScopedUnderTheIslandRoot_SoItCannotRestyleThePemsPage()
    {
        var css = Read("PEMS.Web", "wwwroot", "dist", "dashboards", "dashboards.css");
        // no bare utility selectors such as ".p-4{" or ".grid{": each rule starts with the .bk-scope class (or is an at-rule/keyframe)
        var bare = Regex.Matches(css, @"(?:^|\})\s*\.(p-4|mb-3|grid|flex|text-center|rounded|border)\s*\{");
        Assert.Empty(bare);
        Assert.Contains(".bk-scope", css);
        // Tailwind's theme/base/components/utilities layers would lose to Bootstrap's unlayered reboot; only the harmless `properties` fallback layer may remain
        Assert.DoesNotMatch(@"@layer\s+(theme|base|components|utilities)", css);
    }

    // ------------------------------------------------------------------ translations used by the React UI

    private static IEnumerable<string> UiKeys()
    {
        var root = Path.Combine(RepoRoot(), "frontend", "dashboards", "src");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.tsx", SearchOption.AllDirectories).Where(f => !f.Contains(Path.Combine("components", "charts"))))
            foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(file), "\\bt\\(\\s*\"(Exec_[A-Za-z_]+)\"")) used.Add(m.Groups[1].Value);
        return used;
    }

    [Fact]
    public void EveryStringTheUiReads_ExistsInBothLanguages()
    {
        var keys = UiKeys().ToList();
        Assert.True(keys.Count > 60, "extraction found only " + keys.Count);
        foreach (var file in new[] { "SharedResource.resx", "SharedResource.fa.resx" })
        {
            var map = LoadResx(file);
            Assert.All(keys, k => Assert.True(map.ContainsKey(k) && map[k].Length > 0, $"{file}: {k}"));
        }
    }

    [Fact]
    public void ComposedKeys_BucketsAttentionItemsAndPlaceholders_AreAllTranslated()
    {
        var en = LoadResx("SharedResource.resx");
        var fa = LoadResx("SharedResource.fa.resx");
        var composed = new List<string>();
        composed.AddRange(new[] { "Approved", "InReview", "Returned", "Draft", "Other" }.Select(b => "Exec_Bucket_" + b));
        composed.AddRange(new[] { "EngRejected", "EngReturned", "EngOverdue", "EngInReview", "VdrOverdue", "VdrResubmission", "LetterResponseOverdue", "ReferralOverdue", "CriticalWorkOrders", "MrDraft" }
            .Select(a => "Exec_Attn_" + a));

        // every attention key the service can emit has a sentence
        var emitted = Regex.Matches(Read("PEMS.Persistence", "Servises", "ExecutiveDashboardService.cs"), "Add\\(\"([A-Za-z]+)\",").Select(m => "Exec_Attn_" + m.Groups[1].Value);
        composed.AddRange(emitted);

        Assert.All(composed.Distinct(), k => { Assert.True(en.ContainsKey(k), "en " + k); Assert.True(fa.ContainsKey(k), "fa " + k); });

        // placeholders must survive translation ({0}-style tokens are {name} here)
        foreach (var key in en.Keys.Where(k => k.StartsWith("Exec_")))
        {
            var tokens = Regex.Matches(en[key], "\\{[a-z]+\\}").Select(m => m.Value).OrderBy(x => x);
            Assert.Equal(tokens, Regex.Matches(fa[key], "\\{[a-z]+\\}").Select(m => m.Value).OrderBy(x => x));
        }
    }
}
