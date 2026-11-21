using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// سایدبار داده‌محور: یک بخش (Section)، زیرتیتر (Subheading) یا پیوند (Link) منو. مدیر سیستم ترتیب، والد، عنوان (فارسی/انگلیسی)،
/// آیکون، نمایان/پنهان و شمارنده را بدون تغییر کد عوض می‌کند. قاعده‌ی نمایش (VisibilityRule) و مسیر آیتم‌های سیستمی متعلق به
/// کد است (همان دروازه‌ای که کنترلر مقصد واقعاً اعمال می‌کند) و در هر Startup از کد همگام می‌شود؛ تغییرات مدیر هرگز بازنویسی
/// نمی‌شوند. پنهان‌بودن در منو هیچ مجوزی را نمی‌دهد یا نمی‌گیرد - کنترلر مقصد همچنان مرجع نهایی است (Hidden ≠ Unauthorized).
/// آیتم سیستمی حذف نمی‌شود، فقط IsActive=false می‌شود.
/// </summary>
public class NavigationMenuItem : BaseEntity
{
    public int NavigationMenuItemId { get; set; }

    /// <summary>کلید پایدار (مثلاً «cmms.workorder»)؛ یکتا. منبع ارجاع والد/فرزند و آزمون‌ها.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Key بخش/زیرتیتر والد؛ null = سطح بالا.</summary>
    public string? ParentKey { get; set; }

    /// <summary>Section | Subheading | Link</summary>
    public string Kind { get; set; } = NavigationMenuItemKinds.Link;

    public int SortOrder { get; set; }

    /// <summary>کلید منبع بومی‌سازی پیش‌فرض (SharedResource)؛ عنوان پیش‌فرض آیتم‌های سیستمی.</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>عنوان جایگزین مدیر؛ خالی = همان TitleKey.</summary>
    public string? TitleEn { get; set; }
    public string? TitleFa { get; set; }

    /// <summary>کلاس آیکون Bootstrap Icons (مثلاً «bi-gear»).</summary>
    public string? Icon { get; set; }

    public string? Area { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }

    /// <summary>فقط آیتم‌های سیستمی (مثلاً /hangfire)؛ مدیر نمی‌تواند URL آزاد ثبت کند.</summary>
    public string? ExternalPath { get; set; }

    public bool OpenInNewTab { get; set; }

    /// <summary>
    /// قاعده‌ی نمایش: «|» = یا، «&amp;» = و. اتم‌ها: Always، Module:کلید، CmmsItem:کلید، Perm:منبع.اقدام، RoleAction:ماژول.اقدام،
    /// Flag:نام، Role:نام‌نقش. آیتم سیستمی از کد می‌آید؛ آیتم سفارشی فقط Always یا یک Perm موجود.
    /// </summary>
    public string VisibilityRule { get; set; } = NavigationVisibilityRules.Always;

    /// <summary>وقتی چند پیوند هم‌کنترلر داریم و هیچ‌کدام دقیقاً Action جاری نیست، این یکی فعال (Active) نشان داده می‌شود.</summary>
    public bool IsDefaultForController { get; set; }

    /// <summary>فقط برای Section: کنترلرهای دیگری (بدون پیوند منو) که بخش را باز نگه می‌دارند (CSV).</summary>
    public string? ExtraActiveControllers { get; set; }

    /// <summary>فقط برای Section: هر صفحه‌ای در این Area بخش را باز نگه می‌دارد.</summary>
    public string? ActiveArea { get; set; }

    /// <summary>کلید شمارنده از فهرست بسته‌ی کد (مثلاً «mywork.pending»)؛ مدیر فقط از همان فهرست انتخاب می‌کند.</summary>
    public string? BadgeKey { get; set; }

    /// <summary>false = پنهان توسط مدیر (حذف نرم).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>true = از کاتالوگ کد Seed شده؛ قابل حذف نیست و مسیر/قاعده‌اش از کد است.</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ModifiedDate { get; set; }
}

public static class NavigationMenuItemKinds
{
    public const string Section = "Section";
    public const string Subheading = "Subheading";
    public const string Link = "Link";
    public static readonly string[] All = { Section, Subheading, Link };
}

public static class NavigationVisibilityRules
{
    public const string Always = "Always";
}
