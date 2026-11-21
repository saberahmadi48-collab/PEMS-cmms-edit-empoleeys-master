using System.Globalization;
using PEMS.Application.Definitions;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// The file host (6b of the definition-only sprint): <c>GET /x/{key}</c> answers a file or JSON from the data source definition {key} (provider <c>operation-file</c>): the query values are the arguments the definition names
/// as <c>param:x</c>. The operation answers a file when its result has Bytes (with FileName and ContentType), otherwise its result as JSON. The definition and the operation decide who may; a user without the right gets 403,
/// an unknown key or a missing argument 404. Nothing is cached by the browser or a proxy.
/// </summary>
[Route("x/{key}")]
public sealed class FileHostController(IDefinitionStore store, IDefinitionAccess access, OperationReader reader, ICurrentUserService currentUser) : Controller
{
    private static readonly Regex KeyShape = new("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.Compiled);

    [HttpGet("")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        if (!KeyShape.IsMatch(key)) return NotFound();
        var roles = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var published = await store.GetPublishedAsync("data-source", key, "core", ct);
        if (published is null || JsonNode.Parse(published.PayloadJson) is not JsonObject root || DataSourceDefinitionKind.Str(root["provider"]) != "operation-file") return NotFound();
        if (!await access.CanOpenAsync("data-source", key, DataSourceDefinitionKind.Str(root["permission"]), roles, ct)) return Forbid();

        var parameters = (root["parameters"] as JsonObject)?.ToDictionary(p => p.Key, p => (string?)DataSourceDefinitionKind.Str(p.Value) ?? p.Value?.ToJsonString(), StringComparer.Ordinal) ?? new Dictionary<string, string?>();
        var query = Request.Query.ToDictionary(q => q.Key, q => (string?)q.Value.ToString(), StringComparer.Ordinal);
        OperationReader.Invoked invoked;
        try { invoked = await reader.InvokeAsync(parameters, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, roles, currentUser.UserName, ct, query); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        if (!invoked.Allowed) return Forbid();
        if (invoked.NoInput || invoked.Answer is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        var type = invoked.Answer.GetType();
        if (type.GetProperty("Bytes")?.GetValue(invoked.Answer) is byte[] bytes)
        {
            if (type.GetProperty("Success")?.GetValue(invoked.Answer) is false) return NotFound();
            var contentType = type.GetProperty("ContentType")?.GetValue(invoked.Answer) as string ?? "application/octet-stream";
            var fileName = type.GetProperty("FileName")?.GetValue(invoked.Answer) as string;
            return parameters.GetValueOrDefault("disposition") == "inline" || fileName is null ? File(bytes, contentType) : File(bytes, contentType, fileName);
        }
        return Json(invoked.Answer);
    }
}
