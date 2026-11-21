using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی اعلان‌ها (Notification Center) - برخلاف بقیه‌ی Provider ها، این یکی همیشه فقط
/// روی اعلان‌های خودِ کاربر جاری جستجو می‌کند (اعلان یک موجودیت کاملاً شخصی است).
/// </summary>
public class NotificationSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public string EntityType => "Notification";
    public string DisplayName => "اعلان‌ها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "own notifications only");

    public NotificationSearchProvider(PEMSDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        if (!_currentUser.UserId.HasValue)
            return (new List<GlobalSearchResultItemDto>(), 0);

        var pool = await _context.Notifications
            .Where(x => x.UserId == _currentUser.UserId.Value)
            .Where(x => x.Title.Contains(query) || x.Message.Contains(query))
            .OrderByDescending(x => x.CreatedDate)
            .Take(300)
            .ToListAsync();

        var scored = pool
            .Select(n => new
            {
                Notification = n,
                Score = SearchScoring.ComputeWeightedScore(query, (n.Title, 1.0), (n.Message, 0.6))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Notification.CreatedDate)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Notification.NotificationId,
                Label = x.Notification.Title,
                HighlightedLabel = SearchScoring.Highlight(x.Notification.Title, query),
                SubLabel = x.Notification.Message.Length > 100 ? x.Notification.Message[..100] + "..." : x.Notification.Message,
                Url = x.Notification.LinkUrl,
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
