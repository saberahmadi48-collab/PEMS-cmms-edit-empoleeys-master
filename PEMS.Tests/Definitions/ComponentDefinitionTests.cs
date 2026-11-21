using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-589: a component is a definition (a parameterized fragment of registered components); a page can be a template with arguments, an instance can use one inline.</summary>
public sealed class ComponentDefinitionTests : IDisposable
{
    private readonly FileDefinitionStore _files;
    private readonly ComponentDefinitionKind _kind;
    private readonly PageDefinitionKind _pageKind;
    private readonly PageRenderer _renderer;
    private readonly Dictionary<string, string> _components = new();

    private sealed class Overlay(IDefinitionStore inner, Dictionary<string, string> components) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => kind == "component" ? Task.FromResult(components.TryGetValue(key, out var p) ? new PublishedDefinition("component", key, appKey, 1, p, "h") : null) : inner.GetPublishedAsync(kind, key, appKey, ct);
    }

    public ComponentDefinitionTests()
    {
        var provider = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        var registry = provider.GetRequiredService<IPemsComponentRegistry>();
        var catalog = provider.GetRequiredService<ComponentManifestCatalog>();
        _kind = new ComponentDefinitionKind(registry);
        _pageKind = new PageDefinitionKind(registry, catalog);
        var kinds = ShippedKinds.Build(registry, catalog);
        _files = new FileDefinitionStore(kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Path.Combine(AppContext.BaseDirectory, "definitions-system"), Watch = false }, FilesOwnership.For(kinds));
        _renderer = new PageRenderer(provider.GetRequiredService<IPemsComponentRenderer>(), catalog, new Overlay(_files, _components));
    }

    public void Dispose() => _files.Dispose();

    private static IReadOnlyList<DefinitionFieldError> Validate(ComponentDefinitionKind k, string payload) => k.Validate(payload, new DefinitionLimits());

    private const string Alert = "{\"id\":\"a\",\"component\":\"alert\",\"options\":{\"title\":{\"en\":\"${title}\",\"fa\":\"${title}\"},\"tone\":\"info\"}}";

    // ---- validation

    [Fact]
    public void A_component_definition_with_declared_parameters_is_valid_and_lists_what_it_renders()
    {
        var payload = "{\"parameters\":{\"title\":{\"type\":\"string\",\"required\":true}},\"instances\":[" + Alert + "]}";
        Assert.Empty(Validate(_kind, payload));
        Assert.Equal(new DefinitionRef("component", "alert", "renders"), Assert.Single(_kind.References(payload)));
    }

    [Theory]
    [InlineData("{\"parameters\":{},\"instances\":[],\"regions\":{}}", "")]                                                   // both
    [InlineData("{\"parameters\":{}}", "")]                                                                                   // neither
    [InlineData("{\"parameters\":{\"x\":{\"type\":\"widget\"}},\"instances\":[]}", "/parameters/x/type")]                       // unknown type
    [InlineData("{\"parameters\":{\"x\":{\"type\":\"number\",\"default\":\"a\"}},\"instances\":[]}", "/parameters/x/default")]  // default does not fit
    [InlineData("{\"parameters\":{},\"instances\":[{\"id\":\"a\",\"component\":\"alert\",\"options\":{\"title\":\"${nope}\"}}]}", "/instances/0/options/title")] // undeclared placeholder
    [InlineData("{\"parameters\":{},\"instances\":[{\"id\":\"a\",\"component\":\"no-such\"}]}", "/instances/0/component")]       // unknown component
    [InlineData("{\"parameters\":{},\"instances\":[{\"id\":\"a\",\"component\":\"alert\"},{\"id\":\"a\",\"component\":\"alert\"}]}", "/instances/1/id")] // duplicate id
    [InlineData("{\"parameters\":{},\"instances\":[],\"extra\":1}", "/extra")]
    public void A_bad_component_definition_is_refused_at_the_pointer_of_the_problem(string payload, string pointer)
        => Assert.Contains(Validate(_kind, payload), e => e.Pointer == pointer);

    [Fact]
    public void A_component_may_use_another_component_definition_by_def_key()
    {
        var payload = "{\"parameters\":{},\"instances\":[{\"id\":\"x\",\"component\":\"def:other\",\"options\":{}}]}";
        Assert.Empty(Validate(_kind, payload));
        Assert.Equal(new DefinitionRef("component-definition", "other", "renders"), Assert.Single(_kind.References(payload)));
    }

    [Fact]
    public void A_page_may_be_a_template_with_arguments_and_then_needs_no_regions()
    {
        const string page = "{\"layout\":\"list-page\",\"template\":\"entity-list\",\"args\":{\"id\":\"x\"}}";
        Assert.Empty(_pageKind.Validate(page, new DefinitionLimits()));
        Assert.Contains(new DefinitionRef("component-definition", "entity-list", "templates"), _pageKind.References(page));   // the layout of the page is a reference too (SCRUM-451)
        Assert.Contains(_pageKind.Validate("{\"layout\":\"list-page\",\"args\":{}}", new DefinitionLimits()), e => e.Pointer == "/args");
        Assert.Contains(_pageKind.Validate("{\"layout\":\"list-page\"}", new DefinitionLimits()), e => e.Pointer == "/regions");
    }

    // ---- expansion

    [Fact]
    public void A_text_that_is_only_the_placeholder_keeps_the_type_of_the_value_and_an_unset_optional_parameter_drops_the_property()
    {
        var def = (JsonObject)JsonNode.Parse("{\"parameters\":{\"rows\":{\"type\":\"array\",\"required\":true},\"on\":{\"type\":\"boolean\",\"default\":true},\"note\":{\"type\":\"string\"}}," +
            "\"instances\":[{\"id\":\"a\",\"component\":\"x\",\"options\":{\"rows\":\"${rows}\",\"on\":\"${on}\",\"note\":\"${note}\",\"label\":\"n=${note}!\"}}]}")!;
        var list = ComponentTemplate.ExpandInstances(def, (JsonObject)JsonNode.Parse("{\"rows\":[1,2]}")!, "p");
        var options = (JsonObject)list[0]!["options"]!;
        Assert.Equal(2, ((JsonArray)options["rows"]!).Count);
        Assert.True((bool)options["on"]!);
        Assert.False(options.ContainsKey("note"));
        Assert.Equal("n=!", (string)options["label"]!);
        Assert.Equal("p.a", (string)list[0]!["id"]!);   // child ids are prefixed by the instance that uses the component
    }

    [Fact]
    public void A_missing_required_parameter_a_wrong_type_and_an_unknown_argument_are_refused()
    {
        var def = (JsonObject)JsonNode.Parse("{\"parameters\":{\"n\":{\"type\":\"number\",\"required\":true}},\"instances\":[]}")!;
        Assert.Throws<ComponentTemplateException>(() => ComponentTemplate.Bind(def, new JsonObject()));
        Assert.Throws<ComponentTemplateException>(() => ComponentTemplate.Bind(def, (JsonObject)JsonNode.Parse("{\"n\":\"text\"}")!));
        Assert.Throws<ComponentTemplateException>(() => ComponentTemplate.Bind(def, (JsonObject)JsonNode.Parse("{\"n\":1,\"zzz\":2}")!));
    }

    [Fact]
    public void An_instance_with_if_is_kept_only_when_the_parameter_is_set()
    {
        var def = (JsonObject)JsonNode.Parse("{\"parameters\":{\"url\":{\"type\":\"string\"}},\"regions\":{\"toolbar\":[{\"id\":\"b\",\"if\":\"${url}\",\"component\":\"button\",\"options\":{\"href\":\"${url}\"}}]}}")!;
        Assert.Empty((JsonArray)ComponentTemplate.ExpandRegions(def, new JsonObject())["toolbar"]!);
        var set = (JsonArray)ComponentTemplate.ExpandRegions(def, (JsonObject)JsonNode.Parse("{\"url\":\"/f/x\"}")!)["toolbar"]!;
        Assert.Equal("/f/x", (string)set[0]!["options"]!["href"]!);
        Assert.False(((JsonObject)set[0]!).ContainsKey("if"));
    }

    // ---- rendering

    private static readonly CultureInfo En = new("en");
    private static PipelineRequest Request() => new("page", "demo", "core", "en", [], "hamid");
    private static ResolvedDefinition Page(string payload) => new(new PublishedDefinition("page", "demo", "core", 1, "", "h", "{\"en\":\"Demo\",\"fa\":\"نمونه\"}"), JsonNode.Parse(payload)!);

    [Fact]
    public async Task An_instance_can_use_a_component_definition_inline_and_two_uses_do_not_collide()
    {
        _components["notice"] = "{\"parameters\":{\"title\":{\"type\":\"string\",\"required\":true}},\"instances\":[" + Alert + "]}";
        var page = Page("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"one\",\"component\":\"def:notice\",\"options\":{\"title\":\"First notice\"}},{\"id\":\"two\",\"component\":\"def:notice\",\"options\":{\"title\":\"Second notice\"}}]}}");
        var result = await _renderer.RenderAsync(page, Request(), En);
        Assert.Empty(result.Problems);
        Assert.Contains("First notice", result.Html);
        Assert.Contains("Second notice", result.Html);
    }

    [Fact]
    public async Task A_page_template_supplies_the_regions_and_the_pages_own_regions_come_after_them()
    {
        _components["frame"] = "{\"parameters\":{\"title\":{\"type\":\"string\",\"required\":true}},\"regions\":{\"content\":[" + Alert + "]}}";
        var page = Page("{\"layout\":\"list-page\",\"template\":\"frame\",\"args\":{\"title\":\"From the template\"},\"regions\":{\"content\":[" +
                        "{\"id\":\"own\",\"component\":\"alert\",\"options\":{\"title\":{\"en\":\"From the page\",\"fa\":\"از صفحه\"},\"tone\":\"info\"}}]}}");
        var result = await _renderer.RenderAsync(page, Request(), En);
        Assert.Empty(result.Problems);
        Assert.True(result.Html.IndexOf("From the template", StringComparison.Ordinal) < result.Html.IndexOf("From the page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_template_a_bad_argument_and_a_self_nesting_component_become_problems_never_a_broken_page()
    {
        var missing = await _renderer.RenderAsync(Page("{\"layout\":\"list-page\",\"template\":\"nope\"}"), Request(), En);
        Assert.Contains(missing.Problems, p => p.Contains("\"nope\" does not exist"));

        _components["frame"] = "{\"parameters\":{\"title\":{\"type\":\"string\",\"required\":true}},\"regions\":{\"content\":[]}}";
        var bad = await _renderer.RenderAsync(Page("{\"layout\":\"list-page\",\"template\":\"frame\",\"args\":{}}"), Request(), En);
        Assert.Contains(bad.Problems, p => p.Contains("'title'") || p.Contains("title"));

        _components["loop"] = "{\"parameters\":{},\"instances\":[{\"id\":\"again\",\"component\":\"def:loop\",\"options\":{}}]}";
        var loop = await _renderer.RenderAsync(Page("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"l\",\"component\":\"def:loop\",\"options\":{}}]}}"), Request(), En);
        Assert.Contains(loop.Problems, p => p.Contains("nested too deep"));
    }

    [Fact]
    public async Task The_shipped_entity_list_component_renders_a_grid_over_the_data_source_and_a_new_button_only_with_a_url()
    {
        _components["entity-list"] = ShippedComponents.Payload("entity-list");
        var with = await _renderer.RenderAsync(Page("{\"layout\":\"list-page\",\"template\":\"entity-list\",\"args\":{\"id\":\"things\",\"url\":\"/d/things-grid\",\"idField\":\"thingId\",\"newUrl\":\"/f/things-create\"," +
                                                   "\"newLabel\":{\"en\":\"New thing\",\"fa\":\"جدید\"},\"columns\":[{\"key\":\"name\",\"header\":{\"en\":\"Name\",\"fa\":\"نام\"},\"width\":200}]}}"), Request(), En);
        Assert.Empty(with.Problems);
        Assert.Contains("/d/things-grid", with.Html);
        Assert.Contains("/f/things-create", with.Html);
        var without = await _renderer.RenderAsync(Page("{\"layout\":\"list-page\",\"template\":\"entity-list\",\"args\":{\"id\":\"things\",\"url\":\"/d/things-grid\",\"idField\":\"thingId\",\"columns\":[]}}"), Request(), En);
        Assert.Empty(without.Problems);
        Assert.DoesNotContain("/f/things-create", without.Html);
    }

    [Fact]
    public async Task The_entity_list_passes_the_grid_settings_saved_views_density_page_size_bulk_actions_and_pinned_columns_through()
    {
        _components["entity-list"] = ShippedComponents.Payload("entity-list");
        var page = Page("{\"layout\":\"list-page\",\"template\":\"entity-list\",\"args\":{\"id\":\"things\",\"url\":\"/d/things-grid\",\"idField\":\"thingId\",\"viewsKey\":\"things-saved\",\"density\":\"compact\",\"pageSize\":250,\"selectable\":true," +
                        "\"bulkActions\":[{\"key\":\"del\",\"label\":{\"en\":\"Remove\",\"fa\":\"حذف\"},\"url\":\"/a/things-delete\"}]," +
                        "\"columns\":[{\"key\":\"code\",\"header\":{\"en\":\"Code\",\"fa\":\"کد\"},\"width\":120,\"pinned\":\"start\"}]}}");
        var result = await _renderer.RenderAsync(page, Request(), En);
        Assert.Empty(result.Problems);
        Assert.Contains("things-saved", result.Html);
        Assert.Contains("compact", result.Html);
        Assert.Contains("/a/things-delete", result.Html);
    }
}
