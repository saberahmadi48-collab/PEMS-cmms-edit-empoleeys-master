using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class ContractSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Contract";
    public string DisplayName => "قراردادها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Permission("Contracts");

    public ContractSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var candidatesQuery = _context.Contracts
            .Include(x => x.Vendor)
            .Where(x => x.ContractNo.Contains(query) || (x.ScopeOfWork != null && x.ScopeOfWork.Contains(query))
                || (x.Vendor != null && x.Vendor.Name.Contains(query)));

        if (projectId.HasValue) candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

        var pool = await candidatesQuery.Take(500).ToListAsync();

        var scored = pool
            .Select(c => new
            {
                Contract = c,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (c.ContractNo, 1.0), (c.Vendor?.Name, 0.7), (c.ScopeOfWork, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Contract.ContractNo)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Contract.ContractId,
                Label = x.Contract.ContractNo,
                HighlightedLabel = SearchScoring.Highlight(x.Contract.ContractNo, query),
                SubLabel = x.Contract.Vendor?.Name,
                Url = $"/Contract/Details/{x.Contract.ContractId}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
