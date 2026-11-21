using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی تگ‌های مهندسی (Instrument/Control Loop Tag) - جدا از Equipment؛ دقیقاً منطبق با
/// مثال‌های اصلی درخواست (PI = Pressure Indicator، MCC = Motor Control Center و ...).
/// </summary>
public class EquipmentTagSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "EquipmentTag";
    public string DisplayName => "تگ‌های مهندسی";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public EquipmentTagSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var candidatesQuery = _context.Set<PEMS.Domain.Entities.EquipmentTag>()
            .Include(x => x.Equipment)
            .Where(x => x.IsActive)
            .Where(x =>
                x.TagNumber.Contains(query) ||
                x.TagType.Contains(query) ||
                (x.Area != null && x.Area.Contains(query)) ||
                (x.Service != null && x.Service.Contains(query)) ||
                (x.LoopNumber != null && x.LoopNumber.Contains(query)));

        if (projectId.HasValue)
            candidatesQuery = candidatesQuery.Where(x => x.Equipment != null && x.Equipment.ProjectId == projectId.Value);

        var pool = await candidatesQuery.Take(500).ToListAsync();

        if (pool.Count < 5 && query.Length is >= 2 and <= 25)
        {
            var fallback = await _context.Set<PEMS.Domain.Entities.EquipmentTag>()
                .Include(x => x.Equipment).Where(x => x.IsActive).Take(500).ToListAsync();
            foreach (var t in fallback)
                if (pool.All(x => x.TagId != t.TagId)) pool.Add(t);
        }

        var scored = pool
            .Select(t => new
            {
                Tag = t,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (t.TagNumber, 1.0), (t.TagType, 0.8), (t.Service, 0.6), (t.Area, 0.5), (t.LoopNumber, 0.7))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Tag.TagNumber)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Tag.TagId,
                Label = x.Tag.TagNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Tag.TagNumber, query),
                SubLabel = string.Join(" — ", new[] { x.Tag.TagType, x.Tag.Service, x.Tag.Equipment?.TagNumber }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = x.Tag.EquipmentId > 0 ? $"/Equipment/Details/{x.Tag.EquipmentId}" : null,
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
