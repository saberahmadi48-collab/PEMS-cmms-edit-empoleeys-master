using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI;
using PEMS.UI.Rendering;
using PEMS.UI.TagHelpers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-337 Skeleton: one placeholder markup for Razor (<c>pems-skeleton</c>), for script-filled panels
/// (<c>PemsSkeleton.show</c>) and for the Blazor <c>PemsSkeleton</c>, and the first panels moved to it (wave S1).
/// </summary>
public class SkeletonComponentTests
{
    private static readonly IPemsComponentRenderer Renderer = new ServiceCollection().AddPemsUi().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();

    [Fact]
    public void Markup_IsABusyStatusRegion_WithTheLabelForScreenReaders()
    {
        var html = PemsSkeletonMarkup.Build("text", label: "Loading...");
        Assert.StartsWith("<div class=\"pems-skeleton pems-skeleton--text\" role=\"status\" aria-busy=\"true\" aria-live=\"polite\">", html);
        Assert.Contains("<span class=\"visually-hidden\">Loading...</span>", html);
        Assert.Equal(3, Regex.Matches(html, "class=\"pems-skeleton__line").Count);
        Assert.Contains("pems-skeleton__line pems-skeleton__line--short", html);   // the last line is shorter
        Assert.Equal(3, Regex.Matches(html, "aria-hidden=\"true\"").Count);
    }

    [Theory]
    [InlineData("title", 1)]
    [InlineData("card", 4)]
    [InlineData("kpi", 3)]
    [InlineData("form", 10)]
    [InlineData("unknown", 3)]
    public void Kinds_HaveTheirBars(string kind, int bars) =>
        Assert.Equal(bars, Regex.Matches(PemsSkeletonMarkup.Build(kind), "class=\"pems-skeleton__line").Count);

    [Fact]
    public void Table_HasRowsOfCells_AndCountsAreClamped()
    {
        var html = PemsSkeletonMarkup.Build("table", rows: 2, cols: 3);
        Assert.Equal(2, Regex.Matches(html, "pems-skeleton__row").Count);
        Assert.Equal(6, Regex.Matches(html, "pems-skeleton__line").Count);
        Assert.Equal(30, Regex.Matches(PemsSkeletonMarkup.Build("table", rows: 500, cols: 1), "pems-skeleton__row").Count);
    }

    [Fact]
    public void Label_IsEncoded() =>
        Assert.Contains("<span class=\"visually-hidden\">a &lt;b&gt;</span>", PemsSkeletonMarkup.Build("text", label: "a <b>"));

    [Fact]
    public async Task TagHelper_WritesTheMarkup_AndKeepsItsId()
    {
        var helper = new PemsSkeletonTagHelper(Renderer) { Kind = "kpi", Label = "Loading" };
        var attrs = new TagHelperAttributeList { { "id", "kpiWait" } };
        var context = new TagHelperContext("pems-skeleton", attrs, new Dictionary<object, object>(), "u1");
        var output = new TagHelperOutput("pems-skeleton", attrs, (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.ProcessAsync(context, output).GetAwaiter().GetResult();
        using var w = new StringWriter();
        output.WriteTo(w, HtmlEncoder.Default);
        Assert.StartsWith("<div id=\"kpiWait\" class=\"pems-skeleton pems-skeleton--kpi\"", w.ToString());
        await Task.CompletedTask;
    }

    [Fact]
    public void Script_BuildsTheSameClassesAsTheTagHelper()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-skeleton.js"));
        js += File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "Components", "PemsScriptTemplates.razor"));   // SCRUM-512: the base markup is the script template
        foreach (var cls in new[] { "pems-skeleton\" role", "pems-skeleton--", "pems-skeleton__line--title", "pems-skeleton__line--short", "pems-skeleton__line--square",
                     "pems-skeleton__stack", "pems-skeleton__line--value", "pems-skeleton__row", "pems-skeleton__line--label", "pems-skeleton__line--field",
                     "role=\"status\" aria-busy=\"true\" aria-live=\"polite\"", "visually-hidden", "data-pems-skeleton-label" })
            Assert.Contains(cls, js);
        foreach (var kind in PemsSkeletonMarkup.Kinds) Assert.Contains("'" + kind + "'", js);
    }

    [Fact]
    public void Styles_LiveInTheComponentFile_AndTheOldCopyIsGone()
    {
        var ui = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.UI", "wwwroot", "pems-ui.css"));
        Assert.Contains(".pems-skeleton__row {", ui);
        Assert.Contains("prefers-reduced-motion: no-preference) { .pems-skeleton__line", ui);
        Assert.DoesNotContain(".pems-skeleton", File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "css", "pems-glass.css")));
    }

    [Fact]
    public void Layouts_LoadTheScript_AndGiveItTheLabel()
    {
        foreach (var layout in new[] { "_Layout.cshtml", "_LayoutEmbed.cshtml" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", layout));
            Assert.Contains("_content/PEMS.UI/pems-skeleton.js", text);
            Assert.Contains("data-pems-skeleton-label=\"@Localizer[\"Loading\"]\"", text);
        }
    }

    [Theory]
    [InlineData("wwwroot/js/views/shared-contextualhelpdrawer.js", "PemsSkeleton.show(body, 'card')")]
    [InlineData("wwwroot/js/pems-global-search.js", "PemsSkeleton.show(loading, 'table'")]
    [InlineData("wwwroot/js/letter-compose.js", "PemsSkeleton.show(body, 'text'")]
    [InlineData("wwwroot/js/pems-org-position-chart.js", "PemsSkeleton.show(stateEl, \"card\"")]
    public void WaveS1_PanelsShowTheSkeletonWhileLoading(string file, string call)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", file));
        Assert.Contains(call, text);
        Assert.DoesNotContain("در حال جستجو...", text);
        Assert.DoesNotContain("textContent = T.loading", text);
    }

    [Fact]
    public void WaveS1_HelpDrawer_StartsWithTheSkeleton() =>
        Assert.Contains("<pems-skeleton kind=\"card\"", File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_ContextualHelpDrawer.cshtml")));

    [Theory]
    [InlineData("Views/Team/OrganizationChart.cshtml", "<pems-skeleton kind=\"card\"")]
    [InlineData("wwwroot/js/pems-cartable.js", "PemsSkeleton.show(waitTd, \"table\", { rows: 2, cols: 3 })")]
    [InlineData("wwwroot/js/letter-compose.js", "PemsSkeleton.show(waitLi, 'text', { lines: 3 })")]
    [InlineData("wwwroot/js/pems-part-catalogue-dedup.js", "PemsSkeleton.show(out, 'text'")]
    [InlineData("wwwroot/js/pems-part-catalogue.js", "PemsSkeleton.show(tcLong, 'text'")]
    public void WaveS2_PanelsThatLoadContent_ShowTheSkeleton(string file, string call) =>
        Assert.Contains(call, File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", file)));

    [Fact]
    public void WaveS2_OrgChart_NoLongerUsesASpinnerAsItsPlaceholder()
    {
        var view = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Team", "OrganizationChart.cshtml"));
        var loading = view[view.IndexOf("id=\"orgChartLoading\"", StringComparison.Ordinal)..view.IndexOf("id=\"orgChartError\"", StringComparison.Ordinal)];
        Assert.DoesNotContain("pems-spinner", loading);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
