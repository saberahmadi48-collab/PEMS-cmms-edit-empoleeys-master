using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ==============================================================================
/// هر موجودیتی که باید توی Global Search Framework قابل‌جستجو باشه، این اینترفیس رو
/// پیاده‌سازی می‌کنه و در DI ثبت می‌شه (AddScoped&lt;ISearchableEntityProvider, XProvider&gt;).
/// خودِ Registry (IGlobalSearchService) نیازی به دونستن جزئیات هیچ موجودیتی نداره - فقط از
/// روی EntityType، Provider مناسب رو از DI پیدا می‌کنه. یعنی طبق خواسته‌ی اصلی: "در آینده
/// اگر موجودیت جدیدی اضافه شد، فقط با ثبت آن در این سرویس، جستجوی هوشمند خودکار در دسترس".
/// ==============================================================================
/// </summary>
public interface ISearchableEntityProvider
{
    // شناسه‌ی یکتای نوع موجودیت - همونی که Frontend توی data-search-type="..." می‌فرسته
    string EntityType { get; }

    // نام قابل‌نمایش برای فهرست انواع موجودیت‌های قابل‌جستجو (مثلاً برای UI انتخاب دامنه‌ی جستجو)
    string DisplayName { get; }

    // SCRUM-64: the security contract - the rule of the page the results open. GlobalSearchService checks it before
    // SearchAsync runs; the provider itself only applies its row-level scope. No default: a new provider must decide.
    SearchAccessRule Access { get; }

    Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId);
}
