using PEMS.Application.DTOs;
namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق PEMS_CMMS_ARCHITECTURE.md بخش ۸ (Work Order)، ۱۰ (Failure) و ۱۲ (Maintenance History):
/// جابجایی وضعیت WorkOrder به همراه اثرات جانبی‌اش - ثبت تاریخچه‌ی تغییر وضعیت، همگام‌سازی
/// FailureRecord از روی فیلدهای ISO-14224 خودِ WorkOrder (به‌جای دو منبع حقیقت مستقل)، و
/// ساخت خودکار MaintenanceHistory از روی WorkOrder تکمیل‌شده (نه یک Entity یتیم بدون نویسنده).
/// </summary>
public interface IWorkOrderLifecycleService
{
    Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int workOrderId, string nextStatus, string? reason, int? changedByEmployeeId,
        int? assignedTeamId, int? assignedEmployeeId,
        string? failureModeCode, string? failureCauseCode, string? rootCause, decimal? downtimeHours,
        string? failureEffect = null, string? correctiveAction = null,
        decimal? contractorCost = null, string? contractorName = null);

    // ---------------- Phase 3B: چرخه‌عمر حاکمیتی روی Workflow Platform ----------------

    /// <summary>ایجاد Work Order حاکمیتی: Snapshot دیسیپلین/دامنه‌ی مبدأ + شروع Instance چرخه‌عمر (Binding صریح) در یک تراکنش. بدون Binding خطای شفاف.</summary>
    Task<(bool Success, string? Error, int? Id)> CreateGovernedAsync(WorkOrderCreateRequest request, WorkOrderActor actor);

    /// <summary>اجرای اکشن گردش‌کار. مجوز/سازمان/دیسیپلین/SoD سمت‌سرور از Runtime و هوک Work Order؛ قواعد دامنه اینجا.</summary>
    Task<(bool Success, string? Error)> ExecuteWorkflowActionAsync(int workOrderId, string actionType, string? comment, WorkOrderActor actor, WorkOrderCompletionData? completion = null);

    /// <summary>دیسیپلین مسئول نگهداری (فقط برنامه‌ریزی نگهداری، فقط پیش از صدور). دیسیپلین مبدأ هرگز تغییر نمی‌کند.</summary>
    Task<(bool Success, string? Error)> SetPlanningAsync(int workOrderId, int responsibleDisciplineId, WorkOrderActor actor);

    /// <summary>تخصیص مجری (تیم/کارمند) توسط سرگروه/معاون همان دیسیپلین مسئول.</summary>
    Task<(bool Success, string? Error)> AssignExecutorAsync(int workOrderId, int? teamId, int? employeeId, WorkOrderActor actor);

    /// <summary>زمان‌بندی برنامه‌ریزی‌شده (PlannedStart/PlannedFinish) برای تقویم نگهداری - همان مرحله/مجوز/اسکوپ دیسیپلینِ AssignExecutorAsync.</summary>
    Task<(bool Success, string? Error)> ScheduleWorkOrderAsync(int workOrderId, DateTime plannedStart, DateTime plannedFinish, WorkOrderActor actor);

    /// <summary>مدل خواندنی مرکزی؛ null برای Work Order Legacy (بدون Instance چرخه‌عمر).</summary>
    Task<PEMS.WorkflowPlatform.Interfaces.WorkflowRuntimeViewModel?> GetWorkflowViewAsync(int workOrderId, WorkOrderActor actor);

    /// <summary>
    /// Work Order هایی که بعد از فعال‌شدن گردش‌کار توسط مسیرهای تولید (PM از Plan، کالیبراسیون ابزار) ساخته شده‌اند و هنوز Instance ندارند را
    /// حاکمیتی می‌کند (Snapshot مبدأ از اجراکننده + شروع چرخه‌عمر). Work Order های قبل از Binding (Legacy) هرگز لمس نمی‌شوند. بدون Binding: بدون اثر.
    /// </summary>
    Task<(bool Success, string? Error, int Count)> GovernPendingNewAsync(WorkOrderActor actor);

    /// <summary>آیا Work Order تحت چرخه‌عمر حاکمیتی است (Instance دارد).</summary>
    Task<bool> IsGovernedAsync(int workOrderId);

    /// <summary>
    /// بازگشایی رسمی Work Order بسته‌شده (H28): مجوز جدا (WorkOrder.Reopen)، دلیل الزامی، ممیزی کامل (WorkOrderStatusHistory + WorkflowHistory + WorkOrderChangeLog).
    /// Work Order حاکمیتی به مرحله‌ی «تأیید دیسیپلین مبدأ» (وضعیت Completed) برمی‌گردد تا اصلاح ساعت‌کار/قطعات (با دلیل و ممیزی) و تأیید و تصویب نهایی دوباره انجام شود.
    /// </summary>
    Task<(bool Success, string? Error)> ReopenAsync(int workOrderId, string? reason, WorkOrderActor actor, string? userName = null, int? userId = null, string? ipAddress = null);
}
