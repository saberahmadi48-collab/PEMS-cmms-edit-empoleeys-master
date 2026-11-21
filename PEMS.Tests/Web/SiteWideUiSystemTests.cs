using PEMS.Tests.TestSupport;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace PEMS.Tests.Web;

/// <summary>
/// لایه‌ی یکپارچه‌ی UI: یک استایل/اسکریپت سراسری (نه ده‌ها Patch صفحه‌ای). این تست‌ها تضمین می‌کنند لایه در Layout بارگذاری می‌شود،
/// قراردادهای کلیدی (وضعیت با آیکون، سایدبار موبایل، تک‌پیاده‌سازی) حذف نشده‌اند و کلیدهای جدید در هر دو زبان هستند.
/// </summary>
public class SiteWideUiSystemTests
{
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Web(params string[] parts) => SourceTree.Text(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));

    [Fact]
    public void Layout_LoadsUnifiedLayer_AfterAdminCss_AndSharedScript()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        var admin = layout.IndexOf("~/css/admin.css", StringComparison.Ordinal);
        var ws = layout.IndexOf("~/css/pems-workspace.css", StringComparison.Ordinal);
        Assert.True(admin >= 0 && ws > admin, "pems-workspace.css (unified layer) must load after admin.css");
        Assert.Contains("~/js/pems-ui.js", layout);
    }

    [Fact]
    public void UnifiedLayer_KeepsKeyContracts()
    {
        var css = Web("wwwroot", "css", "pems-workspace.css");
        Assert.Contains("PEMS UNIFIED INDUSTRIAL LAYER", css);
        Assert.Contains(".pems-sidebar.pems-opened", css);                       // layout script toggles .pems-opened on tablet/phone
        Assert.Contains("text-bg-success", css);                                  // every badge family shares one tone map
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("focus-visible", css);
        Assert.DoesNotContain("linear-gradient", css.Substring(css.IndexOf("PEMS UNIFIED INDUSTRIAL LAYER", StringComparison.Ordinal))); // no gradients in the unified layer
    }

    [Fact]
    public void SharedScript_AddsStatusIcons_AndAccessibleNames()
    {
        var js = Web("wwwroot", "js", "pems-ui.js");
        Assert.Contains("enhanceBadges", js);
        Assert.Contains("nameIconOnlyControls", js);
        Assert.Contains("nameFormControls", js);
        Assert.Contains("wrapTables", js);
    }

    [Fact]
    public void AdminHero_DoesNotDuplicateTitleOrDecorate()
    {
        var css = Web("wwwroot", "css", "administration-design-system.css");
        Assert.DoesNotContain("linear-gradient(135deg,#fff", css);
        Assert.DoesNotContain("admin-hero h2", css); // duplicate headings are removed from the markup, not hidden
    }

    [Fact]
    public void NewKeys_ExistInBothLanguages()
    {
        static Dictionary<string, string> Load(string f) => XDocument.Load(f).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "", StringComparer.Ordinal);
        var dir = Path.Combine(Root(), "PEMS.Web", "Resources");
        var en = Load(Path.Combine(dir, "SharedResource.resx"));
        var fa = Load(Path.Combine(dir, "SharedResource.fa.resx"));
        foreach (var k in en.Keys.Concat(fa.Keys).Where(k => k.StartsWith("Cm_") || k.StartsWith("Eq_") || k.StartsWith("Ed_") || k.StartsWith("Lt_") || k.StartsWith("Rte_") || k.StartsWith("Wo_") || k.StartsWith("As_")).Distinct())
        {
            Assert.True(en.TryGetValue(k, out var e) && e.Length > 0, $"EN missing {k}");
            Assert.True(fa.TryGetValue(k, out var f) && f.Length > 0, $"FA missing {k}");
        }
    }

    [Fact]
    public void LetterCreate_EveryFieldHasAnAssociatedLabel_AndEditorControlsAreNamed()
    {
        var view = Web("Views", "Letter", "Create.cshtml");
        foreach (var name in new[] { "letterDate", "letterSubject", "letterBodyEditor", "letterPriority", "letterConf", "letterNumber" })
        {
            Assert.Contains($"for=\"{name}\"", view);
            Assert.Contains($"id=\"{name}\"", view);
        }
        // SCRUM-59: the Letter partial is an adapter over the one editor of PEMS.UI; the editor code lives in pems-rte.js
        var rte = Web("Views", "Shared", "_RichTextEditorScripts.cshtml");
        Assert.Contains("data-pems-rte-for=\"@Model\"", rte);
        Assert.Contains("data-pems-rte-label=\"@Localizer[\"Rte_Editor\"]\"", rte);
        Assert.Contains("pems-rte.js", rte);
        Assert.DoesNotContain("<script>", rte);            // no inline editor code any more
        var js = SourceTree.Text(Path.Combine(Root(), "PEMS.UI", "wwwroot", "pems-rte.js"));
        Assert.Contains("nameToolbar", js);
        Assert.Contains("aria-label", js);
        Assert.Contains("insertBefore(holder, textarea.nextSibling)", js); // editor sits inside its field, not detached below the card
    }

    [Fact]
    public void AdminHeroes_DoNotRepeatPageTitleOrCarryDecorativeIcon()
    {
        var dir = Path.Combine(Root(), "PEMS.Web", "Areas", "Admin", "Views");
        foreach (var f in SourceTree.Files(dir, "*.cshtml"))
        {
            var t = SourceTree.Text(f);
            var i = t.IndexOf("admin-hero", StringComparison.Ordinal);
            if (i < 0) continue;
            var seg = t.Substring(i, Math.Min(700, t.Length - i));
            Assert.DoesNotContain("<h2>", seg);
            Assert.DoesNotContain("admin-hero-icon", seg);
        }
    }

    [Fact]
    public void PageTitles_AreLocalizedAtTheSharedHeader_AndEveryLiteralControllerTitleHasAPersianTranslation()
    {
        var header = Web("Views", "Shared", "_PageHeader.cshtml");
        Assert.Contains("Localizer[s]", header);
        Assert.Contains("ResourceNotFound", header);
        var fa = XDocument.Load(Path.Combine(Root(), "PEMS.Web", "Resources", "SharedResource.fa.resx")).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        var rx = new System.Text.RegularExpressions.Regex(@"ViewData\[""Title""\]\s*=\s*""([^""$]+)""\s*;");
        var missing = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "PEMS.Web", "Controllers"), "*.cs"))
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(SourceTree.Text(f)))
            {
                var t = m.Groups[1].Value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(t, "[\u0600-\u06FF]") && !fa.Contains(t)) missing.Add(t);
            }
        Assert.True(missing.Count == 0, "Controller page titles without a Persian translation: " + string.Join(", ", missing.Distinct()));
    }
}

