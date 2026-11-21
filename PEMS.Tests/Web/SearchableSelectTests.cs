using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-57: searchable selects on every page. The layout loads the vendored Tom Select and the PEMS.UI initialiser once, in order,
/// with the localised texts on &lt;body&gt;; the initialiser keeps the opt-out, the size threshold, the server source through
/// /SearchApi and makes no external call; its styles use the theme tokens. The behaviour itself is checked in the browser (QA).
/// </summary>
public class SearchableSelectTests
{
    private static string Root()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    [Fact]
    public void Layout_loads_tom_select_then_the_initialiser_once_before_the_page_scripts()
    {
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        int Pos(string s) { Assert.Equal(1, Regex.Matches(layout, Regex.Escape(s)).Count); return layout.IndexOf(s, StringComparison.Ordinal); }

        var bootstrap = Pos("~/lib/bootstrap/dist/js/bootstrap.bundle.min.js");
        var tom = Pos("~/lib/tom-select/tom-select.complete.min.js");
        var init = Pos("~/_content/PEMS.UI/pems-select.js");
        var pageScripts = layout.IndexOf("RenderSectionAsync(\"Scripts\"", StringComparison.Ordinal);
        Assert.True(bootstrap < tom && tom < init && init < pageScripts);
        Pos("~/lib/tom-select/tom-select.bootstrap5.min.css");
        Pos("~/_content/PEMS.UI/pems-select.css");
    }

    [Fact]
    public void Texts_are_inputs_from_the_layout_in_both_languages()
    {
        var layout = Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml");
        HashSet<string> Keys(string file) => XDocument.Load(Path.Combine(Root(), "PEMS.Web", "Resources", file))
            .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
        var en = Keys("SharedResource.resx");
        var fa = Keys("SharedResource.fa.resx");
        foreach (var (attr, key) in new[] { ("no-results", "Sel_NoResults"), ("loading", "Sel_Loading"), ("placeholder", "Sel_Placeholder") })
        {
            Assert.Contains($"data-pems-select-{attr}=\"@Localizer[\"{key}\"]\"", layout);
            Assert.Contains(key, en);
            Assert.Contains(key, fa);
        }
        var js = Read("PEMS.UI", "wwwroot", "pems-select.js");
        Assert.DoesNotMatch("[؀-ۿ]", js);   // no Persian literal in the component: texts come from the page
    }

    [Fact]
    public void Initialiser_keeps_the_opt_out_the_threshold_and_the_permission_aware_server_source_and_calls_nothing_outside()
    {
        var js = Read("PEMS.UI", "wwwroot", "pems-select.js");
        Assert.Contains("var MIN_OPTIONS = 8;", js);
        Assert.Contains("flag === 'off'", js);
        Assert.Contains("select.multiple", js);                       // multi-selects keep pems-multiselect.js
        Assert.Contains("'/SearchApi?'", js);                         // same permission-aware search as the global search box
        Assert.Contains("allowEmptyOption: hasBlank", js);            // "-- None --" stays a real choice; a required list gets no blank
        Assert.Contains(".modal, .dropdown-menu", js);                // opens in place inside modals and dropdown panels
        Assert.DoesNotMatch("https?://", js);

        var css = Read("PEMS.UI", "wwwroot", "pems-select.css");
        Assert.Contains("var(--pems-surface", css);
        Assert.DoesNotMatch(@"(margin|padding)-(left|right)\s*:", css);   // logical properties only (RTL and LTR)
    }

    [Fact]
    public void A_large_server_list_is_read_one_page_at_a_time()
    {
        // tools/js-tests/pems-select-paging.test.js (SCRUM-513): virtual_scroll asks for page 2 only when the server says hasMore. Needs Node 18+ on the PATH;
        // PEMS_REQUIRE_NODE=1 fails instead of skipping when it is missing.
        var script = Path.Combine(Root(), "tools", "js-tests", "pems-select-paging.test.js");
        System.Diagnostics.Process? process;
        try { process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "\"" + script + "\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.False(Environment.GetEnvironmentVariable("PEMS_REQUIRE_NODE") == "1", "node is required on this machine");
            return;
        }
        using (process!)
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "the script did not finish");
            Assert.True(process.ExitCode == 0, output);
        }
    }
}
