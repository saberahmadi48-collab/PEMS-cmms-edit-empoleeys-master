using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// رندر View واقعی «نمودار سازمانی تعمیرونگهداری» (بدون HTTP/دیتابیس): ساختار موردنیاز JavaScript وجود دارد - خلاصه‌ی
/// اشغال سمت‌ها، فیلتر همه/تکمیل‌شده/خالی، راهنما (Legend) و جستجوی دیسیپلین که روی همان select موجود کار می‌کند.
/// رفتار JavaScript (شمارش/فیلتر/جستجو) جدا در pems-org-chart-logic.js است. Layout اجرا نمی‌شود، پس Scripts در خروجی نیست.
/// </summary>
public class OrganizationChartViewRenderTests
{
    // ViewBag.Disciplines در View با dynamic خوانده می‌شود؛ نوع بی‌نام (internal) بین Assemblyها قابل‌دسترسی نیست
    public sealed class DisciplineItem
    {
        public int DisciplineId { get; set; }
        public string Name { get; set; } = "";
    }

    private static Task<string> RenderAsync(int? selectedDisciplineId = null)
    {
        var host = new PermissionMatrixController(null!, null!, null!);
        host.ViewBag.Disciplines = new List<DisciplineItem>
        {
            new() { DisciplineId = 1, Name = "Electrical" },
            new() { DisciplineId = 2, Name = "Mechanical (Static)" },
            new() { DisciplineId = 3, Name = "ابزار دقیق" }
        };
        host.ViewBag.SelectedDisciplineId = selectedDisciplineId;
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(host.ViewData) };
        return RazorViewRenderer.RenderAsync(host, result, "~/Views/Team/OrganizationChart.cshtml");
    }

    [Fact]
    public async Task Page_Renders_TheStaffingSummary_AndTheVacancyFilter()
    {
        var html = await RenderAsync();

        Assert.Contains("id=\"orgChartSummary\"", html);
        Assert.Matches(new Regex("id=\"orgChartSummary\"[^>]*\\bhidden\\b"), html);   // hidden until a tree is loaded
        foreach (var id in new[] { "orgChartStatTotal", "orgChartStatFilled", "orgChartStatVacant", "orgChartStatOccupancy",
                     "orgChartFilterFilled", "orgChartFilterVacant", "orgChartProgressFilled", "orgChartProgressVacant" })
            Assert.Contains($"id=\"{id}\"", html);

        // exactly three filter buttons, "all" pressed by default
        Assert.Equal(3, Regex.Matches(html, "data-filter=\"(all|filled|vacant)\"").Count);
        Assert.Matches(new Regex("class=\"btn btn-outline-secondary active\" data-filter=\"all\" aria-pressed=\"true\""), html);
        Assert.Contains("data-filter=\"filled\" aria-pressed=\"false\"", html);
        Assert.Contains("data-filter=\"vacant\" aria-pressed=\"false\"", html);
    }

    [Fact]
    public async Task Page_Renders_TheLegend_ForEveryNodeKindWithTheSameAccents()
    {
        var html = await RenderAsync();

        foreach (var kind in new[] { "Group", "Team", "Shift", "Position-Occupied", "Position-Vacant" })
            Assert.Contains($"class=\"pems-org-swatch\" data-kind=\"{kind}\"", html);
        Assert.Equal(5, Regex.Matches(html, "class=\"pems-org-swatch\"").Count);
    }

    [Fact]
    public async Task Page_Renders_TheSearchBox_ForTheDisciplineSelect_AndKeepsTheSelectAsSourceOfTruth()
    {
        var html = await RenderAsync(selectedDisciplineId: 2);

        Assert.Contains("id=\"orgChartDisciplineSearch\"", html);
        Assert.Contains("id=\"orgChartDisciplineFilter\"", html);
        // the existing server-rendered options (and the selected one) are untouched
        Assert.Contains("<option value=\"1\">Electrical</option>", html);
        Assert.Matches(new Regex("<option value=\"2\" selected[^>]*>Mechanical \\(Static\\)</option>"), html);
        // Razor HTML-encodes non-Latin text; compare what the browser sees
        Assert.Contains("<option value=\"3\">ابزار دقیق</option>", System.Net.WebUtility.HtmlDecode(html));
        Assert.Equal(1, Regex.Matches(html, "<option value=\"\">").Count);   // only the "all disciplines" placeholder

        // texts the script needs are passed as data attributes
        Assert.Contains("data-filter-empty-text=", html);
        Assert.Contains("data-no-discipline-text=", html);
        Assert.Contains("data-all-disciplines-text=", html);
    }

    [Fact]
    public async Task Page_KeepsTheExistingChartStatesAndToolbar()
    {
        var html = await RenderAsync();

        foreach (var id in new[] { "orgChartLoading", "orgChartError", "orgChartEmpty", "orgChartViewport", "orgChartCanvas",
                     "orgChartExpandAll", "orgChartCollapseAll", "orgChartZoomIn", "orgChartZoomOut", "orgChartFit", "orgChartRetry" })
            Assert.Contains($"id=\"{id}\"", html);
    }
}
