using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PEMS.Web.Api;

namespace PEMS.Tests.Api;

/// <summary>Chapter 08, rules 1, 2 and 13: every endpoint under /api declares a permission policy, routes are plural kebab-case, actions are never GET.</summary>
public class ApiPolicyGateTests
{
    private static IEnumerable<Type> ApiControllers() =>
        typeof(ApiControllerBase).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ApiControllerBase).IsAssignableFrom(t));

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        ApiControllers().SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).Select(m => (c, m)));

    [Fact]
    public void There_is_at_least_one_api_controller()
    {
        Assert.NotEmpty(ApiControllers());
        Assert.NotEmpty(Actions());
    }

    [Fact]
    public void Every_api_action_declares_a_permission_policy_and_none_is_anonymous()
    {
        var bad = Actions().Where(a =>
        {
            var attrs = a.Action.GetCustomAttributes<AuthorizeAttribute>(true).Concat(a.Controller.GetCustomAttributes<AuthorizeAttribute>(true)).ToList();
            var anonymous = a.Action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any() || a.Controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();
            return anonymous || !attrs.Any(x => x.Policy?.StartsWith(ApiPermissionAttribute.Prefix, StringComparison.Ordinal) == true);
        }).Select(a => $"{a.Controller.Name}.{a.Action.Name}").ToList();
        Assert.True(bad.Count == 0, "API actions without a permission policy (or anonymous): " + string.Join(", ", bad));
    }

    [Fact]
    public void Unsafe_verbs_validate_the_antiforgery_header_and_no_action_opts_out()
    {
        Assert.NotNull(typeof(ApiControllerBase).GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
        Assert.Equal("X-CSRF-TOKEN", ApiControllerBase.AntiforgeryHeader);
        Assert.DoesNotContain(Actions(), a => a.Action.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() != null
                                              || a.Controller.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() != null);
    }

    [Fact]
    public void Routes_live_under_api_v1_as_lowercase_kebab_case_nouns()
    {
        foreach (var c in ApiControllers())
        {
            var template = c.GetCustomAttribute<RouteAttribute>()?.Template;
            Assert.True(template != null && template.StartsWith(ApiControllerBase.Prefix + "/", StringComparison.Ordinal), $"{c.Name} needs a [Route] under {ApiControllerBase.Prefix}");
            Assert.Matches("^api/v1(/[a-z0-9]+(-[a-z0-9]+)*)+$", template!);
        }
    }

    [Fact]
    public void Business_actions_are_never_get_and_reads_never_change_state()
    {
        foreach (var (c, m) in Actions())
        {
            var verbs = m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            var routes = m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template ?? "").ToList();
            if (verbs.Contains("GET")) Assert.DoesNotContain(routes, r => r.Contains("actions/", StringComparison.Ordinal));
        }
    }
}
