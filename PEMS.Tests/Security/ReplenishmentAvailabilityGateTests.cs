using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Web.Areas.Warehouse.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Security;

/// <summary>
/// دروازه‌ی موقت دسترسی P5 (توصیه‌های تأمین). این تست‌ها یک Pipeline واقعی MVC (TestServer + فیلترهای سراسری دقیقاً مثل Program.cs +
/// ReplenishmentController واقعی) را با دیتابیس InMemory و Fake/Spy اجرا می‌کنند - هیچ SQL Server، هیچ دیتابیس مشترک یا واقعی درگیر نیست.
/// اثبات اصلی: وقتی دروازه غیرفعال است (پیش‌فرض)، هر پنج اکشن 404 می‌دهند، پیش از هر اکشن/سرویس/کوئری/بررسی مجوز، حتی برای SystemAdmin؛
/// و احراز هویت/تغییر رمز عبور همچنان قبل از دروازه اجرا می‌شوند.
/// </summary>
public class ReplenishmentAvailabilityGateTests
{
    private const string Key = ReplenishmentAvailability.ConfigurationKey;
    private const string Module = "ReplenishmentRecommendation";

    // ============================================================ 1. تنظیمات
    private static ReplenishmentAvailability FromConfig(string? value)
    {
        var data = new Dictionary<string, string?>();
        if (value != null) data[Key] = value;
        return new ReplenishmentAvailability(new ConfigurationBuilder().AddInMemoryCollection(data).Build());
    }

    [Fact]
    public void ConfigurationKey_IsTheApprovedOne() =>
        Assert.Equal("Features:ReplenishmentRecommendations:Enabled", Key);

