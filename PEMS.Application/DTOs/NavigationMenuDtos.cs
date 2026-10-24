namespace PEMS.Application.DTOs;

/// <summary>
/// تعریف پیش‌فرض یک آیتم سیستمی سایدبار (کاتالوگ کد). فیلدهای «مالکیت کد» (مسیر، قاعده‌ی نمایش، فعال‌سازی) در هر همگام‌سازی به‌روز
/// می‌شوند؛ فیلدهای «مالکیت مدیر» (ترتیب، والد، آیکون، عنوان جایگزین، نمایان/پنهان، شمارنده) فقط هنگام درج اولیه نوشته می‌شوند.
/// </summary>
public sealed record NavigationMenuItemDefinition(
    string Key,
    string? ParentKey,
    string Kind,
    int SortOrder,
    string TitleKey,
    string? Icon,
    string VisibilityRule,
    string? Area = null,
    string? Controller = null,
    string? Action = null,
    string? ExternalPath = null,
    bool OpenInNewTab = false,
    bool IsDefaultForController = false,
    string? ExtraActiveControllers = null,
    string? ActiveArea = null,
    string? BadgeKey = null);

/// <summary>یک آیتم ذخیره‌شده (برای ساخت سایدبار و صفحه‌ی مدیریت).</summary>
public sealed class NavigationMenuItemDto
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? ParentKey { get; set; }
    public string Kind { get; set; } = "";
    public int SortOrder { get; set; }
    public string TitleKey { get; set; } = "";
    public string? TitleEn { get; set; }
    public string? TitleFa { get; set; }
    public string? Icon { get; set; }
    public string? Area { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public string? ExternalPath { get; set; }
    public bool OpenInNewTab { get; set; }
    public string VisibilityRule { get; set; } = "";
    public bool IsDefaultForController { get; set; }
    public string? ExtraActiveControllers { get; set; }
    public string? ActiveArea { get; set; }
    public string? BadgeKey { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public string? RowVersion { get; set; }
}

/// <summary>ویرایش مدیر: فقط فیلدهای «مالکیت مدیر». مسیر و قاعده‌ی آیتم سیستمی از این‌جا تغییر نمی‌کند.</summary>
public sealed class NavigationMenuItemEditInput
{
    public string Key { get; set; } = "";
    public string? ParentKey { get; set; }
    public string? TitleEn { get; set; }
    public string? TitleFa { get; set; }
    public string? Icon { get; set; }
    public string? BadgeKey { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

/// <summary>پیوند سفارشی مدیر: فقط به مسیر داخلی موجود و با یک مجوز موجود یا بدون مجوز (Always).</summary>
public sealed class NavigationMenuCustomLinkInput
{
    public string ParentKey { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string TitleFa { get; set; } = "";
    public string? Icon { get; set; }
    public string? Area { get; set; }
    public string Controller { get; set; } = "";
    public string Action { get; set; } = "Index";
    /// <summary>«Resource.Action» از فهرست مجوزهای فعال، یا خالی = بدون شرط مجوز.</summary>
    public string? RequiredPermission { get; set; }
}

/// <summary>نتیجه‌ی عملیات مدیریت منو؛ Error کلید بومی‌سازی است.</summary>
public sealed record NavigationMenuResult(bool Success, string? Error = null)
{
    public static NavigationMenuResult Ok() => new(true);
    public static NavigationMenuResult Fail(string errorKey) => new(false, errorKey);
}
