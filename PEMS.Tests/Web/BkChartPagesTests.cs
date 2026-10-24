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
    public async Task Partial_RendersTheHost_WithEncodedJson_AndAnAccessibleTable()
    {
        var spec = BkChartSpec.HBars("Docs by <b>discipline</b>", "none", new[] { "Mech & Elec", "\"Quoted\"" }, false, BkSeries.Of("Docs", new[] { 4, 2 }));

        var html = await Render("~/Views/Shared/_BkChart.cshtml", spec);

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

    private static readonly string[] ReportViews = { "Engineering", "Index", "Cmms", "Procurement", "Vdr" };

    [Fact]
    public void ReportPages_NoLongerShipChartJs_AndLoadTheBklitBundle()
    {
        foreach (var name in ReportViews)
        {
            var source = Read("PEMS.Web", "Views", "Report", name + ".cshtml");
            Assert.DoesNotContain("<canvas", source);
            Assert.DoesNotContain("new Chart(", source);
            Assert.DoesNotContain("chartjs", source);
            Assert.Contains("<partial name=\"_BkChart\"", source);
            Assert.Contains("_BkChartStyles", source);
            Assert.Contains("_BkChartScripts", source);
        }
        var kpi = Read("PEMS.Web", "Views", "Kpi", "Index.cshtml");
        Assert.Contains("_BkChartScripts", kpi);
        Assert.DoesNotContain("<canvas", kpi);
    }

    [Fact]
    public async Task EngineeringReport_RendersThreeCharts_WithFoldedDisciplines()
    {
        var model = new EngineeringReportDto
        {
            TotalDocuments = 40,
            VolumeByMonth = { ("2026-01", 5, 2), ("2026-02", 7, 3) },
            ByStatus = { ("Draft", 30), ("Approved", 10) },
            ByDiscipline = Enumerable.Range(1, 25).Select(i => ($"Disc {i}", i)).ToList()
        };

        var html = await Render("~/Views/Report/Engineering.cshtml", model);
        var specs = Specs(html);

        Assert.Equal(new[] { "bar", "donut", "hbar" }, specs.Select(s => s.GetProperty("kind").GetString()));
        var volume = specs[0];
        Assert.Equal(new[] { "Created", "Issued" }, volume.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var discipline = specs[2];
        Assert.Equal(11, discipline.GetProperty("labels").GetArrayLength());   // top 10 + Other
        Assert.Equal("Other", discipline.GetProperty("labels")[10].GetString());
        Assert.DoesNotContain("<canvas", html);
    }

    [Fact]
    public async Task OtherReportPages_RenderTheirCharts_FromTheirOwnData()
    {
        var corr = await Render("~/Views/Report/Index.cshtml", new CorrespondenceReportDto
        {
            VolumeByMonth = { ("2026-03", 2, 1) }, ByStatus = { ("Sent", 3) }, ByDiscipline = { ("Process", 3) }
        });
        Assert.Equal(new[] { "bar", "donut", "hbar" }, Specs(corr).Select(s => s.GetProperty("kind").GetString()));

        var cmms = await Render("~/Views/Report/Cmms.cshtml", new CmmsReportDto
        {
            VolumeByMonth = { ("2026-03", 4) }, ByPriority = { ("Critical", 1), ("Low", 2) }, ByType = { ("Preventive", 3) }
        });
        var cmmsSpecs = Specs(cmms);
        Assert.Equal(3, cmmsSpecs.Count);
        var colors = cmmsSpecs[1].GetProperty("colors").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains("negative", colors[0]);   // Critical is red whatever order the rows arrive in
        Assert.Contains("neutral", colors[1]);    // Low is grey

        var vdr = await Render("~/Views/Report/Vdr.cshtml", new VDRReportDto
        {
            SubmissionsByMonth = { ("2026-03", 6) }, ByWorkflowStatus = { ("Closed", 5) }, ByReviewCategory = { ("A", 2) }
        });
        Assert.Equal(3, Specs(vdr).Count);

        var proc = await Render("~/Views/Report/Procurement.cshtml", new ProcurementReportDto
        {
            VolumeByMonth = { ("2026-03", 1) }, ByStatus = { ("Open", 1) }, ByDiscipline = { ("Piping", 1) }
        }, c => c.ViewBag.Pipeline = new PagedResult<ProcurementPipelineRowDto> { Items = new List<ProcurementPipelineRowDto>(), Page = 1, PageSize = 25, TotalCount = 0 });
        Assert.Equal(3, Specs(proc).Count);
    }

    [Fact]
    public async Task ReportPages_WithNoData_ShowTheEmptyTextInTheSpec_NotABrokenChart()
    {
        var html = await Render("~/Views/Report/Engineering.cshtml", new EngineeringReportDto());

        var specs = Specs(html);
        Assert.Equal(3, specs.Count);
        Assert.All(specs, s => Assert.Equal("No data for this period", s.GetProperty("emptyText").GetString()));
        Assert.All(specs, s => Assert.Equal(0, s.GetProperty("labels").GetArrayLength()));
    }

    // ------------------------------------------------------------------ KPI page

    [Fact]
    public async Task KpiPage_ShowsCostBreakdownGaugesAndWorkMix_AsCharts_AndKeepsTheLinkedEquipmentTable()
    {
        var model = new KpiDashboardDto
        {
            Cost = new CostKpiDto
            {
                TotalCost = 1000, LaborCost = 600, MaterialCost = 300, ContractorCost = 100,
                TopEquipmentByCost = { new CostByEquipmentItem { EquipmentId = 9, TagNumber = "P-101", Cost = 700 } },
                CostByFunctionalLocation = Enumerable.Range(1, 14).Select(i => new CostByGroupItem { GroupName = "Loc " + i, Cost = i * 10 }).ToList(),
                CostByMaintenanceType = { new CostByGroupItem { GroupName = "Preventive", Cost = 400 } }
            },
            Maintenance = new MaintenanceKpiDto { PmCompliancePercent = 82.5, WorkOrderCompletionRatePercent = null, PlannedPercent = 70, UnplannedPercent = 20, EmergencyPercent = 10 },
            Reliability = new ReliabilitySummaryDto { AverageAvailabilityPercent = 98.2 }
        };

        var html = await Render("~/Views/Kpi/Index.cshtml", model);
        var specs = Specs(html);

        Assert.Equal(new[] { "donut", "hbar", "hbar", "donut", "gauge", "gauge", "donut", "gauge" }, specs.Select(s => s.GetProperty("kind").GetString()));
        Assert.Equal(new[] { 600.0, 300.0, 100.0 }, specs[0].GetProperty("series")[0].GetProperty("values").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal(82.5, specs[4].GetProperty("value").GetDouble());
        Assert.False(specs[5].TryGetProperty("value", out _));   // unknown completion rate is "no data", never a fake 0%
        Assert.Equal(10, specs[2].GetProperty("labels").GetArrayLength());   // locations capped at the ten biggest
        Assert.Equal("Loc 14", specs[2].GetProperty("labels")[0].GetString());
        Assert.Contains("P-101", html);                                      // the drill-down table with equipment links remains
        Assert.DoesNotContain("<canvas", html);
    }

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
            .SelectMany(v => Regex.Matches(Read("PEMS.Web", "Views", v.Split('/')[0], v.Split('/')[1] + ".cshtml"), "Localizer\\[\"(Chart_[A-Za-z]+)\"\\]").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, k => Assert.True(fa.ContainsKey(k), k));
    }

    [Fact]
    public void TheFrontendIslandCode_ReadsEveryKindTheServerCanEmit()
    {
        var island = Read("frontend", "dashboards", "src", "islands", "ChartIsland.tsx");
        foreach (var kind in new[] { "bar", "hbar", "gauge", "donut" })
            Assert.Contains($"case \"{kind}\"", island);

        var serverKinds = Regex.Matches(Read("PEMS.Web", "Models", "BkChartSpec.cs"), "Kind = \"([a-z]+)\"").Select(m => m.Groups[1].Value).Distinct();
        Assert.All(serverKinds, k => Assert.Contains($"case \"{k}\"", island));
    }
}
