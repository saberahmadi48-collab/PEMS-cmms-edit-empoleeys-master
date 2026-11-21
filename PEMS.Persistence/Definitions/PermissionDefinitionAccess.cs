using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;

namespace PEMS.Persistence.Definitions;

/// <summary>Stage 2 of the pipeline with the product's permission service: a definition names its permission as "Module.Action"; none named means no extra right is needed.</summary>
public sealed class PermissionDefinitionAccess(IPermissionService permissions, IDefinitionGrantStore? grants = null) : IDefinitionAccess
{
    /// <summary>SCRUM-591: the permission's answer, then the grants of the definition for the user's roles (a deny wins, an allow adds).</summary>
    public async Task<bool> CanOpenAsync(string kind, string key, string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var byPermission = await CanOpenAsync(permission, roles, ct);
        if (grants is null) return byPermission;
        return DefinitionGrantRules.Decide(byPermission, roles, await grants.ForAsync(kind, key, ct));
    }

    public async Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permission)) return true;
        var dot = permission.LastIndexOf('.');
        if (dot <= 0 || dot == permission.Length - 1) return false;   // a malformed permission never opens the page
        return await permissions.HasPermissionAsync(roles, permission[..dot], permission[(dot + 1)..]);
    }
}
