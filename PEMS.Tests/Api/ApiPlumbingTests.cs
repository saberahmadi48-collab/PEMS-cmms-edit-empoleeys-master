using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PEMS.Tests.TestSupport;
using PEMS.Web.Api;

namespace PEMS.Tests.Api;

public class ApiPlumbingTests
{
    [Fact]
    public void ETag_round_trips_and_a_missing_version_is_zero()
    {
        var v = new byte[] { 0, 0, 0, 0, 0, 0, 0x1F, 0xFB };
        Assert.Equal(v, ApiETag.Parse(ApiETag.Format(v)));
        Assert.Equal("\"0\"", ApiETag.Format(null));
        Assert.Null(ApiETag.Parse("\"0\""));
        Assert.Null(ApiETag.Parse("not base64 !!"));
    }

    [Theory]
    [InlineData(null, ApiETag.Check.Missing)]
    [InlineData("*", ApiETag.Check.Matches)]
    [InlineData("\"AQID\"", ApiETag.Check.Matches)]
    [InlineData("W/\"AQID\"", ApiETag.Check.Matches)]
    [InlineData("\"x\", \"AQID\"", ApiETag.Check.Matches)]
    [InlineData("\"other\"", ApiETag.Check.Stale)]
    public void IfMatch_evaluation(string? header, ApiETag.Check expected)
    {
        var ctx = new DefaultHttpContext();
        if (header != null) ctx.Request.Headers.IfMatch = header;
        Assert.Equal(expected, ApiETag.Evaluate(ctx.Request, new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void List_query_reads_sort_filters_and_defaults()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?sort=-createdDate,name&filter[status]=Open&q=%20pump%20");
        Assert.True(ApiListQuery.TryParse(ctx.Request.Query, out var q, out _));
        Assert.Equal(new[] { ("createdDate", true), ("name", false) }, q.Sort.ToArray());
        Assert.Equal("Open", q.Filters["status"]);
        Assert.Equal(("pump", 1, 25), (q.Q, q.Page, q.PageSize));
    }

    [Fact]
    public void Offset_paging_stops_at_ten_thousand_rows()
    {
        var ctx = new DefaultHttpContext { Request = { QueryString = new QueryString("?page=51&pageSize=200") } };
        Assert.False(ApiListQuery.TryParse(ctx.Request.Query, out _, out var errors));
        Assert.Equal("offset-limit", Assert.Single(errors).Code);
        ctx.Request.QueryString = new QueryString("?page=50&pageSize=200");
        Assert.True(ApiListQuery.TryParse(ctx.Request.Query, out _, out _));
    }

    [Fact]
    public void Problem_responses_use_the_problem_json_content_type_and_a_urn_type()
    {
        var r = ApiProblem.PreconditionFailed();
        Assert.Equal(412, r.StatusCode);
        Assert.Contains("application/problem+json", r.ContentTypes);
        Assert.StartsWith("urn:pems:problem:", ((ApiProblemDetails)r.Value!).Type);
    }

    private static async Task<bool> Authorize(FakePermissionService permissions, string policy, bool signedIn = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<PEMS.Application.Interfaces.IPermissionService>(permissions);
        services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();
        using var sp = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(signedIn ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Engineer") }, "test") : new ClaimsIdentity());
        return (await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy)).Succeeded;
    }

    [Fact]
    public async Task Permission_policy_follows_the_permission_matrix()
    {
        var p = new FakePermissionService();
        Assert.True(await Authorize(p, "perm:DisciplineMaster.View"));
        p.Denied.Add(("DisciplineMaster", "Edit"));
        Assert.False(await Authorize(p, "perm:DisciplineMaster.Edit"));
        Assert.False(await Authorize(new FakePermissionService(), "perm:DisciplineMaster.View", signedIn: false));
    }

    [Fact]
    public async Task Policy_provider_falls_back_to_the_default_for_other_names()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        Assert.Null(await provider.GetPolicyAsync("something-else"));
        Assert.Null(await provider.GetPolicyAsync("perm:NoAction"));
        Assert.NotNull(await provider.GetPolicyAsync("perm:Module.Action"));
    }
}
