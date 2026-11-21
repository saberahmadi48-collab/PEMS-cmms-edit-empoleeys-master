using PEMS.Tests.TestSupport;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PEMS.Tests.Web;

/// <summary>
/// Language purity regression guards (English page = no accidental Persian UI; Persian page = no accidental English UI).
/// Rendered-DOM purity is measured by the browser audit (docs/design); these tests protect the resource layer and stop the
/// number of hard-coded UI strings in views from growing (a ratchet: it may only go down).
/// Exclusions (never treated as UI): technical identifiers, codes, manufacturer/model, imported business data, emails, URLs.
/// </summary>
public class LanguagePurityTests
{
    private static readonly Regex Persian = new(@"[\u0600-\u06FF]", RegexOptions.Compiled);
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
    private static string Here([CallerFilePath] string p = "") => p;

    private static Dictionary<string, string> Load(string file) => XDocument.Load(Path.Combine(Root(), "PEMS.Web", "Resources", file)).Root!.Elements("data")
        .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "", StringComparer.Ordinal);

    [Fact]
    public void EnglishResource_HasNoPersianValues_ExceptTheLanguageNameItself()
    {
        var en = Load("SharedResource.resx");
        var bad = en.Where(kv => Persian.IsMatch(kv.Value) && kv.Value != "فارسی").Select(kv => kv.Key).ToList();
        Assert.True(bad.Count == 0, "English resource values containing Persian text: " + string.Join(", ", bad.Take(20)));
    }

    [Fact]
    public void PersianLiteralKeys_HaveAnEnglishValue_AndAPersianSelfMapping()
    {
        // The neutral resx is English, so a Persian-text key falls back to English unless the FA file maps it to itself.
        var en = Load("SharedResource.resx"); var fa = Load("SharedResource.fa.resx");
        var missing = en.Keys.Where(k => Persian.IsMatch(k) && !fa.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Persian keys without a FA self-mapping: " + string.Join(", ", missing.Take(20)));
    }

    [Fact]
    public void EveryEnglishKey_HasAPersianCounterpart_WithPersianText()
    {
        var en = Load("SharedResource.resx"); var fa = Load("SharedResource.fa.resx");
        var acronymOnly = new Regex(@"^[^a-z\u0600-\u06FF]*$");   // identifiers / acronyms / punctuation may stay Latin
        var missing = en.Keys.Where(k => !Persian.IsMatch(k) && !fa.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "English keys without a Persian entry: " + string.Join(", ", missing.Take(20)));
        var notPersian = fa.Where(kv => !Persian.IsMatch(kv.Key) && !Persian.IsMatch(kv.Value) && kv.Value != kv.Key && !acronymOnly.IsMatch(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(notPersian.Count == 0, "Persian entries that are still English: " + string.Join(", ", notPersian.Take(20)));
    }

    /// <summary>Counts literal (non-Localizer) UI text nodes/attributes in views. The baseline may only go down.</summary>
    [Fact]
    public void HardCodedUiStrings_InViews_NeverGrowBeyondTheBaseline()
    {
        var node = new Regex(@">\s*([^<>@{}\n]{3,}?)\s*<", RegexOptions.Compiled);
        var attr = new Regex(@"(?:placeholder|title|aria-label)=""([^""@]{3,})""", RegexOptions.Compiled);
        var letters = new Regex(@"[A-Za-z\u0600-\u06FF]{3,}", RegexOptions.Compiled);
        var total = 0;
        var web = Path.Combine(Root(), "PEMS.Web");
        foreach (var f in SourceTree.Files(Path.Combine(web, "Views"), "*.cshtml")
                     .Concat(SourceTree.Files(Path.Combine(web, "Areas"), "*.cshtml")))
        {
            var t = SourceTree.Text(f);
            t = Regex.Replace(t, @"@\*.*?\*@", "", RegexOptions.Singleline);
            t = Regex.Replace(t, @"<script.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<style.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            total += node.Matches(t).Count(m => letters.IsMatch(m.Groups[1].Value) && !m.Groups[1].Value.TrimStart().StartsWith('&'));
            total += attr.Matches(t).Count(m => letters.IsMatch(m.Groups[1].Value));
        }
        var baseline = int.Parse(SourceTree.Text(Path.Combine(Root(), "docs", "design", "language-purity-baseline.txt")).Trim());
        Assert.True(total <= baseline, $"Hard-coded UI strings grew: {total} > baseline {baseline}. Use Localizer[...] (and add a Persian entry) instead of literal text.");
    }
}
