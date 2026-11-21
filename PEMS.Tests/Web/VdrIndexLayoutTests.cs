using System.Dynamic;
using System.Reflection;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Helpers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// فهرست VDR با همان قاب فهرست مدارک مهندسی (SCRUM-409، پیرو SCRUM-408): صفحه‌ی تمام‌قد، یک نوار ابزار آیکنی، فیلترها در منوی کشویی
/// با نشان تعداد و چیپ، شمارش‌ها و ریسک فروشنده پیش‌فرض پنهان، جستجوی متنی و خروجی Excel، ستون‌های پروژه/دیسیپلین.
/// View واقعی رندر می‌شود (Localizer متن انگلیسی resx را می‌دهد).
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

    private static async Task<string> RenderAsync(IEnumerable<VDRListItemDto> rows, int? projectId = null, int? vendorId = null, string? status = null,
        string? search = null, VDRDashboardDto? dash = null)
    {
        var (_, letters, ctx, _) = LetterTestWorld.Services("vdrindex");
        var c = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService(), null!, null!, null!, null!, null!, null!).WithTempData();
        c.ViewBag.Dashboard = dash ?? new VDRDashboardDto { TotalItems = 7, OverdueItems = 3, SlaOverdueItems = 2, OnTimePercentage = 80 };
        // anonymous types do not cross assemblies for the view's dynamic binder, so the lookups are expandos
        dynamic project = new ExpandoObject(); project.ProjectId = 1; project.ProjectName = "Polymer Plant Development";
        dynamic vendor = new ExpandoObject(); vendor.VendorId = 9; vendor.Name = "KASRAVAND";
        c.ViewBag.Projects = new List<dynamic> { project };
        c.ViewBag.Vendors = new List<dynamic> { vendor };
        c.ViewBag.SelectedProjectId = projectId;
        c.ViewBag.SelectedVendorId = vendorId;
        c.ViewBag.SelectedStatus = status;
        c.ViewBag.Search = search;

        var list = rows.ToList();
        var model = new PagedResult<VDRListItemDto> { Items = list, Page = 1, PageSize = 25, TotalCount = list.Count };
        var result = new ViewResult { ViewData = new ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/VDR/Index.cshtml");
    }

    [Fact]
    public async Task ThePage_UsesTheSameFrameAsTheEngineeringDocumentsList()
    {
        var html = await RenderAsync(new[] { Row(), Row("PKG-EL-0231-02") });

        Assert.Contains("class=\"lp-page\"", html);
        Assert.Contains("class=\"lp-panel\"", html);
        Assert.Single(Regex.Matches(html, "class=\"lp-toolbar\""));
        Assert.Matches("<h2 title=\"Vendor document register[^\"]*\">Vendor Document Register</h2>", html);
        Assert.Matches("<span [^>]*class=\"lp-total\"[^>]*>2</span>", html);
        Assert.Contains("lp-scroll", html);
        Assert.Contains("class=\"lp-footer\"", html);
        Assert.Matches("<table class=\"[^\"]*lp-table[^\"]*\" data-grid-key=\"VDR\"", html);
        Assert.Equal(2, Regex.Matches(html, "<tr class=\"lp-row ").Count);

        // module sub-navigation with VDR as the active tab, like the documents list
        Assert.Matches("class=\"vs-subnav\"", html);
        Assert.Matches("<a class=\"active\" aria-current=\"page\" href=\"[^\"]*\">Vendor Documents</a>|<a href=\"[^\"]*\" class=\"active\" aria-current=\"page\">", html);

        // toolbar: search, filters, KPIs, Excel, reset columns (hidden on phones), New
        Assert.Contains("data-lp-search-open", html);
        Assert.Contains("id=\"vdrFilterFormBtn\"", html);
        Assert.Contains("data-lp-kpi-toggle", html);
        Assert.Contains("bi-file-earmark-excel", html);
        Assert.Contains("class=\"lp-icon-btn lp-hide-xs\" data-lp-reset-columns=\"VDR\"", html);   // SCRUM-337: pems-list.js resets
        Assert.Contains("New vendor document", html);

        // the old layout is gone
        Assert.DoesNotContain("vdrFilterPanel", html);
        Assert.DoesNotContain("vs-header", html);
        Assert.DoesNotContain("vs-panel__head", html);
        Assert.DoesNotContain("badge rounded-pill", html);
    }

    [Fact]
    public async Task Search_IsOneFieldOverTheTitle_WithTheGlobalAutocompleteAndTheCurrentTerm()
    {
        var html = await RenderAsync(new[] { Row() }, projectId: 1, search: "datasheet");

        var form = Regex.Match(html, "<form [^>]*id=\"vdrSearchForm\"[^>]*>(.*?)</form>", RegexOptions.Singleline);
        Assert.True(form.Success);
        Assert.Contains("data-lp-search", form.Value);
        var input = Regex.Match(form.Value, "<input [^>]*id=\"vdrSearchInput\"[^>]*>").Value;
        Assert.Contains("name=\"search\"", input);
        Assert.Contains("value=\"datasheet\"", input);
        Assert.Contains("data-search-type=\"VDRItem\"", form.Value);
        Assert.Contains("name=\"projectId\" value=\"1\"", form.Value);            // the other filters stay applied
    }

    [Fact]
    public async Task Filters_AreInADropdownPopover_WithLabelledSelects_AndNoBadgeWhenNothingIsFiltered()
    {
        var html = await RenderAsync(new[] { Row() });

        var pop = Regex.Match(html, "<div class=\"dropdown-menu dropdown-menu-end lp-filter-pop p-0\">(.*?)</form>", RegexOptions.Singleline);
        Assert.True(pop.Success);
        foreach (var id in new[] { "vdrFltProject", "vdrFltVendor", "vdrFltStatus" })
        {
            Assert.Contains($"for=\"{id}\"", pop.Value);
            Assert.Contains($"id=\"{id}\"", pop.Value);
        }
        Assert.DoesNotContain("lp-badge", html);
        Assert.DoesNotContain("class=\"lp-chips\"", html);
    }

    [Theory]
    [InlineData(1, null, null, 1)]
    [InlineData(1, 9, null, 2)]
    [InlineData(1, 9, "Closed", 3)]
    [InlineData(null, null, "Planned", 1)]
    public async Task ActiveFilters_ShowACountBadge_AndOneRemovableChipEach(int? project, int? vendor, string? status, int expected)
    {
        var html = await RenderAsync(new[] { Row() }, project, vendor, status);

        Assert.Matches("<span class=\"lp-badge\">" + expected + "<span class=\"visually-hidden\"> active filters</span>", html);
        var chips = Regex.Match(html, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline).Value;
        Assert.Equal(expected, Regex.Matches(chips, "class=\"pems-filter-chip\"").Count);
        if (project != null) Assert.Contains("Polymer Plant Development", chips);
        if (vendor != null) Assert.Contains("KASRAVAND", chips);
    }

    [Fact]
    public async Task Kpis_AndTheVendorRisk_AreHiddenByDefault_BehindTheKpiButton()
    {
        var dash = new VDRDashboardDto
        {
            TotalItems = 7, OverdueItems = 3, SlaOverdueItems = 2, OnTimePercentage = 80,
            TopDelayedVendors = new List<VendorDelayDto> { new() { VendorName = "SLOWCO", OverdueCount = 4, AverageDelayDays = 12, RiskLevel = "High" } }
        };
        var html = await RenderAsync(new[] { Row() }, dash: dash);

        Assert.Matches("<div class=\"lp-kpis\" id=\"vdrKpis\" hidden(=\"hidden\")?>", html);
        Assert.Contains("aria-controls=\"vdrKpis\"", html);
        Assert.Contains("data-storage-key=\"pems.vdr.kpis\"", html);
        var kpis = html[html.IndexOf("id=\"vdrKpis\"", StringComparison.Ordinal)..];
        Assert.Contains("<strong>7</strong>", kpis);
        Assert.Contains("<strong>80%</strong>", kpis);
        Assert.Contains("class=\"lp-kpi-pop\"", kpis);
        Assert.Contains("SLOWCO", kpis);
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
    public async Task OverdueRows_StayMarked()
    {
        var overdue = Row("PKG-ME-0044-00");
        overdue.IsOverdue = true;
        var html = await RenderAsync(new[] { overdue });

        Assert.Contains("lp-row table-danger", html);
        Assert.Contains("vs-pill vs-pill--danger", html);
    }

    [Fact]
    public async Task AnEmptyList_ShowsTheEmptyState_WithAClearButtonOnlyWhenSearchingOrFiltering()
    {
        Assert.DoesNotContain("btn btn-sm btn-outline-secondary mt-2", await RenderAsync(Array.Empty<VDRListItemDto>()));

        var searched = await RenderAsync(Array.Empty<VDRListItemDto>(), search: "zzz");
        Assert.Contains("class=\"lp-empty\"", searched);
        Assert.Contains("btn btn-sm btn-outline-secondary mt-2", searched);
    }

    // ------------------------------------------------------------------ query, search and export

    private static async Task<PEMS.Persistence.Context.PEMSDbContext> SeedAsync(string name)
    {
        var ctx = LetterTestWorld.NewContext(name);
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "P1", ProjectName = "Polymer Plant Development", IsActive = true });
        ctx.Vendors.Add(new Vendor { VendorId = 9, Name = "KASRAVAND", IsActive = true });
        ctx.Vendors.Add(new Vendor { VendorId = 10, Name = "PUMPCO", IsActive = true });
        ctx.Disciplines.Add(new Discipline { DisciplineId = 4, Name = "Electrical", IsActive = true });
        ctx.VDRItems.Add(new VDRItem
        {
            DocumentId = 1, ProjectId = 1, VendorId = 9, DisciplineId = 4, DocumentNumber = "A-1", DocumentDescription = "Switchgear datasheet",
            DocumentTypeCode = "DS", WorkflowStatus = "Planned", IsActive = true
        });
        ctx.VDRItems.Add(new VDRItem
        {
            DocumentId = 2, ProjectId = 1, VendorId = 9, DisciplineId = null, DisciplineCodeRaw = "INST", DocumentNumber = "A-2",
            DocumentDescription = "Transmitter hook-up", DocumentTypeCode = "DW", WorkflowStatus = "Planned", IsActive = true
        });
        ctx.VDRItems.Add(new VDRItem
        {
            DocumentId = 3, ProjectId = 1, VendorId = 10, DocumentNumber = "B-7", DocumentDescription = "Pump curve",
            DocumentTypeCode = "DS", WorkflowStatus = "Closed", IsActive = true
        });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task ListQuery_ProjectsProjectName_AndDisciplineNameWithRawCodeFallback()
    {
        var ctx = await SeedAsync("vdrquery");

        var page = await new VDRService(ctx).GetPagedAsync(null, null, null, 1, 25);

        var byNumber = page.Items.ToDictionary(i => i.DocumentNumber);
        Assert.Equal("Polymer Plant Development", byNumber["A-1"].ProjectName);
        Assert.Equal("Electrical", byNumber["A-1"].DisciplineName);
        Assert.Equal("INST", byNumber["A-2"].DisciplineName);       // imported item without a discipline row shows its raw code
    }

    [Theory]
    [InlineData("A-1", new[] { "A-1" })]                 // document number
    [InlineData("datasheet", new[] { "A-1" })]           // description
    [InlineData("PUMPCO", new[] { "B-7" })]              // vendor name
    [InlineData("  ", new[] { "A-1", "A-2", "B-7" })]    // blank = no search
    [InlineData("nothing-like-this", new string[0])]
    public async Task Search_MatchesNumberDescriptionOrVendor(string term, string[] expected)
    {
        var ctx = await SeedAsync("vdrsearch_" + Guid.NewGuid().ToString("N"));

        var page = await new VDRService(ctx).GetPagedAsync(null, null, null, 1, 25, term);

        Assert.Equal(expected.OrderBy(x => x), page.Items.Select(i => i.DocumentNumber).OrderBy(x => x));
        Assert.Equal(expected.Length, page.TotalCount);
    }

    [Fact]
    public async Task Export_UsesTheSameFiltersAsTheList_ReportsTheRealTotal_AndWritesTheListColumns()
    {
        var ctx = await SeedAsync("vdrexport");
        var service = new VDRService(ctx);

        var (items, total) = await service.GetAllMatchingAsync(1, 9, null, null, 1);
        Assert.Equal(2, total);                                     // real count, even when capped
        Assert.Single(items);

        // InMemory compares case-sensitively; SQL Server's default collation does not
        var (filtered, _) = await service.GetAllMatchingAsync(null, null, "Closed", "Pump", 100);
        var bytes = VdrListExport.Build(filtered);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var sheet = wb.Worksheet(1);
        Assert.Equal("Document No", sheet.Cell(1, 1).GetString());
        Assert.Equal(VdrListExport.Headers.Length, sheet.Row(1).CellsUsed().Count());
        Assert.Equal("B-7", sheet.Cell(2, 1).GetString());
        Assert.Equal("PUMPCO", sheet.Cell(2, 4).GetString());
        Assert.True(sheet.Cell(3, 1).IsEmpty());

        var export = typeof(VDRController).GetMethod(nameof(VDRController.Export))!;
        Assert.NotNull(export.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void EveryListKey_ExistsInBothLanguages()
    {
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "VDR", "Index.cshtml"));
        var keys = Regex.Matches(view, "Localizer\\[\"([A-Za-z_]+)\"\\]").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count >= 20, "expected the keys to be discovered");

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
