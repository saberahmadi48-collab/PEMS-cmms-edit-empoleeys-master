using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Api;

/// <summary>
/// Maps the permission matrix (<see cref="IPermissionService"/>) onto ASP.NET authorization policies so API endpoints declare
/// <c>[ApiPermission("DisciplineMaster", "View")]</c> and the policy gate test can see it (chapter 08, rule 13).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ApiPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "perm:";

    public ApiPermissionAttribute(string module, string action) : base(Prefix + module + "." + action) { }
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string module, string action) { Module = module; Action = action; }
    public string Module { get; }
    public string Action { get; }
}

public sealed class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionRequirementHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (await _permissions.HasPermissionAsync(roles, requirement.Module, requirement.Action))
            context.Succeed(requirement);
    }
}

/// <summary>Builds <c>perm:Module.Action</c> policies on demand; every other name falls back to the default provider.</summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ApiPermissionAttribute.Prefix, StringComparison.Ordinal))
        {
            var rest = policyName[ApiPermissionAttribute.Prefix.Length..];
            var dot = rest.LastIndexOf('.');
            if (dot > 0 && dot < rest.Length - 1)
                return new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(rest[..dot], rest[(dot + 1)..])).Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}
