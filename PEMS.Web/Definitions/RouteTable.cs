using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

// SCRUM-768 (the sprint goal: a screen is data). One route table in the definition store maps a URL pattern to a generic host (page, form or data source) with the captured values, so no
// per-entity controller is needed and an old link keeps working after its controller is deleted. The table is only asked when no controller or hub answered the request, so a definition can never
// shadow a code route; it fills the place a deleted controller leaves. A page may also declare its own public route (the `route` of a page definition).

/// <summary>
/// A route definition: <c>{ "pattern": "WorkOrder/Details/{id}", "target": "/p/work-order-details?id={id}", "permanent": false }</c>. The pattern is an ASP.NET route template (no leading slash);
/// the target is a path of a generic host (<c>/p/</c>, <c>/f/</c>, <c>/d/</c>) whose <c>{name}</c> placeholders are the captured values; the query string of the request is carried over.
/// </summary>
public sealed class RouteDefinitionKind : IDefinitionKind
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "pattern", "target", "permanent" };
    public static readonly string[] HostPrefixes = ["/p/", "/f/", "/d/", "/x/"];
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public string Key => "route";

    public IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits)
    {
        var errors = new List<DefinitionFieldError>();
        if (JsonNode.Parse(payloadJson) is not JsonObject root) return [new("", "wrong-type", "Def_Err_Type", "A route is an object.")];
        foreach (var p in root) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a route."));

        var pattern = Str(root["pattern"]);
        var target = Str(root["target"]);
        if (string.IsNullOrWhiteSpace(pattern)) errors.Add(new("/pattern", "required", "Def_Err_Required", "A route needs a pattern."));
        else
        {
            var bad = PatternProblem(pattern);
            if (bad is not null) errors.Add(new("/pattern", "invalid-pattern", "Def_Err_Pattern", bad));
        }
        if (string.IsNullOrWhiteSpace(target)) errors.Add(new("/target", "required", "Def_Err_Required", "A route needs a target."));
        else if (!HostPrefixes.Any(h => target.StartsWith(h, StringComparison.Ordinal))) errors.Add(new("/target", "invalid-target", "Def_Err_Target", "The target must start with /p/, /f/ or /d/ (a generic host)."));
        else if (!string.IsNullOrWhiteSpace(pattern) && PatternProblem(pattern) is null)
        {
            var names = Parameters(pattern);
            foreach (Match m in Placeholder.Matches(target))
                if (!names.Contains(m.Groups[1].Value)) errors.Add(new("/target", "unknown-placeholder", "Def_Err_Placeholder", $"'{{{m.Groups[1].Value}}}' is not a parameter of the pattern."));
        }
        if (root["permanent"] is { } perm && !(perm is JsonValue pv && pv.GetValueKind() is JsonValueKind.True or JsonValueKind.False)) errors.Add(new("/permanent", "wrong-type", "Def_Err_Type", "permanent is true or false."));
        return errors;
    }

    public IReadOnlyList<DefinitionRef> References(string payloadJson)
    {
        if (JsonNode.Parse(payloadJson) is not JsonObject root || Str(root["target"]) is not { } target) return [];
        var m = Regex.Match(target, "^/(p|f|d)/([a-z0-9][a-z0-9._-]*)");
        if (!m.Success) return [];
        var kind = m.Groups[1].Value switch { "p" => "page", "f" => "form", _ => "data-source" };
        return [new DefinitionRef(kind, m.Groups[2].Value, "routes-to")];
    }

    /// <summary>Why a pattern is refused; null when it is a plain relative route template.</summary>
    internal static string? PatternProblem(string pattern)
    {
        if (pattern.StartsWith('/') || pattern.StartsWith('~')) return "The pattern is relative: no leading slash.";
        if (pattern.Contains("//") || pattern.Contains("..") || pattern.Contains('?') || pattern.Contains('#')) return "The pattern may not contain '//', '..', '?' or '#'.";
        var first = pattern.Split('/', 2)[0].ToLowerInvariant();
        if (first is "api" or "hubs" or "p" or "f" or "a" or "d" or "files" or "lib" or "css" or "js" or "_framework") return $"'{first}' is a reserved prefix.";
        try { TemplateParser.Parse(pattern); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return "Not a valid route template: " + ex.Message; }
        return null;
    }

    internal static HashSet<string> Parameters(string pattern)
        => TemplateParser.Parse(pattern).Parameters.Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);

    private static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;
}

public sealed record RouteMatch(string Location, bool Permanent);

