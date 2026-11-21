using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class DepartmentSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Department";
    public string DisplayName => "دپارتمان‌ها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Roles(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, PEMS.Persistence.Identity.SystemRoles.EngineeringManager).LookupOnly();

    public DepartmentSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.Departments
            .Where(x => x.IsActive)
            .Where(x => x.Name.Contains(query) || x.Code.Contains(query))
            .ToListAsync();

        var scored = pool
            .Select(d => new { Department = d, Score = SearchScoring.ComputeWeightedScore(query, (d.Name, 1.0), (d.Code, 0.9)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Department.Name)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Department.DepartmentId,
                Label = x.Department.Name,
                HighlightedLabel = SearchScoring.Highlight(x.Department.Name, query),
                SubLabel = x.Department.Code,
                Url = null,
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
