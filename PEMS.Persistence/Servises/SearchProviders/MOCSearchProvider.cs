using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class MOCSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "MOC";
    public string DisplayName => "MOC (Management of Change)";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public MOCSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var candidatesQuery = _context.MOCs
            .Include(x => x.Discipline)
            .Where(x => x.IsActive)
            .Where(x =>
                x.MOCNumber.Contains(query) ||
                x.Title.Contains(query) ||
                x.Description.Contains(query));

        if (projectId.HasValue) candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

        var pool = await candidatesQuery.Take(300).ToListAsync();

        var scored = pool
            .Select(m => new
            {
                Item = m,
                Score = SearchScoring.ComputeWeightedScore(query, (m.MOCNumber, 1.0), (m.Title, 0.8), (m.Description, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.RequestedDate)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Item.MOCId,
                Label = x.Item.MOCNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Item.MOCNumber, query),
                SubLabel = string.Join(" — ", new[] { x.Item.Title, x.Item.RiskLevel, x.Item.Status }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/MOC/Details/{x.Item.MOCId}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
