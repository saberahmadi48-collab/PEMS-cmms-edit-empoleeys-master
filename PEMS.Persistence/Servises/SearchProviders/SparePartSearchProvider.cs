using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

public class SparePartSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "SparePart";
    public string DisplayName => "قطعات یدکی";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public SparePartSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.SpareParts
            .Where(x => x.IsActive)
            .Where(x => x.PartNumber.Contains(query) || x.PartName.Contains(query)
                || (x.Manufacturer != null && x.Manufacturer.Contains(query)))
            .Take(300)
            .ToListAsync();

        var scored = pool
            .Select(p => new
            {
                Part = p,
                Score = SearchScoring.ComputeWeightedScore(query, (p.PartNumber, 1.0), (p.PartName, 0.8), (p.Manufacturer, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Part.PartNumber)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Part.SparePartId,
                Label = x.Part.PartNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Part.PartNumber, query),
                SubLabel = string.Join(" — ", new[] { x.Part.PartName, x.Part.StorageLocation, x.Part.IsLowStock ? "کمبود موجودی" : null }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = "/SparePart/Index",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
