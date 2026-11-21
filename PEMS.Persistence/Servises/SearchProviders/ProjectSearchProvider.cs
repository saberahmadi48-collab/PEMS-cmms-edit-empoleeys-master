using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class ProjectSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Project";
    public string DisplayName => "پروژه‌ها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public ProjectSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.Projects
            .Where(x => x.IsActive)
            .Where(x => x.ProjectCode.Contains(query) || x.ProjectName.Contains(query)
                || (x.ClientName != null && x.ClientName.Contains(query)))
            .Take(500)
            .ToListAsync();

        if (pool.Count < 5 && query.Length is >= 2 and <= 25)
        {
            var fallback = await _context.Projects.Where(x => x.IsActive).Take(500).ToListAsync();
            foreach (var p in fallback)
                if (pool.All(x => x.ProjectId != p.ProjectId)) pool.Add(p);
        }

        var scored = pool
            .Select(p => new
            {
                Project = p,
                Score = SearchScoring.ComputeWeightedScore(query, (p.ProjectCode, 1.0), (p.ProjectName, 0.9), (p.ClientName, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Project.ProjectCode)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Project.ProjectId,
                Label = x.Project.ProjectCode,
                HighlightedLabel = SearchScoring.Highlight(x.Project.ProjectCode, query),
                SubLabel = string.Join(" — ", new[] { x.Project.ProjectName, x.Project.ClientName, x.Project.Status }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/p/projects-details?id={x.Project.ProjectId}",   // the Projects controller is a definition page now
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
