using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>نمودار حلقه‌ای داشبورد: Partial مشترک (داده در legend)، اتصال به صفحه‌ی Home، و پاکیزگی CSS/JS.</summary>
public class DonutChartTests
{
    private static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    private static string Read(params string[] parts) => System.IO.File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static async Task<string> Render(DonutChartViewModel model)
    {
        var c = new DCCController(null!, null!, null!, null!, null!).WithTempData();
        return await RazorViewRenderer.RenderAsync(c, new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } },
            "~/Views/Shared/_DonutChart.cshtml");
    }

    private static DonutChartViewModel Model(params (string Label, int Value)[] items) => new()
    {
        Id = "d1", DefaultLabel = "Documents", AriaLabel = "Documents by Status", EmptyText = "Nothing yet",
        Items = items.Select(i => new DonutChartItem(i.Label, i.Value)).ToList()
    };

    [Fact]
    public async Task Partial_RendersTheLegendAsTheData_WithRingAndCentreHooks()
    {
        var html = await Render(Model(("Approved", 12), ("Pending", 5), ("Rejected", 0)));

        Assert.Contains("id=\"d1\"", html);
        Assert.Contains("data-default-label=\"Documents\"", html);
        Assert.Contains("aria-label=\"Documents by Status\"", html);
        Assert.Equal(3, Regex.Matches(html, "class=\"pdn-item\"").Count);   // zero rows stay listed; the script skips them in the ring
        Assert.Contains("data-value=\"12\" data-label=\"Approved\"", html);
        Assert.Contains("<g class=\"pdn-slices\">", html);
        Assert.Contains("pdn-center-value", html);
        Assert.Contains("tabindex=\"0\"", html);   // keyboard focus mirrors hover
        Assert.DoesNotContain("Nothing yet", html);
    }

    [Fact]
    public async Task Partial_EncodesLabels()
    {
        var html = await Render(Model(("<img src=x onerror=alert(1)>", 3)));

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Partial_WithNoPositiveValues_ShowsTheEmptyState(int value)
    {
        var html = await Render(Model(("Approved", value)));

        Assert.Contains("pdn-empty", html);
        Assert.Contains("Nothing yet", html);
        Assert.DoesNotContain("class=\"pdn\"", html);

        var none = await Render(Model());
        Assert.Contains("pdn-empty", none);
    }

    [Fact]
    public void HomeDashboard_UsesTheDonutPartial_InsteadOfTheChartJsCanvas()
    {
        var view = Read("PEMS.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains("<partial name=\"_DonutChart\"", view);
        Assert.Contains("pems-donut.js", view);
        Assert.Contains("pems-donut.css", view);
        Assert.DoesNotContain("docsStatusChart", view);
        Assert.DoesNotContain("new Chart(", view);
        Assert.DoesNotContain("chart-4.4.4", view);
    }

    [Fact]
    public void Stylesheet_UsesThemeTokensOnly()
    {
        var css = Read("PEMS.Web", "wwwroot", "css", "pems-donut.css");
        var colours = Regex.Matches(css, "#[0-9a-fA-F]{3,8}\\b").Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();

        Assert.All(colours, c => Assert.Equal("#94a3b8", c));   // the only literal is the neutral fallback of a token
        Assert.Contains("prefers-reduced-motion", css);
    }

    [Fact]
    public void Script_TakesItsColoursFromTheThemeAwarePalette_AndNeverInjectsHtml()
    {
        var js = Read("PEMS.Web", "wwwroot", "js", "pems-donut.js");

        Assert.Contains("PemsChartColors", js);
        Assert.DoesNotContain("innerHTML", js);   // labels are written with textContent / attributes only
    }
}
