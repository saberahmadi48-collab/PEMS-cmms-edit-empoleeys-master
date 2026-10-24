namespace PEMS.Application.DTOs;

/// <summary>
/// ردیف Grid استراتژی‌های نگهداری (RCM) - طبق سند «PEMS — Global Data Grid Standard»: فقط
/// فیلدهای نمایش‌داده‌شده، نه Include(Equipment) کامل. مقادیر StrategyType/ConsequenceCategory/
/// Frequency کدهای پایدارند و در View با Resource ترجمه می‌شوند.
/// </summary>
public class MaintenanceStrategyListDto
{
    public int MaintenanceStrategyId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public string Status { get; set; } = "Draft";
    public string? Title { get; set; }

    // استراتژی قدیمی تک‌تگی (در صورت وجود) - برای الگوهای کلاس‌محور Null است
    public int? EquipmentId { get; set; }
    public string EquipmentTagNumber { get; set; } = "";

    public int? EquipmentClassId { get; set; }
    public string? EquipmentClassName { get; set; }
    public string? EquipmentClassCode { get; set; }

    public int AssignedTagCount { get; set; }

    public string StrategyType { get; set; } = "";
    public int? FailureModeId { get; set; }
    public string? FailureModeCode { get; set; }
    public string? FailureModeName { get; set; }
    public string? FailureModeNameFa { get; set; }
    public string ConsequenceCategory { get; set; } = "";
    public bool IsHiddenFailure { get; set; }
    public string? Frequency { get; set; }
    public string? Justification { get; set; }
    public DateTime? ReviewDate { get; set; }
    public bool ReviewOverdue { get; set; }
}

public class StrategyListFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int? EquipmentId { get; set; }
    public int? EquipmentClassId { get; set; }
    public string? Status { get; set; }
    public string? StrategyType { get; set; }
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
}
