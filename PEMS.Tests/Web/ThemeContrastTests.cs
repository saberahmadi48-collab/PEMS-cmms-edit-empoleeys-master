using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PEMS.Tests.Web;

/// <summary>
/// Persian Turquoise v3 - ممیزی رنگ/چیدمان. دو دسته‌ی تست:
///  ۱) «Cascade واقعی»: توکن‌های مؤثر هر تم با شبیه‌سازی ترتیب بارگذاری فایل‌ها در _Layout و مشخصیت انتخابگر محاسبه می‌شود
///     (نه فقط خواندن بلوک site.css). علت اصلی ظاهر خراب قبلی دقیقاً همین بود: admin.css بعد از site.css یک :root روشن داشت و
///     برای turquoise بلوک اختصاصی نداشت، پس سطح/متن/پس‌زمینه‌ی روشن زیر شیشه‌ی تیره می‌نشست و هیچ تستی آن را نمی‌دید.
///  ۲) کنتراست WCAG جفت‌رنگ‌های واقعی تم (متن، پیوند، وضعیت، دکمه‌ی اصلی، مرز کنترل، فوکوس) روی همین توکن‌های مؤثر.
///  ۳) قراردادهای ساختاری که هر کدام یک باگ چیدمان واقعی را می‌بستند (backdrop-filter روی عنصر دارای Dropdown، position سایدبار
///     موبایل، انتخابگر RTL، data-bs-theme).
/// </summary>
public class ThemeContrastTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Web(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));
    private static string Css(string file) => StripComments(Web("wwwroot", "css", file));

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    // ------------------------------------------------------------------ cascade simulation

    /// <summary>ترتیب واقعی بارگذاری در _Layout.cshtml (فایل‌های RTL آخر).</summary>
    private static readonly string[] LayoutOrder =
        { "site.css", "pems-global-search.css", "pems-data-grid.css", "admin.css", "pems-workspace.css", "pems-glass.css", "admin.rtl.css" };

    private static readonly Regex Rule = new(@"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex Decl = new(@"(?<n>--[\w-]+)\s*:\s*(?<v>[^;]+?)\s*(;|$)", RegexOptions.Compiled);

    /// <summary>
    /// مشخصیت انتخابگرهایی که فقط :root / html / [data-theme] هستند؛ null یعنی این انتخابگر به توکن تم مربوط نیست.
    /// :root=(0,1,0)  [data-theme]=(0,1,0)  html[data-theme]=(0,1,1)  :root[data-theme]=(0,2,0)
    /// </summary>
    private static (int a, int b, int c)? TokenSelectorSpecificity(string selector, string theme)
    {
        selector = selector.Trim();
        var m = Regex.Match(selector, @"^(?<root>:root|html)?(\[data-theme=""(?<t>[\w-]+)""\])?$");
        if (!m.Success || (!m.Groups["root"].Success && !m.Groups["t"].Success)) return null;

        var hasTheme = m.Groups["t"].Success;
        if (hasTheme && m.Groups["t"].Value != theme) return null;   // رول تم دیگر

        var cls = hasTheme ? 1 : 0;                                   // [attr]
        var pseudoClass = m.Groups["root"].Value == ":root" ? 1 : 0;  // :root شبه‌کلاس است
        var element = m.Groups["root"].Value == "html" ? 1 : 0;
        return (0, cls + pseudoClass, element);
    }

    /// <summary>توکن‌های مؤثر یک تم پس از Cascade همه‌ی فایل‌ها (و حل var()).</summary>
    private static Dictionary<string, string> EffectiveTokens(string theme)
    {
        var winners = new Dictionary<string, ((int a, int b, int c) spec, int order, string value)>();
        var order = 0;
        foreach (var file in LayoutOrder)
            foreach (Match rule in Rule.Matches(Css(file)))
            {
                order++;
                foreach (var sel in rule.Groups["sel"].Value.Split(','))
                {
                    var spec = TokenSelectorSpecificity(sel, theme);
                    if (spec == null) continue;
                    foreach (Match d in Decl.Matches(rule.Groups["body"].Value))
                    {
                        var name = d.Groups["n"].Value;
                        var candidate = (spec.Value, order, d.Groups["v"].Value.Trim());
                        if (!winners.TryGetValue(name, out var cur) || Compare(candidate.Item1, cur.spec) > 0 || (Compare(candidate.Item1, cur.spec) == 0 && order >= cur.order))
                            winners[name] = candidate;
                    }
                }
            }

        var raw = winners.ToDictionary(kv => kv.Key, kv => kv.Value.value);
        string Resolve(string v, int depth = 0)
        {
            var m = Regex.Match(v, @"^var\((?<n>--[\w-]+)\)$");
            return m.Success && depth < 8 && raw.TryGetValue(m.Groups["n"].Value, out var next) ? Resolve(next, depth + 1) : v;
        }
        return raw.ToDictionary(kv => kv.Key, kv => Resolve(kv.Value));
    }

    private static int Compare((int a, int b, int c) x, (int a, int b, int c) y)
        => x.a != y.a ? x.a.CompareTo(y.a) : x.b != y.b ? x.b.CompareTo(y.b) : x.c.CompareTo(y.c);

    // ------------------------------------------------------------------ color math

    private static (double r, double g, double b) Rgb(string value)
    {
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            return (int.Parse(hex[..2], NumberStyles.HexNumber), int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber), int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }
        var m = Regex.Match(value, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
        Assert.True(m.Success, "Unsupported colour value: " + value);
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
    }

    private static double Luminance((double r, double g, double b) c)
    {
        double F(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(c.r) + 0.7152 * F(c.g) + 0.0722 * F(c.b);
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Luminance(Rgb(a)), Luminance(Rgb(b)));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static string T(Dictionary<string, string> tokens, string name)
    {
        Assert.True(tokens.TryGetValue(name, out var v), $"token {name} is not defined");
        return v!;
    }

    // ------------------------------------------------------------------ 1) the cascade bug

    [Theory]
    [InlineData("light", false)]
    [InlineData("dark", true)]
    [InlineData("turquoise", true)]
    public void EachTheme_KeepsItsOwnBaseTokens_AfterEveryStylesheetHasLoaded(string theme, bool isDark)
    {
        var tokens = EffectiveTokens(theme);

        var bg = Luminance(Rgb(T(tokens, "--pems-bg")));
        var surface = Luminance(Rgb(T(tokens, "--pems-surface")));
        var text = Luminance(Rgb(T(tokens, "--pems-text")));
        var topbar = Luminance(Rgb(T(tokens, "--pems-topbar-bg")));

        if (isDark)
        {
            // قبلاً turquoise اینجا شکست می‌خورد: bg=#f3f5f8 / surface=#fff / text=#1b2733 از :root ی admin.css
            Assert.True(bg < 0.02, $"{theme}: --pems-bg must be dark, got {T(tokens, "--pems-bg")}");
            Assert.True(surface < 0.03, $"{theme}: --pems-surface must be dark, got {T(tokens, "--pems-surface")}");
            Assert.True(topbar < 0.03, $"{theme}: --pems-topbar-bg must be dark, got {T(tokens, "--pems-topbar-bg")}");
            Assert.True(text > 0.6, $"{theme}: --pems-text must be light, got {T(tokens, "--pems-text")}");
        }
        else
        {
            Assert.True(bg > 0.85 && surface > 0.85, $"{theme}: light base expected");
            Assert.True(text < 0.1, $"{theme}: dark text expected");
        }
    }

    [Fact]
    public void Turquoise_BlockWinsBySpecificity_NotByFileOrder()
    {
        // اگر کسی دوباره :root را در فایل دیرتر اضافه کند، بلوک :root[data-theme] همچنان برنده است
        Assert.Contains(":root[data-theme=\"turquoise\"] {", Css("site.css"));
        var adminRoot = Rule.Matches(Css("admin.css")).Cast<Match>().First(m => m.Groups["sel"].Value.Trim() == ":root");
        Assert.Contains("--pems-bg", adminRoot.Groups["body"].Value);   // the conflicting light block really exists

        var winner = EffectiveTokens("turquoise")["--pems-bg"];
        Assert.NotEqual("#f3f5f8", winner);
    }

    // ------------------------------------------------------------------ 2) WCAG pairs

    private static readonly string[] Surfaces = { "--pems-bg", "--pems-elev-1", "--pems-elev-2", "--pems-elev-3", "--pems-table-header-bg", "--pems-input-bg" };

    [Fact]
    public void Turquoise_BodyAndMutedText_AreAA_OnEverySurface()
    {
        var t = EffectiveTokens("turquoise");
        foreach (var surface in Surfaces)
            foreach (var fg in new[] { "--pems-text", "--pems-text-muted", "--pems-text-faint" })
                Assert.True(Contrast(T(t, fg), T(t, surface)) >= 4.5, $"{fg} on {surface}: {Contrast(T(t, fg), T(t, surface)):0.00}:1");
    }

    [Fact]
    public void Turquoise_BrandLinkAndAccentText_AreAA_OnEverySurface()
    {
        var t = EffectiveTokens("turquoise");
        foreach (var surface in Surfaces)
            foreach (var fg in new[] { "--pems-turquoise", "--pems-turquoise-bright", "--pems-lapis-light", "--pems-gold", "--pems-accent-dark",
                         "--pems-chart-positive-text", "--pems-chart-warning-text", "--pems-chart-danger-text", "--pems-chart-info-text", "--pems-chart-caution-text" })
                Assert.True(Contrast(T(t, fg), T(t, surface)) >= 4.5, $"{fg} {T(t, fg)} on {surface}: {Contrast(T(t, fg), T(t, surface)):0.00}:1");
    }

    [Fact]
    public void Turquoise_PrimaryButton_DarkTextOnGold_IsAA_AtEveryPointOfTheGradient()
    {
        var t = EffectiveTokens("turquoise");
        foreach (var fill in new[] { "--pems-gold-hi", "--pems-gold", "--pems-gold-lo" })
            Assert.True(Contrast(T(t, "--pems-on-gold"), T(t, fill)) >= 4.5, $"on-gold on {fill}: {Contrast(T(t, "--pems-on-gold"), T(t, fill)):0.00}:1");
        // hover end of the gradient (#ffe08b) is lighter still
        Assert.True(Contrast(T(t, "--pems-on-gold"), "#ffe08b") >= 4.5);
    }

    [Theory]
    [InlineData("--pems-turquoise-dark")]
    [InlineData("--pems-turquoise-deep")]
    [InlineData("--pems-lapis")]
    public void Turquoise_WhiteTextOnSolidBrandFills_IsAA(string fill)
    {
        var t = EffectiveTokens("turquoise");
        Assert.True(Contrast("#ffffff", T(t, fill)) >= 4.5, $"white on {fill}: {Contrast("#ffffff", T(t, fill)):0.00}:1");
    }

    [Fact]
    public void Turquoise_FormControlBorders_AndFocusRing_MeetNonTextContrast()
    {
        var t = EffectiveTokens("turquoise");
        foreach (var surface in new[] { "--pems-elev-1", "--pems-elev-2", "--pems-input-bg" })
        {
            Assert.True(Contrast(T(t, "--pems-border-strong"), T(t, surface)) >= 3.0, $"control border vs {surface}");
            Assert.True(Contrast(T(t, "--pems-focus"), T(t, surface)) >= 3.0, $"focus ring vs {surface}");
        }
        // the input well is darker than the card it sits on, so the field reads as a field
        Assert.True(Luminance(Rgb(T(t, "--pems-input-bg"))) < Luminance(Rgb(T(t, "--pems-elev-1"))));
    }

    [Fact]
    public void Turquoise_ElevationScale_IsStrictlyIncreasing_SoLayersAreDistinguishable()
    {
        var t = EffectiveTokens("turquoise");
        var l = new[] { "--pems-elev-0", "--pems-elev-1", "--pems-elev-2", "--pems-elev-3" }.Select(n => Luminance(Rgb(T(t, n)))).ToArray();

        for (var i = 1; i < l.Length; i++) Assert.True(l[i] > l[i - 1], "elevation " + i + " must be lighter than " + (i - 1));
        // فاصله‌ی روشنایی لایه‌ی شناور از صفحه ≥1.3:1 (اندازه‌گیری‌شده 1.36)؛ جدایی نهایی را حاشیه‌ی فیروزه‌ای ۱px و سایه می‌دهند
        Assert.True(Contrast(T(t, "--pems-elev-3"), T(t, "--pems-elev-0")) >= 1.3, "overlays must be visibly above the page");
    }

    [Fact]
    public void Turquoise_BadgeAndPillTones_AreAA()
    {
        var css = Css("pems-glass.css");
        var pairs = Regex.Matches(css, @"\[data-theme=""turquoise""\][^{}]*\.(?:badge\.(?:bg|text-bg)-\w+|vs-pill--\w+)[^{}]*\{[^{}]*background:\s*(?<bg>#[0-9a-fA-F]{6})\s*(?:!important)?;\s*color:\s*(?<fg>#[0-9a-fA-F]{6})", RegexOptions.Singleline)
            .Select(m => (bg: m.Groups["bg"].Value, fg: m.Groups["fg"].Value)).Distinct().ToList();

        // شش لحن یکتا (موفق/خطر/هشدار/اصلی/اطلاعات/خنثی)؛ Badge و Pill عمداً یک جفت‌رنگ مشترک دارند
        Assert.True(pairs.Count >= 6, "expected the six badge/pill tones to be declared, found " + pairs.Count);
        foreach (var (bg, fg) in pairs)
            Assert.True(Contrast(fg, bg) >= 4.5, $"badge/pill {fg} on {bg}: {Contrast(fg, bg):0.00}:1");
    }

    // ------------------------------------------------------------------ 3) structural layout contracts

    private static List<(string Selector, string Body)> Rules(string file)
        => Rule.Matches(Css(file)).Cast<Match>()
            .SelectMany(m => m.Groups["sel"].Value.Split(',').Select(s => (s.Trim(), m.Groups["body"].Value)))
            .ToList();

    [Fact]
    public void Turquoise_NeverAppliesBackdropFilter_ToAnElementThatCanContainADropdown()
    {
        // backdrop-filter یک Containing Block برای فرزندان fixed می‌سازد؛ pems-ui.js همه‌ی Dropdown ها را strategy=fixed باز می‌کند
        var offenders = Rules("pems-glass.css")
            .Where(r => r.Selector.Contains("[data-theme=\"turquoise\"]") && Regex.IsMatch(r.Selector, @"\.(content-card|kpi-card|vs-panel|pems-topbar|pems-sidebar)$"))
            .Where(r => r.Body.Contains("backdrop-filter"))
            .Select(r => r.Selector).ToList();

        Assert.True(offenders.Count == 0, "backdrop-filter on: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Turquoise_TopbarBlur_LivesOnAPseudoElement_NotOnTheTopbarItself()
    {
        var blur = Rules("pems-glass.css").Where(r => r.Selector == "[data-theme=\"turquoise\"] .pems-topbar::before" && r.Body.Contains("backdrop-filter")).ToList();

        Assert.NotEmpty(blur);
    }

    [Fact]
    public void Turquoise_SidebarKeepsItsFixedPositionFromTheResponsiveRules()
    {
        // [data-theme] .pems-sidebar { position: relative } (مشخصیت بالاتر) قاعده‌ی fixed موبایل را خنثی می‌کرد
        var offenders = Rules("pems-glass.css")
            .Where(r => Regex.IsMatch(r.Selector, @"\[data-theme=""turquoise""\]\s+\.pems-sidebar$") && Regex.IsMatch(r.Body, @"(^|;|\s)position\s*:"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoStylesheet_UsesADescendantThemeSelectorAfterAnHtmlRtlSelector()
    {
        // data-theme روی خودِ <html> است؛ «html[dir="rtl"] [data-theme=...]» هرگز منطبق نمی‌شود (باگ لبه‌ی منوی فعال در فارسی)
        var offenders = Directory.EnumerateFiles(Path.Combine(Root(), "PEMS.Web", "wwwroot", "css"), "*.css")
            .Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"\[dir=""rtl""\]\s+\[data-theme"))
            .Select(Path.GetFileName).ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Turquoise_ActiveSidebarItem_PutsTheMarkerOnTheStartEdge_InBothDirections()
    {
        var rules = Rules("pems-glass.css");
        var ltr = rules.Single(r => r.Selector == "[data-theme=\"turquoise\"] .pems-sidebar .nav-link.active");
        var rtl = rules.Single(r => r.Selector == "html[dir=\"rtl\"][data-theme=\"turquoise\"] .pems-sidebar .nav-link.active");

        Assert.Contains("border-left-color: var(--pems-turquoise)", ltr.Body);
        Assert.Contains("border-right-color: transparent", ltr.Body);
        Assert.Contains("border-right-color: var(--pems-turquoise)", rtl.Body);
        Assert.Contains("border-left-color: transparent", rtl.Body);
        Assert.DoesNotContain("background: var(--pems-turquoise)", ltr.Body);   // a thin edge, never a solid block
    }

    [Fact]
    public void EveryPrimaryButtonFamily_GetsTheSameGoldLookWithDarkText_InTurquoise()
    {
        var rules = Rules("pems-glass.css");
        foreach (var selector in new[]
        {
            "[data-theme=\"turquoise\"] .btn-pems-solid", "[data-theme=\"turquoise\"] .vs-btn--primary", "[data-theme=\"turquoise\"] .btn-pems",
            "[data-theme=\"turquoise\"] .admin-page .btn-primary", "[data-theme=\"turquoise\"] .admin-page-shell .btn-pems-solid"
        })
        {
            var rule = rules.FirstOrDefault(r => r.Selector == selector && r.Body.Contains("linear-gradient"));
            Assert.True(rule.Selector != null, selector + " has no gold rule");
            Assert.Contains("var(--pems-on-gold)", rule.Body);
            Assert.DoesNotContain("color: #fff", rule.Body);
        }
        var auth = Rules("auth.css").First(r => r.Selector == "[data-theme=\"turquoise\"] .btn-pems" && r.Body.Contains("linear-gradient"));
        Assert.Contains("var(--pems-on-gold)", auth.Body);
    }

    [Fact]
    public void Layouts_EnableBootstrapDarkColorMode_ForTheTwoDarkBasedThemes_AndNeverForLight()
    {
        // data-bs-theme="dark" فقط برای تم‌های تیره (turquoise و dark)؛ Light بدون ویژگی می‌ماند (null = حذف ویژگی در Razor)
        const string attr = "data-bs-theme=\"@(currentTheme is \"turquoise\" or \"dark\" ? \"dark\" : null)\"";
        Assert.Contains(attr, Web("Views", "Shared", "_Layout.cshtml"));
        Assert.Contains(attr, Web("Views", "Shared", "_AuthLayout.cshtml"));
    }

    [Fact]
    public void Turquoise_MapsBootstrapVariables_ToThePemsTokens_SoNoComponentFallsBackToDefaultBlue()
    {
        var t = EffectiveTokens("turquoise");

        Assert.Equal(T(t, "--pems-text"), T(t, "--bs-body-color"));
        Assert.Equal(T(t, "--pems-bg"), T(t, "--bs-body-bg"));
        Assert.Equal(T(t, "--pems-text-muted"), T(t, "--bs-secondary-color"));
        Assert.Equal(T(t, "--pems-turquoise-bright"), T(t, "--bs-link-color"));
        Assert.Equal(T(t, "--pems-turquoise"), T(t, "--bs-primary"));
        Assert.Equal("95, 224, 210", T(t, "--bs-link-color-rgb"));
        // ... and the mapped link colour itself is readable
        foreach (var surface in Surfaces) Assert.True(Contrast(T(t, "--bs-link-color"), T(t, surface)) >= 4.5, "link on " + surface);
    }

    [Fact]
    public void Turquoise_Palette_IsThreeTileColours_NoLeftoverCopperAsTheActionColour()
    {
        var t = EffectiveTokens("turquoise");

        // the primary action colour is gold, not the old muddy copper
        Assert.Equal(T(t, "--pems-gold"), T(t, "--pems-accent"));
        Assert.NotEqual("#b5651d", T(t, "--pems-accent").ToLowerInvariant());
        // the three tile colours exist and are distinct hues (turquoise is greener/cyan than lapis, gold is warm)
        var (tr, tg, tb) = Rgb(T(t, "--pems-turquoise"));
        var (lr, lg, lb) = Rgb(T(t, "--pems-lapis"));
        var (gr, gg, gb) = Rgb(T(t, "--pems-gold"));
        Assert.True(tg > tr && tb > tr, "turquoise is green/blue dominant");
        Assert.True(lb > lr && lb > lg, "lapis is blue dominant");
        Assert.True(gr > gb && gg > gb, "gold is warm (red/green over blue)");
    }

    // ------------------------------------------------------------------ Light + Dark (ممیزی رنگ)

    private static readonly string[] LightSurfaces = { "--pems-bg", "--pems-surface", "--pems-table-header-bg", "--pems-input-bg" };
    private static readonly string[] DarkSurfaces = { "--pems-bg", "--pems-surface", "--pems-table-header-bg", "--pems-input-bg" };

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void LightAndDark_TextMutedFaintAndLinks_AreAA_OnEverySurface(string theme)
    {
        var t = EffectiveTokens(theme);
        foreach (var surface in theme == "light" ? LightSurfaces : DarkSurfaces)
            foreach (var fg in new[] { "--pems-text", "--pems-text-muted", "--pems-text-faint", "--pems-link", "--pems-steel" })
                Assert.True(Contrast(T(t, fg), T(t, surface)) >= 4.5, $"{theme}: {fg} {T(t, fg)} on {surface} {T(t, surface)}: {Contrast(T(t, fg), T(t, surface)):0.00}:1");
    }

    [Fact]
    public void Dark_BrandPrimary_AndStatusText_AreAA_OnEverySurface()
    {
        var t = EffectiveTokens("dark");
        foreach (var surface in DarkSurfaces)
            foreach (var fg in new[] { "--pems-primary", "--pems-primary-hover", "--pems-chart-positive-text", "--pems-chart-warning-text", "--pems-chart-danger-text", "--pems-chart-info-text", "--pems-chart-caution-text" })
                Assert.True(Contrast(T(t, fg), T(t, surface)) >= 4.5, $"dark: {fg} on {surface}: {Contrast(T(t, fg), T(t, surface)):0.00}:1");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void LightAndDark_FormControlBorders_AndFocusRing_MeetNonTextContrast(string theme)
    {
        var t = EffectiveTokens(theme);
        foreach (var surface in new[] { "--pems-surface", "--pems-input-bg" })
        {
            Assert.True(Contrast(T(t, "--pems-border-strong"), T(t, surface)) >= 3.0, $"{theme}: control border vs {surface}");
            Assert.True(Contrast(T(t, "--pems-focus"), T(t, surface)) >= 3.0, $"{theme}: focus ring vs {surface}");
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void LightAndDark_PrimaryButton_DarkTextOnAmber_IsAA_AtBothGradientEnds(string theme)
    {
        var t = EffectiveTokens(theme);
        foreach (var fill in new[] { "--pems-cta-start", "--pems-cta-end" })
            Assert.True(Contrast(T(t, "--pems-on-accent"), T(t, fill)) >= 4.5, $"{theme}: on-accent on {fill}: {Contrast(T(t, "--pems-on-accent"), T(t, fill)):0.00}:1");
        Assert.True(Contrast(T(t, "--pems-on-accent"), "#ffcb78") >= 4.5);   // hover start
        Assert.True(Contrast("#ffffff", "#1d4ed8") >= 4.5);                     // active list/pagination/pill fill
    }

    [Fact]
    public void LightAndDark_PrimaryButtonRules_NeverUseWhiteTextOnAmber()
    {
        var css = Css("pems-themes.css");
        var cta = Regex.Match(css, @":is\(\[data-theme=""light""\], \[data-theme=""dark""\]\) :is\(\.btn-pems-solid[^{]*\{(?<body>[^}]*)\}");
        Assert.True(cta.Success, "primary-action rule not found in pems-themes.css");
        Assert.Contains("color: var(--pems-on-accent)", cta.Groups["body"].Value);
        Assert.DoesNotContain("color: #fff", cta.Groups["body"].Value);

        var auth = Rules("auth.css").First(r => r.Selector == ".btn-pems");
        Assert.Contains("color: #1a1305", auth.Body);
        Assert.DoesNotContain("color: #fff", auth.Body);
    }

    [Fact]
    public void ThemeButtonRules_DoNotOutrankTheQuietTableRowActionRule()
    {
        // pems-workspace.css: «.pems-content td .btn-primary» دکمه‌ی ردیف جدول را آرام می‌کند؛ قاعده‌ی تم با «.pems-content .btn-primary»
        // مشخصیت بالاتر می‌گرفت و اقدام‌های ردیف را توپر (طلایی/کهربایی) می‌کرد
        Assert.Contains(".pems-content td .btn-primary", Web("wwwroot", "css", "pems-workspace.css"));
        Assert.DoesNotContain(".pems-content .btn-primary", Css("pems-themes.css"));
        Assert.DoesNotContain(".pems-content .btn-primary", Css("pems-glass.css"));
    }

    [Fact]
    public void ThemesStylesheet_LoadsAfterTheGlassLayer_InTheMainLayout()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        var glass = layout.IndexOf("~/css/pems-glass.css", StringComparison.Ordinal);
        var themes = layout.IndexOf("~/css/pems-themes.css", StringComparison.Ordinal);

        Assert.True(glass >= 0 && themes > glass, "pems-themes.css must load after pems-glass.css");
    }

    [Fact]
    public void Dark_BootstrapVariables_AreMappedToThePemsTokens()
    {
        var t = EffectiveTokens("dark");

        Assert.Equal(T(t, "--pems-text"), T(t, "--bs-body-color"));
        Assert.Equal(T(t, "--pems-bg"), T(t, "--bs-body-bg"));
        Assert.Equal(T(t, "--pems-text-muted"), T(t, "--bs-secondary-color"));
        Assert.Equal(T(t, "--pems-link"), T(t, "--bs-link-color"));
        foreach (var surface in DarkSurfaces) Assert.True(Contrast(T(t, "--bs-link-color"), T(t, surface)) >= 4.5, "link on " + surface);
    }

    [Fact]
    public void Light_UsesOneBrandBlue_NotBootstrapDefaultBlueForLinksAndPrimary()
    {
        var t = EffectiveTokens("light");

        Assert.Equal(T(t, "--pems-link"), T(t, "--bs-link-color"));
        Assert.NotEqual("#0d6efd", T(t, "--bs-link-color").ToLowerInvariant());
        Assert.NotEqual("13, 110, 253", T(t, "--bs-primary-rgb"));
    }
}
