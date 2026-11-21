namespace PEMS.Application.DTOs;

/// <summary>
/// ردیف Grid برنامه‌های نگهداری - طبق سند «PEMS — Global Data Grid Standard»: فقط فیلدهای
/// نمایش‌داده‌شده (نه Include(Equipment)+Include(MaintenanceStrategy) کامل). IsOverdue همون
/// فرمول قبلی MaintenancePlan/Index.cshtml (تاریخ گذشته یا Meter از سررسید عبور کرده) است،
/// اما این‌جا در SQL محاسبه می‌شود.
/// </summary>
public class MaintenancePlanListDto
{
    public int MaintenancePlanId { get; set; }
    public string PlanNumber { get; set; } = "";
    public int EquipmentId { get; set; }
    public string EquipmentTagNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? StrategyType { get; set; }

    // هویت تجهیز و نسخه‌ی استراتژی (RCM کلاس‌محور): تگ، نام، کلاس، بحرانی‌بودن، حالت خرابی، نسخه، سیاست زمان‌بندی
    public string EquipmentName { get; set; } = "";
    public string? EquipmentClassName { get; set; }
    public string? CriticalityClass { get; set; }
    public int? StrategyId { get; set; }
    public int? StrategyVersion { get; set; }
    public string? FailureModeName { get; set; }
    public string? FailureModeNameFa { get; set; }
    public string ScheduleMode { get; set; } = "Fixed";
    public bool HasAssignment { get; set; }
    public string TriggerType { get; set; } = "";
    public int? IntervalDays { get; set; }
    public string? MeterType { get; set; }
    public decimal? MeterInterval { get; set; }
    public decimal? LastExecutionMeterReading { get; set; }
    public decimal? NextDueMeterReading { get; set; }
    public DateTime? LastExecutionDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool IsOverdue { get; set; }
}
