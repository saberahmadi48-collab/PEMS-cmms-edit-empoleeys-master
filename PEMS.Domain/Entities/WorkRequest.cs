using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-05 و بند ۸: قبل از این Entity، هیچ دروازه‌ی رسمی‌ای
/// جلوی ساخت مستقیم WorkOrder اجرایی نبود. گردش‌کار: Draft → Submitted → UnderReview →
/// Approved → {Rejected | Planned → Converted}. تبدیل واقعی به WorkOrder فقط از وضعیت Planned
/// و فقط یک‌بار مجاز است (LinkedWorkOrderId بعد از تبدیل پر می‌شود).
/// </summary>
public class WorkRequest : BaseEntity
{
    public int WorkRequestId { get; set; }

    public string RequestNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int RequestedById { get; set; }
    public Employee? RequestedBy { get; set; }

    public DateTime RequestedDate { get; set; } = DateTime.Now;

    // هرکدام اختیاری - یک درخواست می‌تواند فقط به تجهیز، فقط به Asset، یا فقط به یک
    // Functional Location اشاره کند (مثلاً نشتی در یک منطقه، نه یک تجهیز مشخص)
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    public int? FunctionalLocationId { get; set; }
    public FunctionalLocation? FunctionalLocation { get; set; }

    // شرح وضعیت/خرابی گزارش‌شده - متن آزاد گزارش‌دهنده، جدا از Description رسمی
    public string? ReportedCondition { get; set; }

    // Low, Medium, High, Critical
    public string Priority { get; set; } = "Medium";

    public int? SuggestedDisciplineId { get; set; }
    public Discipline? SuggestedDiscipline { get; set; }

    // Draft, Submitted, UnderReview, Approved, Rejected, Planned, Converted, Cancelled
    public string Status { get; set; } = "Draft";

    public int? ReviewedById { get; set; }
    public Employee? ReviewedBy { get; set; }

    public DateTime? ReviewedDate { get; set; }

    public string? RejectionReason { get; set; }

    public int? ApprovedById { get; set; }
    public Employee? ApprovedBy { get; set; }

    public DateTime? ApprovedDate { get; set; }

    // فقط بعد از تبدیل موفق به WorkOrder پر می‌شود - جلوگیری از تبدیل دوباره
    public int? LinkedWorkOrderId { get; set; }
    public WorkOrder? LinkedWorkOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // طبق ممیزی «PEMS CM Audit» بند ۵: هیچ فیلد Source/Origin ای روی WorkRequest وجود نداشت.
    // نزدیک‌ترین قرارداد موجود همین دو-فیلدی SourceType/SourceIdentifier است که
    // CriticalityAssessmentEvidence از قبل استفاده می‌کند - همان الگو، بدون اختراع تازه.
    // Manual, Finding, MaintenancePlan, Inspection
    public string SourceType { get; set; } = "Manual";

    // مثلاً "Finding#123" - فقط وقتی SourceType != Manual پر می‌شود
    public string? SourceIdentifier { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    // ==============================================================================
    // A4: ارزیابی ریسک/HSE (ماتریس ۵×۵ طبق رویه‌ی استاندارد ISO 31000 / API RP 580): احتمال × شدت → امتیاز → سطح ریسک → اولویت مشتق‌شده + SLA.
    // اولویت نهایی هرگز پایین‌تر از اولویت اعلامی کاربر یا ریسک مشتق‌شده نیست؛ HSE-related حداقل High و Emergency همیشه Critical است.
    // ==============================================================================
    public bool IsHseRelated { get; set; }

    /// <summary>مسیر اضطراری: اولویت Critical، SLA کوتاه و اعلان فوری به HSE/مدیریت نگهداری.</summary>
    public bool IsEmergency { get; set; }

    /// <summary>۱ تا ۵ (بسیار بعید ... تقریباً حتمی)</summary>
    public int? RiskLikelihood { get; set; }

    /// <summary>۱ تا ۵ (ناچیز ... فاجعه‌بار)</summary>
    public int? RiskSeverity { get; set; }

    public int? RiskScore { get; set; }

    /// <summary>Low, Medium, High, Critical (از امتیاز ماتریس)</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? RiskLevel { get; set; }

    /// <summary>اولویت اعلامی توسط ثبت‌کننده (قبل از مشتق‌سازی)؛ Priority مقدار نهایی است.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? RequestedPriority { get; set; }

    /// <summary>توضیح نحوه‌ی مشتق‌سازی اولویت (برای ممیزی و نمایش).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? PriorityRationale { get; set; }

    /// <summary>هدف SLA: مهلت پاسخ اولیه / مهلت رفع (از لحظه‌ی ثبت و بر اساس اولویت نهایی).</summary>
    public DateTime? ResponseDueDate { get; set; }
    public DateTime? ResolutionDueDate { get; set; }

    // ==============================================================================
    // A5: اطلاعات خرابی گزارش‌شده در لحظه‌ی ثبت (ISO 14224) - به Work Order منتقل می‌شود تا اطلاعات خرابی گم نشود.
    // ==============================================================================
    public int? FailureModeId { get; set; }
    public FailureMode? FailureMode { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? FailureEffect { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(9,2)")]
    public decimal? ReportedDowntimeHours { get; set; }
}
