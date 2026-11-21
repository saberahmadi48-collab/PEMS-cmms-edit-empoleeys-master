using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق سند معماری Labor Cost - بند ۱۱ تا ۱۴: ثبت واقعی ساعت‌کار، محاسبه‌ی هزینه در سرور
/// (نه از سمت Client)، و Snapshot کردن نرخ در لحظه‌ی ثبت.
/// طبق «PEMS — Work Order Governance Remediation» F3: ثبت ساعت کار واقعی داده‌ی اجرا است - actor
/// (کاربر واقعاً واردشده، نه فقط employeeId که ساعتش ثبت می‌شود) باید مجری واجد شرایط دیسیپلین/
/// سازمان مسئول باشد، دقیقاً هم‌الگو با Perm.Execute در گردش‌کار.
/// </summary>
public interface IWorkOrderLaborService
{
    Task<int> AddLaborEntryAsync(
        int workOrderId, int employeeId, int? teamId, DateTime workDate,
        decimal regularHours, decimal overtimeHours, string? notes, WorkOrderActor actor, bool isCorrection = false);

    // نرخ فعلی کارمند (Normal/Overtime) طبق تاریخچه‌ی EmployeeLaborRate در یک تاریخ مشخص
    Task<(decimal RegularRate, decimal OvertimeRate)> GetEffectiveRatesAsync(int employeeId, DateTime asOfDate);

    Task<decimal> GetTotalLaborCostAsync(int workOrderId);
}