    [Fact]
    public void MissingConfiguration_DefaultsToDisabled() => Assert.False(FromConfig(null).IsEnabled);

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("enabled")]
    [InlineData("garbage")]
    public void ExplicitFalseOrAnyNonBooleanValue_IsDisabled(string value) => Assert.False(FromConfig(value).IsEnabled);

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public void ExplicitTrue_IsEnabled(string value) => Assert.True(FromConfig(value).IsEnabled);

    [Fact]
    public void NoAppSettingsFileEnablesP5()
    {
        var web = Path.Combine(Root(), "PEMS.Web");
        foreach (var file in Directory.EnumerateFiles(web, "appsettings*.json", SearchOption.TopDirectoryOnly))
            Assert.DoesNotContain("ReplenishmentRecommendations", File.ReadAllText(file));
    }

    // ============================================================ 2. Pipeline واقعی MVC
    private static readonly string[] AllFive =
    {
        "GET /Warehouse/Replenishment",
        "GET /Warehouse/Replenishment/Index?status=Open&page=2",
        "GET /Warehouse/Replenishment/Details/1",
        "POST /Warehouse/Replenishment/Run",
        "POST /Warehouse/Replenishment/Dismiss/1",
        "POST /Warehouse/Replenishment/Reopen/1"
    };

    private static async Task<HttpResponseMessage> SendAsync(GateHost host, string request, string? user, string? roles, bool mustChange = false)
    {
        var parts = request.Split(' ', 2);
        var message = new HttpRequestMessage(new HttpMethod(parts[0]), parts[1]);
        if (parts[0] == "POST") message.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["reason"] = "x", ["requestKey"] = "k" });
        if (user != null) message.Headers.Add("X-Test-User", user);
        if (roles != null) message.Headers.Add("X-Test-Roles", roles);
        if (mustChange) message.Headers.Add("X-Test-MustChange", "true");
        return await host.Client.SendAsync(message);
    }

    [Theory]
    [InlineData(null, RoleCatalog.WarehouseManager)]
    [InlineData("false", RoleCatalog.WarehouseManager)]
    [InlineData("garbage", RoleCatalog.WarehouseManager)]
    [InlineData(null, SystemRoles.SystemAdmin)]
    [InlineData("false", SystemRoles.SystemAdmin)]
    [InlineData("garbage", SystemRoles.SystemAdmin)]
    public async Task Disabled_AllFiveActions_Return404_ForPermittedWhManagerAndForSystemAdmin_AndTouchNothing(string? config, string role)
    {
        await using var host = await GateHost.StartAsync(config);
        var before = host.Snapshot();

        foreach (var request in AllFive)
        {
            var response = await SendAsync(host, request, "tester", role);
            Assert.True(System.Net.HttpStatusCode.NotFound == response.StatusCode, $"{request} as {role} (config={config ?? "<missing>"}) returned {(int)response.StatusCode}");
        }

        host.AssertNothingP5Ran(before);
    }

    [Fact]
    public async Task Disabled_ThePermissionRowAndGrantsOfWhManagerAreStillThere_TheGateDoesNotDependOnThem()
    {
        await using var host = await GateHost.StartAsync(null);

        Assert.True(host.Ctx.RolePermissions.Any(r => r.RoleName == RoleCatalog.WarehouseManager && r.ModuleName == Module && r.CanView && r.CanCreate && r.CanEdit));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await SendAsync(host, "GET /Warehouse/Replenishment", "tester", RoleCatalog.WarehouseManager)).StatusCode);
    }

    [Fact]
    public async Task Disabled_AuthenticationStillRunsFirst_AnonymousIsChallengedNot404()
    {
        await using var host = await GateHost.StartAsync(null);
        var before = host.Snapshot();

        foreach (var request in AllFive)
        {
            var response = await SendAsync(host, request, user: null, roles: null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        host.AssertNothingP5Ran(before);
    }

    [Fact]
    public async Task Disabled_PasswordChangeGateStillRunsFirst_MustChangePasswordIsRedirectedNot404()
    {
        await using var host = await GateHost.StartAsync(null);
        var before = host.Snapshot();

        foreach (var request in AllFive)
        {
            var response = await SendAsync(host, request, "tester", RoleCatalog.WarehouseManager, mustChange: true);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/ChangePassword", response.Headers.Location?.ToString());
        }

        host.AssertNothingP5Ran(before);
    }

    [Fact]
    public async Task Disabled_TheGateRunsBeforeAntiforgery_PostWithoutTokenIs404NotBadRequest_WhileEnabledItIs400()
    {
        await using var host = await GateHost.StartAsync(null);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await SendAsync(host, "POST /Warehouse/Replenishment/Run", "tester", RoleCatalog.WarehouseManager)).StatusCode);

        host.SetEnabled("true");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await SendAsync(host, "POST /Warehouse/Replenishment/Run", "tester", RoleCatalog.WarehouseManager)).StatusCode);
    }

    [Fact]
    public async Task Enabled_ExistingPermissionEnforcementIsUnchanged_DeniedUsersGet403_AndNothingIsWritten()
    {
        await using var host = await GateHost.StartAsync("true");

        foreach (var request in new[] { "GET /Warehouse/Replenishment", "GET /Warehouse/Replenishment/Details/1" })
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await SendAsync(host, request, "nobody", GateHost.NoPermissionRole)).StatusCode);

        // دروازه مجوز را دور نمی‌زند: با «فعال» بودن، درخواست به منطق مجوز واقعی (سرویس) رسیده است
        Assert.True(host.ServiceCalls > 0);
        Assert.Equal(0, host.P5RowCount());
    }

    [Fact]
    public async Task Enabling_NeedsNoRoleOrPermissionChange_OnlyTheConfigurationFlips()
    {
        await using var host = await GateHost.StartAsync(null);
        var permissionsBefore = host.PermissionFingerprint();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await SendAsync(host, "GET /Warehouse/Replenishment/Details/1", "nobody", GateHost.NoPermissionRole)).StatusCode);
        host.SetEnabled("true");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await SendAsync(host, "GET /Warehouse/Replenishment/Details/1", "nobody", GateHost.NoPermissionRole)).StatusCode);
        host.SetEnabled("false");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await SendAsync(host, "GET /Warehouse/Replenishment/Details/1", "nobody", GateHost.NoPermissionRole)).StatusCode);

        Assert.Equal(permissionsBefore, host.PermissionFingerprint());
    }

    [Fact]
    public async Task UnrelatedRoute_IsNotAffectedByTheGate()
    {
        await using var host = await GateHost.StartAsync(null);
        var response = await SendAsync(host, "GET /Probe/Ping", "tester", RoleCatalog.WarehouseManager);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
    }

    // ============================================================ 3. Sidebar (View واقعی کامپایل‌شده)
    [Theory]
    [InlineData(RoleCatalog.WarehouseManager)]
    [InlineData(SystemRoles.SystemAdmin)]
    public async Task Sidebar_Disabled_HidesTheReplenishmentLink_AndPerformsNoP5PermissionLookup(string role)
    {
        await using var host = await GateHost.StartAsync(null);
        var html = await RenderSidebarAsync(host, role);

        Assert.DoesNotContain("/Warehouse/Replenishment", html);
        Assert.Equal(0, host.PermissionLookupsFor(Module));
    }

    [Theory]
    [InlineData(RoleCatalog.WarehouseManager)]
    [InlineData(SystemRoles.SystemAdmin)]
    public async Task Sidebar_Enabled_ShowsTheLinkToPermittedUsers_ThroughTheNormalPermissionCheck(string role)
    {
        await using var host = await GateHost.StartAsync("true");
        var html = await RenderSidebarAsync(host, role);

        Assert.Contains("/Warehouse/Replenishment", html);
        Assert.True(host.PermissionLookupsFor(Module) > 0);
    }

    [Fact]
    public async Task Sidebar_Enabled_StillHidesTheLinkFromUsersWithoutView()
    {
        await using var host = await GateHost.StartAsync("true");
        var html = await RenderSidebarAsync(host, GateHost.NoPermissionRole);

        Assert.DoesNotContain("/Warehouse/Replenishment", html);
    }

    [Theory]
    [InlineData(RoleCatalog.WarehouseManager)]
    [InlineData(SystemRoles.SystemAdmin)]
    public async Task Sidebar_TheOnlyDifferenceBetweenDisabledAndEnabled_IsTheReplenishmentLink(string role)
    {
        string disabled, enabled;
        await using (var host = await GateHost.StartAsync(null)) disabled = await RenderSidebarAsync(host, role);
        await using (var host = await GateHost.StartAsync("true")) enabled = await RenderSidebarAsync(host, role);

        var link = new Regex(@"<a[^>]*/Warehouse/Replenishment[^>]*>.*?</a>", RegexOptions.Singleline);
        Assert.Single(link.Matches(enabled));

        static string Normalize(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        Assert.Equal(Normalize(disabled), Normalize(link.Replace(enabled, "")));
        // سایر لینک‌های انبار/موجودی/مواد برای همین نقش بدون تغییر هستند
        Assert.Contains("/Warehouse/StoreRequisition", disabled);
        Assert.Contains("/Warehouse/InventoryTransaction", disabled);
    }

    private static async Task<string> RenderSidebarAsync(GateHost host, string role)
    {
        var response = await SendAsync(host, "GET /Probe/Sidebar", "tester", role);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"sidebar probe failed {(int)response.StatusCode}: {body[..Math.Min(body.Length, 600)]}");
        return body;
    }

    // ============================================================ 4. دامنه‌ی تغییر
    [Fact]
    public void OnlyTheReplenishmentController_CarriesTheGate_AndItIsNotAGlobalFilter()
    {
        var web = typeof(ReplenishmentController).Assembly;
        var carriers = web.GetTypes()
            .Where(t => t.GetCustomAttribute<RequireReplenishmentAvailableAttribute>() != null
                     || t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Any(m => m.GetCustomAttribute<RequireReplenishmentAvailableAttribute>() != null))
            .Select(t => t.FullName)
            .ToArray();
        Assert.Equal(new[] { typeof(ReplenishmentController).FullName }, carriers);

        var program = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Program.cs"));
        Assert.DoesNotContain("options.Filters.Add(new PEMS.Web.Security.RequireReplenishment", program);
        Assert.DoesNotContain("Filters.Add(new RequireReplenishment", program);
    }

    [Fact]
    public void ProgramCs_GlobalFiltersAreStillExactlyTheTwoThisHostMirrors_AndTheGateServiceIsRegistered()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Program.cs"));

        // این Host آزمایشی فقط وقتی فیلترهای سراسری واقعی را بازتاب می‌دهد که Program.cs همین دو فیلتر را به همین ترتیب ثبت کند
        var authorize = program.IndexOf("new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy)", StringComparison.Ordinal);
        var passwordGate = program.IndexOf("new PEMS.Web.Security.PasswordChangeGateFilter()", StringComparison.Ordinal);
        Assert.True(authorize > 0 && passwordGate > authorize);
        Assert.Equal(2, Regex.Matches(program, @"options\.Filters\.Add\(").Count);
        Assert.Contains("AddSingleton<PEMS.Web.Security.IReplenishmentAvailability, PEMS.Web.Security.ReplenishmentAvailability>()", program);
    }

    [Fact]
    public void TheGate_HasNoDependencyOnPermissionsSeedsOrTheDatabase()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Security", "ReplenishmentAvailability.cs"));
        foreach (var forbidden in new[] { "RolePermission", "PermissionService", "Seed", "DbContext", "EntityFramework", "IAccessControlService" })
            Assert.DoesNotContain(forbidden, text);
    }

    [Fact]
    public void TheSidebar_UsesTheGateBeforeAnyPermissionLookup()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Shared", "_SidebarMenu.cshtml"));
        Assert.Contains("@inject IReplenishmentAvailability ReplenishmentAvailability", text);
        Assert.Contains("var canSeeReplenishment = ReplenishmentAvailability.IsEnabled && await PermissionService.HasPermissionAsync(CurrentUserService.Roles, \"ReplenishmentRecommendation\", \"View\");", text);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

