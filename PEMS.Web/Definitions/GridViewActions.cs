using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-793 (gap of SCRUM-599): the saved views of a generic list live on the server, per user and per role, so a view follows its owner to another device and a role view is shared by its members. Everything is a
// data source (grid-views) and two actions (grid.view.save, grid.view.delete): the list component reads and writes them through the same /d and /a hosts as any definition, with the rights of the signed-in user.
// A user reads their own views and the views of the roles they hold; they write their own views and the views of a role they hold. Every change is in the audit log through the context.

internal static class ViewScopes
{
    public const int MaxViewsPerScope = 50;
    public const int MaxStateChars = 20_000;
    public const string User = "user";
    public const string Role = "role";

    /// <summary>The scope text of the client: <c>user</c> or <c>role:Name</c>; null for anything else.</summary>
    public static (string Kind, string? Key)? Parse(string? scope)
    {
        if (scope == User) return (User, null);
        return scope is { Length: > 5 } && scope.StartsWith("role:", StringComparison.Ordinal) ? (Role, scope[5..]) : null;
    }

    public static string Format(string kind, string key) => kind == Role ? "role:" + key : User;

    /// <summary>The scope key a view is stored under: the user name for a user view, the role name for a role view; null when the user may not write that scope.</summary>
    public static string? Resolve((string Kind, string? Key) scope, string? userName, IReadOnlyCollection<string> roles)
        => scope.Kind == User ? (string.IsNullOrWhiteSpace(userName) ? null : userName)
            : roles.FirstOrDefault(r => string.Equals(r, scope.Key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The saved views of a list for the signed-in user (filter <c>viewsKey</c>): <c>kind</c> <c>view</c> rows (<c>id</c>, <c>name</c>, <c>scope</c> = <c>user</c> or <c>role:Name</c>, <c>state</c> as JSON text) and <c>kind</c> <c>role</c> rows
/// (<c>scope</c>, the roles the user may share a view with). A user view of another user is never returned.
/// </summary>
public sealed class GridViewsProvider(IServiceScopeFactory scopes) : IGridDataProvider
{
    public string Key => "grid-views";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
    {
        var viewsKey = WorkbenchRows.FilterValue(request, "viewsKey");
        if (viewsKey.Length == 0 || string.IsNullOrWhiteSpace(request.UserName)) return ([], 0);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var roles = request.Roles.ToList();
        var user = request.UserName;
        var views = await db.GridSavedViews.AsNoTracking()
            .Where(v => v.ViewsKey == viewsKey && ((v.ScopeKind == ViewScopes.User && v.ScopeKey == user) || (v.ScopeKind == ViewScopes.Role && roles.Contains(v.ScopeKey))))
            .OrderBy(v => v.ScopeKind).ThenBy(v => v.Name).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var v in views)
            rows.Add(new Dictionary<string, object?> { ["id"] = v.GridSavedViewId, ["kind"] = "view", ["name"] = v.Name, ["scope"] = ViewScopes.Format(v.ScopeKind, v.ScopeKey), ["state"] = v.StateJson });
        foreach (var role in roles.Order(StringComparer.OrdinalIgnoreCase))
            rows.Add(new Dictionary<string, object?> { ["id"] = "role:" + role, ["kind"] = "role", ["name"] = role, ["scope"] = "role:" + role, ["state"] = "" });
        return (rows, rows.Count);
    }
}

/// <summary><c>grid.view.save</c>: posted <c>viewsKey</c>, <c>name</c>, <c>scope</c> (<c>user</c> or <c>role:Name</c> of a role the user holds) and <c>state</c> (a JSON object, at most 20000 characters). Saving a name that exists replaces it; 50 views per list and scope at most.</summary>
public sealed class GridViewSaveAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "grid.view.save";
    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    private static TranslatableText Text(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var viewsKey = invocation.Values.GetValueOrDefault("viewsKey")?.Trim();
        var name = invocation.Values.GetValueOrDefault("name")?.Trim();
        var state = invocation.Values.GetValueOrDefault("state") ?? "";
        if (string.IsNullOrEmpty(viewsKey) || viewsKey.Length > 100) return ActionOutcome.Refused("viewsKey", Text("The list is not named.", "فهرست نام ندارد."));
        if (string.IsNullOrEmpty(name) || name.Length > 100) return ActionOutcome.Refused("name", Text("Give the view a name of up to 100 characters.", "برای نما نامی تا ۱۰۰ نویسه بنویسید."));
        if (state.Length > ViewScopes.MaxStateChars || !IsObject(state)) return ActionOutcome.Refused("state", Text("The view is not valid.", "نما معتبر نیست."));
        if (ViewScopes.Parse(invocation.Values.GetValueOrDefault("scope")) is not { } parsed || ViewScopes.Resolve(parsed, invocation.UserName, invocation.Roles) is not { } scopeKey)
            return ActionOutcome.Refused("scope", Text("You may not save a view for this scope.", "اجازه ذخیره نما برای این دامنه را ندارید."));

        using var scope = ActionScope.Open(scopes);
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var existing = await db.GridSavedViews.FirstOrDefaultAsync(v => v.ViewsKey == viewsKey && v.ScopeKind == parsed.Kind && v.ScopeKey == scopeKey && v.Name == name, ct);
        if (existing is null)
        {
            if (await db.GridSavedViews.CountAsync(v => v.ViewsKey == viewsKey && v.ScopeKind == parsed.Kind && v.ScopeKey == scopeKey, ct) >= ViewScopes.MaxViewsPerScope)
                return ActionOutcome.Refused("name", Text($"At most {ViewScopes.MaxViewsPerScope} views per list.", $"حداکثر {ViewScopes.MaxViewsPerScope} نما برای هر فهرست."));
            db.GridSavedViews.Add(new GridSavedView { ViewsKey = viewsKey, Name = name, ScopeKind = parsed.Kind, ScopeKey = scopeKey, StateJson = state, CreatedBy = invocation.UserName, CreatedDate = DateTime.UtcNow });
        }
        else existing.StateJson = state;
        await db.SaveChangesAsync(ct);
        return ActionOutcome.Done();
    }

    private static bool IsObject(string json)
    {
        try { return JsonNode.Parse(json) is JsonObject; }
        catch (JsonException) { return false; }
    }
}

/// <summary><c>grid.view.delete</c>: posted <c>viewsKey</c>, <c>name</c> and <c>scope</c>; a user deletes their own views and the views of a role they hold.</summary>
public sealed class GridViewDeleteAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "grid.view.delete";
    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var viewsKey = invocation.Values.GetValueOrDefault("viewsKey")?.Trim();
        var name = invocation.Values.GetValueOrDefault("name")?.Trim();
        if (string.IsNullOrEmpty(viewsKey) || string.IsNullOrEmpty(name)
            || ViewScopes.Parse(invocation.Values.GetValueOrDefault("scope")) is not { } parsed || ViewScopes.Resolve(parsed, invocation.UserName, invocation.Roles) is not { } scopeKey)
            return ActionOutcome.Refused(TranslatableText.Of(("en", "You may not delete this view."), ("fa", "اجازه حذف این نما را ندارید.")));
        using var scope = ActionScope.Open(scopes);
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var view = await db.GridSavedViews.FirstOrDefaultAsync(v => v.ViewsKey == viewsKey && v.ScopeKind == parsed.Kind && v.ScopeKey == scopeKey && v.Name == name, ct);
        if (view is not null) { db.GridSavedViews.Remove(view); await db.SaveChangesAsync(ct); }
        return ActionOutcome.Done();
    }
}
