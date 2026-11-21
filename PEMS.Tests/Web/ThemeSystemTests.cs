using PEMS.Tests.TestSupport;
using System.Runtime.CompilerServices;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// طبق «PEMS Global Three-Theme Design System»: محصول اکنون دقیقاً سه تم قابل‌انتخاب دارد -
/// Light/Dark/Persian Turquoise. این تست‌ها تضمین می‌کنند: (۱) ThemeController دقیقاً همین سه
/// مقدار را می‌پذیرد و هر مقدار نامعتبر/قدیمی (ocean/warm/midnight یا هر رشته‌ی دیگر) بی‌صدا
/// به Light برمی‌گردد - نه خطا می‌دهد و نه چیزی می‌شکند؛ (۲) توکن‌های تم در site.css برای هر سه
/// تم تعریف شده‌اند؛ (۳) کلید محلی‌سازی تم تازه در هر دو زبان وجود دارد.
/// </summary>
public class ThemeSystemTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Web(params string[] parts) => SourceTree.Text(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));

    [Fact]
    public void AllowedThemes_IsExactlyLightDarkTurquoise()
    {
        Assert.Equal(new[] { "light", "dark", "turquoise" }, ThemeController.AllowedThemes);
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    [InlineData("turquoise", "turquoise")]
    [InlineData("ocean", "light")]
    [InlineData("warm", "light")]
    [InlineData("midnight", "light")]
    [InlineData(null, "light")]
    [InlineData("not-a-theme", "light")]
    public void Normalize_MapsToAllowedTheme_OrFallsBackToLight(string? input, string expected)
    {
        Assert.Equal(expected, ThemeController.Normalize(input));
    }

    // Light/Dark token blocks moved from site.css to PEMS.UI/wwwroot/tokens.css (SCRUM-29)
    private static string Tokens() => SourceTree.Text(Path.Combine(Root(), "PEMS.UI", "wwwroot", "tokens.css"));

    [Theory]
    [InlineData("pems-workspace.css")]
    [InlineData("pems-themes.css")]
    [InlineData("site.css")]
    [InlineData("administration-design-system.css")]
    public void TheShared_stylesheets_hold_no_literal_hex_colours_only_palette_tokens(string file)
    {
        // SCRUM-476: a colour is a token (tokens.css), never a literal in these four files; comments and url(...) are not colours
        var css = System.Text.RegularExpressions.Regex.Replace(Web("wwwroot", "css", file), @"/\*.*?\*/|url\([^)]*\)", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        var hits = System.Text.RegularExpressions.Regex.Matches(css, @"(?<![\w&%#-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])[^{;}]*[;}]").Select(m => m.Value.Trim()).ToList();
        Assert.True(hits.Count == 0, file + " has literal hex colours: " + string.Join(" | ", hits.Take(5)));
        foreach (var token in System.Text.RegularExpressions.Regex.Matches(css, @"var\((--pems-color-[0-9a-f]+)\)").Select(m => m.Groups[1].Value).Distinct())
            Assert.Contains(token + ":", Tokens());
    }

    [Fact]
    public void Inline_style_attributes_of_the_views_only_go_down_and_the_generated_classes_are_linked()
    {
        // SCRUM-347 (strict Content-Security-Policy): 198 static style attributes became generated classes (pems-inline-styles.css); the rest stay until each is checked in a browser.
        // Lower this number when you remove one; never raise it: a new style goes in a component or a stylesheet.
        const int allowed = 587;
        var root = Path.Combine(Root(), "PEMS.Web");
        var count = new[] { "Views", "Areas" }.Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => SourceTree.Files(d, "*.cshtml"))
            .Sum(f => System.Text.RegularExpressions.Regex.Matches(SourceTree.Text(f), "\\sstyle=\"").Count);
        Assert.True(count <= allowed, $"{count} inline style attributes in the views, the limit is {allowed}: use a component, a class or a stylesheet.");
        foreach (var layout in new[] { "_Layout.cshtml", "_LayoutEmbed.cshtml", "_AuthLayout.cshtml" })
            Assert.Contains("~/css/pems-inline-styles.css", SourceTree.Text(Path.Combine(root, "Views", "Shared", layout)));
    }

    [Fact]
    public void Views_hold_no_style_blocks_except_the_two_stage_diagrams_that_compute_their_values()
    {
        // SCRUM-347 (strict Content-Security-Policy): page CSS lives in a stylesheet. The two diagram partials write Razor values into their rules.
        var allowed = Array.Empty<string>();   // SCRUM-486: both diagram partials became the pems-stage-diagram component
        var offenders = SourceTree.Files(Path.Combine(Root(), "PEMS.Web"), "*.cshtml")
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(SourceTree.Text(f), "<style[ >]"))
            .Select(Path.GetFileName).Where(n => !allowed.Contains(n)).ToList();
        Assert.True(offenders.Count == 0, "Move the <style> block to a stylesheet in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void SiteCss_DefinesTokensForAllThreeThemes()
    {
        var css = Tokens() + Web("wwwroot", "css", "site.css");

        Assert.Contains(":root {", css);
        Assert.Contains("[data-theme=\"dark\"]", css);
        Assert.Contains("[data-theme=\"turquoise\"]", css);

        // --pems-primary باید در هر سه محل override/تعریف شود، نه فقط یک‌بار
        var primaryCount = System.Text.RegularExpressions.Regex.Matches(css, "--pems-primary:").Count;
        Assert.True(primaryCount >= 3, $"Expected --pems-primary to be set for root/dark/turquoise, found {primaryCount} occurrences.");
    }

    [Fact]
    public void SiteCss_ChartTokens_ExistForRootAndTurquoise()
    {
        var css = Tokens() + Web("wwwroot", "css", "site.css");
        Assert.Contains("--pems-chart-primary:", css);
        Assert.Contains("--pems-chart-positive:", css);
        Assert.Contains("--pems-chart-negative:", css);
    }

    [Fact]
    public void Layout_ThemeDropdown_OffersExactlyThreeOptions()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        Assert.Contains("\"light\"", layout);
        Assert.Contains("\"dark\"", layout);
        Assert.Contains("\"turquoise\"", layout);
        Assert.DoesNotContain("\"ocean\"", layout);
        Assert.DoesNotContain("\"warm\"", layout);
        Assert.DoesNotContain("\"midnight\"", layout);
    }

    [Fact]
    public void ChartColours_ComeFromSemanticTokens_NotHardCodedHex()
    {
        // SCRUM-337 C3: every chart is <pems-chart>; its colours are BkChartSpec's --pems-chart-* tokens (pems-charts.js is retired)
        Assert.StartsWith("var(--pems-chart-primary", PEMS.Web.Models.BkChartSpec.Primary);
        Assert.StartsWith("var(--pems-chart-negative", PEMS.Web.Models.BkChartSpec.Negative);
    }

    [Fact]
    public void ThemeLocalizationKey_ExistsInBothLanguages()
    {
        var en = Web("Resources", "SharedResource.resx");
        var fa = Web("Resources", "SharedResource.fa.resx");
        Assert.Contains("data name=\"Theme_Turquoise\"", en);
        Assert.Contains("data name=\"Theme_Turquoise\"", fa);
    }
}
