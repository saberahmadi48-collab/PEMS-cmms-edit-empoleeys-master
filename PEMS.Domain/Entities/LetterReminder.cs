using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// یادآور شخصی روی یک نامه («در تاریخ X این نامه را به من یادآوری کن»). برخلاف مهلت پاسخ‌خواهی نامه
/// (Letter.ResponseDueDate) که برای همه است، این یادآور فقط برای صاحبش است و در موعد، یک اعلان درون‌برنامه‌ای می‌سازد.
/// هر یادآور فقط یک بار اعلان می‌دهد (NotifiedDate) و پس از انجام (IsDone) دیگر اعلان نمی‌دهد.
/// </summary>
public class LetterReminder
{
    [Key]
    public int LetterReminderId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    public int OwnerUserId { get; set; }

    public DateTime RemindAt { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public bool IsDone { get; set; }
    public DateTime? NotifiedDate { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
