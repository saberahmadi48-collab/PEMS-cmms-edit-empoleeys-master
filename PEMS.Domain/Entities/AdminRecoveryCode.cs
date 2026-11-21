using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// کد بازیابی یکبارمصرف حساب مدیر (SCRUM-355 / ۳۵۳.۲). هر مجموعه ۱۰ کد است که فقط یک بار (هنگام تولید) نمایش داده می‌شود؛
/// در این جدول فقط هش PBKDF2 و نمک هر کد ذخیره می‌شود - هرگز خود کد.
/// فقط‌افزودنی: ردیف حذف نمی‌شود؛ تنها <see cref="UsedAtUtc"/> (مصرف) و <see cref="RevokedAtUtc"/> (باطل شدن با تولید مجموعه‌ی جدید)
/// یک بار از null به مقدار تغییر می‌کنند. هر دو concurrency token هستند تا یک کد در دو درخواست هم‌زمان دو بار مصرف نشود.
/// </summary>
public class AdminRecoveryCode
{
    [Key]
    public int AdminRecoveryCodeId { get; set; }

    /// <summary>شناسه‌ی کاربر Identity (AspNetUsers.Id) مالک کد.</summary>
    public int UserId { get; set; }

    /// <summary>همه‌ی کدهای یک بار تولید یک BatchId دارند.</summary>
    public Guid BatchId { get; set; }

    public byte[] CodeHash { get; set; } = Array.Empty<byte>();

    public byte[] Salt { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>زمان مصرف کد در بازیابی؛ null = مصرف نشده.</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>زمان باطل شدن به‌خاطر تولید مجموعه‌ی جدید؛ null = هنوز معتبر.</summary>
    public DateTime? RevokedAtUtc { get; set; }
}
