using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// قالب آماده‌ی نامه (موضوع/متن/اولویت/محرمانگی پیش‌فرض) که هنگام ایجاد نامه‌ی جدید پیش‌پر می‌شود. قالب فقط مقدار اولیه‌ی فرم
/// است: بعد از ثبت، نامه کاملاً مستقل از قالب است و تغییر یا غیرفعال‌شدن قالب هیچ نامه‌ی ثبت‌شده‌ای را تغییر نمی‌دهد
/// (نامه‌ی رسمی تغییرناپذیر است). حذف فیزیکی ندارد؛ فقط غیرفعال می‌شود.
/// </summary>
public class LetterTemplate : BaseEntity
{
    [Key]
    public int LetterTemplateId { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    // Incoming | Outgoing | Internal؛ null یعنی برای همه‌ی انواع
    [MaxLength(20)]
    public string? LetterType { get; set; }

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    // HTML پاک‌سازی‌شده (IRichTextSanitizerService)
    public string? Body { get; set; }

    [Required, MaxLength(20)]
    public string Priority { get; set; } = "Normal";

    [Required, MaxLength(30)]
    public string ConfidentialityLevel { get; set; } = "Internal";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
