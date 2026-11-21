using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-542: at 375 px the top-bar action group with the contextual Help button was 55 px wider than the window, so the
/// page scrolled sideways and its title and pager were cut off. Below 576 px the top bar uses less padding, smaller gaps
/// and an icon-only Help button. The layout itself is checked in the browser (QA checklist).
/// </summary>
public class TopbarPhoneWidthTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    private static string PhoneBlock()
    {
        var css = Read("PEMS.Web", "wwwroot", "css", "admin.css");
        var start = css.IndexOf("@media (max-width: 575.98px) {", css.IndexOf("SCRUM-542", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(start >= 0, "the SCRUM-542 phone-width block is missing from admin.css");
        var depth = 0;
        for (var i = start; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[start..(i + 1)];
        }
        return css[start..];
    }

    [Fact]
    public void BelowSmall_TheTopBarUsesLessPaddingAndSmallerGaps()
    {
        var block = PhoneBlock();
        Assert.Matches(@"\.pems-topbar\s*\{\s*padding:\s*0 10px;", block);
        Assert.Matches(@"\.pems-topbar > \.d-flex\.gap-3\s*\{\s*gap:\s*\.5rem !important;", block);
    }

    [Fact]
    public void BelowSmall_TheHelpButtonShowsItsIconOnly()
    {
        var block = PhoneBlock();
        Assert.Matches(@"\.pems-topbar \.pems-contextual-help-btn\s*\{\s*font-size:\s*0;", block);
        Assert.Matches(@"\.pems-contextual-help-btn \.bi\s*\{\s*font-size:\s*1rem;", block);
    }

    [Fact]
    public void TheActionGroup_IsTheFlexRowTheRuleTargets()
        => Assert.Contains("<pems-flex align=\"center\" gap=\"3\">", Read("PEMS.Web", "Views", "Shared", "_Layout.cshtml"));
}
