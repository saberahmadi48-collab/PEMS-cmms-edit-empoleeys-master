using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PEMS.Persistence.Services;

/// <summary>
/// ==============================================================================
/// پیاده‌سازی سرویس مشترک Global Search. عمداً هیچ دانشی از هیچ موجودیت خاصی (Equipment،
/// VDR، Letter، ...) ندارد - فقط از روی EntityType، Provider مناسب را از میان تمام
/// ISearchableEntityProvider های ثبت‌شده در DI پیدا می‌کند و کار را به او واگذار می‌کند.
/// این دقیقاً یعنی "هیچ پیاده‌سازی تکراری" طبق درخواست اصلی.
/// ==============================================================================
/// </summary>
public class GlobalSearchService : IGlobalSearchService
{
    private readonly IEnumerable<ISearchableEntityProvider> _providers;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GlobalSearchService> _logger;

    public GlobalSearchService(IEnumerable<ISearchableEntityProvider> providers, IPermissionService permissions,
        ICurrentUserContext currentUser, ILogger<GlobalSearchService>? logger = null)
    {
        _providers = providers;
        _permissions = permissions;
        _currentUser = currentUser;
        _logger = logger ?? NullLogger<GlobalSearchService>.Instance;
    }

    public async Task<bool> IsAllowedAsync(SearchAccessRule rule)
    {
        if (!_currentUser.UserId.HasValue) return false;   // every rule needs a signed-in user
        return rule.Kind switch
        {
            SearchAccessKind.AnyUser => true,
            SearchAccessKind.Permission => await _permissions.HasPermissionAsync(_currentUser.Roles, rule.Module!, rule.Action!),
            SearchAccessKind.Roles => _currentUser.Roles.Any(r => rule.AnyOfRoles.Contains(r, StringComparer.OrdinalIgnoreCase)),
            _ => false,   // an unknown kind never grants access
        };
    }

    public async Task<IReadOnlyList<GlobalSearchGroupDto>> SearchEverywhereAsync(string query, int perType = 5, CancellationToken ct = default)
    {
        var groups = new List<GlobalSearchGroupDto>();
        var term = query?.Trim() ?? "";
        if (term.Length < 2) return groups;
        perType = Math.Clamp(perType, 1, 20);

        // One at a time: the providers share the request's DbContext.
        foreach (var provider in _providers.Where(p => p.Access.Navigable))
        {
            ct.ThrowIfCancellationRequested();
            if (!await IsAllowedAsync(provider.Access)) continue;

            try
            {
                var (items, total) = await provider.SearchAsync(term, 1, perType, null);
                var linked = items.Where(i => !string.IsNullOrWhiteSpace(i.Url) && IsLocalUrl(i.Url!)).ToList();
                if (linked.Count > 0)
                    groups.Add(new GlobalSearchGroupDto { EntityType = provider.EntityType, TotalCount = total, Items = linked });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken provider must not empty the whole palette; the failure is logged, not shown.
                _logger.LogWarning(ex, "Search provider {EntityType} failed", provider.EntityType);
            }
        }

        return groups.OrderByDescending(g => g.Items.Max(i => i.Score)).ToList();
    }

    // Results only open pages of this application (a notification link is stored data).
    private static bool IsLocalUrl(string url) =>
        url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");

    public async Task<GlobalSearchResponseDto> SearchAsync(
        string entityType, string query, int page = 1, int pageSize = 20, int? projectId = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var response = new GlobalSearchResponseDto
        {
            EntityType = entityType,
            Query = query ?? "",
            Page = page,
            PageSize = pageSize
        };

        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(query))
            return response;

        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.EntityType, entityType, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            // طبق اصل "بدون شکست خاموش": نوع موجودیت ناشناخته باید صریح مشخص باشد، نه فقط
            // یک نتیجه‌ی خالی که کاربر فکر کند جستجویش نتیجه‌ای نداشته
            throw new InvalidOperationException(
                $"نوع موجودیت «{entityType}» در Global Search Framework ثبت نشده است. Providerهای موجود: " +
                string.Join(", ", _providers.Select(p => p.EntityType)));
        }

        // Form lookups (pickers) keep their behaviour: the contract is enforced for the palette and the search page
        // (SearchEverywhereAsync); see the SCRUM-64 Jira comment for the open decision on lookups.
        var (items, totalCount) = await provider.SearchAsync(query.Trim(), page, pageSize, projectId);

        response.Items = items;
        response.TotalCount = totalCount;
        response.HasMore = (page * pageSize) < totalCount;

        return response;
    }

    public IReadOnlyList<SearchEntityTypeDto> GetRegisteredEntityTypes()
    {
        return _providers
            .Select(p => new SearchEntityTypeDto { EntityType = p.EntityType, DisplayName = p.DisplayName })
            .OrderBy(x => x.DisplayName)
            .ToList();
    }
}
