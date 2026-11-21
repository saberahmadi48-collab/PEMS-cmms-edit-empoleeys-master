using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// سایدبار داده‌محور. خواندن (GetMenuAsync) کش می‌شود و با هر تغییر مدیر باطل می‌شود. این سرویس هیچ تصمیم مجوزی نمی‌گیرد؛
/// ارزیابی قاعده‌ی نمایش در لایه‌ی Web و با IPermissionService انجام می‌شود. کنترل «فقط SystemAdmin» در کنترلر مدیریت است.
/// </summary>
public interface INavigationMenuService
{
    /// <summary>همه‌ی آیتم‌های فعال، مرتب بر اساس SortOrder (کش‌شده).</summary>
    Task<IReadOnlyList<NavigationMenuItemDto>> GetMenuAsync(CancellationToken ct = default);

    /// <summary>همه‌ی آیتم‌ها شامل پنهان‌ها، برای صفحه‌ی مدیریت (بدون کش).</summary>
    Task<IReadOnlyList<NavigationMenuItemDto>> GetAllForAdminAsync(CancellationToken ct = default);

    /// <summary>همگام‌سازی افزایشی کاتالوگ کد؛ تعداد آیتم‌های درج/به‌روزشده را برمی‌گرداند.</summary>
    Task<int> SyncDefaultsAsync(IReadOnlyList<NavigationMenuItemDefinition> definitions, CancellationToken ct = default, IReadOnlyCollection<string>? retiredKeys = null);

    Task<NavigationMenuResult> UpdateAsync(NavigationMenuItemEditInput input, CancellationToken ct = default);

    /// <summary>جابه‌جایی یک پله بالا/پایین بین هم‌والدها.</summary>
    Task<NavigationMenuResult> MoveAsync(string key, bool up, CancellationToken ct = default);

    /// <param name="permissionExists">اعتبارسنجی «Resource.Action» در کاتالوگ مجوزها.</param>
    /// <param name="routeExists">اعتبارسنجی وجود Area/Controller/Action در برنامه (لایه‌ی Web).</param>
    Task<NavigationMenuResult> AddCustomLinkAsync(NavigationMenuCustomLinkInput input,
        Func<string, Task<bool>> permissionExists, Func<string?, string, string, bool> routeExists, CancellationToken ct = default);

    /// <summary>فقط آیتم سفارشی حذف می‌شود؛ آیتم سیستمی فقط پنهان‌شدنی است.</summary>
    Task<NavigationMenuResult> DeleteCustomAsync(string key, CancellationToken ct = default);

    /// <summary>نام مجوزهای فعال «Resource.View» برای انتخاب شرط پیوند سفارشی (مجوز تازه ساخته نمی‌شود).</summary>
    Task<IReadOnlyList<string>> GetViewPermissionNamesAsync(CancellationToken ct = default);
}

/// <summary>شمارش‌های دامنه برای شمارنده‌های سایدبار (کوئری‌ها در Persistence، نه کنترلر).</summary>
public interface INavigationCountService
{
    /// <summary>Work Orderهای فعالی که بسته/لغو/رد نشده‌اند.</summary>
    Task<int> CountOpenWorkOrdersAsync(CancellationToken ct = default);

    /// <summary>درخواست‌های انبار در وضعیت Submitted (منتظر تأیید).</summary>
    Task<int> CountStoreRequisitionsAwaitingApprovalAsync(CancellationToken ct = default);
}
