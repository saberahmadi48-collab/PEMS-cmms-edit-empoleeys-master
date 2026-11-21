using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class DisciplineSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Discipline";
    public string DisplayName => "دیسیپلین‌ها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Permission("DisciplineWorkspace");

    public DisciplineSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.Disciplines
            .Where(x => x.IsActive)
            .Where(x => x.Name.Contains(query) || x.Code.Contains(query))
            .ToListAsync();

        var scored = pool
            .Select(d => new { Discipline = d, Score = SearchScoring.ComputeWeightedScore(query, (d.Name, 1.0), (d.Code, 0.9)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Discipline.Name)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Discipline.DisciplineId,
                Label = x.Discipline.Name,
                HighlightedLabel = SearchScoring.Highlight(x.Discipline.Name, query),
                SubLabel = x.Discipline.Code,
                Url = $"/p/disciplines-details?id={x.Discipline.DisciplineId}",   // SCRUM-682: the Discipline controller is a definition page now
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
