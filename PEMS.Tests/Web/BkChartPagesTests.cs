using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Components;
using PEMS.UI.Rendering;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>نمودارهای Bklit در صفحه‌های گزارش و KPI نگهداری/هزینه: توصیف نمودار (BkChartSpec)، Partial میزبان، رندر واقعی صفحه‌ها و نبودِ Chart.js.</summary>
public class BkChartPagesTests
{
    private static readonly IPemsComponentRenderer ChartRenderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
    private static string ChartMarkup(BkChartSpec spec) => ChartRenderer.RenderAsync<PemsChart>(new Dictionary<string, object?> { ["Options"] = new ChartOptions { Spec = spec } }).GetAwaiter().GetResult();

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

    private static Controller Host()
    {
        var c = new DCCController(null!, null!, null!, null!, null!).WithTempData();
        c.ViewBag.Projects = new List<object>();
        c.ViewBag.Disciplines = new List<object>();
        return c;
    }

    private static async Task<string> Render(string view, object model, Action<Controller>? tweak = null)
    {
        var c = Host();
        tweak?.Invoke(c);
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, view);
    }

    private static List<JsonElement> Specs(string html)
        => Regex.Matches(html, "data-bk-chart=\"([^\"]*)\"").Select(m => JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).RootElement.Clone()).ToList();

    // ------------------------------------------------------------------ spec + partial

    [Fact]
    public void MonthlyBars_SerializesCamelCase_WithoutNulls()
    {
        var spec = BkChartSpec.MonthlyBars("Letters", "none", new[] { "2026-01", "2026-02" },
            BkSeries.Of("Incoming", new[] { 3, 5 }, BkChartSpec.Positive), BkSeries.Of("Outgoing", new[] { 1, 0 }));

        var json = JsonDocument.Parse(spec.ToJson()).RootElement;

        Assert.Equal("bar", json.GetProperty("kind").GetString());
        Assert.True(json.GetProperty("monthly").GetBoolean());
        Assert.Equal(new[] { "2026-01", "2026-02" }, json.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { 3.0, 5.0 }, json.GetProperty("series")[0].GetProperty("values").EnumerateArray().Select(x => x.GetDouble()));
        Assert.False(json.TryGetProperty("value", out _));        // nulls are omitted
        Assert.False(json.GetProperty("series")[1].TryGetProperty("color", out _));
    }

    [Fact]
    public void TopWithOthers_KeepsTheLargest_AndFoldsTheRestIntoOneRow_WithoutLosingAnything()
    {
        var rows = Enumerable.Range(1, 30).Select(i => ($"D{i}", i)).ToList();

        var top = BkChartSpec.TopWithOthers(rows, 10, "Other");

        Assert.Equal(11, top.Count);
        Assert.Equal("D30", top[0].Label);
        Assert.Equal("Other", top[^1].Label);
        Assert.Equal(rows.Sum(r => r.Item2), top.Sum(r => r.Value));   // totals are preserved
        Assert.Equal(10, BkChartSpec.TopWithOthers(rows.Take(10), 10, "Other").Count);   // nothing to fold: no "Other" row
        Assert.Empty(BkChartSpec.TopWithOthers(Array.Empty<(string, int)>(), 10, "Other"));
    }

    [Fact]
    public void Gauge_HasAValueOrNone_AndDonutCarriesItsColors()
    {
        Assert.Equal(97.5, JsonDocument.Parse(BkChartSpec.Gauge("a", "n", "c", 97.5).ToJson()).RootElement.GetProperty("value").GetDouble());
        Assert.False(JsonDocument.Parse(BkChartSpec.Gauge("a", "n", "c", null).ToJson()).RootElement.TryGetProperty("value", out _));

        var donut = JsonDocument.Parse(BkChartSpec.Donut("a", "n", "Total", new (string, double)[] { ("A", 1), ("B", 2) }, new[] { "red", "blue" }).ToJson()).RootElement;
        Assert.Equal(new[] { "red", "blue" }, donut.GetProperty("colors").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void TagHelper_RendersTheHost_WithEncodedJson_AndAnAccessibleTable()
    {
        var spec = BkChartSpec.HBars("Docs by <b>discipline</b>", "none", new[] { "Mech & Elec", "\"Quoted\"" }, false, BkSeries.Of("Docs", new[] { 4, 2 }));

        var html = ChartMarkup(spec);   // SCRUM-337 C1: <pems-chart> replaces the _BkChart partial

        Assert.Contains("class=\"bk-scope bk-chart", html);
        Assert.DoesNotContain("<b>discipline</b>", html);
        var parsed = Specs(html).Single();
        Assert.Equal("Mech & Elec", parsed.GetProperty("labels")[0].GetString());
        Assert.Equal("\"Quoted\"", parsed.GetProperty("labels")[1].GetString());   // attribute-encoded, round-trips intact
        Assert.Contains("<table class=\"visually-hidden\">", html);
        Assert.Contains("<th scope=\"row\">Mech &amp; Elec</th>", html);
        Assert.Contains("<td>4</td>", html);
    }

    // ------------------------------------------------------------------ report pages

    // ------------------------------------------------------------------ KPI page

    // ------------------------------------------------------------------ strings

    [Fact]
    public void ChartStrings_ExistInBothLanguages_AndEveryPageStringIsTranslated()
    {
        foreach (var file in new[] { "SharedResource.resx", "SharedResource.fa.resx" })
        {
            var map = LoadResx(file);
            Assert.True(map.ContainsKey("Chart_NoData"), file);
            Assert.True(map.ContainsKey("Chart_Other"), file);
        }

        var fa = LoadResx("SharedResource.fa.resx");
        var used = new[] { "Report/Engineering", "Report/Index", "Report/Cmms", "Report/Procurement", "Report/Vdr", "Kpi/Index" }
            .Where(v => System.IO.File.Exists(Path.Combine(RepoRoot(), "PEMS.Web", "Views", v.Split('/')[0], v.Split('/')[1] + ".cshtml")))   // the report pages that became definition pages have no view
            .SelectMany(v => Regex.Matches(Read("PEMS.Web", "Views", v.Split('/')[0], v.Split('/')[1] + ".cshtml"), "Localizer\\[\"(Chart_[A-Za-z]+)\"\\]").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        // the report pages are definition pages now, so there may be no view left that names a chart string
        Assert.All(used, k => Assert.True(fa.ContainsKey(k), k));
    }

    [Fact]
    public void TheFrontendIslandCode_ReadsEveryKindTheServerCanEmit()
    {
        var island = Read("frontend", "dashboards", "src", "islands", "ChartIsland.tsx");
        foreach (var kind in new[] { "bar", "hbar", "gauge", "donut" })
            Assert.Contains($"case \"{kind}\"", island);

        var serverKinds = Regex.Matches(Read("PEMS.UI.Abstractions", "BkChartSpec.cs"), "Kind = \"([a-z]+)\"").Select(m => m.Groups[1].Value).Distinct();
        Assert.All(serverKinds, k => Assert.Contains($"case \"{k}\"", island));
    }
}
