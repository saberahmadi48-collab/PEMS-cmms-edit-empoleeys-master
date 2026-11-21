using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

public static class PartNoGenerationMethods
{
    /// <summary>Part Cataloguer شماره‌ی قطعه‌ی ثبت‌شده در IFS را در گام «ثبت» دستی وارد می‌کند (رفتار فعلی و پیش‌فرض).</summary>
    public const string Manual = "MANUAL";

    /// <summary>سیستم شماره‌ی قطعه را خودکار تولید می‌کند. فقط پرچم پیکربندی است؛ رفتار آن در فاز ۲ پیاده‌سازی می‌شود.</summary>
    public const string Api = "API";

    public static readonly string[] All = { Manual, Api };
}

/// <summary>
/// تنظیمات سراسری ماژول کاتالوگ قطعه (یک ردیف برای کل سیستم). SingletonKey همیشه ۱ است و یک ایندکس یکتا روی آن تضمین می‌کند
/// هیچ‌وقت دو ردیف تنظیمات وجود نداشته باشد. اگر ردیفی ثبت نشده باشد، مقدار پیش‌فرض (MANUAL) اعمال می‌شود.
/// </summary>
public class PartCatalogueSettings : BaseEntity
{
    public int PartCatalogueSettingsId { get; set; }

    /// <summary>همیشه ۱ - فقط برای یکتایی ردیف تنظیمات.</summary>
    public int SingletonKey { get; set; } = 1;

    /// <summary>MANUAL | API (PartNoGenerationMethods).</summary>
    public string PartNoGenerationMethod { get; set; } = PartNoGenerationMethods.Manual;

    /// <summary>
    /// قالب شماره‌ی قطعه (مثلاً PCR-YYYY-NNNN: YYYY = سال، NNNN = شماره‌ی ترتیبی). فقط وقتی روش API است الزامی است؛
    /// در MANUAL اختیاری و فعلاً مصرف‌کننده‌ای ندارد.
    /// </summary>
    public string? PartNoFormat { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ModifiedDate { get; set; }
}
