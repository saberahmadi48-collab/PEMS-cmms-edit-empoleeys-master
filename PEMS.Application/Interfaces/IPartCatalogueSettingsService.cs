namespace PEMS.Application.Interfaces;

/// <summary>تنظیمات سراسری کاتالوگ قطعه که از پنل مدیریت ویرایش می‌شود.</summary>
public sealed class PartCatalogueSettingsDto
{
    /// <summary>MANUAL | API.</summary>
    public string PartNoGenerationMethod { get; set; } = "MANUAL";
    public string? PartNoFormat { get; set; }

    /// <summary>برای کنترل هم‌زمانی در فرم؛ null وقتی هنوز ردیفی ذخیره نشده.</summary>
    public byte[]? RowVersion { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>false یعنی هنوز هیچ تنظیمی ذخیره نشده و مقادیر پیش‌فرض نمایش داده می‌شوند.</summary>
    public bool IsPersisted { get; set; }
}

public interface IPartCatalogueSettingsService
{
    /// <summary>تنظیمات فعلی؛ بدون ردیف، پیش‌فرض (MANUAL) برمی‌گردد. خواندن مجوز خاصی نمی‌خواهد (فاز ۲ از آن استفاده می‌کند).</summary>
    Task<PartCatalogueSettingsDto> GetAsync();

    /// <summary>ذخیره‌ی تنظیمات؛ فقط PartCatalogue.Admin. <paramref name="rowVersion"/> برای جلوگیری از رونویسی هم‌زمان.</summary>
    Task<PEMS.Application.DTOs.PcResult> SaveAsync(string? method, string? format, byte[]? rowVersion, PcActor actor);
}
