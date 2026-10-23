using System.Dynamic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// فهرست VDR (طراحی جدید): عنوان + زیرعنوان، آیکون فیلتر با نشان تعداد فیلتر فعال، پنل فیلتر جمع‌شونده، ستون‌های پروژه/دیسیپلین،
/// و حذف نوارهای KPI و جستجوی سریع. View واقعی رندر می‌شود (Localizer متن انگلیسی resx را می‌دهد).
/// </summary>
public class VdrIndexLayoutTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId => 1;
        public string? UserName => "u";
        public string? FullName => "U";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => new[] { "Staff" };
    }

    private static VDRListItemDto Row(string number = "PKG-EL-0231-01", string? discipline = "Electrical") => new()
    {
        VDRItemId = 1, DocumentNumber = number, DocumentDescription = "Panel datasheet", DocumentTypeCode = "DS", VendorName = "KASRAVAND",
        ProjectName = "Polymer Plant Development", DisciplineName = discipline, WorkflowStatus = "Submitted"
    };

    private static async Task<string> RenderAsync(IEnumerable<VDRListItemDto> rows, int? projectId = null, int? vendorId = null, string? status = null)
    {
        var (_, letters, ctx, _) = LetterTestWorld.Services("vdrindex");
        var c = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService(), null!, null!, null!, null!, null!, null!).WithTempData();
        c.ViewBag.Dashboard = new VDRDashboardDto { TotalItems = 7, OverdueItems = 3, SlaOverdueItems = 2, OnTimePercentage = 80 };
        // anonymous types do not cross assemblies for the view's dynamic binder, so the lookups are expandos
        dynamic project = new ExpandoObject(); project.ProjectId = 1; project.ProjectName = "Polymer Plant Development";
        dynamic vendor = new ExpandoObject(); vendor.VendorId = 9; vendor.Name = "KASRAVAND";
        c.ViewBag.Projects = new List<dynamic> { project };
        c.ViewBag.Vendors = new List<dynamic> { vendor };
        c.ViewBag.SelectedProjectId = projectId;
        c.ViewBag.SelectedVendorId = vendorId;
        c.ViewBag.SelectedStatus = status;

        var list = rows.ToList();
        var model = new PagedResult<VDRListItemDto> { Items = list, Page = 1, PageSize = 25, TotalCount = list.Count };
        var result = new ViewResult { ViewData = new ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/VDR/Index.cshtml");
    }

    [Fact]
    public async Task Header_HasTitleSubtitleAndTheNewDocumentButton()
    {
        var html = await RenderAsync(new[] { Row() });

        Assert.Matches(@"<h1 class=""vs-title"">Vendor Document Register</h1>", html);
        Assert.Contains("Vendor document register - submissions, review status and overdue tracking", html);
        Assert.Contains("New vendor document", html);
    }

    [Fact]
    public async Task KpiChipsAndQuickSearch_AreGone()
    {
        var html = await RenderAsync(new[] { Row() });

        Assert.DoesNotContain("vs-chips", html);
        Assert.DoesNotContain("vdrQuickSearch", html);
        Assert.DoesNotContain("data-pems-search", html);
    }

    [Fact]
    public async Task FilterIcon_HasNoBadge_WhenNothingIsFiltered_AndTheSelectsAreLabelled()
    {
        var html = await RenderAsync(new[] { Row() });

        Assert.Matches(@"data-bs-target=""#vdrFilterPanel""[^>]*aria-expanded=""false""", html);
        Assert.DoesNotContain("badge rounded-pill", html);
        Assert.Matches(@"<div class=""collapse "" id=""vdrFilterPanel"">", html);
        foreach (var id in new[] { "vdrFltProject", "vdrFltVendor", "vdrFltStatus" })
        {
            Assert.Contains($"for=\"{id}\"", html);
            Assert.Contains($"id=\"{id}\"", html);
        }
    }

    [Theory]
    [InlineData(null, null, null, 0)]
    [InlineData(1, null, null, 1)]
    [InlineData(1, 9, null, 2)]
    [InlineData(1, 9, "Closed", 3)]
    [InlineData(null, null, "Planned", 1)]
    public async Task Badge_CountsTheActiveFilters_AndOpensThePanel(int? project, int? vendor, string? status, int expected)
    {
        var html = await RenderAsync(new[] { Row() }, project, vendor, status);

        if (expected == 0)
        {
            Assert.DoesNotContain("badge rounded-pill", html);
            return;
        }

        Assert.Matches(@"badge rounded-pill bg-primary"">" + expected + @"<span class=""visually-hidden""> active filters</span>", html);
        Assert.Matches(@"data-bs-target=""#vdrFilterPanel""[^>]*aria-expanded=""true""", html);
        Assert.Contains("collapse show", html);                 // active filters stay visible
        Assert.Contains(">Reset<", html);                       // and can be cleared in one click
    }

    [Fact]
    public async Task Table_ShowsProjectAndDisciplineColumns_AndFallsBackToADashWhenThereIsNoDiscipline()
    {
        var html = await RenderAsync(new[] { Row(), Row("PKG-IN-0107-02", null) });

        var vendor = html.IndexOf("data-col-key=\"vendor\"", StringComparison.Ordinal);
        var project = html.IndexOf("data-col-key=\"project\"", StringComparison.Ordinal);
        var discipline = html.IndexOf("data-col-key=\"discipline\"", StringComparison.Ordinal);
        var status = html.IndexOf("data-col-key=\"status\"", StringComparison.Ordinal);
        Assert.True(vendor > 0 && project > vendor && discipline > project && status > discipline, "columns must read Vendor, Project, Discipline, ... Status");

        Assert.Contains("Polymer Plant Development", html);
        Assert.Contains(">Electrical<", html);
        Assert.Matches(@">(—|&#x2014;)<", html);        // the dash is HTML-encoded in the output
    }

    [Fact]
    public async Task OverdueRows_AndTheRiskSectionRemain()
    {
        var overdue = Row("PKG-ME-0044-00");
        overdue.IsOverdue = true;
        var html = await RenderAsync(new[] { overdue });

        Assert.Contains("table-danger", html);
        Assert.Contains("vs-pill vs-pill--danger", html);
    }

    // ------------------------------------------------------------------ query

    [Fact]
    public async Task ListQuery_ProjectsProjectName_AndDisciplineNameWithRawCodeFallback()
    {
        var ctx = LetterTestWorld.NewContext("vdrquery");
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "Polymer Plant Development", IsActive = true });
        ctx.Vendors.Add(new Vendor { VendorId = 9, Name = "KASRAVAND", IsActive = true });
        ctx.Disciplines.Add(new Discipline { DisciplineId = 4, Name = "Electrical", IsActive = true });
        ctx.VDRItems.Add(new VDRItem
        {
            DocumentId = 1, ProjectId = 1, VendorId = 9, DisciplineId = 4, DocumentNumber = "A-1", DocumentTypeCode = "DS", WorkflowStatus = "Planned", IsActive = true
        });
        ctx.VDRItems.Add(new VDRItem
        {
            DocumentId = 2, ProjectId = 1, VendorId = 9, DisciplineId = null, DisciplineCodeRaw = "INST", DocumentNumber = "A-2", DocumentTypeCode = "DS", WorkflowStatus = "Planned", IsActive = true
        });
        await ctx.SaveChangesAsync();

        var page = await new VDRService(ctx).GetPagedAsync(null, null, null, 1, 25);

        var byNumber = page.Items.ToDictionary(i => i.DocumentNumber);
        Assert.Equal("Polymer Plant Development", byNumber["A-1"].ProjectName);
        Assert.Equal("Electrical", byNumber["A-1"].DisciplineName);
        Assert.Equal("INST", byNumber["A-2"].DisciplineName);       // imported item without a discipline row shows its raw code
    }

    [Fact]
    public void EveryNewListKey_ExistsInBothLanguages()
    {
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "VDR", "Index.cshtml"));
        var keys = Regex.Matches(view, "Localizer\\[\"(Vs_(?:VdrRegisterHeading|NewVendorDocument|FilterToggle|ActiveFilters|VdrSubtitle|Discipline|Project))\"\\]")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count >= 7, "expected the keys to be discovered");

        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var text = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Resources", file));
            var missing = keys.Where(k => !text.Contains("name=\"" + k + "\"")).ToList();
            Assert.True(missing.Count == 0, file + " is missing: " + string.Join(", ", missing));
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }
}
