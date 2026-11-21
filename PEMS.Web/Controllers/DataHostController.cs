using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// The rows of a grid data source definition (SCRUM-437, ADR-0014): <c>GET /d/{key}</c> with the standard grid query (page, pageSize, sort, q, f.field=op:value) answers <c>{ items, total }</c> for a component
/// whose options name <c>/d/{key}</c> as its url (data grid, views). The definition decides which registered provider reads, who may, and the provider applies the row-level rules; a user without the
/// right gets 403 and no row.
/// </summary>
[Route("d/{key}")]
public sealed class DataHostController(DataSourceExecutor executor, ICurrentUserService currentUser) : Controller
{
    private static readonly Regex KeyShape = new("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.Compiled);

    [HttpGet("")]
    public async Task<IActionResult> Rows(string key, CancellationToken ct)
    {
        if (!KeyShape.IsMatch(key)) return NotFound();
        var roles = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var query = Request.Query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString()));
        var result = await executor.QueryAsync(key, query, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, roles, currentUser.UserName, "core", ct);
        return result.Status switch
        {
            DataStatus.Ok => Json(new { items = result.Items, total = result.Total }),
            DataStatus.Forbidden => Forbid(),
            DataStatus.Invalid => BadRequest(new { error = result.Error }),
            _ => NotFound()
        };
    }
}
