using System.Runtime.CompilerServices;

namespace PEMS.Tests.Web;

/// <summary>
/// قانون سراسری هدر صفحه: هیچ مسیر/URL فنی یا زنجیره‌ی Breadcrumb «Dashboard / Section / Title» به‌عنوان هدر
/// پیش‌فرض رندر نمی‌شود؛ یک ردیف فشرده (عنوان + برچسب ماژول) از یک منبع مشترک (_PageHeader).
/// </summary>
public class GlobalPageHeaderTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;

    [Fact]
    public void Layout_RendersNoPageHeader_AndNoBreadcrumbChain()
    {
        var layout = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.DoesNotContain("_PageHeader", layout);   // the global title row was removed on request: pages use the whole content area
        Assert.DoesNotContain("_Breadcrumb", layout);
        Assert.DoesNotContain("pems-page-title", layout);
        Assert.False(File.Exists(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_Breadcrumb.cshtml")));
    }

    [Fact]
    public void PageHeaderPartial_NeverRendersRouteOrPath()
    {
        var partial = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_PageHeader.cshtml"));
        Assert.DoesNotContain("Request.Path", partial);
        Assert.DoesNotContain("RouteData", partial);
        Assert.DoesNotContain("asp-controller", partial);
        Assert.Contains("HidePageTitle", partial);
    }

    [Fact]
    public void NoView_RendersItsOwnBreadcrumbChainFromRoute()
    {
        var views = Path.Combine(Root(), "PEMS.Web", "Views");
        foreach (var f in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var t = File.ReadAllText(f);
            Assert.False(t.Contains("pems-breadcrumb") || t.Contains("vs-crumbs"), $"{f} renders its own breadcrumb chain");
        }
    }
}