// ====================================================================== Host آزمایشی (فقط InMemory + Fake/Spy)
/// <summary>Spy عمومی: تمام فراخوانی‌های یک اینترفیس را (نام متد + آرگومان‌ها) ثبت می‌کند و به هدف واقعی می‌دهد.</summary>
public class RecordingProxy : DispatchProxy
{
    public object? Target { get; set; }
    public List<string> Calls { get; set; } = new();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        lock (Calls) Calls.Add(targetMethod!.Name + "(" + string.Join(",", (args ?? Array.Empty<object?>()).Select(a => a?.ToString())) + ")");
        try { return targetMethod!.Invoke(Target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    public static T Wrap<T>(T target, out List<string> calls) where T : class
    {
        var proxy = Create<T, RecordingProxy>();
        var recording = (RecordingProxy)(object)proxy;
        recording.Target = target;
        calls = recording.Calls;
        return proxy;
    }
}

public sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()), new(ClaimTypes.NameIdentifier, "77") };
        foreach (var role in Request.Headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
            claims.Add(new Claim(ClaimTypes.Role, role));
        if (Request.Headers["X-Test-MustChange"] == "true") claims.Add(new Claim("MustChangePassword", "true"));

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class HttpContextCurrentUser : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;
    private ClaimsPrincipal? User => _accessor.HttpContext?.User;
    public int? UserId => int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? UserName => User?.Identity?.Name;
    public string? FullName => User?.Identity?.Name;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
    public IReadOnlyList<string> Roles => User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();
}

