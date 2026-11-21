using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// طرف مکاتبه‌ی بیرونی (کارفرما، مشاور، پیمانکار، وندور). قبلاً فرستنده‌ی نامه‌ی وارده یک متن آزاد بود و نام یک شرکت به چند شکل ثبت می‌شد؛
/// این رجیستری باعث می‌شود همه‌ی نامه‌های وارده/صادره‌ی یک طرف در یک «رشته‌ی مکاتبه» دیده شوند. طرف‌های وندور از جدول Vendor
/// (یک‌بار، بدون تکرار) ساخته می‌شوند؛ طرفی که ثبت‌کننده در لحظه می‌سازد <see cref="IsProvisional"/> است تا سرپرست تایید کند.
/// </summary>
public class CorrespondentParty : BaseEntity
{
    [Key]
    public int CorrespondentPartyId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // کد کوتاه (مثلاً JPC) برای نمایش؛ اختیاری
    [MaxLength(30)]
    public string? Code { get; set; }

    // Client, Consultant, Contractor, Vendor
    [Required, MaxLength(20)]
    public string PartyType { get; set; } = "Vendor";

    // اگر از جدول وندورها آمده باشد؛ برای جلوگیری از ساخت دوباره‌ی همان وندور
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    // طرفی که حین ثبت نامه ساخته شده و هنوز توسط سرپرست تایید نشده است
    public bool IsProvisional { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
