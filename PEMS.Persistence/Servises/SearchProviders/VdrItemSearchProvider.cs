using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی مدارک وندور (VDR) - طبق مثال درخواست اصلی: نام Vendor، شماره مدرک، Status،
/// Discipline، یا بخشی از عنوان.
/// </summary>
public class VdrItemSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "VDRItem";
    public string DisplayName => "مدارک وندور (VDR)";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser();

    public VdrItemSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var matches = await TryFullTextSearchAsync(query, projectId);

        if (matches == null)
        {
            var candidatesQuery = _context.VDRItems
                .Include(x => x.Vendor)
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    x.DocumentNumber.Contains(query) ||
                    (x.DocumentDescription != null && x.DocumentDescription.Contains(query)) ||
                    (x.Vendor != null && x.Vendor.Name.Contains(query)) ||
                    x.WorkflowStatus.Contains(query) ||
                    (x.DisciplineCodeRaw != null && x.DisciplineCodeRaw.Contains(query)));

            if (projectId.HasValue)
                candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

            matches = await candidatesQuery.Take(500).ToListAsync();

            if (matches.Count < 5 && query.Length is >= 2 and <= 25)
            {
                var fallbackQuery = _context.VDRItems.Include(x => x.Vendor).Include(x => x.Discipline).Where(x => x.IsActive);
                if (projectId.HasValue) fallbackQuery = fallbackQuery.Where(x => x.ProjectId == projectId.Value);

                var fallbackPool = await fallbackQuery.OrderByDescending(x => x.CreatedDate).Take(500).ToListAsync();
                foreach (var item in fallbackPool)
                {
                    if (matches.All(x => x.DocumentId != item.DocumentId))
                        matches.Add(item);
                }
            }
        }

        var scored = matches
            .Select(v => new
            {
                Item = v,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (v.DocumentNumber, 1.0),
                    (v.Vendor?.Name, 0.8),
                    (v.DocumentDescription, 0.6),
                    (v.WorkflowStatus, 0.5),
                    (v.DisciplineCodeRaw, 0.5))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.DocumentNumber)
            .ToList();

        var totalCount = scored.Count;

        var pageItems = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Item.DocumentId,
                Label = x.Item.DocumentNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Item.DocumentNumber, query),
                SubLabel = string.Join(" — ", new[]
                {
                    x.Item.DocumentDescription,
                    x.Item.Vendor?.Name,
                    x.Item.WorkflowStatus
                }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/VDR/Details/{x.Item.DocumentId}",
                Score = x.Score
            })
            .ToList();

        return (pageItems, totalCount);
    }


    // ==============================================================================
    // محدودیت شناخته‌شده‌ی فاز C: این مسیر Full-Text فقط DocumentNumber/Description/Vendor
    // رو پوشش می‌ده (چون این‌ها توی Migration با نام FullTextSearch Index شدن) - جستجو با
    // Status/Discipline (که رشته‌های کوتاهن و LIKE براشون کافیه) توی این مسیر نیست. یعنی
    // وقتی FTS فعاله، جستجوی «UnderReview» یا «Mechanical» ممکنه نتیجه‌ای نده مگه این‌که
    // توی Description هم اومده باشه. رفعش (ترکیب FTS + LIKE در یک Query) به فاز بعدی موکول شده.
    // ==============================================================================
    private async Task<List<PEMS.Domain.Entities.VDRItem>?> TryFullTextSearchAsync(string query, int? projectId)
    {
        try
        {
            var ftsTerm = EquipmentSearchProvider.BuildPrefixTerm(query);
            if (ftsTerm == null) return null;

            // نکته: CONTAINS روی Vendor.Name نیاز داره که Vendors هم Full-Text Index داشته
            // باشه (طبق Migration با نام FullTextSearch) - وگرنه کل Query شکست می‌خوره و
            // به‌درستی به مسیر LIKE برمی‌گرده (نه فقط اون بخش، کل نتیجه - چون یک Query واحده)
            var ftsQuery = _context.VDRItems
                .Include(x => x.Vendor)
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    EF.Functions.Contains(x.DocumentNumber, ftsTerm) ||
                    (x.DocumentDescription != null && EF.Functions.Contains(x.DocumentDescription, ftsTerm)) ||
                    (x.Vendor != null && EF.Functions.Contains(x.Vendor.Name, ftsTerm)));

            if (projectId.HasValue)
                ftsQuery = ftsQuery.Where(x => x.ProjectId == projectId.Value);

            return await ftsQuery.Take(500).ToListAsync();
        }
        catch
        {
            return null;
        }
    }
}
