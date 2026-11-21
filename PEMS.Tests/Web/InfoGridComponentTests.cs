using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 InfoGrid: the label / value block of a detail page (<c>pems-info-grid</c> / <c>pems-info-item</c>) and the
/// first pages built from it (wave I1: user, engineering document, equipment, VDR and vendor details).
/// </summary>
public class InfoGridComponentTests
{
    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    private static async Task<string> Run(TagHelper helper, string tag, string childHtml = "", TagHelperAttributeList? attributes = null)
    {
        var context = new TagHelperContext(tag, attributes ?? new TagHelperAttributeList(), new Dictionary<object, object>(), "u1");
        var output = new TagHelperOutput(tag, attributes ?? new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(childHtml)));
        helper.Init(context);
        await helper.ProcessAsync(context, output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task Grid_IsADescriptionList_AndKeepsThePagesClass()
    {
        var html = await Run(new PemsInfoGridTagHelper(Renderer), "pems-info-grid", "<div>x</div>", new TagHelperAttributeList { { "class", "mt-2" } });
        Assert.StartsWith("<dl class=\"pems-info mt-2\">", html);
        Assert.EndsWith("</dl>", html);
    }

    [Fact]
    public async Task Item_PairsTheLabelWithItsValue()
    {
        var html = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Vendor code" }, "pems-info-item", "<a href=\"/v/1\">V-1</a>");
        Assert.Equal("<div class=\"pems-info__item\"><dt class=\"pems-info__label\">Vendor code</dt><dd class=\"pems-info__value\"><a href=\"/v/1\">V-1</a></dd></div>", html);
    }

    [Fact]
    public async Task Item_ShowsADash_WhenTheValueIsEmpty()
    {
        Assert.Contains("<dd class=\"pems-info__value\">-</dd>", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Country" }, "pems-info-item", "  "));
        Assert.Contains("<dd class=\"pems-info__value\">N/A</dd>", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Country", EmptyText = "N/A" }, "pems-info-item"));
    }

    [Fact]
    public async Task Item_Options_Wide_Num_Mono_Ltr_Tone()
    {
        var wide = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Description", Wide = true }, "pems-info-item", "text");
        Assert.StartsWith("<div class=\"pems-info__item pems-info__item--wide\">", wide);

        // numbers and phone numbers run left to right inside the value, so the value stays at the start edge of a Persian page
        var num = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Revision", Num = true }, "pems-info-item", "B2");
        Assert.Contains("<dd class=\"pems-info__value\"><span class=\"pems-info__num\" dir=\"ltr\">B2</span></dd>", num);
        var ltr = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Phone", Ltr = true }, "pems-info-item", "+98 21 1234");
        Assert.Contains("<dd class=\"pems-info__value\"><span dir=\"ltr\">+98 21 1234</span></dd>", ltr);

        Assert.Contains("class=\"pems-info__value pems-info__value--mono\"", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Code", Mono = true }, "pems-info-item", "C"));
        Assert.Contains("class=\"pems-info__value text-danger\"", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Overdue", Tone = "danger" }, "pems-info-item", "3"));
        Assert.Contains("<dd class=\"pems-info__value\">0</dd>", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Overdue", Tone = "" }, "pems-info-item", "0"));
        Assert.Contains("<dd class=\"pems-info__value\" id=\"permInfo\">", await Run(new PemsInfoItemTagHelper(Renderer) { Label = "P", ValueId = "permInfo" }, "pems-info-item", "1"));
    }

    [Fact]
    public async Task Grid_RowsLayout_PutsEachLabelBesideItsValue()
    {
        var html = await Run(new PemsInfoGridTagHelper(Renderer) { Layout = "rows" }, "pems-info-grid", "", new TagHelperAttributeList { { "class", "small" } });
        Assert.StartsWith("<dl class=\"pems-info pems-info--rows small\">", html);
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-ui.css"));
        Assert.Contains(".pems-info--rows .pems-info__item { display: contents; }", css);
    }

    [Fact]
    public async Task Item_KeepsThePagesValueClass()
    {
        var html = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "Full text", ValueClass = "pc-fft-view" }, "pems-info-item", "a\nb");
        Assert.Contains("<dd class=\"pems-info__value pc-fft-view\">", html);
    }

    [Fact]
    public async Task Item_EncodesItsLabel()
    {
        var html = await Run(new PemsInfoItemTagHelper(Renderer) { Label = "P&ID <x>" }, "pems-info-item", "1");
        Assert.Contains("<dt class=\"pems-info__label\">P&amp;ID &lt;x&gt;</dt>", html);
    }

    [Fact]
    public void Stylesheet_HasTheGrid_InTheGlobalComponentFile()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-ui.css"));
        Assert.Contains(".pems-info {", css);
        Assert.Contains(".pems-info__item--wide { grid-column: 1 / -1; }", css);
        Assert.Contains(".pems-info__num {", css);
    }

    [Theory]
    [InlineData("Views/EngineeringDocuments/Details.cshtml", 2, 15)]
    [InlineData("Views/Equipment/Details.cshtml", 1, 7)]
    [InlineData("Views/VDR/Details.cshtml", 2, 10)]   // SCRUM-529: + the vendor-format number
    public void WaveI1_TakesItsLabelValueBlocksFromTheComponent(string view, int grids, int items)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(grids, Regex.Matches(text, "<pems-info-grid[ >]").Count);
        Assert.Equal(items, Regex.Matches(text, "<pems-info-item ").Count);
        Assert.DoesNotContain("vs-info", text);
    }

    [Theory]
    [InlineData("Views/Task/Details.cshtml", 1)]
    public void WaveI2_TheDescriptionListPagesUseTheRowsLayout(string view, int grids)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(grids, Regex.Matches(text, "<pems-info-grid layout=\"rows\"").Count);
        Assert.DoesNotContain("<dl", text);
        Assert.DoesNotContain("<dt", text);
    }

