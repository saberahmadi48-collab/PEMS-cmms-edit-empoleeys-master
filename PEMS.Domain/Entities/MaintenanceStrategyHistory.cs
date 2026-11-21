using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// تاریخچه‌ی غیرقابل‌حذف تغییرات استراتژی/تخصیص (چه کسی، چه زمانی، قبل/بعد). Append-only:
/// هیچ مسیر کدی نباید این ردیف‌ها را ویرایش یا حذف کند. (AuditLog سراسری هم تغییرات خام را ثبت
/// می‌کند؛ این جدول رویدادهای چرخه‌عمر را با معنای کسب‌وکاری برای نمایش در UI نگه می‌دارد.)
/// </summary>
public class MaintenanceStrategyHistory
{
    [Key]
    public int MaintenanceStrategyHistoryId { get; set; }

    public int MaintenanceStrategyId { get; set; }
    public MaintenanceStrategy? MaintenanceStrategy { get; set; }

    public int? MaintenanceStrategyAssignmentId { get; set; }

    public int VersionNumber { get; set; }

    // RcmCodes.HistoryAction
    [MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? FromStatus { get; set; }

    [MaxLength(20)]
    public string? ToStatus { get; set; }

    // هر خط: "Field: old -> new"
    public string? ChangesSummary { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public int? ActorEmployeeId { get; set; }

    [MaxLength(200)]
    public string? ActorName { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;
}
