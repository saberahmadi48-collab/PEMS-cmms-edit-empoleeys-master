using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Api;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Tests.Api;

/// <summary>Sign-in stand-in: a request with the header X-Test-User is that user; without it nobody is signed in.</summary>
public sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";

    public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user.ToString()), new Claim(ClaimTypes.Role, "Tester") }, Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>
/// The real API pipeline in a test server (SCRUM-436): the real controllers, filters, permission policies, antiforgery, problem+json and cookie
/// challenge, over the in-memory database and a fake permission matrix. Conformance tests drive it from the operations of the OpenAPI document.
/// </summary>
public sealed class ApiConformanceHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public FakePermissionService Permissions { get; }
    public FakeDisciplineService Disciplines { get; }

    private string? _csrfToken;
    private string? _csrfCookie;

    private ApiConformanceHost(WebApplication app, FakePermissionService permissions, FakeDisciplineService disciplines)
    {
        _app = app;
        Permissions = permissions;
        Disciplines = disciplines;
        Client = app.GetTestClient();
    }

    public static async Task<ApiConformanceHost> StartAsync()
    {
        var permissions = new FakePermissionService();
        var disciplines = new FakeDisciplineService();
        var database = "conformance-" + Guid.NewGuid();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews(o => o.Filters.Add<ApiExceptionFilter>()).AddApplicationPart(typeof(ApiControllerBase).Assembly);
        builder.Services.AddLocalization();
        builder.Services.AddMemoryCache();
        builder.Services.AddAntiforgery();
        PEMS.Web.Api.ApiServiceCollectionExtensions.AddPemsApi(builder.Services);
        PEMS.Web.Api.ApiServiceCollectionExtensions.ConfigureApiAuthenticationResponses(builder.Services);
        PEMS.UI.ServiceCollectionExtensions.AddPemsUi(builder.Services);
        DefinitionKindExtensions.AddPemsDefinitionKinds(builder.Services);

        builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = HeaderAuthHandler.Scheme;
            o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            o.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
        })
        .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.Scheme, null)
        .AddCookie(IdentityConstants.ApplicationScheme, o => { o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/AccessDenied"; });
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IPermissionService>(permissions);
        builder.Services.AddSingleton<IDisciplineMasterService>(disciplines);
        builder.Services.AddSingleton<ICurrentUserService>(new FakeCurrentUserService { UserName = "tester" });
        builder.Services.AddSingleton<ApiIdempotencyStore>();
        builder.Services.AddSingleton(new DefinitionLimits());
        builder.Services.AddDbContext<PEMSDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<DefinitionStore>();
        builder.Services.AddScoped<IDefinitionStore>(sp => sp.GetRequiredService<DefinitionStore>());
        builder.Services.AddScoped<IDefinitionService, DefinitionService>();
        builder.Services.AddScoped<IDefinitionImpact, DefinitionImpactService>();
        builder.Services.AddScoped<DefinitionPublishGuard>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapOpenApi("/api/v1/openapi.json");
        // hands out an antiforgery cookie + request token the way a page does
        app.MapGet("/test/csrf", (HttpContext ctx, IAntiforgery af) =>
        {
            var tokens = af.GetAndStoreTokens(ctx);
            return Results.Json(new { token = tokens.RequestToken });
        });
        await app.StartAsync();
        return new ApiConformanceHost(app, permissions, disciplines);
    }

    public async Task<JsonDocument> OpenApiAsync() => JsonDocument.Parse(await Client.GetStringAsync("/api/v1/openapi.json"));

    private async Task EnsureCsrfAsync()
    {
        if (_csrfToken != null) return;
        // the token is issued to the signed-in user, as a page does
        var res = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/test/csrf") { Headers = { { "X-Test-User", "tester" } } });
        _csrfToken = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();
        _csrfCookie = string.Join("; ", res.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    public async Task<HttpResponseMessage> SendAsync(string method, string path, bool signedIn = true, bool csrf = true, string? body = null, string? ifMatch = null)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (signedIn) req.Headers.Add("X-Test-User", "tester");
        if (csrf)
        {
            await EnsureCsrfAsync();
            req.Headers.Add("Cookie", _csrfCookie);
            req.Headers.Add(ApiControllerBase.AntiforgeryHeader, _csrfToken);
        }
        if (ifMatch != null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body != null)
            req.Content = new StringContent(body, System.Text.Encoding.UTF8, method == "PATCH" ? "application/merge-patch+json" : "application/json");
        return await Client.SendAsync(req);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
