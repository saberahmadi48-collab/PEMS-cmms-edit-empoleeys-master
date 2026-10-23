using PEMS.Application.DTOs;

namespace PEMS.Web.Models;

/// <summary>مدل صفحه‌ی داشبورد مدیریتی KPI (/Kpi/Executive).</summary>
public sealed class ExecutiveKpiPageViewModel
{
    public ExecutiveKpiDashboardDto Dashboard { get; set; } = new();
    public List<KpiTargetDto> Targets { get; set; } = new();

    /// <summary>Reliability.Edit - فقط این کاربران فرم ویرایش هدف را می‌بینند.</summary>
    public bool CanEditTargets { get; set; }

    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
