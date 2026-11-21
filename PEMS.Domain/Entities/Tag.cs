using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// برچسب دلخواه و قابل‌ساخت توسط کاربر (طبق نمونه‌ی کارتابل مرجع Pargar/Fonix) - برخلاف
/// میانبرهای دیسیپلین که ثابت و از پیش تعریف‌شده بودند، این‌ها را خود کاربران می‌سازند.
/// </summary>
public class Tag : BaseEntity
{
    [Key]
    public int TagId { get; set; }

    public string Name { get; set; } = string.Empty;

    // کد رنگ Bootstrap badge: primary, secondary, success, danger, warning, info, dark
    public string Color { get; set; } = "secondary";

    public int? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// برچسب والد (درخت برچسب مهندسی: مثلاً «مهندسی ← برق ← کابل‌کشی»). null = برچسب ریشه. عمق مجاز و نبودن دور
    /// در TagTreeRules سمت سرور بررسی می‌شود؛ برچسب هرگز حذف فیزیکی نمی‌شود (غیرفعال‌سازی نرم).
    /// </summary>
    public int? ParentTagId { get; set; }
    public Tag? ParentTag { get; set; }

    /// <summary>
    /// null = برچسب سازمانی (برای همه‌ی کاربران دیده می‌شود؛ رفتار قبلی همه‌ی برچسب‌های موجود). مقدار پر = برچسب شخصی
    /// همان کاربر (ApplicationUser.Id) که هیچ کاربر دیگری آن را نمی‌بیند و نمی‌تواند روی نامه‌هایش بزند.
    /// </summary>
    public int? OwnerUserId { get; set; }

    /// <summary>ترتیب نمایش در درخت (کوچک‌تر بالاتر)؛ پیش‌فرض صفر.</summary>
    public int SortOrder { get; set; }
}
