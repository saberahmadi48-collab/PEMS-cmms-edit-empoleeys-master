using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-433 / #433: every component describes itself from the code; stored options JSON is checked against that description.</summary>
public sealed class ComponentManifestTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private ComponentManifestCatalog _catalog = default!;
    private IPemsComponentRenderer _renderer = default!;
    private IPemsComponentRegistry _registry = default!;

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        _catalog = _provider.GetRequiredService<ComponentManifestCatalog>();
        _registry = _provider.GetRequiredService<IPemsComponentRegistry>();
        _renderer = _provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static string Here([CallerFilePath] string p = "") => p;
    private static string SnapshotDir => Path.Combine(Path.GetDirectoryName(Here())!, "Manifests");

    public static IEnumerable<object[]> Keys() =>
        new[] { "matrix", "record-view", "button", "badge", "skeleton", "field", "table", "form", "empty-state", "error-state", "loading-state", "form-section", "action-bar", "alert", "modal", "drawer", "confirm-dialog", "tabs", "breadcrumb", "stepper", "workflow-history", "uploader", "comments-thread", "audit-trail", "action-button", "workflow-action-bar", "menu", "side-panel-nav", "card", "progress", "accordion", "accordion-item", "list-group", "spinner", "info-grid", "info-item", "kpi-tile", "list-page", "list-panel", "list-toolbar", "list-icon-tool", "list-search", "list-tools", "list-filters", "list-filter-select", "list-popover", "list-kpi-toggle", "list-export", "list-reset-columns", "list-new", "list-kpis", "list-chips", "list-chip", "list-grid", "list-table", "list-footer", "list-bulk", "list-menu", "related-list", "related-table", "related-add", "related-remove", "related-row-form", "related-save", "related-act", "tree", "tree-item", "chart", "date", "button-frame", "dropdown-menu", "nav", "crumb-trail", "table-frame", "list-group-item", "toast", "script-templates", "input", "select", "textarea", "form-label", "validation-summary", "icon", "row", "col", "check-row", "form-group", "tree-select", "data-grid", "data-view", "pager", "content-card", "page-shell", "flex", "help-text", "table-scroll", "tab-content", "tab-pane", "tab", "nav-link", "heading", "text", "link", "bullet-list", "bullet", "divider", "stage-diagram", "side-link", "modal-header", "modal-body", "modal-footer", "stat", "stat-grid", "metric", "page-hero", "data-table", "form-frame" }.Select(k => new object[] { k });

    [Fact]
    public void Every_registered_component_has_a_manifest_and_the_expected_keys_exist()
    {
        var keys = _registry.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var expected in Keys().Select(o => (string)o[0])) Assert.Contains(expected, keys);
        foreach (var key in keys)
        {
            var m = _catalog.Get(key);                               // throws when the attribute is missing or the key differs
            Assert.False(string.IsNullOrWhiteSpace(m.Category));
            Assert.StartsWith("component.", m.NameKey, StringComparison.Ordinal);
            Assert.NotNull(m.Sample);
        }
        Assert.Equal(keys.Count, _catalog.All().Count);
    }

    [Fact]
    public void A_component_without_the_attribute_has_no_manifest_and_the_generator_says_so()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ComponentManifestGenerator.Generate("plain", typeof(Unannotated)));
        Assert.Contains("[PemsComponent]", ex.Message, StringComparison.Ordinal);
    }

    private sealed class Unannotated : Microsoft.AspNetCore.Components.ComponentBase { }

    [Theory, MemberData(nameof(Keys))]
    public async Task The_sample_validates_and_renders_in_both_languages(string key)
    {
        var sample = _catalog.Get(key).Sample!.ToJsonString();
        var result = _catalog.Validate(key, sample);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Pointer + " " + e.Code)));
        foreach (var culture in new[] { "en", "fa" })
        {
            var html = await _renderer.RenderAsync(key, _catalog.ToParameters(key, sample), new CultureInfo(culture));
            Assert.False(string.IsNullOrWhiteSpace(html));
        }
    }

    [Theory, MemberData(nameof(Keys))]
    public void The_generated_manifest_matches_the_approved_snapshot(string key)
    {
        var actual = JsonSerializer.Serialize(_catalog.Get(key), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).Replace("\r\n", "\n");
        var path = Path.Combine(SnapshotDir, key + ".json");
        if (Environment.GetEnvironmentVariable("PEMS_GOLDEN_RECORD") == "1") { File.WriteAllText(path, actual); return; }
        Assert.True(File.Exists(path), "Snapshot missing: " + path + " (record it with PEMS_GOLDEN_RECORD=1)");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    [Fact]
    public void Invalid_options_are_rejected_with_one_error_per_field()
    {
        var result = _catalog.Validate("button", """{"tone":"blue","size":"huge","extra":1}""");
        Assert.False(result.IsValid);
        var by = result.Errors.ToDictionary(e => e.Pointer, e => e.Code);
        Assert.Equal("required", by["/label"]);                      // a missing property gets its own pointer
        Assert.Equal("enum", by["/tone"]);
        Assert.Equal("enum", by["/size"]);
        Assert.Contains(result.Errors, e => e.Code == "unknown-property");
        Assert.All(result.Errors, e => Assert.StartsWith("validation.", e.MessageKey, StringComparison.Ordinal));
    }

    [Fact]
    public void The_translatable_text_shape_accepts_a_string_or_a_language_map_and_nothing_else()
    {
        Assert.True(_catalog.Validate("badge", """{"label":"Active"}""").IsValid);
        Assert.True(_catalog.Validate("badge", """{"label":{"en":"Active","fa":"فعال"}}""").IsValid);
        Assert.False(_catalog.Validate("badge", """{"label":42}""").IsValid);
        Assert.False(_catalog.Validate("badge", """{"label":{"en":1}}""").IsValid);
        Assert.False(_catalog.Validate("badge", """{"label":{}}""").IsValid);
    }

    [Fact]
    public void Components_that_take_several_parameters_expect_an_object_of_parameters()
    {
        var m = _catalog.Get("form");
        Assert.Null(m.OptionsParameter);
        var empty = _catalog.Validate("form", "{}");
        Assert.Contains(empty.Errors, e => e.Pointer == "/definition" && e.Code == "required");
        Assert.True(_catalog.Get("button").OptionsParameter == "Options");
    }

    [Fact]
    public void Malformed_json_is_reported_not_thrown()
    {
        var r = _catalog.Validate("button", "{not json");
        Assert.False(r.IsValid);
        Assert.Equal("malformed", r.Errors[0].Code);
        Assert.Throws<ArgumentException>(() => _catalog.ToParameters("button", "{}"));
    }

    [Fact]
    public void Unknown_component_keys_fail_clearly()
    {
        Assert.Throws<KeyNotFoundException>(() => _catalog.Get("does-not-exist"));
    }
}
