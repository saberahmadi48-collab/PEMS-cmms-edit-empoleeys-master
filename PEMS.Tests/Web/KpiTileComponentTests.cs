using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 KpiTile: one number with its label (<c>pems-kpi-tile</c>), the same kpi-card markup as the <c>_KpiCard</c> partial,
/// and the first pages built from it (wave K1: the six report pages).
/// </summary>
public class KpiTileComponentTests
{
    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    private static async Task<string> Run(PemsKpiTileTagHelper helper, string childHtml = "")
    {
        var context = new TagHelperContext("pems-kpi-tile", new TagHelperAttributeList(), new Dictionary<object, object>(), "u1");
        var output = new TagHelperOutput("pems-kpi-tile", new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(childHtml)));
        helper.Init(context);
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task Tile_RendersTheKpiCardMarkup()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Open", Icon = "bi-tools", Color = "green" }, "12");
        Assert.Equal("<div class=\"kpi-card\"><div class=\"kpi-icon bg-icon-green\"><i aria-hidden=\"true\" class=\"bi bi-tools\"></i></div>"
                     + "<div><div class=\"kpi-value\">12</div><div class=\"kpi-label\">Open</div></div></div>", html);
    }

    [Fact]
    public async Task Tile_WithHref_IsALinkWithItsHint()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Open", Icon = "bi-tools", Href = "/WorkOrder?status=open", Hint = "Not yet closed" }, "3");
        Assert.StartsWith("<a class=\"kpi-card kpi-link text-decoration-none\" href=\"/WorkOrder?status=open\" title=\"Not yet closed\">", html);
        Assert.EndsWith("</a>", html);
    }

    [Fact]
    public async Task Tile_EmptyValueShowsADash_NoIconNoSquare_AndASubLine()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Held runs", Sub = "since Monday" });
        Assert.Contains("<div class=\"kpi-value\">-</div>", html);
        Assert.DoesNotContain("kpi-icon", html);
        Assert.Contains("<div class=\"kpi-sub\">since Monday</div>", html);
    }

    [Theory]
    [InlineData(null, "bg-icon-blue")]
    [InlineData("amber", "bg-icon-amber")]
    [InlineData("bg-icon-teal", "bg-icon-teal")]
    public void Color_BecomesTheIconClass(string? color, string expected) => Assert.Equal(expected, PemsKpiTileTagHelper.ColorClass(color));

    [Fact]
    public async Task Tile_EncodesItsLabel_AndKeepsTheValueMarkup()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "R&D <x>" }, "42 <small>%</small>");
        Assert.Contains("<div class=\"kpi-label\">R&amp;D &lt;x&gt;</div>", html);
        Assert.Contains("<div class=\"kpi-value\">42 <small>%</small></div>", html);
    }

    // ---- K2: the tile built in code (the retired _KpiCard partial's view model)
    [Fact]
    public async Task Model_FillsTheTile_LikeTheRetiredPartial()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer)
        {
            Model = new PEMS.Web.Models.KpiCardViewModel
            {
                Title = "Open work orders", Count = 1234, IconClass = "bi-tools", ColorClass = "bg-icon-green",
                Url = "/WorkOrders?status=open", Hint = "Work orders not yet closed"
            }
        });
        Assert.Equal("<a class=\"kpi-card kpi-link text-decoration-none\" href=\"/WorkOrders?status=open\" title=\"Work orders not yet closed\">"
                     + "<div class=\"kpi-icon bg-icon-green\"><i aria-hidden=\"true\" class=\"bi bi-tools\"></i></div>"
                     + "<div><div class=\"kpi-value\">1,234</div><div class=\"kpi-label\">Open work orders</div></div></a>", html);
    }

    [Fact]
    public async Task Model_ZeroCountShowsADash_DefaultsApply_AndTagAttributesWin()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Model = new PEMS.Web.Models.KpiCardViewModel { Title = "Held runs", Count = 0 } });
        Assert.StartsWith("<div class=\"kpi-card\"><div class=\"kpi-icon bg-icon-blue\"><i aria-hidden=\"true\" class=\"bi bi-card-list\"></i></div>", html);
        Assert.Contains("<div class=\"kpi-value\">-</div>", html);

        var own = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Mine", Color = "red", Model = new PEMS.Web.Models.KpiCardViewModel { Title = "Theirs", Count = 2 } });
        Assert.Contains("<div class=\"kpi-label\">Mine</div>", own);
        Assert.Contains("bg-icon-red", own);
    }

    [Theory]
    [InlineData("Views/EngineeringDocumentIntelligence/Index.cshtml", 4)]
    [InlineData("Views/OCRProcessing/Index.cshtml", 4)]
    [InlineData("Views/Production/Dashboard.cshtml", 1)]
    [InlineData("Areas/Warehouse/Views/PartCatalogueReport/Index.cshtml", 6)]
    public void WaveK2_TakesItsTilesFromTheComponent_AndThePartialIsGone(string view, int tags)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(tags, Regex.Matches(text, "<pems-kpi-tile[ >]").Count);
        Assert.DoesNotContain("kpi-card", text);
        Assert.DoesNotContain("_KpiCard", text);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "PEMS.UI", "Views", "Shared", "_KpiCard.cshtml")));
    }

    // ---- K3: small tiles on detail pages
    [Fact]
    public async Task Compact_AddsItsClass_AndToneColoursTheValue()
    {
        var html = await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Difference", Compact = true, Tone = "danger" }, "1.500");
        Assert.StartsWith("<div class=\"kpi-card kpi-card--compact\"><div><div class=\"kpi-value text-danger\">1.500</div>", html);
        Assert.Contains("<div class=\"kpi-value\">0</div>", await Run(new PemsKpiTileTagHelper(Renderer) { Label = "Difference", Tone = "" }, "0"));
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "css", "admin.css"));
        Assert.Contains(".kpi-card--compact .kpi-value { font-size: 18px; }", css);
    }

    [Theory]
    [InlineData("Views/Asset/Details.cshtml", 4)]
    [InlineData("Views/Reliability/Details.cshtml", 4)]
    [InlineData("Views/ProductionRun/Details.cshtml", 7)]
    public void WaveK3_DetailPages_UseCompactTiles(string view, int tiles)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(tiles, Regex.Matches(text, "<pems-kpi-tile compact ").Count);
        Assert.DoesNotContain("fs-5 fw-bold", text);
        Assert.DoesNotMatch(@"<div class=""(small )?text-muted( small)?"">[^<]*</div><div class=""(fw-semibold|fs-5)", text);
    }

    // ---- K4: dashboards with their own large-number tiles
    [Theory]
    [InlineData("Areas/Warehouse/Views/Inventory/Index.cshtml", 6)]
    [InlineData("Views/DCC/Index.cshtml", 2)]
    [InlineData("Views/EquipmentClassification/Index.cshtml", 5)]
    [InlineData("Views/QC/Index.cshtml", 2)]
    [InlineData("Views/QC/Kpi.cshtml", 3)]
    [InlineData("Views/Relationship/GapReport.cshtml", 4)]
    [InlineData("Views/Reliability/CBMDashboard.cshtml", 4)]
    [InlineData("Views/Reliability/DataQuality.cshtml", 9)]
    [InlineData("Views/Reliability/Simulate.cshtml", 4)]
    public void WaveK4_Dashboards_TakeTheirTilesFromTheComponent(string view, int tiles)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(tiles, Regex.Matches(text, "<pems-kpi-tile ").Count);
        // a large bold number over a muted label is a tile; the DCC navigation card (an icon, no number) is the one left
        var left = Regex.Matches(text, @"<div class=""fs-[345] fw-bold[^""]*"">(?!<i |<pems-icon )").Count;
        Assert.Equal(0, left);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
