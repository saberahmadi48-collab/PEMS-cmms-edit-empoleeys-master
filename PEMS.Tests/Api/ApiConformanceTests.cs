using System.Net;
using System.Text.Json;

namespace PEMS.Tests.Api;

/// <summary>
/// REST conformance of every operation in the OpenAPI document, through the real pipeline (SCRUM-436, guideline chapter 08). A new endpoint is
/// picked up automatically because the cases come from the document, so it cannot skip the rules.
/// </summary>
public class ApiConformanceTests : IAsyncLifetime
{
    private ApiConformanceHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiConformanceHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    public sealed record Operation(string Method, string Path, bool HasId)
    {
        public string Url => Path.Replace("{id}", "999999").Replace("{number}", "1");
        public override string ToString() => $"{Method} {Path}";
    }

    private async Task<List<Operation>> OperationsAsync()
    {
        using var doc = await _host.OpenApiAsync();
        var ops = new List<Operation>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var m in path.Value.EnumerateObject())
                ops.Add(new Operation(m.Name.ToUpperInvariant(), path.Name, path.Name.Contains("{id}")));
        return ops;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage res, HttpStatusCode expected, string what)
    {
        Assert.True(res.StatusCode == expected, $"{what}: expected {(int)expected}, got {(int)res.StatusCode}");
        Assert.True(res.Content.Headers.ContentType?.MediaType == "application/problem+json", $"{what}: content type was '{res.Content.Headers.ContentType}'");
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("status").GetInt32() == (int)expected, $"{what}: body.status differs from the HTTP status");
        Assert.StartsWith("urn:pems:problem:", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()), $"{what}: no title");
        Assert.Equal(JsonValueKind.Array, body.GetProperty("errors").ValueKind);
        return body;
    }

    [Fact]
    public async Task The_document_is_served_and_has_enough_operations_to_make_the_suite_meaningful()
    {
        var ops = await OperationsAsync();
        Assert.True(ops.Count >= 20, $"only {ops.Count} operations found");
        Assert.Contains(ops, o => o.Method == "PATCH");
        Assert.Contains(ops, o => o.Path.Contains("/actions/"));
    }

    [Fact]
    public async Task Anonymous_calls_get_401_problem_json_never_a_login_redirect()
    {
        foreach (var op in await OperationsAsync())
        {
            var res = await _host.SendAsync(op.Method, op.Url, signedIn: false, csrf: false, body: op.Method is "GET" or "DELETE" ? null : "{}");
            Assert.NotEqual(HttpStatusCode.Redirect, res.StatusCode);
            await AssertProblemAsync(res, HttpStatusCode.Unauthorized, op + " anonymous");
        }
    }

    [Fact]
    public async Task Signed_in_users_without_the_permission_get_403_problem_json()
    {
        _host.Permissions.AllowAll = false;
        foreach (var op in await OperationsAsync())
        {
            var res = await _host.SendAsync(op.Method, op.Url, csrf: false, body: op.Method is "GET" or "DELETE" ? null : "{}");
            await AssertProblemAsync(res, HttpStatusCode.Forbidden, op + " without permission");
        }
    }

    [Fact]
    public async Task Unsafe_verbs_without_the_antiforgery_header_are_refused_with_problem_json()
    {
        foreach (var op in (await OperationsAsync()).Where(o => o.Method is "POST" or "PUT" or "PATCH" or "DELETE"))
        {
            var res = await _host.SendAsync(op.Method, op.Url, csrf: false, body: op.Method == "DELETE" ? null : "{}", ifMatch: "*");
            await AssertProblemAsync(res, HttpStatusCode.BadRequest, op + " without X-CSRF-TOKEN");
        }
    }

    [Fact]
    public async Task Unknown_ids_give_404_problem_json_on_every_operation_that_has_an_id()
    {
        foreach (var op in (await OperationsAsync()).Where(o => o.HasId))
        {
            var res = await _host.SendAsync(op.Method, op.Url, body: op.Method is "GET" or "DELETE" ? null : "{}", ifMatch: "*");
            await AssertProblemAsync(res, HttpStatusCode.NotFound, op + " unknown id");
        }
    }

    [Fact]
    public async Task Writes_on_an_existing_resource_need_if_match_428_and_a_stale_one_gives_412()
    {
        var d = _host.Disciplines.Seed("ME", "Mechanical");
        foreach (var op in new[] { ("PUT", """{"code":"ME","name":"M"}"""), ("PATCH", """{"name":"M"}"""), ("DELETE", null) })
        {
            var url = $"/api/v1/disciplines/{d.DisciplineId}";
            await AssertProblemAsync(await _host.SendAsync(op.Item1, url, body: op.Item2), HttpStatusCode.PreconditionRequired, op.Item1 + " without If-Match");
            await AssertProblemAsync(await _host.SendAsync(op.Item1, url, body: op.Item2, ifMatch: "\"AAAA\""), HttpStatusCode.PreconditionFailed, op.Item1 + " stale");
        }
    }

    [Fact]
    public async Task Bad_list_queries_give_422_with_field_errors_on_every_collection()
    {
        foreach (var path in new[] { "/api/v1/disciplines", "/api/v1/definitions" })
        {
            var res = await _host.SendAsync("GET", path + "?pageSize=9999&sort=bogus&fields=nope");
            var body = await AssertProblemAsync(res, HttpStatusCode.UnprocessableEntity, path + " bad query");
            Assert.All(body.GetProperty("errors").EnumerateArray(), e =>
            {
                Assert.StartsWith("/", e.GetProperty("pointer").GetString());
                Assert.False(string.IsNullOrEmpty(e.GetProperty("code").GetString()));
            });
        }
    }

    [Fact]
    public async Task Successful_reads_are_json_camelCase_with_an_etag_on_single_resources()
    {
        var d = _host.Disciplines.Seed("ME", "Mechanical");
        var list = await _host.SendAsync("GET", "/api/v1/disciplines");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("application/json", list.Content.Headers.ContentType?.MediaType);
        var one = await _host.SendAsync("GET", $"/api/v1/disciplines/{d.DisciplineId}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.True(one.Headers.ETag != null, "a single resource needs an ETag");
        var body = JsonDocument.Parse(await one.Content.ReadAsStringAsync()).RootElement;
        Assert.All(body.EnumerateObject(), p => Assert.True(char.IsLower(p.Name[0]), $"'{p.Name}' is not camelCase"));
        Assert.Contains("allowedActions", body.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_create_answers_201_with_location_and_etag_and_an_invalid_body_422_with_a_pointer()
    {
        var created = await _host.SendAsync("POST", "/api/v1/disciplines", body: """{"code":"pr","name":"Process"}""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/v1/disciplines/1", created.Headers.Location?.OriginalString);
        Assert.NotNull(created.Headers.ETag);

        var dup = await _host.SendAsync("POST", "/api/v1/disciplines", body: """{"code":"pr","name":"Other"}""");
        var body = await AssertProblemAsync(dup, HttpStatusCode.UnprocessableEntity, "duplicate code");
        Assert.Equal("/code", body.GetProperty("errors")[0].GetProperty("pointer").GetString());
    }

    [Fact]
    public async Task Malformed_json_is_a_problem_json_400_or_422_not_an_html_page()
    {
        var res = await _host.SendAsync("POST", "/api/v1/disciplines", body: "{ not json");
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        Assert.True(res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_exception_inside_an_action_is_a_500_problem_json_without_a_stack_trace()
    {
        _host.Disciplines.ThrowOnQuery = true;
        var res = await _host.SendAsync("GET", "/api/v1/disciplines");
        var body = await AssertProblemAsync(res, HttpStatusCode.InternalServerError, "exception");
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("boom", text);               // the exception message is not shown to the caller
        Assert.DoesNotContain("   at ", text);             // neither is a stack trace
    }
}
