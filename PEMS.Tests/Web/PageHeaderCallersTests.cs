using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-425: pages that set a title but showed no heading at all now show the shared title row (_PageHeader).
/// The layout still does not render it (GlobalPageHeaderTests); each page calls it itself.
/// SCRUM-337 W2: Finding/Index left this list; its heading now comes from the list-page toolbar (CmmsListPagesTests).
/// </summary>
public class PageHeaderCallersTests
{
    public static readonly string[] Callers =
    [
        "CriticalityAssessment/Create.cshtml"
    ];

    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;

    [Fact]
    public void EachCallerPage_CallsThePartial_OnceAndDoesNotHideTheTitle()
    {
        foreach (var rel in Callers)
        {
            var t = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", rel.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(t, "_PageHeader").Count);
            Assert.DoesNotContain("HidePageTitle", t);
        }
    }

}
