using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// سرویس مشترک Global Search - طبق درخواست: "یک سرویس مشترک ایجاد شود تا همه ماژول‌ها از
/// همان سرویس استفاده کنند و هیچ پیاده‌سازی تکراری وجود نداشته باشد."
/// </summary>
public interface IGlobalSearchService
{
    Task<GlobalSearchResponseDto> SearchAsync(
        string entityType, string query, int page = 1, int pageSize = 20, int? projectId = null);

    // فهرست تمام موجودیت‌هایی که تا این لحظه در سیستم ثبت شده‌اند (برای UI/مستندسازی/تست)
    IReadOnlyList<SearchEntityTypeDto> GetRegisteredEntityTypes();

    // SCRUM-64 (command palette, /GlobalSearch page): searches every navigable provider whose access rule the current
    // user passes, page 1 each, and keeps only results that open a page. Providers the user may not open are not run.
    Task<IReadOnlyList<GlobalSearchGroupDto>> SearchEverywhereAsync(string query, int perType = 5, CancellationToken ct = default);

    // SCRUM-64: true when the current user passes the rule (module permission, role or signed in).
    Task<bool> IsAllowedAsync(SearchAccessRule rule);
}
