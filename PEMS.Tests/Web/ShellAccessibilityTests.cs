using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;

namespace PEMS.Tests.Web;

/// <summary>
/// مرور UI/UX - اصلاحات دسترس‌پذیری پوسته: Skip link، توکن‌های رنگ وضعیتِ مناسب متن (WCAG AA 4.5:1) برای هر سه تم،
/// aria-live روی Toast، صفحه‌ی دوستانه‌ی 404/403/500 و کلیدهای Localization مربوطه. کنتراست از مقادیر واقعی site.css
/// با فرمول استاندارد WCAG محاسبه می‌شود (نه یک عدد ثابت در تست).
/// </summary>
public class ShellAccessibilityTests
{
    // ------------------------------------------------------------------ locating repository files

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, relative))) return Path.Combine(root, relative);
        throw new FileNotFoundException("Repository file not found (set PEMS_REPO_ROOT when running from a copied output folder): " + relative);
    }

    private static string Read(string relative) => File.ReadAllText(RepoFile(relative));

    // ------------------------------------------------------------------ contrast

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
        double Channel(int i)
        {
            var c = int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Custom properties of one theme block, with the root block as fallback and var() resolved.</summary>
    private static Dictionary<string, string> ThemeTokens(string css, string selector)
    {
        Dictionary<string, string> Parse(string sel)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match block in Regex.Matches(css, Regex.Escape(sel) + @"\s*\{(?<body>[^}]*)\}"))
                foreach (Match decl in Regex.Matches(block.Groups["body"].Value, @"(?<name>--[\w-]+)\s*:\s*(?<value>[^;]+);"))
                    result[decl.Groups["name"].Value] = decl.Groups["value"].Value.Trim();
            return result;
        }

        var tokens = Parse(":root");
        foreach (var kv in Parse(selector)) tokens[kv.Key] = kv.Value;

        string Resolve(string value, int depth = 0)
        {
            var m = Regex.Match(value, @"^var\((?<n>--[\w-]+)\)$");
            return m.Success && depth < 6 && tokens.TryGetValue(m.Groups["n"].Value, out var next) ? Resolve(next, depth + 1) : value;
        }

        return tokens.ToDictionary(kv => kv.Key, kv => Resolve(kv.Value), StringComparer.Ordinal);
    }

    public static IEnumerable<object[]> Themes() => new[]
    {
        new object[] { "light", ":root" },
        new object[] { "dark", "[data-theme=\"dark\"]" },
        new object[] { "turquoise", "[data-theme=\"turquoise\"]" },
    };

    [Theory]
    [MemberData(nameof(Themes))]
    public void StatusTextTokens_MeetWcagAA_OnEveryBackgroundOfTheTheme(string theme, string selector)
    {
        var tokens = ThemeTokens(Read("PEMS.Web/wwwroot/css/site.css"), selector);

        var backgrounds = new[] { "--pems-surface", "--pems-bg" }
            .Select(n => tokens.TryGetValue(n, out var v) ? v : null)
            .Where(v => v != null && v.StartsWith('#')).Select(v => v!).Distinct().ToList();
        Assert.NotEmpty(backgrounds);

        foreach (var kind in new[] { "positive", "warning", "danger", "info", "caution" })
        {
            Assert.True(tokens.TryGetValue($"--pems-chart-{kind}-text", out var colour), $"{theme}: --pems-chart-{kind}-text is not defined");
            Assert.StartsWith("#", colour);
            foreach (var bg in backgrounds)
                Assert.True(Contrast(colour!, bg) >= 4.5, $"{theme}: {kind} {colour} on {bg} = {Contrast(colour!, bg):0.00}:1 (needs 4.5:1)");
        }
    }

    [Fact]
    public void TheOldFillColours_AreStillBelowAA_WhichIsWhyTextUsesTheNewTokens()
    {
        // documents the measured problem: the fill tokens are fine for bars/graphics but not for text on the light surface
        var tokens = ThemeTokens(Read("PEMS.Web/wwwroot/css/site.css"), ":root");

        Assert.True(Contrast(tokens["--pems-chart-warning"], tokens["--pems-surface"]) < 3.0);
        Assert.True(Contrast(tokens["--pems-chart-positive"], tokens["--pems-surface"]) < 3.0);
    }

    [Theory]
    [InlineData("PEMS.Web/wwwroot/css/pems-kpi-dashboard.css")]
    [InlineData("PEMS.Web/wwwroot/css/pems-org-chart.css")]
    [InlineData("PEMS.Web/wwwroot/css/pems-permission-matrix.css")]
    public void FeatureCss_NeverUsesTheRawFillColoursAsTextColour(string file)
    {
        var offenders = Read(file).Split('\n')
            .Select((line, i) => (line, number: i + 1))
            .Where(x => Regex.IsMatch(x.line, @"(^|[;{\s])color\s*:\s*var\(--pems-chart-(positive|warning|negative)\)"))
            .Select(x => $"{file}:{x.number}: {x.line.Trim()}").ToList();

        Assert.True(offenders.Count == 0, "Raw status fill colour used as text:\n" + string.Join("\n", offenders));
    }

    // ------------------------------------------------------------------ layout

    [Fact]
    public void Layout_HasASkipLinkTargetingTheMainLandmark_AndAnnouncesToasts()
    {
        var layout = Read("PEMS.Web/Views/Shared/_Layout.cshtml");

        Assert.Matches(new Regex(@"<a class=""pems-skip-link"" href=""#pemsMainContent"">"), layout);
        Assert.Contains("<main class=\"pems-content\" id=\"pemsMainContent\" tabindex=\"-1\">", layout);
        Assert.Matches(new Regex(@"class=""toast-container[^""]*""[^>]*aria-live=""polite"""), layout);
        Assert.Contains("~/js/pems-sidebar-search.js", layout);
        // the skip link is the first focusable element: it sits before the top bar
        Assert.True(layout.IndexOf("pems-skip-link", StringComparison.Ordinal) < layout.IndexOf("<header class=\"pems-topbar\">", StringComparison.Ordinal));

        var css = Read("PEMS.Web/wwwroot/css/site.css");
        Assert.Contains(".pems-skip-link:focus", css);
    }

    [Fact]
    public void ProgramConfiguration_ReExecutesEmptyErrorResponsesToTheStatusPage()
    {
        var program = Read("PEMS.Web/Program.cs");

        Assert.Contains("app.UseStatusCodePagesWithReExecute(\"/Home/StatusPage\", \"?code={0}\");", program);
        // it must come after the exception handler and before routing/auth so every 4xx/5xx without a body is covered
        Assert.True(program.IndexOf("UseExceptionHandler", StringComparison.Ordinal) < program.IndexOf("UseStatusCodePagesWithReExecute", StringComparison.Ordinal));
        Assert.True(program.IndexOf("UseStatusCodePagesWithReExecute", StringComparison.Ordinal) < program.IndexOf("app.UseRouting()", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ status page

    private static HomeController NewHome() => new(null!, null!, null!, null!, null!, null!, null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Theory]
    [InlineData(404, 404)]
    [InlineData(403, 403)]
    [InlineData(500, 500)]
    [InlineData(99, 500)]       // out of range -> generic
    [InlineData(200, 500)]
    [InlineData(700, 500)]
    public void StatusPage_ClampsTheCode_AndKeepsAValidOne(int requested, int expected)
    {
        var result = Assert.IsType<ViewResult>(NewHome().StatusPage(requested));
        var model = Assert.IsType<StatusPageViewModel>(result.Model);

        Assert.Equal(expected, model.Code);
        Assert.False(string.IsNullOrEmpty(model.RequestId));
    }

    [Fact]
    public void StatusPage_IsAnonymousAccessible_SoA404DoesNotRedirectToLogin()
    {
        var method = typeof(HomeController).GetMethod(nameof(HomeController.StatusPage))!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(404, "bi-compass")]
    [InlineData(403, "bi-shield-lock")]
    [InlineData(401, "bi-shield-lock")]
    [InlineData(500, "bi-exclamation-triangle")]
    [InlineData(503, "bi-exclamation-triangle")]
    public async Task StatusPage_Renders_ADistinctMessagePerKindOfError(int code, string expectedIcon)
    {
        var host = NewHome();
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(host.ViewData) { Model = new StatusPageViewModel { Code = code, RequestId = "REQ-123" } } };

        var html = await RazorViewRenderer.RenderAsync(host, result, "~/Views/Home/StatusPage.cshtml");

        Assert.Contains($"data-status-code=\"{code}\"", html);
        Assert.Contains(expectedIcon, html);
        Assert.Contains("aria-labelledby=\"statusTitle\"", html);
        Assert.Contains("id=\"statusTitle\"", html);
        Assert.Contains("REQ-123", html);
        Assert.Contains(">" + code + "<", html);
    }

    // ------------------------------------------------------------------ localization

    [Theory]
    [InlineData("PEMS.Web/Resources/SharedResource.resx")]
    [InlineData("PEMS.Web/Resources/SharedResource.fa.resx")]
    public void ShellAndStatusStrings_ExistInBothLanguages(string resx)
    {
        var keys = XDocument.Load(RepoFile(resx)).Descendants("data").Select(d => (string?)d.Attribute("name")).ToHashSet();

        foreach (var key in new[]
        {
            "Shell_SkipToContent", "Shell_SidebarSearch", "Shell_SidebarSearchResults", "Shell_SidebarSearchEmpty",
            "Status_NotFound_Title", "Status_NotFound_Text", "Status_Forbidden_Title", "Status_Forbidden_Text",
            "Status_Generic_Title", "Status_Generic_Text", "Status_Back", "Status_Home", "Status_Code", "Status_RequestId"
        })
            Assert.Contains(key, keys);
    }
}