/// <summary>The compiled table: matches a request path and builds the target location. Rebuilt only when the published routes change (the signature of their content hashes).</summary>
public sealed class RouteTable(IDefinitionStore store)
{
    private sealed record Entry(string Pattern, TemplateMatcher Matcher, string Target, bool Permanent, string Key);
    private sealed record Compiled(string Signature, IReadOnlyList<Entry> Entries);
    private volatile Compiled _compiled = new("", []);

    public async Task<RouteMatch?> MatchAsync(string path, string? queryString, string appKey = "core", CancellationToken ct = default)
    {
        var table = await CompileAsync(appKey, ct);
        if (table.Entries.Count == 0) return null;
        var trimmed = path.Trim('/');
        var segments = new RouteValueDictionary();
        // exact patterns first (no parameters), then the more specific templates; the order is the key order of the definitions
        foreach (var e in table.Entries)
        {
            segments.Clear();
            if (!e.Matcher.TryMatch("/" + trimmed, segments)) continue;
            var target = Regex.Replace(e.Target, @"\{([A-Za-z_][A-Za-z0-9_]*)\}", m => segments.TryGetValue(m.Groups[1].Value, out var v) ? Uri.EscapeDataString(v?.ToString() ?? "") : "");
            if (!string.IsNullOrEmpty(queryString) && queryString != "?") target += (target.Contains('?') ? "&" : "?") + queryString.TrimStart('?');
            return new RouteMatch(target, e.Permanent);
        }
        return null;
    }

    private async Task<Compiled> CompileAsync(string appKey, CancellationToken ct)
    {
        var routes = await store.ListPublishedAsync("route", appKey, ct);
        var pages = await store.ListPublishedAsync("page", appKey, ct);
        var signature = Signature(routes, pages);
        var current = _compiled;
        if (current.Signature == signature) return current;

        var entries = new List<Entry>();
        foreach (var r in routes)
        {
            if (JsonNode.Parse(r.PayloadJson) is not JsonObject o) continue;
            var pattern = (string?)o["pattern"]; var target = (string?)o["target"];
            if (pattern is null || target is null || RouteDefinitionKind.PatternProblem(pattern) is not null) continue;
            entries.Add(new Entry(pattern, new TemplateMatcher(TemplateParser.Parse(pattern), new RouteValueDictionary()), target, o["permanent"] is JsonValue pv && pv.GetValueKind() == JsonValueKind.True, r.Key));
        }
        // a page may declare its own public route: /{route} shows /p/{key}
        foreach (var p in pages)
        {
            if (JsonNode.Parse(p.PayloadJson) is not JsonObject o || o["route"] is not JsonValue rv || rv.GetValueKind() != JsonValueKind.String) continue;
            var pattern = ((string)rv!).Trim('/');
            if (pattern.Length == 0 || RouteDefinitionKind.PatternProblem(pattern) is not null || entries.Any(e => e.Pattern == pattern)) continue;
            entries.Add(new Entry(pattern, new TemplateMatcher(TemplateParser.Parse(pattern), new RouteValueDictionary()), "/p/" + p.Key, false, p.Key));
        }
        var compiled = new Compiled(signature, entries.OrderBy(e => e.Pattern.Contains('{') ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal).ToList());
        _compiled = compiled;
        return compiled;
    }

    private static string Signature(IReadOnlyList<PublishedDefinition> routes, IReadOnlyList<PublishedDefinition> pages)
    {
        var sb = new StringBuilder();
        foreach (var d in routes.OrderBy(d => d.Key, StringComparer.Ordinal)) sb.Append(d.Key).Append(':').Append(d.ContentHash).Append(';');
        sb.Append('|');
        foreach (var d in pages.Where(d => d.PayloadJson.Contains("\"route\"", StringComparison.Ordinal)).OrderBy(d => d.Key, StringComparer.Ordinal)) sb.Append(d.Key).Append(':').Append(d.ContentHash).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

public static class RouteTableExtensions
{
    /// <summary>Asks the route table for every GET that no endpoint answered; a match redirects to the generic host. Place it after routing and authentication, before the endpoints.</summary>
    public static IApplicationBuilder UseRouteTable(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.GetEndpoint() is null && HttpMethods.IsGet(context.Request.Method) && context.Request.Path.HasValue && context.Request.Path.Value != "/")
        {
            var table = context.RequestServices.GetRequiredService<RouteTable>();
            var match = await table.MatchAsync(context.Request.Path.Value!, context.Request.QueryString.Value, ct: context.RequestAborted);
            if (match is not null)
            {
                context.Response.Redirect(match.Location, match.Permanent);
                return;
            }
        }
        await next();
    });
}
