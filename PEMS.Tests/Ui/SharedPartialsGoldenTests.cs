using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Models;
using PEMS.Web.ViewModels;
using System.Globalization;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;

namespace PEMS.Tests.Ui;

/// <summary>
/// Golden output of shared partials that live in PEMS.UI but are rendered by PEMS.Web (SCRUM-29): moving a partial between projects
/// must not change one byte of what pages receive. Expected HTML is in <c>Ui/Golden/*.html</c>. To record a new golden file set
/// <c>PEMS_GOLDEN_RECORD=1</c> and run the test once on the code that is known to be right, then review the file in the diff.
/// </summary>
public class SharedPartialsGoldenTests
{
    private static async Task<string> RenderAsync(string view, object? model, Action<ViewDataDictionary>? configure = null)
    {
        var host = new PEMS.Tests.TestSupport.ViewRenderHost();
        host.ViewData.Model = model;
        configure?.Invoke(host.ViewData);   // the renderer copies the controller's ViewData
        var result = new ViewResult { ViewData = new ViewDataDictionary(host.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(host, result, view);
    }

    private static void AssertGolden(string name, string actual)
    {
        var path = Path.Combine(FindGoldenDirectory(), name + ".html");
        if (Environment.GetEnvironmentVariable("PEMS_GOLDEN_RECORD") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"Golden file missing: {path} (record it with PEMS_GOLDEN_RECORD=1)");
        Assert.Equal(Lf(File.ReadAllText(path)), Lf(actual));   // line-ending style only: Windows checkouts convert the files (autocrlf)
    }

    private static string Lf(string s) => s.Replace("\r\n", "\n");

    private static string FindGoldenDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PEMS.Tests", "Ui", "Golden"))) dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("PEMS.Tests/Ui/Golden") : Path.Combine(dir.FullName, "PEMS.Tests", "Ui", "Golden");
    }

    // The _KpiCard partial is retired (SCRUM-337 KpiTile K2); its markup is now <pems-kpi-tile model="..."> (KpiTileComponentTests).

    [Fact]
    public async Task LoadingOverlay_RendersTheSameMarkup() =>
        AssertGolden("loading-overlay", await RenderAsync("~/Views/Shared/_PemsLoadingOverlay.cshtml", null));

    // ---- _Pagination (SCRUM-418): the markup the 54 list pages receive must not change when the partial moves into PEMS.UI
    private static async Task<string> InCultureAsync(string culture, Func<Task<string>> render)
    {
        var (c, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try { return await render(); }
        finally { CultureInfo.CurrentCulture = c; CultureInfo.CurrentUICulture = ui; }
    }

    private static PaginationViewModel Pager(int page, int pages, int total, string? gridKey = null, int pageSize = 25, string query = "") => new()
    {
        Page = page, TotalPages = pages, TotalCount = total, BaseUrl = "/WorkOrders", ExistingQueryString = query, GridKey = gridKey, PageSize = pageSize
    };

    [Theory]
    [InlineData("en", 1, 5, 120, null, "q=pump&status=open", "pagination-en-first")]
    [InlineData("en", 3, 10, 250, "wo", "", "pagination-en-middle-gridkey")]
    [InlineData("en", 10, 10, 250, "wo", "q=a", "pagination-en-last-gridkey")]
    [InlineData("en", 0, 0, 0, null, "", "pagination-en-empty")]
    [InlineData("en", 1, 1, 7, "wo", "", "pagination-en-single-gridkey")]
    [InlineData("fa", 3, 10, 250, "wo", "", "pagination-fa-middle-gridkey")]
    [InlineData("fa", 1, 5, 120, null, "q=pump", "pagination-fa-first")]
    [InlineData("fa", 0, 0, 0, "wo", "", "pagination-fa-empty-gridkey")]
    public async Task Pagination_RendersTheSameMarkup(string culture, int page, int pages, int total, string? gridKey, string query, string golden) =>
        AssertGolden(golden, await InCultureAsync(culture, () => RenderAsync("~/Views/Shared/_Pagination.cshtml", Pager(page, pages, total, gridKey, 25, query))));

    // ---- _PemsState (SCRUM-424): the facade page authors call for loading, empty and error states
    private static TranslatableText Tx(string fa, string en) => TranslatableText.Of(("fa", fa), ("en", en));

    public static IEnumerable<object[]> StateCases()
    {
        var action = new StateAction { Label = Tx("افزودن", "Add"), Href = "/x/create" };
        yield return new object[] { "en", new EmptyStateOptions { Title = Tx("رکوردی نیست", "No records"), Description = Tx("اولین رکورد را بسازید", "Create the first record"), Icon = "bi-inbox", Action = action }, "state-empty-en" };
        yield return new object[] { "fa", new EmptyStateOptions { Title = Tx("رکوردی نیست", "No records"), Action = action }, "state-empty-fa" };
        yield return new object[] { "en", new ErrorStateOptions { Title = Tx("بارگذاری نشد", "Could not load"), Description = Tx("دوباره تلاش کنید", "Try again later"), Detail = Tx("کد ۵۰۳", "Code 503"), Action = new StateAction { Label = Tx("تلاش دوباره", "Retry"), Href = "/x" } }, "state-error-en" };
        yield return new object[] { "fa", new ErrorStateOptions { Title = Tx("بارگذاری نشد", "Could not load") }, "state-error-fa" };
        yield return new object[] { "en", new LoadingStateOptions { Title = Tx("در حال بارگذاری", "Loading"), Lines = 3 }, "state-loading-en" };
        yield return new object[] { "fa", new LoadingStateOptions { Title = Tx("در حال بارگذاری", "Loading") }, "state-loading-fa" };
    }

    [Theory]
    [MemberData(nameof(StateCases))]
    public async Task State_facade_renders_the_same_markup(string culture, object options, string golden) =>
        AssertGolden(golden, await InCultureAsync(culture, () => RenderAsync("~/Views/Shared/_PemsState.cshtml", options)));

    // ---- _PageHeader (SCRUM-425): the title row must not change when its markup moves into PEMS.UI
    [Theory]
    [InlineData("en", "Equipment", null, false, "page-header-en-title-only")]
    [InlineData("en", "Equipment", "Contracts", false, "page-header-en-with-section")]
    [InlineData("en", "Dashboard", "dashboard", false, "page-header-en-section-same-as-title")]
    [InlineData("fa", "Equipment", "Contracts", false, "page-header-fa-with-section")]
    [InlineData("en", "Not In The Resource File", null, false, "page-header-en-untranslated-title")]
    [InlineData("en", "", "Contracts", false, "page-header-en-empty-title")]
    [InlineData("en", "Equipment", "Contracts", true, "page-header-en-hidden")]
    public async Task PageHeader_RendersTheSameMarkup(string culture, string title, string? section, bool hide, string golden) =>
        AssertGolden(golden, await InCultureAsync(culture, () => RenderAsync("~/Views/Shared/_PageHeader.cshtml", null, vd =>
        {
            vd["Title"] = title;
            if (section is not null) vd["Section"] = section;
            if (hide) vd["HidePageTitle"] = true;
        })));

    [Fact]
    public async Task Pagination_PersianShowsPersianDigitsInTextButLinksStayLatin()
    {
        var html = System.Net.WebUtility.HtmlDecode(await InCultureAsync("fa", () => RenderAsync("~/Views/Shared/_Pagination.cshtml", Pager(3, 10, 250, "wo"))));
        Assert.Contains("۵۱ تا ۷۵ از ۲۵۰", html, StringComparison.Ordinal);                       // range text
        Assert.Contains(">۳</a>", html, StringComparison.Ordinal);                               // page number
        Assert.Contains(">۵۰</a>", html, StringComparison.Ordinal);                              // page size button
        Assert.Contains("href=\"/WorkOrders?page=4\"", html, StringComparison.Ordinal);          // links: Latin, so the page parameter still binds
        Assert.Contains("href=\"/WorkOrders?pageSize=50&page=1\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"50\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("href=\"[^\"]*[۰-۹]", html);
        var en = await InCultureAsync("en", () => RenderAsync("~/Views/Shared/_Pagination.cshtml", Pager(3, 10, 250, "wo")));
        Assert.DoesNotMatch("[۰-۹]", en);                                                        // English: no Persian digit anywhere
    }
}