    [Theory]
    [InlineData("Views/Contract/Details.cshtml", 2, 12)]
    [InlineData("Views/Tender/Details.cshtml", 1, 7)]
    public void WaveI3_ProcurementHeaders_TakeTheirLabelsFromTheComponent(string view, int grids, int items)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(grids, Regex.Matches(text, "<pems-info-grid[ >]").Count);
        Assert.Equal(items, Regex.Matches(text, "<pems-info-item ").Count);
        // no "Label:" in bold before a value, no muted label over a value, no label key carrying its own colon
        Assert.DoesNotMatch(@"<strong>[^<]*:</strong>\s*@Model", text);
        Assert.DoesNotMatch(@"class=""col-md-3""><div class=""text-muted small"">", text);
        Assert.DoesNotMatch(@"label=""@Localizer\[""[^""]*:""\]""", text);
    }

    [Theory]
    [InlineData("Views/JobPlan/Details.cshtml", 1, 4)]
    [InlineData("Views/MaintenanceStrategy/Details.cshtml", 1, 18)]
    [InlineData("Views/Transmittal/Details.cshtml", 1, 6)]
    [InlineData("Views/Permit/Details.cshtml", 1, 9)]
    [InlineData("Views/WorkOrder/Details.cshtml", 1, 7)]
    [InlineData("Views/WorkRequest/Details.cshtml", 1, 12)]
    public void WaveI4_CmmsAndDocumentHeaders_TakeTheirLabelsFromTheComponent(string view, int grids, int items)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", view));
        Assert.Equal(grids, Regex.Matches(text, "<pems-info-grid[ >]").Count);
        Assert.Equal(items, Regex.Matches(text, "<pems-info-item ").Count);
        Assert.DoesNotMatch(@"class=""col-[\w-]+""><strong>[^<]*:</strong>", text);
        Assert.DoesNotMatch(@"class=""col-[\w-]+""><div class=""text-muted"">", text);
        Assert.DoesNotMatch(@"label=""@Localizer\[""[^""]*:""\]""", text);
    }

    [Fact]
    public void WaveI4_NewLabels_AreTranslated()
    {
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Resources", "SharedResource.resx"));
        var fa = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Resources", "SharedResource.fa.resx"));
        foreach (var key in new[] { "Info_Asset", "Info_ReportedCondition", "Info_SuggestedDiscipline", "Info_ResponseDue", "Info_ResolutionDue",
                     "Info_SafetyControls", "Info_PermitIssuer", "Info_PermitReceiver", "Info_ValidFrom", "Info_ValidTo", "Info_Started", "Info_Completed", "Info_FromPlan" })
        {
            Assert.Contains($"<data name=\"{key}\"", en);
            Assert.Contains($"<data name=\"{key}\"", fa);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
