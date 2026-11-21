using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// یک مرحله از زنجیره‌ی تایید ترتیبی نامه. مراحل به‌ترتیب StepOrder (۱، ۲، ...) تصمیم گرفته می‌شوند؛ فقط تاییدکننده‌ی «مرحله‌ی
/// جاری» (اولین Pending) اجازه‌ی تصمیم دارد. نویسنده‌ی نامه نمی‌تواند در زنجیره‌ی نامه‌ی خودش باشد و یک نفر دو بار نمی‌آید
/// (تفکیک وظایف / جلوگیری از تایید دوری). پس از «بازگشت برای اصلاح» و ارسال مجدد، همه‌ی مراحل از نو Pending می‌شوند.
/// </summary>
public class LetterApprovalStep
{
    [Key]
    public int LetterApprovalStepId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    public int StepOrder { get; set; }

    public int ApproverUserId { get; set; }

    // Pending | Approved | Rejected | Returned
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime? DecidedDate { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