/// <summary>کنترلرهای آزمایشی: مسیر نامرتبط، رندر سایدبار واقعی، و AccountController (مقصد Redirect فیلتر تغییر رمز).</summary>
public class ProbeController : Controller
{
    public IActionResult Ping() => Content("pong");
    public IActionResult Sidebar() => PartialView("_SidebarMenu");
}

public class AccountController : Controller
{
    public IActionResult ChangePassword() => Content("change-password");
}

public sealed class QueryCounter : IQueryExpressionInterceptor
{
    public int Count;
    public System.Linq.Expressions.Expression QueryCompilationStarting(System.Linq.Expressions.Expression queryExpression, QueryExpressionEventData eventData)
    {
        Interlocked.Increment(ref Count);
        return queryExpression;
    }
}

public sealed class ActionProbe : IActionFilter
{
    public int ReplenishmentActionsExecuted;
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor d && d.ControllerName == "Replenishment")
            Interlocked.Increment(ref ReplenishmentActionsExecuted);
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}

internal sealed class OnlyTheseControllers : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly HashSet<Type> _allowed;
    public OnlyTheseControllers(params Type[] allowed) => _allowed = new HashSet<Type>(allowed);
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var type in feature.Controllers.ToList())
            if (!_allowed.Contains(type.AsType())) feature.Controllers.Remove(type);
    }
}

