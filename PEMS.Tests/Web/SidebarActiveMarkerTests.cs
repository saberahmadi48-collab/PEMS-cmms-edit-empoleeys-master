using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-541: the active sidebar link showed an accent bar on both edges: site.css set <c>border-left</c>, admin.css
/// (loaded later) added <c>border-right</c>, and admin.rtl.css / pems-glass.css swapped them for RTL. The marker is now
/// one <c>border-inline-start</c> rule (start edge: left in LTR, right in RTL) and no sidebar link rule uses a physical
/// left or right border.
/// </summary>
public class SidebarActiveMarkerTests
{
    private static string Css(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var css = File.ReadAllText(Path.Combine(dir!.FullName, "PEMS.Web", "wwwroot", "css", file));
        return Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
    }

    private static IEnumerable<(string Selector, string Body)> SidebarLinkRules(string file) =>
        Regex.Matches(Css(file), @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}")
            .Select(m => (Selector: m.Groups["sel"].Value.Trim(), Body: m.Groups["body"].Value))
            .Where(r => Regex.IsMatch(r.Selector, @"\.pems-sidebar[^,]*\.nav-link"));

    [Theory]
    [InlineData("site.css")]
    [InlineData("admin.css")]
    [InlineData("admin.rtl.css")]
    [InlineData("pems-glass.css")]
    public void NoSidebarLinkRule_UsesAPhysicalLeftOrRightBorder(string file)
    {
        var offenders = SidebarLinkRules(file).Where(r => Regex.IsMatch(r.Body, @"border-(left|right)")).Select(r => r.Selector).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheMarker_IsOneStartEdgeRule_InSiteCss()
    {
        var rules = SidebarLinkRules("site.css").ToList();
        Assert.Contains(rules, r => r.Selector == ".pems-sidebar .nav-link" && r.Body.Contains("border-inline-start: 3px solid transparent"));
        Assert.Contains(rules, r => r.Selector == ".pems-sidebar .nav-link.active" && r.Body.Contains("border-inline-start-color: var(--pems-accent)"));
    }

    [Fact]
    public void AdminCss_UsesTheColourToken_NotAHardCodedColour()
    {
        var link = SidebarLinkRules("admin.css").Single(r => r.Selector == ".pems-sidebar .nav-link");
        Assert.Contains("var(--pems-color-cbd6e4)", link.Body);
    }
}
