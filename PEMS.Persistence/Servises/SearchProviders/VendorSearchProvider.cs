using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class VendorSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Vendor";
    public string DisplayName => "وندورها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Permission("Vendors");

    public VendorSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.Vendors
            .Where(x => x.IsActive)
            .Where(x => x.Name.Contains(query) || (x.Country != null && x.Country.Contains(query))
                || (x.ContactName != null && x.ContactName.Contains(query)))
            .Take(500)
            .ToListAsync();

        if (pool.Count < 5 && query.Length is >= 2 and <= 25)
        {
            var fallback = await _context.Vendors.Where(x => x.IsActive).Take(500).ToListAsync();
            foreach (var v in fallback)
                if (pool.All(x => x.VendorId != v.VendorId)) pool.Add(v);
        }

        var scored = pool
            .Select(v => new
            {
                Vendor = v,
                Score = SearchScoring.ComputeWeightedScore(query, (v.Name, 1.0), (v.Country, 0.5), (v.ContactName, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Vendor.Name)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Vendor.VendorId,
                Label = x.Vendor.Name,
                HighlightedLabel = SearchScoring.Highlight(x.Vendor.Name, query),
                SubLabel = string.Join(" — ", new[] { x.Vendor.Country, x.Vendor.RiskLevel }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/Vendor/Details/{x.Vendor.VendorId}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
