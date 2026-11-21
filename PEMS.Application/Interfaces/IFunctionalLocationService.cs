using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IFunctionalLocationService
{
    Task<List<FunctionalLocationListDto>> GetListAsync();

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): این یک ساختار درختی
    // (Parent/Child) است که با Breadcrumb مرتب می‌شود - این مرتب‌سازی ذاتاً باید روی کل
    // مجموعه در حافظه انجام شود (چون به مسیر کامل هر گره در درخت نیاز دارد، نه یک ستون DB).
    // بنابراین صفحه‌بندی این‌جا روی نتیجه‌ی نهایی/مرتب‌شده انجام می‌شود، نه قبل از آن - همون
    // اصلی که در Permit.pendingMyApproval رعایت شد.
    Task<PagedResult<FunctionalLocationListDto>> GetPagedListAsync(int page, int pageSize);

    Task<List<FunctionalLocationPickerItemDto>> GetPickerListAsync();

    /// <summary>
    /// مسیر کامل (Plant / Area / Unit / .../ Code) برای نمایش در صفحات Equipment/Asset -
    /// null اگر functionalLocationId خالی یا نامعتبر باشد.
    /// </summary>
    Task<string?> GetBreadcrumbAsync(int? functionalLocationId);

    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        string code, string name, int? parentId, string? plant, string? area, string? unit,
        string? description, string? currentUserName);

    Task<(bool Success, string? Error)> UpdateAsync(
        int id, string code, string name, int? parentId, string? plant, string? area, string? unit,
        string? description, string? currentUserName);

    /// <summary>
    /// طبق «PEMS CMMS — Bulk Define Maintenance Strategy»: رفع تفاوت Exact/Descendants برای
    /// یک یا چند Functional Location - با پیمایش درخت Parent/Child واقعی (نه فرض بر مبنای
    /// پیشوند رشته‌ای Code). وقتی includeSelf=false، فقط فرزندان واقعی برمی‌گردند (نه خودِ
    /// شناسه‌های ورودی) - برای مواردی که فراخوان‌کننده خودش شناسه‌های Exact را جدا مدیریت می‌کند.
    /// </summary>
    /// <summary>
    /// SCRUM-513: one level of the tree picker. With <paramref name="query"/> empty: the active children of <paramref name="parentId"/> (null = the roots), sorted by code;
    /// with a query (two characters or more): active locations whose code or name contains it, over the whole tree, as a flat list. Paged, so a tree of any size is read 20 nodes at a time.
    /// </summary>
    Task<TreeNodePage> GetTreeNodesAsync(int? parentId, string? query, int page, int pageSize);

    Task<List<int>> GetDescendantIdsAsync(IEnumerable<int> functionalLocationIds, bool includeSelf);
}
