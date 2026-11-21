using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PEMS.Application.Definitions;

namespace PEMS.Web.Definitions;

/// <summary>
/// The values of the request that a stored page may put into its options (SCRUM-600): <c>{{params.id}}</c> (the id of the route or query string), <c>{{params.id|url}}</c> (the same, escaped for a url).
/// Replaced at render time in every text of an instance's options; an unknown name becomes empty. Never evaluated as an expression.
/// </summary>
public static class RuntimeValues
{
    private static readonly Regex Placeholder = new(@"\{\{params\.([A-Za-z0-9_]{1,64})(\|url)?\}\}", RegexOptions.Compiled);

    /// <summary>A copy of the node with the request values put in (the stored definition is never changed).</summary>
    public static JsonNode? Substitute(JsonNode? node, PipelineRequest request)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var text = (string)v!;
                return text.Contains("{{", StringComparison.Ordinal) ? JsonValue.Create(Placeholder.Replace(text, m => Lookup(request, m.Groups[1].Value, m.Groups[2].Success))) : JsonValue.Create(text);
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (key, child) in o) copy[key] = Substitute(child, request);
                return copy;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var child in a) list.Add(Substitute(child, request));
                return list;
            default:
                return node.DeepClone();
        }
    }

    private static string Lookup(PipelineRequest request, string name, bool url)
    {
        var value = request.Parameters is not null && request.Parameters.TryGetValue(name, out var v) ? v : "";
        return url ? Uri.EscapeDataString(value) : value;
    }
}
