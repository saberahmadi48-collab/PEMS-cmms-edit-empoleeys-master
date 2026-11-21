using PEMS.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// Static guards for docs/design/PEMS_UI_ENGINEERING_STANDARD.md. Each rule here comes from a defect reproduced in a real browser
/// (docs/audits/PEMS_LEGACY_UI_DEFECT_AUDIT_2026-10-10.md); the browser-level proof is tools/UiAudit. These tests only stop the
/// structural cause from coming back, they do not replace the browser audit.
/// </summary>
public class UiStandardTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Web(params string[] parts) => SourceTree.Text(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));

    // ---------------------------------------------------------------- D1: dropdown menus are never clipped by card/scroll containers

    [Fact]
    public void PemsUi_SetsBootstrapDropdownPopperStrategyToFixed_Once()
    {
        var js = Web("wwwroot", "js", "pems-ui.js");

        Assert.Contains("bs.Dropdown.Default.popperConfig", js);
        Assert.Contains("strategy: 'fixed'", js);
        Assert.Contains("pemsFixed", js);                       // idempotent: never wraps the config twice
    }

    [Fact]
    public void Layout_LoadsBootstrapBundle_BeforePemsUi()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");

        var bootstrap = layout.IndexOf("bootstrap.bundle.min.js", StringComparison.Ordinal);
        var pemsUi = layout.IndexOf("~/js/pems-ui.js", StringComparison.Ordinal);

        Assert.True(bootstrap >= 0 && pemsUi > bootstrap, "pems-ui.js must run after Bootstrap so the dropdown default can be applied");
    }

    [Fact]
    public void Views_DoNotHardCodeDropdownPositioningWorkarounds()
    {
        // per-page boundary/strategy hacks are what the shared default replaces
        var offenders = AllViews().Where(f => Regex.IsMatch(SourceTree.Text(f), @"data-bs-boundary|data-bs-popper-config|data-bs-strategy"))
            .Select(f => Path.GetRelativePath(Root(), f)).ToList();

        Assert.True(offenders.Count == 0, "Use the shared Popper default instead: " + string.Join(", ", offenders));
    }

    // ---------------------------------------------------------------- D2: overlays must not live inside the topbar stacking context

    [Fact]
    public void Layout_RendersHelpDrawerOutsideTheTopbarHeader_AndOnlyTheButtonInside()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        var open = layout.IndexOf("<header class=\"pems-topbar\">", StringComparison.Ordinal);
        var close = layout.IndexOf("</header>", open, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open);
        var header = layout.Substring(open, close - open);

        Assert.DoesNotContain("_ContextualHelpDrawer", header);
        Assert.Contains("_ContextualHelpButton", header);
        Assert.True(layout.IndexOf("_ContextualHelpDrawer", close, StringComparison.Ordinal) > close, "the drawer partial must be rendered after </header>");
    }

    [Fact]
    public void TopbarHeader_ContainsNoOffcanvasOrModalMarkup()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        var open = layout.IndexOf("<header class=\"pems-topbar\">", StringComparison.Ordinal);
        var header = layout.Substring(open, layout.IndexOf("</header>", open, StringComparison.Ordinal) - open);

        Assert.DoesNotMatch(new Regex(@"class=""[^""]*\b(offcanvas|modal)\b"), header);
    }

    [Fact]
    public void HelpDrawerPartial_NoLongerRendersTheTriggerButton()
    {
        var drawer = Web("Views", "Shared", "_ContextualHelpDrawer.cshtml");
        var button = Web("Views", "Shared", "_ContextualHelpButton.cshtml");

        Assert.DoesNotContain("pems-contextual-help-btn", drawer);
        Assert.Contains("data-bs-target=\"#pemsHelpDrawer\"", button);
        Assert.Contains("id=\"pemsHelpDrawer\"", drawer);
    }

    // ---------------------------------------------------------------- D3: content inserted after load is enhanced

    [Fact]
    public void PemsUi_ObservesInsertedContent_WithoutReactingToItsOwnNodes()
    {
        var js = Web("wwwroot", "js", "pems-ui.js");

        Assert.Contains("new MutationObserver", js);
        Assert.Contains("dataset.pemsAuto", js);                // nodes created by pems-ui are ignored (no feedback loop)
        Assert.Contains("setTimeout(flush", js);                // debounced
        Assert.Contains("window.PemsUi = { refresh: run }", js);
    }

    // ---------------------------------------------------------------- D6: tab state survives a POST -> redirect

    [Fact]
    public void PemsUi_PreservesActiveTabsAcrossPostRedirect_SafelyAndBounded()
    {
        var js = Web("wwwroot", "js", "pems-ui.js");

        Assert.Contains("pems.tabs.v1", js);
        Assert.Contains("addEventListener('submit'", js);                       // remembered only when a POST form is submitted
        Assert.Contains("toLowerCase() !== 'post'", js);
        Assert.Contains("Date.now() - s.at > 20000", js);                        // bounded window: no stale restore on later visits
        Assert.Contains("s.path !== location.pathname", js);                     // same page only
        Assert.Contains("location.hash", js);                                    // never overrides an explicit #hash
        Assert.Matches(new Regex(@"try \{[^}]*sessionStorage\.setItem"), js);   // blocked storage must not break the page
        Assert.Matches(new Regex(@"try \{ raw = sessionStorage\.getItem"), js);
    }

    // ---------------------------------------------------------------- D1 side effects with `fixed`: menus must fit every viewport

    [Fact]
    public void WorkspaceCss_CapsDropdownMenusToTheViewport_AndDefinesTheNotificationsMenuWidth()
    {
        var css = Web("wwwroot", "css", "pems-workspace.css");

        Assert.Contains(".dropdown-menu { max-width: calc(100vw - 16px); }", css);
        Assert.Contains("#notifDropdownBody { width: min(360px, calc(100vw - 16px)); }", css);
        Assert.Matches(new Regex(@"@media \(max-width: 767\.98px\)\s*\{\s*\.pems-topbar \.dropdown-menu\.show \{ position: fixed !important;"), css);
    }

    // ---------------------------------------------------------------- D7: topbar must not overflow at tablet-portrait widths

    [Fact]
    public void Topbar_ShowsTheUserNameBlockOnlyFromMd_SoItCannotOverflowAt576To767()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");

        Assert.Contains("d-none d-md-flex flex-column text-start", layout);
        Assert.DoesNotContain("d-none d-sm-flex flex-column text-start", layout);
    }

    // ---------------------------------------------------------------- D4: one <h1> per page

    [Fact]
    public void ViewsThatRenderTheirOwnH1_MustHideTheGlobalPageTitle()
    {
        var offenders = AllViews()
            .Where(f => !Path.GetFileName(f).StartsWith("_") && Regex.IsMatch(SourceTree.Text(f), @"<h1[\s>]"))
            .Where(f => !SourceTree.Text(f).Contains("HidePageTitle"))
            .Select(f => Path.GetRelativePath(Root(), f)).ToList();

        Assert.True(offenders.Count == 0, "A second <h1> duplicates the global page title; use <h2> or set ViewData[\"HidePageTitle\"]: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> AllViews() =>
        SourceTree.Files(Path.Combine(Root(), "PEMS.Web"), "*.cshtml")
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
}
