using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PEMS.Tests.Web;

/// <summary>
/// D21: تشخیص کلید ترجمه‌ی گمشده. هر <c>Localizer["..."]</c> ثابت در View/Controller باید در SharedResource.fa.resx باشد (وگرنه صفحه‌ی فارسی انگلیسی می‌شود و صفحه‌ها
/// «نیمه فارسی/نیمه انگلیسی» می‌مانند)؛ کلیدهای تکراری در resx هم نادیده گرفته می‌شوند و باید نباشند. بدون Baseline: صفر.
/// </summary>
public class LocalizationKeyCoverageTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Web() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "PEMS.Web"));

    private static readonly Regex Key = new(@"Localizer\[""((?:[^""\\]|\\.)*)""\s*[\],]", RegexOptions.Compiled);

    private static IEnumerable<string> SourceFiles()
    {
        var web = Web();
        foreach (var dir in new[] { "Views", "Areas", "Controllers", "Models", "Components", "Helpers" })
        {
            var path = Path.Combine(web, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cshtml") || f.EndsWith(".cs")))
                if (!f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    yield return f;
        }
    }

    [Fact]
    public void Every_constant_localizer_key_in_views_and_controllers_exists_in_the_persian_resource()
    {
        var fa = XDocument.Load(Path.Combine(Web(), "Resources", "SharedResource.fa.resx")).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles())
            foreach (Match m in Key.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value.Replace("\\\"", "\"");
                if (!fa.Contains(key)) missing.TryAdd(key, Path.GetRelativePath(Web(), file));
            }
        Assert.True(missing.Count == 0, "Localizer keys without a Persian entry (" + missing.Count + "):\n" + string.Join("\n", missing.Take(25).Select(kv => $"  \"{kv.Key}\"  [{kv.Value}]")));
    }

    [Theory]
    [InlineData("SharedResource.fa.resx")]
    [InlineData("SharedResource.resx")]
    public void Resource_files_have_no_duplicate_names(string file)
    {
        var names = XDocument.Load(Path.Combine(Web(), "Resources", file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToList();
        var dup = names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Duplicate resource names (the build ignores all but the first): " + string.Join(", ", dup.Take(20)));
    }

    [Fact]
    public void Persian_resource_values_are_never_empty()
    {
        var empty = XDocument.Load(Path.Combine(Web(), "Resources", "SharedResource.fa.resx")).Root!.Elements("data")
            .Where(e => string.IsNullOrWhiteSpace((string?)e.Element("value"))).Select(e => (string)e.Attribute("name")!).ToList();
        Assert.True(empty.Count == 0, "Empty Persian values: " + string.Join(", ", empty.Take(20)));
    }
}
