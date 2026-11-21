using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-600: the details page is data: the entity-details component definition, a record read through the permission-checked data source for params.id, a record-view that shows the fields.</summary>
public sealed class RecordViewTests : IDisposable
{
    private readonly FileDefinitionStore _files;
    private readonly PageRenderer _renderer;
    private readonly PageDefinitionKind _pageKind;
    private readonly Dictionary<string, string> _store = new();
    private readonly FakeRows _rows = new();

    private sealed class FakeRows : IGridDataProvider
    {
        public string Key => "fake";
        public IReadOnlyCollection<string>? AllowedRoles { get; set; }
        public List<GridDataRequest> Requests { get; } = [];
        public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var wanted = request.Grid.Filters.FirstOrDefault(f => f.Field == "thingId")?.Value;
            IReadOnlyList<object> items = wanted is null or "7" ? [new Dictionary<string, object?> { ["thingId"] = 7, ["code"] = "T-7", ["name"] = "<b>Pump</b>", ["isActive"] = true, ["created"] = new DateTime(2026, 11, 10) }] : [];
            return Task.FromResult((items, items.Count));
        }
    }

    private sealed class Allow(bool open) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(open);
    }

    private sealed class Overlay(IDefinitionStore inner, Dictionary<string, string> extra) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => extra.TryGetValue(kind + "/" + key, out var p) ? Task.FromResult<PublishedDefinition?>(new PublishedDefinition(kind, key, appKey, 1, p, "h")) : inner.GetPublishedAsync(kind, key, appKey, ct);
    }

    public RecordViewTests() : this(true) { }

    private RecordViewTests(bool open)
    {
        var provider = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        var registry = provider.GetRequiredService<IPemsComponentRegistry>();
        var catalog = provider.GetRequiredService<ComponentManifestCatalog>();
        var kinds = ShippedKinds.Build(registry, catalog);
        _pageKind = new PageDefinitionKind(registry, catalog);
        _files = new FileDefinitionStore(kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Path.Combine(AppContext.BaseDirectory, "definitions-system"), Watch = false }, FilesOwnership.For(kinds));
        var overlay = new Overlay(_files, _store);
        _store["component/entity-details"] = ShippedComponents.Payload("entity-details");
        _store["data-source/things-grid"] = "{\"kind\":\"grid\",\"provider\":\"fake\",\"parameters\":{}}";
        _renderer = new PageRenderer(provider.GetRequiredService<IPemsComponentRenderer>(), catalog, overlay, new DataSourceExecutor(overlay, new Allow(open), [_rows]));
    }

    public void Dispose() => _files.Dispose();

    private static readonly CultureInfo En = new("en");
    private const string Details = "{\"layout\":\"detail-page\",\"template\":\"entity-details\",\"args\":{\"id\":\"thing\",\"url\":\"/d/things-grid\",\"idField\":\"thingId\"," +
        "\"fields\":[{\"key\":\"code\",\"label\":{\"en\":\"Code\",\"fa\":\"کد\"},\"mono\":true},{\"key\":\"name\",\"label\":{\"en\":\"Name\",\"fa\":\"نام\"}}," +
        "{\"key\":\"isActive\",\"label\":{\"en\":\"Active\",\"fa\":\"فعال\"},\"type\":\"bool\"},{\"key\":\"created\",\"label\":{\"en\":\"Created\",\"fa\":\"ایجاد\"},\"type\":\"date\"}]," +
        "\"editUrl\":\"/f/things-edit\",\"editLabel\":{\"en\":\"Edit\",\"fa\":\"ویرایش\"}}}";

    private static ResolvedDefinition Page(string payload = Details) => new(new PublishedDefinition("page", "thing-details", "core", 1, "", "h", "{\"en\":\"Thing\",\"fa\":\"چیز\"}"), JsonNode.Parse(payload)!);
    private static PipelineRequest Request(string? id, params string[] roles) => new("page", "thing-details", "core", "en", roles, "hamid", id is null ? null : new Dictionary<string, string> { ["id"] = id });

    [Fact]
    public async Task The_page_shows_the_fields_of_the_record_of_the_request_with_the_edit_button_for_that_id()
    {
        var result = await _renderer.RenderAsync(Page(), Request("7", "Admin"), En);
        Assert.Empty(result.Problems);
        Assert.Contains("T-7", result.Html);
        Assert.Contains("Yes", result.Html);                              // the bool in the language of the request
        Assert.Contains("&lt;b&gt;Pump&lt;/b&gt;", result.Html);          // a value is text, never markup
        Assert.Contains("/f/things-edit/7", result.Html);
        var asked = Assert.Single(_rows.Requests);
        Assert.Equal(1, asked.Grid.PageSize);
        Assert.Contains(asked.Grid.Filters, f => f.Field == "thingId" && f.Value == "7");
    }

    [Fact]
    public async Task A_record_that_does_not_exist_or_a_request_without_an_id_shows_not_found_and_reads_nothing_it_should_not()
    {
        var missing = await _renderer.RenderAsync(Page(), Request("999"), En);
        Assert.Empty(missing.Problems);
        Assert.Contains("The record was not found.", missing.Html);

        var noId = await _renderer.RenderAsync(Page(), Request(null), En);
        Assert.Contains("The record was not found.", noId.Html);
        Assert.Single(_rows.Requests);                                   // only the first request read; no id, no read
    }

    private const string ChartPage = "{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"c\",\"component\":\"def:entity-chart\",\"options\":{\"url\":\"/d/things-grid\",\"labelField\":\"code\"," +
                                    "\"series\":[{\"field\":\"thingId\"}],\"ariaLabel\":\"Chart\"}}]}}";
    private const string MatrixPage = "{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"m\",\"component\":\"def:entity-matrix\",\"options\":{\"id\":\"fmea\",\"url\":\"/d/things-grid\"," +
                                      "\"rowField\":\"code\",\"columnField\":\"name\",\"valueField\":\"thingId\"}}]}}";

    // one rule for every part that reads a data source (record view, chart, matrix): no permission, no row, the part is hidden and a problem says why
    [Theory]
    [InlineData("details")]
    [InlineData("chart")]
    [InlineData("matrix")]
    public async Task A_user_who_may_not_read_the_data_source_gets_no_row_and_the_part_is_hidden_with_a_problem(string part)
    {
        using var closed = new RecordViewTests(false);
        closed.Ship("entity-chart"); closed.Ship("entity-matrix");
        var page = Page(part switch { "chart" => ChartPage, "matrix" => MatrixPage, _ => Details });
        var result = await closed._renderer.RenderAsync(page, Request("7"), En);
        Assert.DoesNotContain("T-7", result.Html);
        Assert.DoesNotContain("data-bk-chart", result.Html);
        Assert.DoesNotContain("pems-matrix", result.Html);
        Assert.Contains(result.Problems, p => p.Contains("not allowed"));
        Assert.Empty(closed._rows.Requests);
    }

    [Fact]
    public async Task A_crafted_id_cannot_widen_the_read_it_is_one_equality_filter_on_one_field()
    {
        await _renderer.RenderAsync(Page(), Request("7&f.name=like:x"), En);
        var asked = Assert.Single(_rows.Requests);
        Assert.Single(asked.Grid.Filters);
    }

    [Fact]
    public void The_binding_is_validated_and_tracked_as_a_reference_to_its_data_source()
    {
        const string page = "{\"layout\":\"detail-page\",\"regions\":{\"content\":[{\"id\":\"r\",\"component\":\"record-view\",\"options\":{\"fields\":[]}," +
                            "\"bindings\":{\"record\":{\"source\":\"/d/things-grid\",\"idField\":\"thingId\",\"id\":\"{{params.id}}\"}}}]}}";
        Assert.Empty(_pageKind.Validate(page, new DefinitionLimits()));
        Assert.Contains(_pageKind.References(page), r => r is { ToKind: "data-source", ToKey: "things-grid" });

        const string bad = "{\"layout\":\"detail-page\",\"regions\":{\"content\":[{\"id\":\"r\",\"component\":\"record-view\",\"options\":{\"fields\":[]},\"bindings\":{\"record\":{\"source\":\"http://x/y\"},\"other\":{}}}]}}";
        var errors = _pageKind.Validate(bad, new DefinitionLimits());
        Assert.Contains(errors, e => e.Pointer.EndsWith("/bindings/record/source"));
        Assert.Contains(errors, e => e.Pointer.EndsWith("/bindings/record/idField"));
        Assert.Contains(errors, e => e.Pointer.EndsWith("/bindings/other"));
    }

    [Fact]
    public async Task Request_values_can_be_put_into_any_option_text_and_an_unknown_one_is_empty()
    {
        var page = Page("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"a\",\"component\":\"alert\",\"options\":{\"title\":{\"en\":\"Item {{params.id}} / {{params.missing}} / {{params.id|url}}\",\"fa\":\"x\"},\"tone\":\"info\"}}]}}");
        var result = await _renderer.RenderAsync(page, Request("a b"), En);
        Assert.Empty(result.Problems);
        Assert.Contains("Item a b /  / a%20b", result.Html);
    }

    [Fact]
    public async Task A_binding_with_a_map_feeds_named_options_from_row_properties_so_any_component_can_show_a_value()
    {
        var page = Page("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"b\",\"component\":\"badge\",\"options\":{\"label\":\"x\"}," +
                        "\"bindings\":{\"record\":{\"source\":\"/d/things-grid\",\"idField\":\"thingId\",\"id\":\"{{params.id}}\",\"map\":{\"label\":\"code\"}}}}]}}");
        var result = await _renderer.RenderAsync(page, Request("7"), En);
        Assert.Empty(result.Problems);
        Assert.Contains("T-7", result.Html);
        Assert.Empty(_pageKind.Validate(page.Payload.ToJsonString(), new DefinitionLimits()));
        Assert.Contains(_pageKind.Validate("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"b\",\"component\":\"badge\",\"options\":{\"label\":\"x\"},\"bindings\":{\"record\":{\"source\":\"/d/a\",\"idField\":\"i\",\"id\":\"1\",\"map\":{\"label\":1},\"zzz\":1}}}]}}", new DefinitionLimits()), e => e.Pointer.EndsWith("/map"));
    }

    private void Ship(string key) => _store["component/" + key] = ShippedComponents.Payload(key);

    [Fact]
    public async Task The_entity_dashboard_repeats_a_tile_per_item_and_each_number_comes_from_the_row_the_user_may_read()
    {
        Ship("entity-dashboard");
        var page = Page("{\"layout\":\"dashboard-page\",\"template\":\"entity-dashboard\",\"args\":{\"url\":\"/d/things-grid\",\"idField\":\"thingId\",\"rowId\":\"7\"," +
                        "\"tiles\":[{\"key\":\"code\",\"label\":\"Code tile\",\"icon\":\"bi-tools\"},{\"key\":\"name\",\"label\":\"Name tile\"}]}}");
        var result = await _renderer.RenderAsync(page, Request(null), En);
        Assert.Empty(result.Problems);
        Assert.Contains("Code tile", result.Html);
        Assert.Contains("Name tile", result.Html);
        Assert.Contains("T-7", result.Html);
        Assert.Equal(2, _rows.Requests.Count);                              // one read per tile, each through the permission-checked source
    }

    private const string Views = "{\"layout\":\"list-page\",\"template\":\"entity-views\",\"args\":{\"id\":\"jobs\",\"url\":\"/d/things-grid\",\"fields\":{\"id\":\"thingId\",\"title\":\"name\",\"group\":\"code\",\"date\":\"created\"},\"kanban\":true,\"calendarView\":true}}";

    [Fact]
    public async Task The_entity_views_show_cards_by_default_and_the_view_of_the_request_otherwise_with_a_switcher_for_the_enabled_views_only()
    {
        Ship("entity-views");
        var def = await _renderer.RenderAsync(Page(Views), Request(null), En);
        Assert.Empty(def.Problems);
        Assert.Contains("pems-view--cards", def.Html);
        Assert.DoesNotContain("pems-view--kanban", def.Html);
        Assert.Contains("?view=kanban", def.Html);
        Assert.Contains("?view=calendar", def.Html);
        Assert.DoesNotContain("?view=gantt", def.Html);                       // not enabled: no view, no button

        var kanban = await _renderer.RenderAsync(Page(Views), new PipelineRequest("page", "x", "core", "en", [], "hamid", new Dictionary<string, string> { ["view"] = "kanban" }), En);
        Assert.Empty(kanban.Problems);
        Assert.Contains("pems-view--kanban", kanban.Html);
        Assert.DoesNotContain("pems-view--cards", kanban.Html);
    }

    [Fact]
    public async Task The_entity_chart_draws_the_rows_of_a_data_source()
    {
        Ship("entity-chart");
        var page = Page("{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"c\",\"component\":\"def:entity-chart\",\"options\":{\"url\":\"/d/things-grid\",\"labelField\":\"code\"," +
                        "\"series\":[{\"field\":\"thingId\",\"name\":{\"en\":\"Things\",\"fa\":\"چیزها\"}}],\"ariaLabel\":{\"en\":\"Things chart\",\"fa\":\"نمودار\"}}}]}}");
        // the fake source answers only for thingId=7: ask for the row through a sort-less read; the chart reads page 1 and gets nothing, so it shows the empty chart without a problem
        var open = await _renderer.RenderAsync(page, Request(null), En);
        Assert.Empty(open.Problems);
        Assert.Contains("data-bk-chart", open.Html);
        Assert.Contains("Things chart", open.Html);

    }

    [Fact]
    public async Task The_entity_matrix_builds_cells_from_the_source_and_posts_edits_only_with_an_action()
    {
        Ship("entity-matrix");
        string Matrix(string extra) => "{\"layout\":\"list-page\",\"regions\":{\"content\":[{\"id\":\"m\",\"component\":\"def:entity-matrix\",\"options\":{\"id\":\"fmea\",\"url\":\"/d/things-grid\",\"rowField\":\"code\",\"columnField\":\"name\",\"valueField\":\"thingId\",\"idField\":\"thingId\"" + extra + "}}]}}";
        var editable = await _renderer.RenderAsync(Page(Matrix(",\"actionUrl\":\"/a/fmea-cell\",\"cellType\":\"number\"")), Request(null), En);
        Assert.Empty(editable.Problems);
        Assert.Contains("data-pems-matrix=\"/a/fmea-cell\"", editable.Html);
        Assert.Contains("T-7", editable.Html);
        Assert.Contains("data-row=\"T-7\"", editable.Html);

        var readOnly = await _renderer.RenderAsync(Page(Matrix("")), Request(null), En);
        Assert.Empty(readOnly.Problems);
        Assert.DoesNotContain("data-row=", readOnly.Html);

    }
}
