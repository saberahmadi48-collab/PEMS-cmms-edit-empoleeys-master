using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// پیوند یک نامه به چند مدرک/مورد مرتبط (مدرک مهندسی، آیتم VDR، MR، ترنسمیتال). فیلدهای تکی RelatedDocumentId / RelatedMRId روی Letter
/// برای سازگاری با گذشته می‌مانند و اولین پیوند مدرک/MR همچنان در آن‌ها هم نوشته می‌شود؛ پیوندهای بیشتر فقط اینجا نگه‌داری می‌شوند.
/// شماره‌ی هدف به‌صورت «عکس لحظه‌ای» ذخیره می‌شود تا با تغییر بعدی شماره‌ی مدرک، نمایش نامه‌ی قدیمی عوض نشود.
/// </summary>
public class LetterLink
{
    [Key]
    public int LetterLinkId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    // EngineeringDocument, VdrItem, MaterialRequisition, Transmittal
    [Required, MaxLength(30)]
    public string LinkType { get; set; } = string.Empty;

    public int TargetId { get; set; }

    [MaxLength(100)]
    public string? TargetNumber { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
