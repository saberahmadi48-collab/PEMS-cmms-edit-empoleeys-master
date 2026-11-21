using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using PEMS.Application.Interfaces;
using PEMS.Web.Navigation;

namespace PEMS.Web.Services;

/// <summary>SCRUM-703: the results of the global search page. It asks <see cref="CommandPaletteService"/> (permission and row scope of SCRUM-64) and never reads a table itself.</summary>
public sealed class GlobalSearchOperations(CommandPaletteService palette, IHttpContextAccessor http, IUrlHelperFactory urls) : IGlobalSearchOperations
{
    public const int MinLength = 2;
    public const int PerType = 15;

    public async Task<List<GlobalSearchRow>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinLength) return [];
        var context = http.HttpContext ?? throw new InvalidOperationException("The search needs the request it runs in.");
        var url = urls.GetUrlHelper(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
        var groups = await palette.SearchAsync(query, url, PerType);
        return groups.SelectMany(g => g.Items.Select(i => new GlobalSearchRow(g.Title, i.Label, i.Sub ?? "", i.Url))).ToList();
    }
}
