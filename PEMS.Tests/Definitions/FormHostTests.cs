using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;
using PEMS.Tests.TestSupport;
using PEMS.UI.Abstractions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-578 and 437 end to end with the shipped pilot definitions: the host renders the form from its definition, validates the post, runs the action definition and hands the cleaned values to its handler.</summary>
public sealed class FormHostTests : IDisposable
{
    private readonly IDefinitionStore _files;
    private readonly IPemsComponentRenderer _renderer;
    private readonly Recording _handler = new();

    private sealed class Recording : IActionHandler
    {
        public ActionInvocation? Seen;
        public ActionOutcome Outcome = ActionOutcome.Done();
        public string Key => "department.create";
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct) { Seen = invocation; return Task.FromResult(Outcome); }
    }

    private sealed class Departments : IDataSourceProvider
    {
        public string Key => "departments";
        public IReadOnlyCollection<string>? AllowedRoles => null;
        public Task<IReadOnlyList<FieldOption>> LoadAsync(DataSourceRequest request, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<FieldOption>>([new() { Value = "7", Label = TranslatableText.Literal("Engineering") }]);
    }

    private sealed class Allow(bool allowed) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allowed);
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

    public FormHostTests()
    {
        var provider = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        _files = new InlineStore(Form, DataSource, ActionDef);
        _renderer = provider.GetRequiredService<IPemsComponentRenderer>();
    }

    public void Dispose() { }

    // the form of this test is its own definition (conditions, a select from a data source, a handler by key), so the host is tested whatever the product ships
    private sealed class InlineStore(params (string Kind, string Key, string Payload)[] items) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
        {
            var hit = items.FirstOrDefault(i => i.Kind == kind && i.Key == key);
            return Task.FromResult(hit.Key is null ? null : new PublishedDefinition(kind, key, appKey, 1, hit.Payload, "h", """{"fa":"ایجاد دپارتمان","en":"Create department"}"""));
        }
    }

    private static readonly (string, string, string) Form = ("form", "department-create", """
    {"id":"department-create","title":{"fa":"ایجاد دپارتمان","en":"Create department"},"submitLabel":{"fa":"ایجاد","en":"Create"},"actionKey":"department-create","columns":2,"warnOnLeave":true,
     "fields":[
      {"name":"code","label":{"fa":"کد","en":"Code"},"kind":"text","maxLength":20,"pattern":"[A-Za-z0-9_-]+","messages":{"pattern":{"fa":"کد نامعتبر است.","en":"The code is not valid."}}},
      {"name":"name","label":{"fa":"نام","en":"Name"},"kind":"text","required":true,"minLength":2,"maxLength":100,"messages":{"required":{"fa":"نام دپارتمان الزامی است.","en":"The department name is required."}}},
      {"name":"parentDepartmentId","label":{"fa":"دپارتمان والد","en":"Parent department"},"kind":"select","optionsFrom":"departments","visible":"values.isSubDepartment","requiredWhen":"values.isSubDepartment",
       "messages":{"required":{"fa":"دپارتمان والد را انتخاب کنید.","en":"Choose the parent department of a sub-department."}}},
      {"name":"isSubDepartment","label":{"fa":"زیرمجموعه‌ی دپارتمان دیگر است","en":"Is a sub-department"},"kind":"switch","span":2},
      {"name":"description","label":{"fa":"توضیحات","en":"Description"},"kind":"textArea","maxLength":500,"span":2}]}
    """);
    private static readonly (string, string, string) DataSource = ("data-source", "departments", """{"kind":"provider","provider":"departments"}""");
    private static readonly (string, string, string) ActionDef = ("action", "department-create", """{"kind":"handler","handler":"department.create","success":{"fa":"دپارتمان ایجاد شد.","en":"Department created."},"redirect":"/Department"}""");

    private FormHostController Host(bool allowed = true, Dictionary<string, string>? post = null, string? culture = null)
    {
        if (culture is not null) { CultureInfo.CurrentUICulture = new CultureInfo(culture); CultureInfo.CurrentCulture = new CultureInfo(culture); }
        var access = new Allow(allowed);
        var pipeline = new DefinitionPipeline(_files, access, [new FormOptionsHydrator(_files, access, [new Departments()])]);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "SystemAdmin")], "test")) };
        if (post is not null) { http.Request.Method = "POST"; http.Request.ContentType = "application/x-www-form-urlencoded"; http.Request.Form = new FormCollection(post.ToDictionary(p => p.Key, p => new StringValues(p.Value))); }
        return new FormHostController(pipeline, new ActionExecutor(_files, access, [_handler]), new FakeCurrentUserService { UserName = "hamid" }, new Tokens(), _renderer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FormHostController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            Url = new Urls(),
            TempData = new TempDataDictionary(http, new NullTempData())
        };
    }

    private sealed class NullTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static string Html(IActionResult result) => ((RenderedPage)((ViewResult)result).Model!).Html;

    [Fact]
    public async Task The_form_is_rendered_from_its_definition_with_the_options_of_its_data_source_and_the_conditions_for_the_browser()
    {
        var result = await Host(culture: "en").Index("department-create", null, CancellationToken.None);
        var html = System.Net.WebUtility.HtmlDecode(Html(result));
        Assert.Contains("Create department", html);
        Assert.Contains("class=\"pems-form\"", html);
        Assert.Contains("pems-form__grid", html);                       // columns: 2
        Assert.Contains("name=\"code\"", html);
        Assert.Contains("<option value=\"7\"", html);                    // the parent options came from the data source
        Assert.Matches("data-field=\"parentDepartmentId\"[^>]*hidden", html);   // visible: values.isSubDepartment is false
        Assert.Contains("data-pems-rules=", html);
        Assert.Contains("values.isSubDepartment", html);
        Assert.Contains("value=\"request-token\"", html);
        Assert.Contains("action=\"/f/department-create\"", html);
    }

    [Fact]
    public async Task The_form_follows_the_language_of_the_request()
    {
        var fa = System.Net.WebUtility.HtmlDecode(Html(await Host(culture: "fa").Index("department-create", null, CancellationToken.None)));
        Assert.Contains("ایجاد دپارتمان", fa);
        Assert.Contains("زیرمجموعه‌ی دپارتمان دیگر است", fa);
    }

    [Fact]
    public async Task An_unknown_form_a_bad_key_and_a_refused_user_get_404_and_403()
    {
        Assert.IsType<NotFoundResult>(await Host().Index("nope", null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Host().Index("../x", null, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Host(allowed: true).Index("departments", null, CancellationToken.None));   // a data source key is not a form
        var form = await Host(allowed: false).Index("department-create", null, CancellationToken.None);
        Assert.True(form is ForbidResult or NotFoundResult);
    }

    [Fact]
    public async Task A_post_with_an_empty_name_shows_the_message_of_the_definition_and_runs_nothing()
    {
        var host = Host(post: new() { ["code"] = "ENG", ["name"] = "  " }, culture: "en");
        var html = System.Net.WebUtility.HtmlDecode(Html(await host.Submit("department-create", null, CancellationToken.None)));
        Assert.Contains("The department name is required.", html);
        Assert.Contains("value=\"ENG\"", html);                          // the typed values stay
        Assert.Null(_handler.Seen);
        var fa = System.Net.WebUtility.HtmlDecode(Html(await Host(post: new() { ["name"] = "" }, culture: "fa").Submit("department-create", null, CancellationToken.None)));
        Assert.Contains("نام دپارتمان الزامی است.", fa);
    }

    [Fact]
    public async Task The_conditions_decide_on_the_server_the_parent_is_required_only_for_a_sub_department()
    {
        var missing = Html(await Host(post: new() { ["name"] = "Pumps", ["isSubDepartment"] = "true" }, culture: "en").Submit("department-create", null, CancellationToken.None));
        Assert.Contains("Choose the parent department of a sub-department.", System.Net.WebUtility.HtmlDecode(missing));
        var notListed = Html(await Host(post: new() { ["name"] = "Pumps", ["isSubDepartment"] = "true", ["parentDepartmentId"] = "999" }, culture: "en").Submit("department-create", null, CancellationToken.None));
        Assert.Contains("Choose one of the listed values.", System.Net.WebUtility.HtmlDecode(notListed));
        Assert.Null(_handler.Seen);
    }

    [Fact]
    public async Task A_valid_post_hands_the_cleaned_values_to_the_action_and_redirects_with_the_message()
    {
        // the parent is posted but the switch is off: the field is hidden, so its value is dropped
        var host = Host(post: new() { ["code"] = " ENG ", ["name"] = " Engineering ", ["description"] = "d", ["parentDepartmentId"] = "7" }, culture: "en");
        var result = await host.Submit("department-create", null, CancellationToken.None);
        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Department", redirect.Url);
        Assert.Equal("Department created.", host.TempData["Message"]);
        var seen = _handler.Seen!.Values;
        Assert.Equal(("ENG", "Engineering", "d", "false"), (seen["code"], seen["name"], seen["description"], seen["isSubDepartment"]));
        Assert.False(seen.ContainsKey("parentDepartmentId"));
        Assert.Equal(["SystemAdmin"], _handler.Seen.Roles);
    }

    [Fact]
    public async Task A_sub_department_keeps_its_parent_and_a_refused_action_shows_its_error_on_the_field()
    {
        _handler.Outcome = ActionOutcome.Refused("name", TranslatableText.Of(("en", "Already exists."), ("fa", "تکراری است.")));
        var refused = Html(await Host(post: new() { ["name"] = "Pumps", ["isSubDepartment"] = "true", ["parentDepartmentId"] = "7" }, culture: "en").Submit("department-create", null, CancellationToken.None));
        Assert.Equal("7", _handler.Seen!.Values["parentDepartmentId"]);
        Assert.Contains("Already exists.", System.Net.WebUtility.HtmlDecode(refused));
    }

    [Fact]
    public async Task Only_the_declared_fields_are_read_from_the_post()
    {
        var host = Host(post: new() { ["name"] = "Xy", ["isAdmin"] = "true", ["__RequestVerificationToken"] = "t" }, culture: "en");
        await host.Submit("department-create", null, CancellationToken.None);
        Assert.DoesNotContain("isAdmin", _handler.Seen!.Values.Keys);
        Assert.DoesNotContain("__RequestVerificationToken", _handler.Seen.Values.Keys);
    }
}
