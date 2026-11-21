namespace PEMS.Application.DTOs;

public class WorkRequestListDto
{
    public int WorkRequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedDate { get; set; }
    public string? EquipmentTagNumber { get; set; }
}

/// <summary>
/// طبق CMMS Production Baseline v1 بند ۸: جزئیات کامل یک Work Request - شامل تاریخچه‌ی
/// تغییر وضعیت (Append-only) برای ردیابی کامل مسیر Draft تا Converted/Rejected/Cancelled.
/// </summary>
public class WorkRequestDetailDto
{
    public int WorkRequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string RequestedByName { get; set; } = "";
    public DateTime RequestedDate { get; set; }
    public int? EquipmentId { get; set; }
    public string? EquipmentTagNumber { get; set; }
    public int? AssetId { get; set; }
    public string? AssetNumber { get; set; }
    public int? FunctionalLocationId { get; set; }
    public string? FunctionalLocationBreadcrumb { get; set; }
    public string? ReportedCondition { get; set; }
    public string Priority { get; set; } = "";
    public string? EquipmentCriticalityClass { get; set; }
    public int? SuggestedDisciplineId { get; set; }
    public string? SuggestedDisciplineName { get; set; }
    public string Status { get; set; } = "";
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedDate { get; set; }
    public string? RejectionReason { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public int? LinkedWorkOrderId { get; set; }
    public string? LinkedWorkOrderNumber { get; set; }

    // A4/A5
    public bool IsHseRelated { get; set; }
    public bool IsEmergency { get; set; }
    public int? RiskLikelihood { get; set; }
    public int? RiskSeverity { get; set; }
    public int? RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public string? RequestedPriority { get; set; }
    public string? PriorityRationale { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public DateTime? ResolutionDueDate { get; set; }
    public string? FailureModeName { get; set; }
    public string? FailureEffect { get; set; }
    public decimal? ReportedDowntimeHours { get; set; }

    /// <summary>ممیزی کسب‌وکاری Work Request (Append-only، جدا از تاریخچه‌ی گردش‌کار).</summary>
    public List<WorkRequestStatusHistoryDto> StatusHistory { get; set; } = new();

    /// <summary>
    /// مدل خواندنی مرکزی گردش‌کار (مراحل، مرحله‌ی جاری، اکشن‌های مجاز، شرکت‌کنندگان، تاریخچه). null = رکورد Legacy
    /// (هنوز Instance ندارد) یا پیش‌نویس (هنوز ارسال نشده). UI ساختار گردش‌کار را فقط از همین می‌خواند.
    /// </summary>
    public PEMS.WorkflowPlatform.Interfaces.WorkflowRuntimeViewModel? Workflow { get; set; }

    /// <summary>تصمیم سمت‌سرور: آیا اکشن «تبدیل به WorkOrder» (اکشن کسب‌وکاری) در دسترس کاربر جاری است.</summary>
    public bool CanConvert { get; set; }

    /// <summary>رکورد قبل از مهاجرت که هنوز Instance گردش‌کار ندارد (مسیر Legacy).</summary>
    public bool IsLegacyWithoutWorkflow { get; set; }
}

/// <summary>ورودی کامل ثبت Work Request (A2/A4/A5). مسیر قدیمی CreateAsync با پارامترهای ساده همچنان کار می‌کند.</summary>
public class WorkRequestCreateRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int RequestedById { get; set; }
    public int? EquipmentId { get; set; }
    public int? AssetId { get; set; }
    public int? FunctionalLocationId { get; set; }
    public string? ReportedCondition { get; set; }
    public string Priority { get; set; } = "Medium";
    public int? SuggestedDisciplineId { get; set; }
    public bool IsHseRelated { get; set; }
    public bool IsEmergency { get; set; }
    public int? RiskLikelihood { get; set; }
    public int? RiskSeverity { get; set; }
    public int? FailureModeId { get; set; }
    public string? FailureEffect { get; set; }
    public decimal? ReportedDowntimeHours { get; set; }
}

/// <summary>کاربر اجراکننده: شناسه‌ی کارمند + نقش‌های واقعی. توسط Controller از سشن سمت‌سرور ساخته می‌شود، نه از ورودی کاربر.</summary>
public sealed record WorkRequestActor(int? EmployeeId, IReadOnlyList<string> Roles);

public class WorkRequestStatusHistoryDto
{
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string? ChangedByName { get; set; }
    public DateTime ChangedDate { get; set; }
    public string? Reason { get; set; }
}
