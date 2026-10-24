using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>داشبورد مدیریتی KPI: فقط شاخص‌هایی که منبع داده‌ی واقعی دارند (KpiCatalog)، با وضعیت نسبت به هدف.</summary>
public interface IExecutiveKpiService
{
    /// <param name="from">ابتدای بازه (شاخص‌های وابسته به بازه: هزینه، دستورکار، تولید).</param>
    /// <param name="to">انتهای بازه (شامل کل روز).</param>
    Task<ExecutiveKpiDashboardDto> GetAsync(DateTime from, DateTime to);
}

public interface IKpiTargetService
{
    /// <summary>هدف مؤثر همه‌ی شاخص‌های کاتالوگ (ردیف ذخیره‌شده، وگرنه پیش‌فرض کاتالوگ).</summary>
    Task<List<KpiTargetDto>> GetEffectiveAsync();

    /// <summary>
    /// ذخیره‌ی هدف یک شاخص. target=null یعنی «بدون هدف». Warn باید در سمت درست هدف باشد (بیشتر-بهتر: ≤ هدف، کمتر-بهتر: ≥ هدف).
    /// مجوز در لایه‌ی Controller بررسی می‌شود؛ این سرویس فقط اعتبارسنجی داده و ذخیره است.
    /// </summary>
    Task<PcResult> SaveAsync(string key, decimal? target, decimal? warn);
}
