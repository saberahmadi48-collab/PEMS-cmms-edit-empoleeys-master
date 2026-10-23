using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// تاریخچه‌ی نسخه‌های موضوع/متن نامه (Append-only): هر ذخیره‌ی صریح، ارسال برای تایید، ارسال مجدد و ارسال نهایی یک ردیف می‌سازد؛
/// ذخیره‌ی خودکار پشت‌سرهم از همان نویسنده در بازه‌ی کوتاه همان ردیف را به‌روز می‌کند (نه یک ردیف به‌ازای هر ۳۰ ثانیه).
/// </summary>
public class LetterVersion
{
    [Key]
    public int LetterVersionId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    public int VersionNo { get; set; }

    [Required, MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    public string? Body { get; set; }

    // Draft | AutoSave | Submitted | Resubmitted | Sent
    [Required, MaxLength(20)]
    public string Reason { get; set; } = "Draft";

    public int? SavedByUserId { get; set; }

    public DateTime SavedDate { get; set; } = DateTime.Now;
}
