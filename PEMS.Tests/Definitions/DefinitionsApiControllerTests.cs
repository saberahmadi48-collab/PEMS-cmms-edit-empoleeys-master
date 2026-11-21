using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PEMS.Tests.Api;
using PEMS.Tests.TestSupport;
using PEMS.Web.Api;
using PEMS.Web.Api.V1;
using static PEMS.Tests.Api.ApiTestHost;

namespace PEMS.Tests.Definitions;

/// <summary>The definition store through its REST controller on the real service: the "Done when" of SCRUM-434, call by call.</summary>
public class DefinitionsApiControllerTests
{
    private readonly DefinitionTestWorld _world = new();
    private readonly AllowAllAuthorization _auth = new();

    private DefinitionsApiController Api(string? ifMatch = null, string query = "")
    {
        var (service, _, _) = _world.NewService();
        var c = new DefinitionsApiController(service, _auth, new FakeCurrentUserService { UserName = "tester" });
        var http = new DefaultHttpContext();
        if (ifMatch != null) http.Request.Headers[HeaderNames.IfMatch] = ifMatch;
        http.Request.QueryString = new QueryString(query);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static string ETag(ControllerBase c) => c.Response.Headers.ETag.ToString();
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static DefinitionCreateInput NewForm(string key = "items.new") =>
        new("form", key, null, Json("""{"en":"Item form"}"""), null, Json(DefinitionTestWorld.Form), "first");

    private async Task<int> CreateAsync(string key = "items.new")
    {
        var created = Assert.IsType<CreatedResult>(await Api().Create(NewForm(key), default));
        return Assert.IsType<DefinitionResource>(created.Value).Id;
    }

    [Fact]
    public async Task Whole_lifecycle_through_the_api_only()
    {
        // create as a draft
        var c = Api();
        var created = Assert.IsType<CreatedResult>(await c.Create(NewForm(), default));
        var id = Assert.IsType<DefinitionResource>(created.Value).Id;
        Assert.Equal($"/api/v1/definitions/{id}", created.Location);
        Assert.False(string.IsNullOrEmpty(ETag(c)));

        // read the draft, edit it with If-Match
        c = Api();
        var draft = Assert.IsType<DefinitionVersionPayloadResource>(Assert.IsType<OkObjectResult>(await c.GetDraft(id, default)).Value);
        var etag = ETag(c);
        Assert.Equal("Draft", draft.Version.Status);
        c = Api(etag);
        var edited = Assert.IsType<OkObjectResult>(await c.SaveDraft(id, new DefinitionDraftInput(Json(DefinitionTestWorld.Form.Replace("Item", "Thing")), "rename"), default));
        var etag2 = ETag(c);
        Assert.NotEqual(etag, etag2);

        // the second of two concurrent edits (still holding the first ETag) is refused with 412
        Problem(await Api(etag).SaveDraft(id, new DefinitionDraftInput(Json(DefinitionTestWorld.Form), "late"), default), 412);
        // an edit without If-Match is 428
        Problem(await Api().SaveDraft(id, new DefinitionDraftInput(Json(DefinitionTestWorld.Form), null), default), 428);

        // publish
        c = Api(etag2);
        var published = Assert.IsType<DefinitionResource>(Assert.IsType<OkObjectResult>(await c.Publish(id, default)).Value);
        Assert.Equal((1, false), (published.PublishedVersion, published.HasDraft));
        Assert.Contains("create-draft", published.AllowedActions);

        // second version, then roll back to the first
        c = Api(ETag(c));
        var draft2 = Assert.IsType<CreatedResult>(await c.CreateDraft(id, null, default));
        var d2 = Assert.IsType<DefinitionVersionPayloadResource>(draft2.Value);
        Assert.Equal(2, d2.Version.Number);
        c = Api(ETag(c));
        Assert.IsType<OkObjectResult>(await c.SaveDraft(id, new DefinitionDraftInput(Json(DefinitionTestWorld.Form.Replace("Item", "Other")), null), default));
        c = Api(ETag(c));
        Assert.IsType<OkObjectResult>(await c.Publish(id, default));

        var read = Api();
        var detail = Assert.IsType<DefinitionResource>(Assert.IsType<OkObjectResult>(await read.Get(id, default)).Value);
        Assert.Equal(2, detail.PublishedVersion);
        c = Api(ETag(read));
        var back = Assert.IsType<DefinitionResource>(Assert.IsType<OkObjectResult>(await c.Rollback(id, new RollbackInput(1), default)).Value);
        Assert.Equal(3, back.PublishedVersion);
        Assert.Equal(new[] { "Retired", "Retired", "Published" }, back.Versions!.Select(v => v.Status));

        // the old version stays readable
        var v1 = Assert.IsType<DefinitionVersionPayloadResource>(Assert.IsType<OkObjectResult>(await Api().GetVersion(id, 1, default)).Value);
        Assert.Contains("Thing", v1.Payload.GetRawText());
    }

    [Fact]
    public async Task Create_validates_the_body_and_uses_problem_details()
    {
        var p = Problem(await Api().Create(new DefinitionCreateInput("form", "items.new", null, default, null, default, null), default), 422);
        Assert.Contains(p.Errors, e => e.Pointer == "/title");
        Assert.Contains(p.Errors, e => e.Pointer == "/payload");
        Problem(await Api().Create(NewForm("Bad Key"), default), 422);
        await CreateAsync();
        var dup = Problem(await Api().Create(NewForm(), default), 409);
        Assert.Equal("urn:pems:problem:duplicate-key", dup.Type);
    }

    [Fact]
    public async Task Publish_of_an_invalid_form_is_422_with_pointers_and_nothing_changes()
    {
        var created = Assert.IsType<CreatedResult>(await Api().Create(NewForm("items.bad") with { Payload = Json("""{"id":"x","title":"T","action":"/x","fields":[],"submitLabel":"S"}""") }, default));
        var id = Assert.IsType<DefinitionResource>(created.Value).Id;
        var c = Api();
        await c.GetDraft(id, default);
        var p = Problem(await Api(ETag(c)).Publish(id, default), 422);
        Assert.Contains(p.Errors, e => e.Pointer == "/fields" && e.Code == "required");
        var read = Api();
        Assert.Null(Assert.IsType<DefinitionResource>(Assert.IsType<OkObjectResult>(await read.Get(id, default)).Value).PublishedVersion);
    }

    [Fact]
    public async Task Unknown_ids_are_404_and_actions_without_a_draft_say_so()
    {
        Problem(await Api().Get(99, default), 404);
        Problem(await Api("*").Publish(99, default), 404);
        var id = await CreateAsync();
        var c = Api(); await c.GetDraft(id, default);
        await Api(ETag(c)).Publish(id, default);   // only valid after fixes; payload is valid so this publishes
        var p = Problem(await Api("*").Publish(id, default), 404);
        Assert.Equal("urn:pems:problem:no-draft", p.Type);
    }

    [Fact]
    public async Task Create_draft_when_one_exists_is_409_and_when_nothing_is_published_is_409()
    {
        var id = await CreateAsync();
        var c = Api(); await c.Get(id, default);
        Assert.Equal("urn:pems:problem:draft-exists", Problem(await Api(ETag(c)).CreateDraft(id, null, default), 409).Type);
    }

    [Fact]
    public async Task Rollback_needs_a_version_number_and_a_precondition()
    {
        var id = await CreateAsync();
        var c = Api(); await c.Get(id, default);
        Assert.Contains(Problem(await Api(ETag(c)).Rollback(id, new RollbackInput(null), default), 422).Errors, e => e.Pointer == "/toVersion");
        Problem(await Api().Rollback(id, new RollbackInput(1), default), 428);
        Problem(await Api("\"stale\"").Rollback(id, new RollbackInput(1), default), 412);
    }

    [Fact]
    public async Task Discard_draft_is_204_and_the_envelope_can_be_renamed_with_if_match()
    {
        var id = await CreateAsync();
        var c = Api(); await c.Get(id, default);
        var rename = Api(ETag(c));
        var renamed = Assert.IsType<DefinitionResource>(Assert.IsType<OkObjectResult>(await rename.UpdateEnvelope(id, new DefinitionEnvelopeInput(Json("""{"en":"Renamed"}"""), null), default)).Value);
        var renamedRead = rename;
        Assert.Contains("Renamed", renamed.Title.GetRawText());
        Problem(await Api(ETag(c)).UpdateEnvelope(id, new DefinitionEnvelopeInput(Json("\"x\""), null), default), 412);
        Assert.Contains(Problem(await Api(ETag(renamedRead)).UpdateEnvelope(id, new DefinitionEnvelopeInput(default, null), default), 422).Errors, e => e.Pointer == "/title");

        // the only version cannot be discarded
        var d = Api(); await d.GetDraft(id, default);
        Problem(await Api(ETag(d)).DiscardDraft(id, default), 409);
    }

    [Fact]
    public async Task List_applies_the_query_contract_and_rejects_bad_queries()
    {
        for (var i = 0; i < 3; i++) await CreateAsync($"items.f{i}");
        var page = Assert.IsType<ApiPage<object>>(Assert.IsType<OkObjectResult>(await Api(query: "?filter[kind]=form&sort=-key&pageSize=2&fields=id,key").List(default)).Value);
        Assert.Equal((3, 2), (page.Total, page.Items.Count));
        Assert.Equal(new[] { "id", "key" }, JsonSerializer.SerializeToElement(page.Items[0]).EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Contains(Problem(await Api(query: "?sort=bogus&filter[nope]=1&filter[published]=maybe").List(default), 422).Errors, e => e.Pointer == "/sort");
    }

    [Fact]
    public async Task Export_and_import_round_trip_through_the_api_and_a_changed_package_is_refused()
    {
        var id = await CreateAsync();
        var c = Api(); await c.GetDraft(id, default);
        await Api(ETag(c)).Publish(id, default);
        Problem(await Api().Export(id, 5, default), 404);
        var package = Assert.IsType<DefinitionPackageResource>(Assert.IsType<OkObjectResult>(await Api().Export(id, null, default)).Value);

        var other = new DefinitionTestWorld();
        var (service, _, _) = other.NewService();
        var target = new DefinitionsApiController(service, _auth, new FakeCurrentUserService { UserName = "tester" }) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var ok = Assert.IsType<ObjectResult>(await target.Import(package, default));
        Assert.Equal(201, ok.StatusCode);
        Assert.Equal(1, Assert.IsType<DefinitionImportResource>(ok.Value).Created);

        var tampered = package with { Items = new[] { package.Items[0] with { Payload = Json(DefinitionTestWorld.Form.Replace("Item", "Evil")) } } };
        var p = Problem(await Api().Import(tampered, default), 422);
        Assert.Contains(p.Errors, e => e.Code == "hash-mismatch");
    }
}
