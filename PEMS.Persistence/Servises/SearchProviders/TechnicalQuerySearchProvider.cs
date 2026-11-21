using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class TechnicalQuerySearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "TechnicalQuery";
    public string DisplayName => "TQ / TSR / RFI";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public TechnicalQuerySearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var candidatesQuery = _context.TechnicalQueries
            .Include(x => x.Discipline)
            .Where(x => x.IsActive)
            .Where(x =>
                x.QueryNumber.Contains(query) ||
                x.Subject.Contains(query) ||
                (x.Description != null && x.Description.Contains(query)));

        if (projectId.HasValue) candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

        var pool = await candidatesQuery.Take(300).ToListAsync();

        var scored = pool
            .Select(t => new
            {
                Item = t,
                Score = SearchScoring.ComputeWeightedScore(query, (t.QueryNumber, 1.0), (t.Subject, 0.8), (t.Description, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.RaisedDate)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Item.TechnicalQueryId,
                Label = x.Item.QueryNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Item.QueryNumber, query),
                SubLabel = string.Join(" — ", new[] { x.Item.Subject, x.Item.Discipline?.Name, x.Item.Status }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/TechnicalQuery/Details/{x.Item.TechnicalQueryId}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
