using System.Runtime.CompilerServices;

namespace PEMS.Tests.Web;

/// <summary>
/// طبق «PEMS — Premium Three-Theme UI/UX Redesign»: پوشش رگرسیون سیستم Glass/Persian
/// Geometry/Loading تازه - سیاه‌غالب‌بودن Turquoise، محدودبودن فیروزه به لهجه (نه سطح بزرگ)،
/// وجود توکن‌های Glass/Elevation، حلقه‌ی فوکوس قابل‌مشاهده، و احترام به
/// prefers-reduced-motion. مثل ThemeSystemTests، مستقیماً محتوای فایل را می‌خواند - بدون
/// نیاز به DB/Host.
/// </summary>
public class PremiumGlassSystemTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Web(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));

    [Fact]
    public void Layout_LoadsGlassLayer_AfterSiteCss_AndLoadingOverlayPartial()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        var site = layout.IndexOf("~/css/site.css", StringComparison.Ordinal);
        var glass = layout.IndexOf("~/css/pems-glass.css", StringComparison.Ordinal);
        Assert.True(site >= 0 && glass > site, "pems-glass.css must load after site.css so its overrides can extend the base tokens");
        Assert.Contains("_PemsLoadingOverlay", layout);
        Assert.Contains("~/js/pems-loading.js", layout);
    }

    [Fact]
    public void TurquoiseTheme_BackgroundIsVeryDark_InkTealBase()
    {
        var css = Web("wwwroot", "css", "site.css");
        // v3: انتخابگر :root[data-theme] (مشخصیت بالاتر از :root ی admin.css)؛ علت اصلی ظاهر خراب نسخه‌ی قبل
        var start = css.IndexOf(":root[data-theme=\"turquoise\"] {", StringComparison.Ordinal);
        Assert.True(start >= 0, "turquoise token block must exist");
        var end = css.IndexOf("\n}", start, StringComparison.Ordinal);
        var block = css.Substring(start, end - start);

        // پایه هنوز تیره‌ی غالب است (v3: جوهری-فیروزه‌ای، نه خاکستری خنثی - تا فیروزه یک رنگ «بومی» تم باشد)
        Assert.Contains("--pems-bg: #061219;", block);
        Assert.Contains("color-scheme: dark;", block);
        Assert.Contains("--pems-elev-0", block);
        Assert.Contains("--pems-elev-1", block);
        Assert.Contains("--pems-elev-2", block);
        Assert.Contains("--pems-glass-bg", block);
        Assert.Contains("--pems-glass-blur", block);
        Assert.Contains("--pems-accent-glow", block);
    }

    [Fact]
    public void GlassCss_DefinesPersianGeometry_AndGlassPrimitives()
    {
        var css = Web("wwwroot", "css", "pems-glass.css");
        Assert.Contains("--pems-pattern-fine", css);
        Assert.Contains("--pems-pattern-hero", css);
        Assert.Contains(".pems-glass {", css);
        Assert.Contains("backdrop-filter", css);
    }

    [Fact]
    public void GlassCss_TurquoiseSidebarActiveState_UsesThinEdge_NotSolidFill()
    {
        var css = Web("wwwroot", "css", "pems-glass.css");
        // طبق بند ۳۱/۳۲ سند («فیروزه هرگز نباید غالب باشد»): وضعیت فعال باید لبه‌ی نازک +
        // درخشش محدود باشد - نه یک background: var(--pems-turquoise) تمام‌سطح.
        var activeRuleStart = css.IndexOf("[data-theme=\"turquoise\"] .pems-sidebar .nav-link.active {", StringComparison.Ordinal);
        Assert.True(activeRuleStart >= 0);
        var activeRuleEnd = css.IndexOf('}', activeRuleStart);
        var rule = css.Substring(activeRuleStart, activeRuleEnd - activeRuleStart);
        Assert.DoesNotContain("background: var(--pems-turquoise)", rule);
        Assert.Contains("border-left-color: var(--pems-turquoise)", rule);
    }

    [Fact]
    public void GlassCss_TablesStayOpaque_NotGlass()
    {
        var css = Web("wwwroot", "css", "pems-glass.css");
        // طبق بند ۸/۱۸ سند: جدول‌های متراکم عمداً Opaque می‌مانند، نه شیشه‌ای
        var tableRuleStart = css.IndexOf("[data-theme=\"turquoise\"] .table-enterprise,", StringComparison.Ordinal);
        Assert.True(tableRuleStart >= 0);
        var tableRuleEnd = css.IndexOf('}', tableRuleStart);
        var rule = css.Substring(tableRuleStart, tableRuleEnd - tableRuleStart);
        Assert.Contains("var(--pems-elev-1)", rule);
        Assert.DoesNotContain("backdrop-filter", rule);
    }

    [Fact]
    public void GlassCss_RespectsReducedMotion()
    {
        var css = Web("wwwroot", "css", "pems-glass.css");
        Assert.Contains("prefers-reduced-motion: reduce", css);
        Assert.Contains("animation: none !important;", css);
    }

    [Fact]
    public void GlassCss_HasVisibleFocusRing_ForKeyboardNavigation()
    {
        var css = Web("wwwroot", "css", "pems-glass.css");
        Assert.Contains(":focus-visible", css);
        Assert.Contains("outline-offset: 2px", css);
    }

    [Fact]
    public void LoadingOverlay_HiddenAttribute_ActuallyHidesIt()
    {
        // طبق باگ واقعی که در همین جلسه پیدا و رفع شد: بدون این override، Author Style
        // (display:flex) روی UA Stylesheet پیش‌فرض [hidden] برنده می‌شد و overlay هرگز واقعاً
        // پنهان نمی‌شد.
        var css = Web("wwwroot", "css", "pems-glass.css");
        Assert.Contains(".pems-loading-overlay[hidden] {", css);
        var idx = css.IndexOf(".pems-loading-overlay[hidden] {", StringComparison.Ordinal);
        var end = css.IndexOf('}', idx);
        Assert.Contains("display: none", css.Substring(idx, end - idx));
    }

    [Fact]
    public void LoadingOverlayPartial_Exists_AndUsesPathLengthNormalizedTrace()
    {
        var partial = Web("Views", "Shared", "_PemsLoadingOverlay.cshtml");
        Assert.Contains("pemsGlobalLoading", partial);
        Assert.Contains("pathLength=\"100\"", partial);
        Assert.Contains("hidden", partial);
    }

    [Fact]
    public void AuthLayout_LoadsGlassSystem_ForLoginShowcase()
    {
        var authLayout = Web("Views", "Shared", "_AuthLayout.cshtml");
        Assert.Contains("~/css/site.css", authLayout);
        Assert.Contains("~/css/pems-glass.css", authLayout);
    }

    [Fact]
    public void AuthCss_TurquoiseLoginCard_UsesGlassTokens()
    {
        var css = Web("wwwroot", "css", "auth.css");
        var idx = css.IndexOf("[data-theme=\"turquoise\"] .auth-card {", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var end = css.IndexOf('}', idx);
        Assert.Contains("var(--pems-glass-bg-strong", css.Substring(idx, end - idx));
    }
}
