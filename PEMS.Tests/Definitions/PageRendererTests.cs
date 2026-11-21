using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-438: a page definition is rendered through its layout definition and the component registry; visible expressions decide, a problem hides the part and never breaks the page.</summary>
public sealed class PageRendererTests : IDisposable
{
    private readonly FileDefinitionStore _files;
    private readonly PageRenderer _renderer;

    public PageRendererTests()
    {
        var provider = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        var registry = provider.GetRequiredService<IPemsComponentRegistry>();
        var catalog = provider.GetRequiredService<ComponentManifestCatalog>();
        var kinds = ShippedKinds.Build(registry, catalog);
        _files = new FileDefinitionStore(kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Path.Combine(AppContext.BaseDirectory, "definitions-system"), Watch = false }, FilesOwnership.For(kinds));
        _renderer = new PageRenderer(provider.GetRequiredService<IPemsComponentRenderer>(), catalog, _files);
    }

    public void Dispose() => _files.Dispose();

    private static readonly CultureInfo Fa = new("fa");
    private static readonly CultureInfo En = new("en");

    private static string Alert(string id, string en, string fa, string? visible = null, string extra = "")
        => "{\"id\":\"" + id + "\"," + (visible is null ? "" : "\"visible\":" + System.Text.Json.JsonSerializer.Serialize(visible) + ",")
           + "\"component\":\"alert\",\"options\":{\"title\":{\"en\":\"" + en + "\",\"fa\":\"" + fa + "\"},\"tone\":\"info\"" + extra + "}}";

    private static ResolvedDefinition Page(string regions, string layout = "list-page", string? titleJson = "{\"fa\":\"فهرست\",\"en\":\"List\"}")
        => new(new PublishedDefinition("page", "demo", "core", 1, "", "h", titleJson), JsonNode.Parse("{\"layout\":\"" + layout + "\",\"regions\":" + regions + "}")!);

    private static PipelineRequest Request(params string[] roles) => new("page", "demo", "core", "fa", roles, "hamid");

    [Fact]
    public async Task Regions_are_written_in_the_order_of_the_layout_inside_its_containers_whatever_the_order_in_the_page()
    {
        var page = Page("{\"content\":[" + Alert("a", "First", "اول") + "," + Alert("b", "Second", "دوم") + "],\"header\":[" + Alert("h", "Head", "سر") + "]}");
        var result = await _renderer.RenderAsync(page, Request(), En);
        Assert.Empty(result.Problems);
        Assert.StartsWith("<div class=\"lp-page\">", result.Html);
        Assert.Contains("<section class=\"lp-panel\">", result.Html);
        Assert.True(result.Html.IndexOf("Head", StringComparison.Ordinal) < result.Html.IndexOf("First", StringComparison.Ordinal));
        Assert.True(result.Html.IndexOf("First", StringComparison.Ordinal) < result.Html.IndexOf("Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_texts_of_the_definition_and_the_title_follow_the_language_of_the_request()
    {
        var page = Page("{\"content\":[" + Alert("a", "Hello", "سلام") + "]}");
        var en = await _renderer.RenderAsync(page, Request(), En);
        var fa = await _renderer.RenderAsync(page, Request(), Fa);
        Assert.Contains("Hello", en.Html);
        Assert.DoesNotContain("سلام", System.Net.WebUtility.HtmlDecode(en.Html));
        Assert.Contains("سلام", System.Net.WebUtility.HtmlDecode(fa.Html));   // the markup may write Persian letters as character references
        Assert.Equal(("List", "فهرست"), (en.Title, fa.Title));
        Assert.Null(PageRenderer.TitleOf(null, En));
        Assert.Equal("فهرست", PageRenderer.TitleOf("{\"fa\":\"فهرست\"}", new CultureInfo("de")));   // no German: Persian, then English
    }

    [Fact]
    public async Task A_visible_expression_decides_with_the_user_the_request_and_the_values_of_the_page()
    {
        var page = Page("{\"content\":["
            + Alert("admin", "OnlyAdmin", "x", visible: "'Admin' in user.roles") + ","
            + Alert("fa", "OnlyFa", "x", visible: "request.culture == 'fa'") + ","
            + Alert("never", "Never", "x", visible: "false") + ","
            + Alert("always", "Always", "x") + "]}");
        var admin = await _renderer.RenderAsync(page, Request("Admin"), En);
        var other = await _renderer.RenderAsync(page, Request("Viewer"), En);
        Assert.Contains("OnlyAdmin", admin.Html);
        Assert.DoesNotContain("OnlyAdmin", other.Html);
        Assert.Contains("OnlyFa", other.Html);          // the request says fa
        Assert.DoesNotContain("Never", admin.Html);
        Assert.Contains("Always", other.Html);

        page.Hydrated["flag"] = true;
        var valued = Page("{\"content\":[" + Alert("v", "Valued", "x", visible: "values.flag") + "]}");
        valued.Hydrated["flag"] = true;
        Assert.Contains("Valued", (await _renderer.RenderAsync(valued, Request(), En)).Html);
    }

    [Fact]
    public async Task A_broken_instance_is_hidden_and_reported_and_the_rest_of_the_page_still_renders()
    {
        var page = Page("{\"content\":["
            + Alert("typeerr", "TypeErr", "x", visible: "values.n + 'a'") + ","
            + Alert("badopt", "BadOption", "x", extra: ",\"nonsense\":1") + ","
            + Alert("good", "Good", "x") + "]}");
        page.Hydrated["n"] = 5d;
        var result = await _renderer.RenderAsync(page, Request(), En);
        Assert.Contains("Good", result.Html);
        Assert.DoesNotContain("TypeErr", result.Html);
        Assert.DoesNotContain("BadOption", result.Html);
        Assert.Contains(result.Problems, p => p.Contains("typeerr") && p.Contains("type"));
        Assert.Contains(result.Problems, p => p.Contains("badopt"));
    }

    [Fact]
    public async Task Unknown_regions_empty_required_regions_and_a_missing_layout_are_reported()
    {
        var withUnknown = await _renderer.RenderAsync(Page("{\"nowhere\":[" + Alert("a", "A", "x") + "],\"content\":[" + Alert("b", "B", "x") + "]}"), Request(), En);
        Assert.Contains(withUnknown.Problems, p => p.Contains("\"nowhere\""));
        Assert.Contains("B", withUnknown.Html);

        var emptyRequired = await _renderer.RenderAsync(Page("{\"header\":[" + Alert("h", "H", "x") + "]}"), Request(), En);
        Assert.Contains(emptyRequired.Problems, p => p.Contains("required region \"content\""));

        var missing = await _renderer.RenderAsync(Page("{}", layout: "no-such-layout"), Request(), En);
        Assert.Equal("", missing.Html);
        Assert.Contains(missing.Problems, p => p.Contains("no-such-layout"));
    }

    private sealed class OneDefinition(PublishedDefinition? d) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult(kind == "page" && d is not null && key == d.Key ? d : null);
    }

    private sealed class Allow(bool yes) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(yes);
    }

    private PageHostController Host(bool allowed, string? permissionInPayload = null)
    {
        var payload = "{\"layout\":\"list-page\"," + (permissionInPayload is null ? "" : "\"permission\":\"" + permissionInPayload + "\",") + "\"regions\":{\"content\":[" + Alert("a", "Shown", "نمایش") + "]}}";
        var definition = new PublishedDefinition("page", "demo", "core", 1, payload, "h", "{\"fa\":\"فهرست\",\"en\":\"List\"}");
        var pipeline = new DefinitionPipeline(new OneDefinition(definition), new Allow(allowed), []);
        var controller = new PageHostController(pipeline, _renderer, new FakeCurrentUserService { UserName = "hamid" }, Microsoft.Extensions.Logging.Abstractions.NullLogger<PageHostController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };
        return controller;
    }

    [Fact]
    public async Task The_host_serves_a_page_definition_at_its_key_and_answers_404_and_403_from_the_pipeline()
    {
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await Host(allowed: true).Index("demo", CancellationToken.None));
        var model = Assert.IsType<RenderedPage>(ok.Model);
        Assert.Contains("lp-page", model.Html);
        Assert.Equal("Index", ok.ViewName);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await Host(true).Index("other-page", CancellationToken.None));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await Host(true).Index("../secret", CancellationToken.None));   // a key has a fixed shape
        Assert.IsType<Microsoft.AspNetCore.Mvc.ForbidResult>(await Host(allowed: false, permissionInPayload: "Equipment.View").Index("demo", CancellationToken.None));
    }
}
