using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using PEMS.Application.Interfaces;
using PEMS.Web.Api;
using PEMS.Web.Api.V1;
using static PEMS.Tests.Api.ApiTestHost;

namespace PEMS.Tests.Api;

/// <summary>Contract tests of the pilot resource against ADR-0009 / guideline chapter 08 (SCRUM-435). Row-version behaviour on SQL Server is a QA line.</summary>
public class DisciplinesApiControllerTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static string IfMatch(DisciplineDetail d) => ApiETag.Format(d.RowVersion);

    [Fact]
    public async Task List_pages_with_total_and_applies_search()
    {
        var s = new FakeDisciplineService();
        for (var i = 0; i < 30; i++) s.Seed($"D{i:00}", $"Discipline {i:00}");
        var c = Controller(s, configure: r => r.QueryString = new QueryString("?page=2&pageSize=10"));
        var page = Assert.IsType<ApiPage<object>>(Assert.IsType<OkObjectResult>(await c.List(default)).Value);
        Assert.Equal((2, 10, 30), (page.Page, page.PageSize, page.Total));
        Assert.Equal(10, page.Items.Count);

        c = Controller(s, configure: r => r.QueryString = new QueryString("?q=Discipline 07"));
        Assert.Equal(1, Assert.IsType<ApiPage<object>>(Assert.IsType<OkObjectResult>(await c.List(default)).Value).Total);
    }

    [Theory]
    [InlineData("?pageSize=500", "/pageSize")]
    [InlineData("?page=0", "/page")]
    [InlineData("?page=1000&pageSize=200", "/page")]
    [InlineData("?sort=bogus", "/sort")]
    [InlineData("?filter[nope]=1", "/filter/nope")]
    [InlineData("?filter[isActive]=maybe", "/filter/isActive")]
    [InlineData("?fields=code,secret", "/fields")]
    public async Task List_rejects_bad_query_with_a_422_field_error(string query, string pointer)
    {
        var c = Controller(new FakeDisciplineService(), configure: r => r.QueryString = new QueryString(query));
        var p = Problem(await c.List(default), 422);
        Assert.Contains(p.Errors, e => e.Pointer == pointer);
    }

    [Fact]
    public async Task List_fields_keeps_only_the_requested_properties()
    {
        var s = new FakeDisciplineService();
        s.Seed("ME", "Mechanical");
        var c = Controller(s, configure: r => r.QueryString = new QueryString("?fields=code,name"));
        var page = Assert.IsType<ApiPage<object>>(Assert.IsType<OkObjectResult>(await c.List(default)).Value);
        var item = JsonSerializer.SerializeToElement(page.Items[0]);
        Assert.Equal(new[] { "code", "name" }, item.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Get_returns_etag_allowed_actions_and_304_on_match()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        var c = Controller(s);
        var ok = Assert.IsType<OkObjectResult>(await c.Get(d.DisciplineId, default));
        var res = Assert.IsType<DisciplineResource>(ok.Value);
        Assert.Equal(IfMatch(d), c.Response.Headers.ETag.ToString());
        Assert.Contains("edit", res.AllowedActions);
        Assert.Equal(DateTimeKind.Utc, res.CreatedDate.Kind);

        var c2 = Controller(s, configure: r => r.Headers.IfNoneMatch = IfMatch(d));
        Assert.Equal(304, Assert.IsType<StatusCodeResult>(await c2.Get(d.DisciplineId, default)).StatusCode);
    }

    [Fact]
    public async Task Get_hides_actions_the_user_may_not_perform()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        var c = Controller(s, new AllowAllAuthorization { Allow = false });
        var res = Assert.IsType<DisciplineResource>(Assert.IsType<OkObjectResult>(await c.Get(d.DisciplineId, default)).Value);
        Assert.Empty(res.AllowedActions);
    }

    [Fact]
    public async Task Get_unknown_id_is_a_404_problem()
    {
        Problem(await Controller(new FakeDisciplineService()).Get(99, default), 404);
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_etag()
    {
        var s = new FakeDisciplineService();
        var c = Controller(s);
        var created = Assert.IsType<CreatedResult>(await c.Create(new DisciplineInput("pr", "Process", null, null, null, null), default));
        Assert.Equal("/api/v1/disciplines/1", created.Location);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.False(string.IsNullOrEmpty(c.Response.Headers.ETag));
        Assert.Single(s.Rows);
    }

    [Fact]
    public async Task Create_validation_error_is_422_pointing_at_the_field()
    {
        var s = new FakeDisciplineService();
        s.Seed("PR", "Process");
        var p = Problem(await Controller(s).Create(new DisciplineInput("pr", "Another", null, null, null, null), default), 422);
        var e = Assert.Single(p.Errors);
        Assert.Equal(("/code", "duplicate-code", "Disc_Err_DuplicateCode"), (e.Pointer, e.Code, e.MessageKey));
    }

    [Fact]
    public async Task Create_with_the_same_idempotency_key_creates_one_record_and_replays_the_first_response()
    {
        var s = new FakeDisciplineService();
        var store = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var shared = new ApiIdempotencyStore(store);
        var input = new DisciplineInput("pr", "Process", null, null, null, null);
        var first = Assert.IsType<CreatedResult>(await Controller(s, store: shared, configure: r => r.Headers["Idempotency-Key"] = "k1").Create(input, default));
        var second = await Controller(s, store: shared, configure: r => r.Headers["Idempotency-Key"] = "k1").Create(input, default);
        Assert.Equal(201, Assert.IsType<ObjectResult>(second).StatusCode);
        Assert.Single(s.Rows);
        Assert.Equal(first.Location, "/api/v1/disciplines/1");
    }

    [Fact]
    public async Task Create_reusing_an_idempotency_key_with_another_body_is_rejected()
    {
        var s = new FakeDisciplineService();
        var shared = new ApiIdempotencyStore(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        await Controller(s, store: shared, configure: r => r.Headers["Idempotency-Key"] = "k1").Create(new DisciplineInput("pr", "Process", null, null, null, null), default);
        var p = Problem(await Controller(s, store: shared, configure: r => r.Headers["Idempotency-Key"] = "k1").Create(new DisciplineInput("me", "Mechanical", null, null, null, null), default), 422);
        Assert.Equal("idempotency-key-reuse", Assert.Single(p.Errors).Code);
        Assert.Single(s.Rows);
    }

    [Fact]
    public async Task Put_without_if_match_is_428_and_with_a_stale_one_is_412()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        var input = new DisciplineInput("ME", "Mechanical Eng", null, null, null, true);
        Problem(await Controller(s).Replace(d.DisciplineId, input, default), 428);
        Problem(await Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = "\"AAAA\"").Replace(d.DisciplineId, input, default), 412);
        Assert.Equal("Mechanical", s.Rows[0].Name);
    }

    [Fact]
    public async Task Put_with_the_current_etag_replaces_and_returns_a_new_etag()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        var c = Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = IfMatch(d));
        var res = Assert.IsType<DisciplineResource>(Assert.IsType<OkObjectResult>(await c.Replace(d.DisciplineId, new DisciplineInput("ME", "Mechanical Eng", "d", null, null, true), default)).Value);
        Assert.Equal("Mechanical Eng", res.Name);
        Assert.NotEqual(IfMatch(d), c.Response.Headers.ETag.ToString());
    }

    [Fact]
    public async Task Put_unknown_id_is_404_before_the_precondition_is_looked_at()
    {
        Problem(await Controller(new FakeDisciplineService()).Replace(9, new DisciplineInput("A", "B", null, null, null, null), default), 404);
    }

    [Fact]
    public async Task Patch_merges_only_the_sent_fields_and_null_clears_a_nullable_one()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        s.Rows[0] = s.Rows[0] with { Description = "old" };
        d = s.Rows[0];
        var c = Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = IfMatch(d));
        var res = Assert.IsType<DisciplineResource>(Assert.IsType<OkObjectResult>(await c.Patch(d.DisciplineId, Json("""{ "description": null, "isActive": false }"""), default)).Value);
        Assert.Equal(("Mechanical", null, false), (res.Name, res.Description, res.IsActive));
    }

    [Theory]
    [InlineData("""{ "name": null }""", "/name", "null-not-allowed")]
    [InlineData("""{ "nope": 1 }""", "/nope", "unknown-property")]
    [InlineData("""{ "isActive": "yes" }""", "/isActive", "wrong-type")]
    [InlineData("""{ "code": 5 }""", "/code", "wrong-type")]
    [InlineData("""[1]""", "", "invalid-body")]
    public async Task Patch_rejects_a_bad_body_with_a_field_error(string body, string pointer, string code)
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        var c = Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = IfMatch(d));
        var p = Problem(await c.Patch(d.DisciplineId, Json(body), default), 422);
        Assert.Contains(p.Errors, e => e.Pointer == pointer && e.Code == code);
    }

    [Fact]
    public async Task Delete_needs_if_match_returns_204_and_409_when_the_discipline_is_in_use()
    {
        var s = new FakeDisciplineService();
        var d = s.Seed("ME", "Mechanical");
        Problem(await Controller(s).Delete(d.DisciplineId, default), 428);

        s.DeleteInUse = true;
        var p = Problem(await Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = IfMatch(d)).Delete(d.DisciplineId, default), 409);
        Assert.Equal("urn:pems:problem:in-use", p.Type);

        s.DeleteInUse = false;
        Assert.IsType<NoContentResult>(await Controller(s, configure: r => r.Headers[HeaderNames.IfMatch] = IfMatch(d)).Delete(d.DisciplineId, default));
        Assert.Empty(s.Rows);
    }
}
