using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// ارجاع یک نامه به یک کاربر یا دپارتمان، با دستورالعمل/توضیح، مهلت پیگیری، و ردیابی
/// خوانده/نخوانده. هر ارجاع جدید یک رکورد مستقل است، پس "تاریخچه‌ی ارجاع" خودکار از همین
/// جدول به‌دست می‌آید (نیازی به جدول History جدا نیست).
/// </summary>
public class LetterReferral : BaseEntity
{
    [Key]
    public int LetterReferralId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    public int? ReferredToEmployeeId { get; set; }
    public Employee? ReferredToEmployee { get; set; }

    public int? ReferredToDepartmentId { get; set; }
    public Department? ReferredToDepartment { get; set; }

    public string? Instructions { get; set; }

    public DateTime? DueDate { get; set; }

    // Pending, InProgress, Completed
    // (Overdue محاسبه‌شده است: Pending/InProgress + DueDate گذشته)
    public string Status { get; set; } = "Pending";

    public bool IsRead { get; set; }

    public DateTime? ReadDate { get; set; }

    public string? ReferredByName { get; set; }

    public DateTime ReferredDate { get; set; } = DateTime.Now;

    public string? ResponseNote { get; set; }

    public DateTime? CompletedDate { get; set; }

    /// <summary>نامه‌ی ارجاعی (Letter.IsReferral) که این ارجاع با آن صادر شد؛ null برای ارجاع‌های قدیمیِ فرم ساده.</summary>
    public int? ReferralLetterId { get; set; }
}
