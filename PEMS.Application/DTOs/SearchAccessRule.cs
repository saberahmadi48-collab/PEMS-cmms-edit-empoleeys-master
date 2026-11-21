namespace PEMS.Application.DTOs;

/// <summary>How a <see cref="SearchAccessRule"/> decides who may see a provider's results.</summary>
public enum SearchAccessKind
{
    /// <summary>Any signed-in user: the target page has no module check (row scope may still apply inside the provider).</summary>
    AnyUser,
    /// <summary>The user needs a permission-matrix grant (module + action), the same check the target page makes.</summary>
    Permission,
    /// <summary>The user needs one of the listed roles, the same <c>[Authorize(Roles = ...)]</c> the target page has.</summary>
    Roles,
}

/// <summary>
/// SCRUM-64: the security contract of an <c>ISearchableEntityProvider</c>. Every provider states the rule of the page
/// its results open (list or details page), and the central <c>GlobalSearchService</c> checks it before the provider
/// runs, so a module the user cannot open never answers a search. Row-level limits (confidential letters, own
/// discipline, own notifications) stay inside the provider's query and are named in <see cref="RowScope"/>.
/// </summary>
public sealed record SearchAccessRule
{
    public SearchAccessKind Kind { get; private init; }
    public string? Module { get; private init; }
    public string? Action { get; private init; }
    public IReadOnlyList<string> AnyOfRoles { get; private init; } = Array.Empty<string>();

    /// <summary>The row-level filter the provider applies itself (null = none); shown in tests and docs.</summary>
    public string? RowScope { get; private init; }

    /// <summary>
    /// False for lookup-only providers (no details page, e.g. Employee): the command palette and the search page skip
    /// them; form pickers (<c>/SearchApi?type=</c>) still use them.
    /// </summary>
    public bool Navigable { get; private init; } = true;

    public static SearchAccessRule AnyUser(string? rowScope = null) =>
        new() { Kind = SearchAccessKind.AnyUser, RowScope = rowScope };

    public static SearchAccessRule Permission(string module, string action = "View", string? rowScope = null) =>
        new() { Kind = SearchAccessKind.Permission, Module = module, Action = action, RowScope = rowScope };

    public static SearchAccessRule Roles(params string[] roles) =>
        new() { Kind = SearchAccessKind.Roles, AnyOfRoles = roles };

    /// <summary>Marks the provider as lookup-only (see <see cref="Navigable"/>).</summary>
    public SearchAccessRule LookupOnly() => this with { Navigable = false };

    public override string ToString() => Kind switch
    {
        SearchAccessKind.Permission => $"{Module}/{Action}",
        SearchAccessKind.Roles => "roles: " + string.Join(", ", AnyOfRoles),
        _ => "any user",
    } + (RowScope == null ? "" : $" + row scope: {RowScope}") + (Navigable ? "" : " (lookup only)");
}