public sealed class GateHost : IAsyncDisposable
{
    public const string NoPermissionRole = "NO_PERMISSION_ROLE";

    private WebApplication _app = null!;
    private ConfigurationManager _config = null!;
    private List<string> _serviceCalls = null!;
    private List<string> _permissionCalls = null!;
    private List<string> _accessCalls = null!;
    private readonly QueryCounter _queries = new();
    private readonly ActionProbe _actions = new();

    public HttpClient Client { get; private set; } = null!;
    public PEMSDbContext Ctx { get; private set; } = null!;

    public int ServiceCalls { get { lock (_serviceCalls) return _serviceCalls.Count; } }
    public int PermissionLookupsFor(string module) { lock (_permissionCalls) return _permissionCalls.Count(c => c.Contains(module)); }

    public static async Task<GateHost> StartAsync(string? enabledValue)
    {
        var host = new GateHost();
        await host.InitAsync(enabledValue);
        return host;
    }

    private async Task InitAsync(string? enabledValue)
    {
        Ctx = new PEMSDbContext(new DbContextOptionsBuilder<PEMSDbContext>()
            .UseInMemoryDatabase("ReplGate_" + Guid.NewGuid().ToString("N"))
            .AddInterceptors(_queries)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options);

        // نقش‌ها و مجوزها: WH.Manager دقیقاً View/Create/Edit (مثل ردیف واقعی)، SystemAdmin بدون ردیف (فقط Bypass عمومی)، NO_PERMISSION_ROLE بدون ردیف
        foreach (var role in new[] { RoleCatalog.WarehouseManager, SystemRoles.SystemAdmin, NoPermissionRole })
            Ctx.Roles.Add(new ApplicationRole(role) { NormalizedName = role.ToUpperInvariant() });
        Ctx.RolePermissions.Add(new RolePermission { RoleName = RoleCatalog.WarehouseManager, ModuleName = "ReplenishmentRecommendation", CanView = true, CanCreate = true, CanEdit = true, CreatedDate = DateTime.Now });
        await Ctx.SaveChangesAsync();

        // WH.Manager در پیش‌فرض MenuAccess (CmmsIntegration) بخش «Inventory & Materials» سایدبار را نمی‌بیند؛ این Override مدیر
        // (NavigationVisibilityOverride) همان بخش را برای این نقش آشکار می‌کند تا حالت «لینک قابل‌مشاهده» واقعاً آزموده شود
        Ctx.NavigationVisibilityOverrides.Add(new NavigationVisibilityOverride
        {
            RoleId = Ctx.Roles.First(r => r.Name == RoleCatalog.WarehouseManager).Id, ModuleKey = "Inventory", IsVisible = true
        });
        await Ctx.SaveChangesAsync();

        var seedServices = new ServiceCollection();
        seedServices.AddSingleton(Ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(seedServices.BuildServiceProvider());

        var access = new AccessControlService(Ctx);
        var permissions = new PermissionService(Ctx, access);
        var netDemand = new NetDemandService(new NetDemandCollector(Ctx), new UsableStockService(Ctx), permissions);
        var realService = new ReplenishmentRecommendationService(Ctx, netDemand, permissions);

        var spyService = RecordingProxy.Wrap<IReplenishmentRecommendationService>(realService, out _serviceCalls);
        var spyPermissions = RecordingProxy.Wrap<IPermissionService>(permissions, out _permissionCalls);
        var spyAccess = RecordingProxy.Wrap<IAccessControlService>(access, out _accessCalls);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        _config = builder.Configuration;
        _config.Sources.Clear();
        _config.AddInMemoryCollection(new Dictionary<string, string?>());
        if (enabledValue != null) _config[ReplenishmentAvailability.ConfigurationKey] = enabledValue;

        var services = builder.Services;
        services.AddLocalization(o => o.ResourcesPath = "");
        services.AddHttpContextAccessor();
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", _ => { });

        services.AddSingleton(Ctx);
        services.AddSingleton(spyAccess);
        services.AddSingleton(spyPermissions);
        services.AddSingleton(spyService);
        services.AddSingleton<ICurrentUserService, HttpContextCurrentUser>();
        services.AddSingleton<IRoleModuleActionService>(new RoleModuleActionService(Ctx, access));
        services.AddSingleton<INavigationVisibilityOverrideService>(new NavigationVisibilityOverrideService(Ctx));
        services.AddSingleton<NavigationVisibilityResolver>();
        // دروازه‌ی تحت آزمایش، دقیقاً همان ثبت Program.cs
        services.AddSingleton<IReplenishmentAvailability, ReplenishmentAvailability>();

        services.AddControllersWithViews(options =>
            {
                // فیلترهای سراسری دقیقاً مطابق Program.cs (به‌همراه ترتیب) - ProgramCs_GlobalFiltersAreStillExactlyTheTwoThisHostMirrors محافظ انحراف است
                var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                options.Filters.Add(new AuthorizeFilter(policy));
                options.Filters.Add(new PasswordChangeGateFilter());
                options.Filters.Add(_actions);      // شاهد: اگر دروازه نشت کند، اکشن اجرا شده و شمارنده بالا می‌رود
            })
            .AddApplicationPart(typeof(ReplenishmentController).Assembly)
            .AddApplicationPart(typeof(GateHost).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(
                new OnlyTheseControllers(typeof(ReplenishmentController), typeof(ProbeController), typeof(AccountController))));

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
        _app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await _app.StartAsync();

        Client = _app.GetTestClient();
    }

    public void SetEnabled(string value) => _config[ReplenishmentAvailability.ConfigurationKey] = value;

    public record Counters(int Service, int Actions, int Queries, int PermissionP5, int AccessP5);

    public Counters Snapshot() => new(ServiceCalls, _actions.ReplenishmentActionsExecuted, _queries.Count, PermissionLookupsFor("ReplenishmentRecommendation"),
        AccessCallsFor("ReplenishmentRecommendation"));

    private int AccessCallsFor(string module) { lock (_accessCalls) return _accessCalls.Count(c => c.Contains(module)); }

    public int P5RowCount() => Ctx.NetDemandRuns.Count() + Ctx.ReplenishmentRecommendations.Count();

    /// <summary>هیچ اکشن/سرویس/کوئری/بررسی مجوز/ثبت ممیزی/نوشتن P5 بین Snapshot و اکنون رخ نداده است.</summary>
    public void AssertNothingP5Ran(Counters before)
    {
        var after = Snapshot();       // پیش از شمارش‌های خود تست (که کوئری مصرف می‌کنند)
        Assert.Equal(before, after);
        Assert.Equal(0, Ctx.AuthorizationAuditLogs.Count());
        Assert.Equal(0, P5RowCount());
    }

    public string PermissionFingerprint() => string.Join("|",
        Ctx.RolePermissions.AsNoTracking().OrderBy(r => r.RoleName).ThenBy(r => r.ModuleName).Select(r => $"{r.RoleName}:{r.ModuleName}:{r.CanView}{r.CanCreate}{r.CanEdit}{r.CanDelete}{r.CanApprove}"))
        + "#" + Ctx.RolePermissionGrants.Count();

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await Ctx.DisposeAsync();
    }
}
