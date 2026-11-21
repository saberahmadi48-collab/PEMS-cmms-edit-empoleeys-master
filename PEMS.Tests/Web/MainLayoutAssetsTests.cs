namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-526, SCRUM-527: a merge (97e11c85) dropped three lines from the main layout without any test failing.
/// The stage diagrams lost their stylesheet, and every data-pems action (the notification bell, Back, print, check-all) lost its script.
/// Every layout that renders pages with these features must load them.
/// </summary>
public class MainLayoutAssetsTests
{
    [Theory]
    [InlineData("_Layout.cshtml", "~/css/pems-stage-diagram.css")]
    [InlineData("_Layout.cshtml", "~/js/pems-actions.js")]
    [InlineData("_Layout.cshtml", "<pems-script-templates set=\"shell\">")]
    [InlineData("_LayoutEmbed.cshtml", "~/js/pems-actions.js")]
    [InlineData("_LayoutEmbed.cshtml", "<pems-script-templates set=\"shell\">")]
    [InlineData("_AuthLayout.cshtml", "~/js/pems-actions.js")]
    public void Layout_LoadsWhatItsPagesUse(string layout, string asset) =>
        Assert.Contains(asset, File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", layout)));

    [Fact]
    public void TheNotificationBell_RunsThroughTheActionsScript()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("data-pems-click=\"call\" data-pems-fn=\"loadNotifDropdown\"", layout);
        // the actions script must come before the page scripts that rely on it
        Assert.True(layout.IndexOf("~/js/pems-actions.js", StringComparison.Ordinal) < layout.IndexOf("@await RenderSectionAsync(\"Scripts\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("_Layout.cshtml", "~/_content/PEMS.UI/pems-errors.js", "~/js/pems-sidebar-search.js", "~/_content/PEMS.UI/pems-command-palette.js", "~/js/pems-template.js")]
    [InlineData("_LayoutEmbed.cshtml", "~/_content/PEMS.UI/pems-errors.js", "~/_content/PEMS.UI/pems-confirm.js", "~/js/pems-template.js")]
    public void ScriptTemplates_ComeBeforeTheScriptsThatCloneThemAtStartUp(string layout, params string[] scripts)
    {
        // SCRUM-536: the shell set came after pems-sidebar-search.js, whose auto-start threw "no <template sb-search>" on every page
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", layout));
        var lastTemplate = text.LastIndexOf("<pems-script-templates", StringComparison.Ordinal);
        Assert.True(lastTemplate > 0);
        Assert.True(lastTemplate < text.IndexOf("<script", text.IndexOf("RenderBody()", StringComparison.Ordinal), StringComparison.Ordinal), "a script comes before the templates");
        foreach (var script in scripts)
        {
            var at = text.IndexOf(script, StringComparison.Ordinal);
            Assert.True(at > lastTemplate, script + " must come after the script templates");
        }
    }

    [Fact]
    public void AuthLayout_LoadsTheTemplateHelper_BeforeThePageScripts()
    {
        // SCRUM-536: account-login.js calls PemsTemplate.clone('login-busy'); _AuthLayout did not load pems-template.js
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Views", "Shared", "_AuthLayout.cshtml"));
        var helper = text.IndexOf("~/js/pems-template.js", StringComparison.Ordinal);
        Assert.True(helper > 0);
        Assert.True(helper < text.IndexOf("RenderSectionAsync(\"Scripts\"", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
