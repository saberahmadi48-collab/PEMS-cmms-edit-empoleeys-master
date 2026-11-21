using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class EmployeeSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Employee";
    public string DisplayName => "کارمندان";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Roles(PEMS.Persistence.Identity.SystemRoles.SystemAdmin, PEMS.Persistence.Identity.SystemRoles.EngineeringManager).LookupOnly();

    public EmployeeSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.Employees
            .Include(x => x.Department)
            .Include(x => x.Discipline)
            .Where(x => x.IsActive)
            .Where(x => x.FirstName.Contains(query) || x.LastName.Contains(query)
                || x.PersonnelCode.Contains(query) || (x.Email != null && x.Email.Contains(query)))
            .Take(500)
            .ToListAsync();

        if (pool.Count < 5 && query.Length is >= 2 and <= 25)
        {
            var fallback = await _context.Employees.Include(x => x.Department).Include(x => x.Discipline)
                .Where(x => x.IsActive).Take(500).ToListAsync();
            foreach (var e in fallback)
                if (pool.All(x => x.EmployeeId != e.EmployeeId)) pool.Add(e);
        }

        var scored = pool
            .Select(e => new
            {
                Employee = e,
                FullName = e.FirstName + " " + e.LastName,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (e.PersonnelCode, 1.0), (e.FirstName + " " + e.LastName, 0.9), (e.Email, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.FullName)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Employee.EmployeeId,
                Label = x.FullName,
                HighlightedLabel = SearchScoring.Highlight(x.FullName, query),
                SubLabel = string.Join(" — ", new[] { x.Employee.Position, x.Employee.Department?.Name, x.Employee.Discipline?.Name }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = null, // صفحه‌ی Details جدا نداره - فقط برای انتخاب در فرم استفاده می‌شه
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
