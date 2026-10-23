using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// ==============================================================================
/// پیوند «نسخه‌ی استراتژی ← تگ تجهیز» (RCM کلاس‌محور). الگوی استراتژی روی کلاس تجهیز تعریف
/// می‌شود و اینجا به یک یا چند تگ تخصیص می‌یابد؛ MaintenancePlan از همین تخصیص ساخته می‌شود
/// (نه از یک جفت آزاد equipmentId + strategyId). هر تگ برای یک الگو فقط یک تخصیص فعال دارد.
///
/// Override سطح تگ (تناوب/توضیح کار/توجیه) مجاز است اما فقط پس از تایید مستقل (OverrideStatus=Approved)
/// روی Plan اعمال می‌شود؛ درخواست‌کننده نمی‌تواند تاییدکننده‌ی Override خودش باشد.
/// ==============================================================================
/// </summary>
public class MaintenanceStrategyAssignment : BaseEntity
{
    [Key]
    public int MaintenanceStrategyAssignmentId { get; set; }

    public int MaintenanceStrategyId { get; set; }
    public MaintenanceStrategy? MaintenanceStrategy { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    // RcmCodes.AssignmentStatus
    public string Status { get; set; } = RcmCodes.AssignmentStatus.Active;

    public DateTime AssignedDate { get; set; } = DateTime.Now;

    public int? AssignedByEmployeeId { get; set; }

    // ==================== Override مستند سطح تگ ====================
    public int? OverrideIntervalDays { get; set; }

    [MaxLength(1000)]
    public string? OverrideTaskNote { get; set; }

    [MaxLength(1000)]
    public string? OverrideJustification { get; set; }

    // RcmCodes.OverrideStatus
    public string OverrideStatus { get; set; } = RcmCodes.OverrideStatus.None;

    public int? OverrideRequestedByEmployeeId { get; set; }
    public int? OverrideApprovedByEmployeeId { get; set; }
    public DateTime? OverrideApprovedAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
