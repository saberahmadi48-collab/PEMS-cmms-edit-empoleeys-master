using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// ممیزی تغییرات «رکوردهای فرزند» Work Order (و بازگشایی): چه‌کسی، چه‌زمانی، روی کدام بخش، در کدام وضعیت/مرحله‌ی Work Order، با چه مقدار قبل/بعد و چرا.
/// Append-only. تلاش‌های ردشده (مثلاً تغییر روی Work Order بسته‌شده) هم با Outcome=Denied و IsSecurityEvent ثبت می‌شوند. FK به WorkOrder عمداً ندارد
/// تا حذف احتمالی داده‌ی تستی ردپای ممیزی را پاک نکند.
/// </summary>
public class WorkOrderChangeLog
{
    [Key]
    public int WorkOrderChangeLogId { get; set; }

    public int WorkOrderId { get; set; }

    [MaxLength(50)]
    public string Section { get; set; } = string.Empty;

    // Add, Edit, Remove, Reopen
    [MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RecordRef { get; set; }

    // Allowed, Denied
    [MaxLength(20)]
    public string Outcome { get; set; } = "Allowed";

    [MaxLength(60)]
    public string Code { get; set; } = "Allowed";

    [MaxLength(30)]
    public string? WorkOrderStatus { get; set; }

    [MaxLength(100)]
    public string? WorkflowStage { get; set; }

    public int? ChangedByEmployeeId { get; set; }
    public int? ChangedByUserId { get; set; }

    [MaxLength(256)]
    public string? ChangedByUserName { get; set; }

    public DateTime ChangedDate { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string? Reason { get; set; }

    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }

    public bool IsSecurityEvent { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }
}
