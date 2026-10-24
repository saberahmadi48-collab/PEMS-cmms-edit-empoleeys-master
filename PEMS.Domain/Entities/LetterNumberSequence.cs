using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// شمارنده‌ی ایمن شماره‌ی نامه به‌ازای (کد دپارتمان، سال). قبلاً شماره با «تعداد نامه‌های همان سال + ۱» ساخته می‌شد و دو ثبت هم‌زمان
/// شماره‌ی تکراری می‌گرفتند؛ اکنون هر شماره با افزایش این ردیف (با RowVersion و تلاش مجدد در تعارض) گرفته می‌شود.
/// </summary>
public class LetterNumberSequence
{
    [Key]
    public int LetterNumberSequenceId { get; set; }

    [Required, MaxLength(20)]
    public string Scope { get; set; } = "GEN";

    public int Year { get; set; }

    public int LastValue { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
