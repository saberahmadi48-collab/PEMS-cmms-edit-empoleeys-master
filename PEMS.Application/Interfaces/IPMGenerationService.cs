using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق «PEMS Phase 6» بند ۱۵/۵۵-۵۶: تولید WorkOrder از MaintenancePlan - قطعی و Idempotent.
/// قاعده‌ی Idempotency: اگر یک Plan از قبل یک WorkOrder فعال (غیر از Closed/Cancelled/Rejected)
/// دارد، اجرای دوباره برای همان Plan هیچ WorkOrder جدیدی نمی‌سازد - نه یک کلید ترکیبی پیچیده،
/// بلکه ساده‌ترین قاعده‌ای که واقعاً تضمین می‌کند «تولید دوباره = بدون تکرار».
/// </summary>
public interface IPMGenerationService
{
    /// <summary>
    /// تولید برای یک Plan مشخص (مسیر دستی، از UI موجود) - اگر Idempotency Guard رد شود، Null برمی‌گرداند.
    /// </summary>
    Task<string?> GenerateForPlanAsync(int planId);

    /// <summary>
    /// طبق بند ۱۵: تولید انبوه برای همه‌ی Plan های Active که سررسید شده‌اند (NextDueDate &lt;= اکنون).
    /// </summary>
    Task<PMGenerationResultDto> GenerateDueWorkOrdersAsync();

    /// <summary>
    /// سیاست Floating: برای Plan هایی که WO آن‌ها تکمیل شده، سررسید بعدی را از تاریخ تکمیل واقعی
    /// محاسبه می‌کند. تعداد Plan های به‌روزشده را برمی‌گرداند.
    /// </summary>
    Task<int> ReconcileFloatingSchedulesAsync();
}
