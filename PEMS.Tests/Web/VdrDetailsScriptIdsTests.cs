using System.Text.RegularExpressions;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-560: vdr-details.js read <c>#vdrSummaryDisc</c>, which a markup conversion (SCRUM-508) had dropped, so every VDR details page with
/// the start-review form threw on load ("Something went wrong") and the discipline summary stopped updating. Every element id the script reads
/// must exist in the view, either as <c>id="..."</c> or as a component's <c>value-id="..."</c>.
/// </summary>
public class VdrDetailsScriptIdsTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SupportedReleases.RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Every_id_the_vdr_details_script_reads_exists_in_the_view()
    {
        var script = Read("PEMS.Web", "wwwroot", "js", "views", "vdr-details.js");
        var view = Read("PEMS.Web", "Views", "VDR", "Details.cshtml");

        var ids = Regex.Matches(script, @"getElementById\('([A-Za-z0-9_-]+)'\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("vdrSummaryDisc", ids);
        var missing = ids.Where(id => !Regex.IsMatch(view, $@"\b(?:value-)?id=""{Regex.Escape(id)}""")).ToList();
        Assert.True(missing.Count == 0, "ids read by vdr-details.js but missing in VDR/Details.cshtml: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_discipline_summary_never_throws_when_an_element_is_missing()
    {
        var script = Read("PEMS.Web", "wwwroot", "js", "views", "vdr-details.js");

        Assert.Contains("if (side) side.textContent", script);
        Assert.Contains("if (summary) {", script);
        Assert.DoesNotContain("side.getAttribute('data-none');\n", script.Replace("\r\n", "\n"));
    }
}
