using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;
using PEMS.Persistence.Context;
using PEMS.Persistence.Definitions;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.UI.Abstractions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-579, 417 and 52 (the cutover, ADR-0014): the definitions that ship with the product are installed into the definition tables as System definitions (fresh install and upgrade are the same, idempotent
/// code), a customer's change to one is never overwritten, the engines read the database and produce exactly what they produced from the files, and a form created or translated in the database is served
/// on the next request.
/// </summary>
public sealed class DefinitionCutoverTests
{
    private static readonly string Package = Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName);

    private sealed class Fixture
    {
        private readonly string _database = "cutover-" + Guid.NewGuid();
        private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        public IServiceProvider Ui { get; } = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        public IDefinitionKind[] Kinds { get; }

        public Fixture()
        {
            var registry = Ui.GetRequiredService<IPemsComponentRegistry>();
            var catalog = Ui.GetRequiredService<ComponentManifestCatalog>();
            Kinds = ShippedKinds.Build(registry, catalog);
        }

        public PEMSDbContext NewDb() => PemsDbContextFactory.CreateInMemory(_database);

        public (DefinitionService Service, DefinitionStore Store, PEMSDbContext Db) NewService()
        {
            var db = NewDb();
            var store = new DefinitionStore(db, _cache);
            return (new DefinitionService(db, Kinds, new DefinitionLimits(), store), store, db);
        }

        public IReadOnlyList<ShippedDefinition> PackageItems() => SystemDefinitionSeeder.LoadPackage(Kinds, new DefinitionLimits(), Package);
    }

    private sealed class Recording : IActionHandler
    {
        public string Key => "department.create";
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct) => Task.FromResult(ActionOutcome.Done());
    }

    private sealed class Departments : IDataSourceProvider
    {
        public string Key => "departments";
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct) => Task.FromResult<IReadOnlyList<FieldOption>>([new() { Value = "7", Label = TranslatableText.Literal("Engineering") }]);
    }

    // ---- shipping

    [Fact]
    public async Task A_fresh_install_gets_every_shipped_definition_as_a_published_system_definition()
    {
        var f = new Fixture();
        var items = f.PackageItems();
        Assert.True(items.Count >= 7);   // four layouts and the three pilot definitions
        var (service, store, db) = f.NewService();
        var result = await service.ShipAsync(items, "system");
        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.Equal((items.Count, 0, 0, 0, 0), (result.Created, result.Upgraded, result.Unchanged, result.KeptCustomized, result.CustomerOwned));

        var rows = await db.Definitions.Include(d => d.Versions).ToListAsync();
        Assert.All(rows, d =>
        {
            Assert.Equal(DefinitionOrigin.System, d.Origin);
            Assert.NotNull(d.PublishedVersionId);
            Assert.Equal(d.PublishedVersionId, d.ShippedVersionId);
            Assert.False(d.IsCustomized);
            Assert.Equal(DefinitionStatus.Published, d.Versions.Single().Status);
        });
        foreach (var item in items)
        {
            var read = await store.GetPublishedAsync(item.Kind, item.Key, item.AppKey);
            Assert.NotNull(read);
            Assert.False(string.IsNullOrEmpty(read!.TitleJson));      // the runtime read side carries the title (page host, form host)
        }
    }

    [Fact]
    public async Task Shipping_again_changes_nothing_so_install_and_every_start_are_the_same_code()
    {
        var f = new Fixture();
        var items = f.PackageItems();
        var (service, _, db) = f.NewService();
        await service.ShipAsync(items, "system");
        var before = await db.DefinitionVersions.CountAsync();
        var again = await f.NewService().Service.ShipAsync(items, "system");
        Assert.Equal((0, 0, items.Count), (again.Created, again.Upgraded, again.Unchanged));
        Assert.Equal(before, await db.DefinitionVersions.CountAsync());
    }

    [Fact]
    public async Task A_new_shipped_version_upgrades_the_definition_nobody_changed()
    {
        var f = new Fixture();
        var items = f.PackageItems().ToList();
        var (service, store, db) = f.NewService();
        await service.ShipAsync(items, "system");

        var i = items.FindIndex(x => x.Kind == "layout" && x.Key == "list-page");
        var payload = JsonNode.Parse(items[i].PayloadJson)!.AsObject();
        payload["root"]!["css"] = "lp-page lp-page--upgraded";
        items[i] = items[i] with { PayloadJson = payload.ToJsonString() };

        var result = await f.NewService().Service.ShipAsync(items, "system");
        Assert.Equal((0, 1, items.Count - 1), (result.Created, result.Upgraded, result.Unchanged));
        var row = await f.NewDb().Definitions.Include(d => d.Versions).SingleAsync(d => d.Kind == "layout" && d.Key == "list-page");   // a fresh context: the first one still tracks the old state
        Assert.Equal(2, row.Versions.Count);
        Assert.Equal(row.Versions.Single(v => v.VersionNumber == 2).DefinitionVersionId, row.PublishedVersionId);
        Assert.Equal(row.PublishedVersionId, row.ShippedVersionId);
        Assert.False(row.IsCustomized);
        Assert.Contains("lp-page--upgraded", (await store.GetPublishedAsync("layout", "list-page"))!.PayloadJson);   // the next read sees it (stamp evicted)
    }

    [Fact]
    public async Task A_definition_a_customer_changed_is_never_overwritten_by_an_upgrade()
    {
        var f = new Fixture();
        var items = f.PackageItems().ToList();
        var (service, store, db) = f.NewService();
        await service.ShipAsync(items, "system");

        // the customer opens a draft of the shipped layout, changes it and publishes
        var def = await db.Definitions.AsNoTracking().SingleAsync(d => d.Kind == "layout" && d.Key == "list-page");
        var draft = (await service.CreateDraftAsync(def.DefinitionId, null, def.Revision, "customer")).Value!;
        var custom = JsonNode.Parse(draft.PayloadJson)!.AsObject();
        custom["root"]!["css"] = "customer-page";
        var saved = (await service.SaveDraftAsync(def.DefinitionId, custom.ToJsonString(), "mine", draft.Info.Revision, "customer")).Value!;
        var validated = await service.ValidateAsync(def.DefinitionId, saved.Info.Revision, "customer");
        Assert.True(validated.Success, string.Join(",", validated.Errors?.Select(e => e.Message) ?? []));
        var published = await service.PublishAsync(def.DefinitionId, validated.Value!.Info.Revision, "customer");
        Assert.True(published.Success);
        Assert.True(published.Value!.Summary.IsCustomized);

        // the product ships a new version of that layout
        var i = items.FindIndex(x => x.Kind == "layout" && x.Key == "list-page");
        var next = JsonNode.Parse(items[i].PayloadJson)!.AsObject();
        next["root"]!["css"] = "lp-page lp-page--v2";
        items[i] = items[i] with { PayloadJson = next.ToJsonString() };
        var result = await f.NewService().Service.ShipAsync(items, "system");

        Assert.Equal((0, 0, 1), (result.Upgraded, result.Created, result.KeptCustomized));
        Assert.Contains("customer-page", (await store.GetPublishedAsync("layout", "list-page"))!.PayloadJson);   // still the customer's
        var row = await db.Definitions.Include(d => d.Versions).AsNoTracking().SingleAsync(d => d.Kind == "layout" && d.Key == "list-page");
        Assert.True(row.IsCustomized);
        var shipped = row.Versions.Single(v => v.DefinitionVersionId == row.ShippedVersionId);
        Assert.Equal(DefinitionStatus.Retired, shipped.Status);            // kept to compare with or roll back to
        Assert.Contains("lp-page--v2", shipped.PayloadJson);
    }

    // SCRUM-448: what a product update did to a definition the customer changed, and the way back
    private static async Task<(Fixture F, List<ShippedDefinition> Items, int Id)> CustomisedLayoutAsync(bool shipUpgrade)
    {
        var f = new Fixture();
        var items = f.PackageItems().ToList();
        var (service, _, db) = f.NewService();
        await service.ShipAsync(items, "system");
        var def = await db.Definitions.AsNoTracking().SingleAsync(d => d.Kind == "layout" && d.Key == "list-page");
        var draft = (await service.CreateDraftAsync(def.DefinitionId, null, def.Revision, "customer")).Value!;
        var custom = JsonNode.Parse(draft.PayloadJson)!.AsObject();
        custom["root"]!["css"] = "customer-page";
        var saved = (await service.SaveDraftAsync(def.DefinitionId, custom.ToJsonString(), "mine", draft.Info.Revision, "customer")).Value!;
        var validated = await service.ValidateAsync(def.DefinitionId, saved.Info.Revision, "customer");
        Assert.True((await service.PublishAsync(def.DefinitionId, validated.Value!.Info.Revision, "customer")).Success);
        if (shipUpgrade)
        {
            var i = items.FindIndex(x => x.Kind == "layout" && x.Key == "list-page");
            var next = JsonNode.Parse(items[i].PayloadJson)!.AsObject();
            next["root"]!["css"] = "lp-page lp-page--v2";
            items[i] = items[i] with { PayloadJson = next.ToJsonString() };
            await f.NewService().Service.ShipAsync(items, "system");
        }
        return (f, items, def.DefinitionId);
    }

    [Fact]
    public async Task An_upstream_change_to_a_customised_definition_is_flagged_with_a_diff_of_what_differs()
    {
        var (f, _, id) = await CustomisedLayoutAsync(shipUpgrade: true);
        var upstream = (await f.NewService().Service.GetUpstreamAsync(id)).Value!;
        Assert.True(upstream.Customized);
        Assert.True(upstream.UpstreamChanged);
        Assert.Equal(1, upstream.BasedOnShippedVersion);
        Assert.Equal(3, upstream.ShippedVersion);                      // v1 shipped, v2 the customer's, v3 shipped again
        var d = Assert.Single(upstream.Diff);
        Assert.Equal(("/root/css", "changed"), (d.Pointer, d.Change));
        Assert.Contains("customer-page", d.Published);
        Assert.Contains("lp-page--v2", d.Shipped);
    }

    [Fact]
    public async Task A_customised_definition_with_no_new_shipped_version_shows_its_diff_but_no_upstream_change()
    {
        var (f, _, id) = await CustomisedLayoutAsync(shipUpgrade: false);
        var upstream = (await f.NewService().Service.GetUpstreamAsync(id)).Value!;
        Assert.True(upstream.Customized);
        Assert.False(upstream.UpstreamChanged);
        Assert.Single(upstream.Diff);
    }

    [Fact]
    public async Task A_definition_nobody_changed_has_no_diff_and_a_customer_definition_has_no_upstream()
    {
        var f = new Fixture();
        var (service, _, db) = f.NewService();
        await service.ShipAsync(f.PackageItems(), "system");
        var id = (await db.Definitions.AsNoTracking().FirstAsync(d => d.Kind == "layout")).DefinitionId;
        var upstream = (await service.GetUpstreamAsync(id)).Value!;
        Assert.False(upstream.Customized);
        Assert.Empty(upstream.Diff);
        Assert.Equal(DefinitionFailure.NotFound, (await service.GetUpstreamAsync(987654)).Failure);
    }

    [Fact]
    public async Task Reset_to_shipped_republishes_the_shipped_payload_as_a_new_version_and_keeps_the_customers_in_the_history()
    {
        var (f, _, id) = await CustomisedLayoutAsync(shipUpgrade: true);
        var (service, store, db) = f.NewService();
        var revision = (await db.Definitions.AsNoTracking().SingleAsync(d => d.DefinitionId == id)).Revision;
        var reset = await service.ResetToShippedAsync(id, revision, "admin");
        Assert.True(reset.Success, reset.Code);
        Assert.False(reset.Value!.Summary.IsCustomized);
        Assert.Contains("lp-page--v2", (await store.GetPublishedAsync("layout", "list-page"))!.PayloadJson);
        Assert.Equal(4, reset.Value.Versions.Count);                    // the customer's version 2 is still there to roll back to
        Assert.False((await service.GetUpstreamAsync(id)).Value!.Customized);
        Assert.Equal("already-published", (await service.ResetToShippedAsync(id, reset.Value.Summary.Revision, "admin")).Code);
    }

    [Fact]
    public async Task Reset_to_shipped_refuses_a_stale_revision_and_a_definition_that_was_never_shipped()
    {
        var (f, _, id) = await CustomisedLayoutAsync(shipUpgrade: false);
        var (service, _, _) = f.NewService();
        Assert.Equal(DefinitionFailure.Concurrency, (await service.ResetToShippedAsync(id, 1, "admin")).Failure);
        var created = await service.CreateAsync(new CreateDefinition("data-source", "mine.list", "core", """{"en":"x","fa":"x"}""", null, """{"kind":"static","items":[{"value":"1","label":{"en":"a","fa":"a"}}]}""", null), "customer");
        Assert.True(created.Success);
        Assert.Equal("not-shipped", (await service.ResetToShippedAsync(created.Value!.Summary.Id, created.Value.Summary.Revision, "admin")).Code);
    }

    [Fact]
    public async Task A_key_a_customer_created_themselves_is_not_touched()
    {
        var f = new Fixture();
        var items = f.PackageItems();
        var (service, store, _) = f.NewService();
        var layout = items.First(x => x.Kind == "layout");
        var created = await service.CreateAsync(new CreateDefinition(layout.Kind, layout.Key, layout.AppKey, layout.TitleJson, null, layout.PayloadJson, "mine"), "customer");
        Assert.True(created.Success);
        var result = await f.NewService().Service.ShipAsync(items.Where(x => x.Kind == layout.Kind && x.Key == layout.Key).ToList(), "system");
        Assert.Equal((0, 0, 1), (result.Created, result.Upgraded, result.CustomerOwned));
        Assert.Null(await store.GetPublishedAsync(layout.Kind, layout.Key, layout.AppKey));   // the customer's draft stays unpublished, nothing was shipped over it
    }

    [Fact]
    public async Task An_invalid_shipped_definition_is_reported_and_not_written()
    {
        var f = new Fixture();
        var (service, _, db) = f.NewService();
        var bad = new ShippedDefinition("ghost-kind", "x", "core", """{"en":"x","fa":"x"}""", null, "{}");
        var badAction = new ShippedDefinition("action", "a", "core", """{"en":"x","fa":"x"}""", null, """{"kind":"handler","handler":"drop.database"}""");
        var result = await service.ShipAsync([bad, badAction], "system");
        Assert.False(result.Ok);
        Assert.Equal(2, result.Problems.Count);
        Assert.Empty(await db.Definitions.ToListAsync());
    }

    // ---- the engines read the database and give the same output as the files

    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Fa = new("fa");

    private static string Alert(string id, string en, string fa)
        => "{\"id\":\"" + id + "\",\"component\":\"alert\",\"options\":{\"title\":{\"en\":\"" + en + "\",\"fa\":\"" + fa + "\"},\"tone\":\"info\"}}";

    [Fact]
    public async Task The_page_renderer_writes_the_same_page_from_the_database_as_from_the_files()
    {
        var f = new Fixture();
        var (service, dbStore, _) = f.NewService();
        await service.ShipAsync(f.PackageItems(), "system");
        using var files = new FileDefinitionStore(f.Kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Package, Watch = false }, FilesOwnership.For(f.Kinds));
        var renderer = f.Ui.GetRequiredService<IPemsComponentRenderer>();
        var catalog = f.Ui.GetRequiredService<ComponentManifestCatalog>();
        var fromFiles = new PageRenderer(renderer, catalog, files);
        var fromDb = new PageRenderer(renderer, catalog, dbStore);

        foreach (var layout in new[] { "list-page", "form-page", "detail-page", "dashboard-page" })
        {
            var known = new[] { "messages", "header", "toolbar", "filters", "content", "footer", "main", "aside", "actions", "summary", "tabs", "kpis", "charts" };
            var regions = new JsonObject();
            foreach (var region in known) regions[region] = new JsonArray(JsonNode.Parse(Alert("a-" + region, "Hello " + region, "سلام " + region)));
            var page = new ResolvedDefinition(new PublishedDefinition("page", "demo", "core", 1, "", "h", """{"fa":"صفحه","en":"Page"}"""), new JsonObject { ["layout"] = layout, ["regions"] = regions });
            var request = new PipelineRequest("page", "demo", "core", "en", ["Admin"], "hamid");
            foreach (var culture in new[] { En, Fa })
            {
                var a = await fromFiles.RenderAsync(page, request, culture);
                var b = await fromDb.RenderAsync(page, request, culture);
                Assert.Equal(a.Html, b.Html);
                Assert.Equal(a.Title, b.Title);
                Assert.Equal(a.Problems, b.Problems);
                Assert.NotEmpty(a.Html);
            }
        }
    }

    private sealed class Allow : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Tokens : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new("request-token", "cookie-token", "__RequestVerificationToken", "X-CSRF");
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => GetAndStoreTokens(httpContext);
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }

    private sealed class Urls : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext c) => "/f/" + (c.Values is null ? "" : c.Values.GetType().GetProperty("key")?.GetValue(c.Values));
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url[0] == '/' && !url.StartsWith("//");
        public string? RouteUrl(UrlRouteContext routeContext) => null;
        public string? Link(string? routeName, object? values) => null;
    }

    private static FormHostController Host(Fixture f, IDefinitionStore store, string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        var access = new Allow();
        var pipeline = new DefinitionPipeline(store, access, [new FormOptionsHydrator(store, access, [new Departments()])]);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SystemAdmin")], "test")) };
        return new FormHostController(pipeline, new ActionExecutor(store, access, [new Recording()]), new FakeCurrentUserService { UserName = "hamid" }, new Tokens(), f.Ui.GetRequiredService<IPemsComponentRenderer>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FormHostController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http }, Url = new Urls() };
    }

    private static string Html(IActionResult result) => ((RenderedPage)((ViewResult)result).Model!).Html;

    [Fact]
    public async Task The_form_host_serves_the_pilot_form_from_the_database_exactly_as_from_the_files()
    {
        var f = new Fixture();
        var (service, dbStore, _) = f.NewService();
        await service.ShipAsync(f.PackageItems(), "system");
        using var files = new FileDefinitionStore(f.Kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Package, Watch = false }, FilesOwnership.For(f.Kinds));
        foreach (var culture in new[] { "en", "fa" })
        {
            var a = Html(await Host(f, files, culture).Index("departments-create", null, CancellationToken.None));
            var b = Html(await Host(f, dbStore, culture).Index("departments-create", null, CancellationToken.None));
            Assert.Equal(a, b);
            Assert.Contains("data-pems-rules=", b);
        }
    }

    private const string NoteForm = """{"id":"note","title":{"en":"Note","fa":"یادداشت"},"submitLabel":{"en":"Save","fa":"ذخیره"},"fields":[{"name":"text","label":{"en":"Text","fa":"متن"},"required":true}]}""";

    [Fact]
    public async Task A_form_created_in_the_database_renders_in_both_languages_with_no_deploy_and_a_saved_translation_shows_on_the_next_request()
    {
        var f = new Fixture();
        var (service, store, db) = f.NewService();

        var created = await service.CreateAsync(new CreateDefinition("form", "note", "core", """{"en":"Note","fa":"یادداشت"}""", null, NoteForm, "first"), "designer");
        Assert.True(created.Success, string.Join(",", created.Errors?.Select(e => e.Message) ?? []));
        var id = created.Value!.Summary.Id;
        var draft = await service.ValidateAsync(id, created.Value.Versions.Single().Revision, "designer");
        Assert.True(draft.Success);
        Assert.True((await service.PublishAsync(id, draft.Value!.Info.Revision, "designer")).Success);

        var en = System.Net.WebUtility.HtmlDecode(Html(await Host(f, store, "en").Index("note", null, CancellationToken.None)));
        var fa = System.Net.WebUtility.HtmlDecode(Html(await Host(f, store, "fa").Index("note", null, CancellationToken.None)));
        Assert.Contains("Text", en);
        Assert.Contains("متن", fa);

        // the designer changes a translation and publishes a new version: the next request shows it
        var row = await db.Definitions.AsNoTracking().SingleAsync(d => d.Key == "note");
        var next = await service.CreateDraftAsync(row.DefinitionId, null, row.Revision, "designer");
        var changed = JsonNode.Parse(next.Value!.PayloadJson)!.AsObject();
        changed["fields"]![0]!["label"]!["fa"] = "متن یادداشت";
        var saved = await service.SaveDraftAsync(row.DefinitionId, changed.ToJsonString(), "label", next.Value.Info.Revision, "designer");
        var validated = await service.ValidateAsync(row.DefinitionId, saved.Value!.Info.Revision, "designer");
        Assert.True((await service.PublishAsync(row.DefinitionId, validated.Value!.Info.Revision, "designer")).Success);

        var after = System.Net.WebUtility.HtmlDecode(Html(await Host(f, store, "fa").Index("note", null, CancellationToken.None)));
        Assert.Contains("متن یادداشت", after);
        Assert.DoesNotContain("متن یادداشت", fa);
    }
}
