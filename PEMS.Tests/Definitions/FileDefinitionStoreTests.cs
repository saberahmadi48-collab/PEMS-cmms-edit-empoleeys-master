using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-572 (ADR-0014): the definition files are checked at load, texts need both languages, a change is picked up, a broken edit keeps the last good version.</summary>
public sealed class FileDefinitionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "defs-" + Guid.NewGuid());

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static IDefinitionKind[] Kinds()
    {
        var registry = new PemsComponentRegistry();
        return ShippedKinds.Build(registry, new ComponentManifestCatalog(registry));
    }

    private FileDefinitionStore Store(bool failOnInvalid = true) => new(Kinds(), new DefinitionLimits(), new DefinitionFileOptions { Folder = _folder, FailOnInvalid = failOnInvalid, Watch = false }, FilesOwnership.For(Kinds()));

    private void Write(string kind, string key, string json, string appKey = "core")
    {
        var path = Path.Combine(_folder, kind, appKey, key + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private const string OneRegion = "{\"tag\":\"div\",\"children\":[{\"region\":\"content\",\"required\":true}]}";

    private static string Layout(string key = "demo", string fa = "نمونه", string en = "Demo", string root = OneRegion)
        => $"{{\"kind\":\"layout\",\"key\":\"{key}\",\"appKey\":\"core\",\"title\":{{\"fa\":\"{fa}\",\"en\":\"{en}\"}},\"payload\":{{\"root\":{root}}}}}";

    [Fact]
    public async Task A_valid_file_is_served_with_its_payload_version_and_hash()
    {
        Write("layout", "demo", Layout());
        using var store = Store();
        var d = await store.GetPublishedAsync("layout", "demo");
        Assert.NotNull(d);
        Assert.Equal((1, "core"), (d!.VersionNumber, d.AppKey));
        Assert.Contains("\"region\":\"content\"", d.PayloadJson);
        Assert.Equal(64, d.ContentHash.Length);
        Assert.Empty(store.Problems);
        Assert.Null(await store.GetPublishedAsync("layout", "missing"));
    }

    [Theory]
    [InlineData("", "Demo")]
    [InlineData("نمونه", "")]
    public void A_text_without_both_languages_stops_the_start(string fa, string en)
    {
        Write("layout", "demo", Layout(fa: fa, en: en));
        var ex = Assert.Throws<InvalidOperationException>(() => Store());
        Assert.Contains("translation", ex.Message);
    }

    [Fact]
    public void A_file_in_the_wrong_place_an_unknown_kind_and_bad_json_are_all_reported()
    {
        Write("layout", "other", Layout("demo"));                       // the name does not match the key
        Write("widget", "x", "{\"kind\":\"widget\",\"key\":\"x\",\"title\":{\"fa\":\"a\",\"en\":\"a\"},\"payload\":{}}");
        Write("layout", "broken", "{ not json");
        using var store = Store(failOnInvalid: false);
        var all = string.Join("|", store.Problems.Select(p => p.File + p.Pointer + ":" + p.Message));
        Assert.Contains("layout/core/other.json", all);
        Assert.Contains("must be layout/core/demo.json", all);
        Assert.Contains("one owner per kind", all);      // "widget" has no owner
        Assert.Contains("Not valid JSON", all);
        Assert.Empty(store.All);
    }

    [Fact]
    public void The_payload_is_checked_by_its_kind()
    {
        Write("layout", "demo", Layout(root: "{\"tag\":\"script\",\"children\":[{\"region\":\"content\"}]}"));
        using var store = Store(failOnInvalid: false);
        Assert.Contains(store.Problems, p => p.File == "layout/core/demo.json" && p.Pointer.StartsWith("/payload/root/tag"));
    }

    [Fact]
    public async Task A_change_is_picked_up_and_a_broken_edit_keeps_the_last_good_version()
    {
        Write("layout", "demo", Layout());
        using var store = Store(failOnInvalid: false);
        var generation = store.Generation;
        Write("layout", "demo", Layout(root: "{\"tag\":\"section\",\"children\":[{\"region\":\"content\"},{\"region\":\"side\"}]}"));
        store.Reload();
        var changed = await store.GetPublishedAsync("layout", "demo");
        Assert.Contains("side", changed!.PayloadJson);
        Assert.True(store.Generation > generation);

        Write("layout", "demo", Layout(en: ""));                         // a broken edit
        store.Reload();
        Assert.Single(store.Problems);
        Assert.Contains("side", (await store.GetPublishedAsync("layout", "demo"))!.PayloadJson);   // still served

        Write("layout", "demo", Layout());                               // fixed
        store.Reload();
        Assert.Empty(store.Problems);
    }

    [Fact]
    public async Task The_watcher_reloads_by_itself()
    {
        Write("layout", "demo", Layout());
        using var store = new FileDefinitionStore(Kinds(), new DefinitionLimits(), new DefinitionFileOptions { Folder = _folder, Watch = true }, FilesOwnership.For(Kinds()));
        var reloaded = new TaskCompletionSource();
        store.Reloaded += () => reloaded.TrySetResult();
        Write("layout", "demo", Layout(root: "{\"tag\":\"div\",\"children\":[{\"region\":\"content\"},{\"region\":\"extra\"}]}"));
        Assert.True(await Task.WhenAny(reloaded.Task, Task.Delay(5000)) == reloaded.Task, "the watcher did not reload within 5 seconds");
        Assert.Contains("extra", (await store.GetPublishedAsync("layout", "demo"))!.PayloadJson);
    }

    [Fact]
    public void Every_shipped_file_is_valid_and_the_four_layouts_are_there()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "definitions-system");
        Assert.True(Directory.Exists(shipped), "the definitions folder was not copied next to the application: " + shipped);
        using var store = new FileDefinitionStore(Kinds(), new DefinitionLimits(), new DefinitionFileOptions { Folder = shipped, Watch = false }, FilesOwnership.For(Kinds()));
        Assert.Empty(store.Problems);
        Assert.Equal(new[] { "dashboard-page", "detail-page", "form-page", "list-page", "print-page" }, store.All.Where(d => d.Kind == "layout").Select(d => d.Key));
        // SCRUM-437/578 pilot: the department create form, its data source and its action
        Assert.Contains(store.All, d => d.Kind == "form" && d.Key == "departments-create");
        Assert.Contains(store.All, d => d.Kind == "data-source" && d.Key == "departments-grid");
        Assert.Contains(store.All, d => d.Kind == "action" && d.Key == "departments-create");
    }
}
