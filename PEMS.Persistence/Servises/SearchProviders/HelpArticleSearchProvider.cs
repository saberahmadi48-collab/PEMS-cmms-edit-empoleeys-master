using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// طبق «PEMS Bilingual Help & Knowledge System» بخش ۶.۴: راهنما در همان چارچوب پویای جستجوی
/// موجود پروژه (ISearchableEntityProvider) هم ثبت می‌شود - تا از ویجت Typeahead موجود
/// (pems-global-search.js) رایگان استفاده کند. نتیجه همیشه با زبان جاری کاربر منطبق است -
/// جستجوی متمرکز Help Center خودش (HelpService.SearchAsync) قوی‌تر است (شامل Content هم
/// می‌شود، نه فقط Title/Summary)؛ این Provider برای درج سریع/پیشنهاد لحظه‌ای در فرم‌هاست.
/// </summary>
public class HelpArticleSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "HelpArticle";
    public string DisplayName => "راهنما";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "published articles only");

    public HelpArticleSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var pool = await _context.HelpArticleTranslations
            .Include(t => t.Article)
            .Where(t => t.Article!.IsPublished)
            .Where(t => t.Title.Contains(query) || t.Summary.Contains(query)
                || (t.SearchKeywords != null && t.SearchKeywords.Contains(query)))
            .Take(500)
            .ToListAsync();

        var scored = pool
            .Select(t => new
            {
                Translation = t,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (t.Title, 1.0), (t.SearchKeywords, 0.7), (t.Summary, 0.4))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Translation.Title)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Translation.HelpArticleId,
                Label = x.Translation.Title,
                HighlightedLabel = SearchScoring.Highlight(x.Translation.Title, query),
                SubLabel = x.Translation.Summary,
                // SCRUM-548: HelpController.Article(string code) reads the code from the query string; "/Help/Article/{code}" was a 404.
                Url = $"/Help/Article?code={Uri.EscapeDataString(x.Translation.Article!.Code)}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
