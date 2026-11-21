using PEMS.UI.Components;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 Chart: <c>&lt;pems-chart spec="..."&gt;</c> is the one chart host (wave C1: the report pages, KPI, letter cartable report
/// and part catalogue reports). The host markup itself is covered by BkChartPagesTests.
/// </summary>
public class ChartComponentTests
{
    private static readonly IPemsComponentRenderer ChartRenderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
    private static string ChartMarkup(BkChartSpec spec) => ChartRenderer.RenderAsync<PemsChart>(new Dictionary<string, object?> { ["Options"] = new ChartOptions { Spec = spec } }).GetAwaiter().GetResult();

    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    private static string Run(BkChartSpec? spec)
    {
        var helper = new PemsChartTagHelper(Renderer) { Spec = spec };
        var context = new TagHelperContext("pems-chart", new TagHelperAttributeList(), new Dictionary<object, object>(), "u1");
        var output = new TagHelperOutput("pems-chart", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var w = new StringWriter();
        output.WriteTo(w, HtmlEncoder.Default);
        return w.ToString();
    }

    [Fact]
    public void Tag_WritesOnlyTheHostAndTheAccessibleCopy()
    {
        var html = Run(BkChartSpec.MonthlyBars("Letters", "none", new[] { "2026-01" }, BkSeries.Of("In", new[] { 3 })));
        Assert.StartsWith("<div class=\"bk-scope bk-chart min-w-0\" data-bk-chart=", html);
        Assert.DoesNotContain("pems-chart", html);
        Assert.Contains("<caption>Letters</caption>", html);
    }

    [Fact]
    public void Gauge_HasAScreenReaderLine_AndNoValueMeansNoLine()
    {
        Assert.Contains("<span class=\"visually-hidden\">PM compliance: 97.5%</span>", Run(BkChartSpec.Gauge("PM compliance", "none", "PM", 97.5, "%", 1)));
        Assert.DoesNotContain("visually-hidden", Run(BkChartSpec.Gauge("PM compliance", "none", "PM", null)));
    }

    [Fact]
    public void NoSpec_WritesNothing() => Assert.Equal("", Run(null));

    [Fact]
    public void Spec_LivesInTheComponentLibrary_AndThePartialIsGone()
    {
        Assert.Equal("PEMS.UI.Abstractions", typeof(BkChartSpec).Assembly.GetName().Name);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_BkChart.cshtml")));
    }

    [Theory]
    [InlineData("Views/LetterCartable/Report.cshtml", 4)]
    [InlineData("Areas/Warehouse/Views/PartCatalogueReport/Index.cshtml", 4)]
    public void WaveC1_PagesTakeTheirChartsFromTheComponent(string view, int charts)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(charts, Regex.Matches(text, "<pems-chart spec=").Count);
        Assert.DoesNotContain("_BkChart\"", text);
    }

    // ---- C2: line charts, Chart.js retired
    [Fact]
    public void Line_LabelsAreInvariantIsoTimes_AndCarryTheDecimals()
    {
        var spec = BkChartSpec.Line("Vibration", "none", new[] { new DateTime(2026, 4, 1, 8, 30, 0), new DateTime(2026, 4, 2, 9, 5, 0) }, 2,
            BkSeries.Of("mm/s", new[] { 1.25m, 0m }));
        Assert.Equal("line", spec.Kind);
        Assert.Equal(new[] { "2026-04-01T08:30", "2026-04-02T09:05" }, spec.Labels);
        Assert.Equal(2, spec.Decimals);
        var html = ChartMarkup(spec);
        Assert.Contains("<th scope=\"row\">2026-04-01T08:30</th>", html);
        Assert.Contains("<td>0</td>", html);   // a zero reading is a value, not a gap
    }

    [Fact]
    public void Island_DrawsTheLineKind_AndZeroReadingsAreNotEmpty()
    {
        var island = File.ReadAllText(Path.Combine(RepoRoot(), "frontend", "dashboards", "src", "islands", "ChartIsland.tsx"));
        Assert.Contains("case \"line\": return <LineView", island);
        Assert.Contains("<LineChart", island);
        Assert.Contains("curve={curveMonotoneX}", island);   // never overshoots between readings
        var spec = File.ReadAllText(Path.Combine(RepoRoot(), "frontend", "dashboards", "src", "islands", "spec.ts"));
        Assert.Contains("\"line\"", spec);
        Assert.Contains("if (spec.kind === \"line\") return spec.labels.length === 0;", spec);
        var bundle = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "dist", "dashboards", "dashboards.js"));
        Assert.Contains("case`line`:", bundle);   // the built (minified) bundle carries it: npm run build in frontend/dashboards
    }

    [Fact]
    public void WaveC2_TrendPage_UsesTheChartHost_AndChartJsIsGone()
    {
        var trend = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Reliability", "Trend.cshtml"));
        Assert.Contains("BkChartSpec.Line(", trend);
        Assert.Contains("<pems-chart spec=\"@trend\" />", trend);
        Assert.Contains("_BkChartScripts", trend);
        Assert.DoesNotContain("<canvas", trend);
        Assert.DoesNotContain("new Chart(", trend);
        Assert.DoesNotContain("chartjs", trend);
        Assert.False(Directory.Exists(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "lib", "chartjs")));
    }

    [Fact]
    public void Donut_WithoutPositiveValues_IsEmpty_AndKeepsItsLabels()
    {
        var spec = BkChartSpec.Donut("Documents by status", "No documents to chart yet.", "Documents", new[] { ("Draft", 3d), ("Approved", 0d) });
        Assert.Equal("donut", spec.Kind);
        Assert.Equal(new[] { "Draft", "Approved" }, spec.Labels);
        Assert.Equal("Documents", spec.CenterLabel);
        Assert.Contains("<th scope=\"row\">Draft</th><td>3</td>", ChartMarkup(spec));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
