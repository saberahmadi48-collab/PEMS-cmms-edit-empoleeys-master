using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-408: فهرست مدارک مهندسی با ظاهر فهرست نامه‌ها - صفحه‌ی تمام‌قد، یک نوار ابزار آیکنی، فیلترها در منوی کشویی (نه Offcanvas)،
/// شمارش‌ها پیش‌فرض پنهان با دکمه‌ی نمایش، و رفتارهای قبلی (جستجو، اکسل، صفحه‌بندی، تغییر اندازه‌ی ستون، حذف) سر جایشان.
/// </summary>
public class EngineeringDocumentsListLookTests
{
    private static Task<string> Render(EngineeringDocumentListFilter filter, params EngineeringDocumentDto[] items)
    {
        // رندر فقط به ViewData کنترلر نیاز دارد؛ سازنده (سرویس‌ها) استفاده نمی‌شود
        var controller = (EngineeringDocumentsController)RuntimeHelpers.GetUninitializedObject(typeof(EngineeringDocumentsController));
        controller.ViewData["Filter"] = filter;
        controller.ViewData["Summary"] = new EngineeringDocumentSummaryCountsDto { Total = 12, Approved = 5, Pending = 3, Rejected = 1, Frozen = 2, Overdue = 4 };
        controller.ViewData["Disciplines"] = new List<Discipline>
        {
            new() { DisciplineId = 1, Code = "ME", Name = "Mechanical" },
            new() { DisciplineId = 2, Code = "EL", Name = "Electrical" }
        };
        var model = new PagedResult<EngineeringDocumentDto> { Items = items.ToList(), Page = 1, PageSize = 25, TotalCount = items.Length };
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(controller.ViewData) { Model = model } };
        return RazorViewRenderer.RenderAsync(controller, result, "/Views/EngineeringDocuments/Index.cshtml");
    }

    private static EngineeringDocumentDto Doc(int id, string no) => new()
    {
        DocumentId = id, DocumentNo = no, Title = "Pump datasheet " + id, DocumentType = "DS", ProjectName = "JPC",
        DisciplineName = "Mechanical", RevisionNo = "B", Status = "Approved", CreatedDate = new DateTime(2026, 10, 1)
    };

    [Fact]
    public async Task ThePage_UsesTheCorrespondenceListFrame_FullPagePanel_OneIconToolbar_TableAndFooter()
    {
        var html = await Render(new EngineeringDocumentListFilter(), Doc(1, "JPC-ME-DS-001"), Doc(2, "JPC-ME-DS-002"));

        Assert.Contains("class=\"lp-page\"", html);
        Assert.Contains("class=\"lp-panel\"", html);
        Assert.Single(Regex.Matches(html, "class=\"lp-toolbar\""));
        Assert.Contains("class=\"lp-tablewrap\"", html);
        Assert.Contains("lp-scroll", html);
        Assert.Contains("class=\"lp-footer\"", html);
        Assert.Matches("<table class=\"[^\"]*lp-table[^\"]*\" data-grid-key=\"EngineeringDocuments\"", html);
        Assert.Equal(2, Regex.Matches(html, "<tr class=\"lp-row\"").Count);

        // the old layout is gone: no in-flow search bar, no off-canvas filters, no always-visible KPI row
        Assert.DoesNotContain("pems-search-bar", html);
        Assert.DoesNotContain("offcanvas", html);
        Assert.DoesNotContain("class=\"vs-chips\"", html);
        Assert.DoesNotContain("vs-panel", html);

        // icon buttons of the toolbar: search, filters, KPIs, Excel, reset columns; New stays a labelled button
        Assert.Contains("data-lp-search-open", html);
        Assert.Contains("id=\"edFilterFormBtn\"", html);
        Assert.Contains("data-lp-kpi-toggle", html);
        Assert.Contains("bi-file-earmark-excel", html);
        Assert.Matches("class=\"lp-icon-btn lp-hide-xs\" data-lp-reset-columns=\"EngineeringDocuments\"", html);   // hidden on phones; pems-list.js resets
        Assert.Contains("lp-compose", html);

    }

    [Fact]
    public async Task Filters_AreInADropdownPopover_WithAllThreeFields()
    {
        var html = await Render(new EngineeringDocumentListFilter());

        var pop = Regex.Match(html, "<div class=\"dropdown-menu dropdown-menu-end lp-filter-pop p-0\">(.*?)</form>", RegexOptions.Singleline);
        Assert.True(pop.Success);
        Assert.Contains("name=\"DisciplineId\"", pop.Value);
        Assert.Contains("name=\"Status\"", pop.Value);
        Assert.Contains("name=\"Area\"", pop.Value);
        Assert.Contains("value=\"2\">Electrical</option>", pop.Value);
        var button = Regex.Match(html, "<button[^>]*id=\"edFilterFormBtn\"[^>]*>").Value;
        Assert.Contains("data-bs-toggle=\"dropdown\"", button);
        Assert.Contains("data-bs-auto-close=\"outside\"", button);
        Assert.DoesNotContain("lp-badge", html);                                // nothing filtered
        Assert.DoesNotContain("class=\"lp-chips\"", html);
    }

    [Fact]
    public async Task ActiveFilters_ShowACountBadge_AndRemovableChips()
    {
        var html = await Render(new EngineeringDocumentListFilter { DisciplineId = 2, Status = "Approved", Search = "pump" });

        Assert.Matches("<span class=\"lp-badge\">2<", html);
        var chips = Regex.Match(html, "<div class=\"lp-chips\">(.*?)</div>", RegexOptions.Singleline);
        Assert.True(chips.Success);
        Assert.Equal(2, Regex.Matches(chips.Value, "class=\"pems-filter-chip\"").Count);
        Assert.Contains("Electrical", chips.Value);
        Assert.Contains("value=\"2\" selected=\"selected\"", html);           // popover keeps the current values
        Assert.Contains("value=\"pump\"", html);                              // the search term stays in the search field
    }

    [Fact]
    public async Task Kpis_AreRenderedHidden_AndTheToggleControlsThem()
    {
        var html = await Render(new EngineeringDocumentListFilter());

        Assert.Matches("<div class=\"lp-kpis\" id=\"edKpis\" hidden(=\"hidden\")?>", html);
        Assert.Contains("aria-controls=\"edKpis\"", html);
        Assert.Contains("data-storage-key=\"pems.engdocs.kpis\"", html);
        var kpis = Regex.Match(html, "id=\"edKpis\" hidden(?:=\"hidden\")?>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(6, Regex.Matches(kpis, "class=\"vs-chip").Count);
        Assert.Contains("<strong>12</strong>", kpis);
        Assert.Contains("<strong>4</strong>", kpis);
    }

    [Fact]
    public async Task AnEmptyList_ShowsTheCorrespondenceEmptyState_WithAClearButtonOnlyWhenFiltered()
    {
        Assert.DoesNotContain("btn btn-sm btn-outline-secondary mt-2", await Render(new EngineeringDocumentListFilter()));

        var filtered = await Render(new EngineeringDocumentListFilter { Area = "U-100" });
        Assert.Contains("class=\"lp-empty\"", filtered);
        Assert.Contains("btn btn-sm btn-outline-secondary mt-2", filtered);
    }

    [Fact]
    public void TheSharedStylesheet_HasTheFullPageFrame_AndTheListScriptTogglesKpisAndSearch()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        // SCRUM-337: the list styles and script belong to the component library now
        var css = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.UI", "wwwroot", "pems-list.css"));
        var js = File.ReadAllText(Path.Combine(dir.FullName, "PEMS.UI", "wwwroot", "pems-list.js"));

        Assert.Matches(@"\.lp-page \{[^}]*height: calc\(100dvh - 112px\)", css);
        Assert.Contains(".lp-page .lp-scroll", css);
        Assert.Contains("data-lp-kpi-toggle", js);
        Assert.Contains("localStorage", js);
        Assert.Contains("data-lp-search-open", js);

        // the page is built from the component and loads its stylesheet and script (sections are not rendered without a layout)
        var view = File.ReadAllText(Path.Combine(dir.FullName, "PEMS.Web", "Views", "EngineeringDocuments", "Index.cshtml"));
        Assert.Contains("<pems-list-page texts=", view);
        Assert.Contains("~/_content/PEMS.UI/pems-list.css", view);
        Assert.Contains("~/_content/PEMS.UI/pems-list.js", view);
    }
}
