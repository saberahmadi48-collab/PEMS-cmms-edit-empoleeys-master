using PEMS.Application.DTOs;

namespace PEMS.Web.ViewModels;

public class StrategyIndexViewModel
{
    public PagedResult<MaintenanceStrategyListDto> Page { get; set; } = new();
    public StrategyListFilter Filter { get; set; } = new();
    public string? SelectedEquipmentLabel { get; set; }
    public List<LookupItem> Classes { get; set; } = new();
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanApprove { get; set; }
}

public class LookupItem
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public string? Code { get; set; }
}

public class StrategyFormViewModel
{
    public int? MaintenanceStrategyId { get; set; }
    public string Status { get; set; } = "Draft";
    public int VersionNumber { get; set; } = 1;
    public StrategyInputDto Input { get; set; } = new();
    public List<LookupItem> Classes { get; set; } = new();
    public List<LookupItem> Disciplines { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    // نمایش فقط‌خواندنی هویت تجهیز (در صورت شروع از یک تگ)
    public EquipmentIdentityDto? StartedFromEquipment { get; set; }

    // شناسه‌های انتخاب‌شده برای نمایش اولیه‌ی Picker حالت خرابی
    public string? SelectedFailureModeLabel { get; set; }
    public bool WillCreateNewVersion { get; set; }
}

public class StrategyDetailsViewModel
{
    public StrategyDetailDto Strategy { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool CanApprove { get; set; }
    public bool IsAuthorOrSubmitter { get; set; }
    public List<string> Errors { get; set; } = new();

    // گزارش نتیجه‌ی آخرین تخصیص: هر ردیف [تگ، دلیل رد، استراتژی متعارض]
    public List<string[]> AssignReport { get; set; } = new();
}

public class BulkAssignViewModel
{
    public int? StrategyId { get; set; }
    public List<LookupItem> UsableStrategies { get; set; } = new();
    public Dictionary<int, BulkAssignCandidateDto> Candidates { get; set; } = new();
}

public class PlanFormViewModel
{
    public PlanFormContextDto? Context { get; set; }
    public PlanInputDto Input { get; set; } = new();
    public List<LookupItem> JobPlans { get; set; } = new();
    public List<AssignmentChoiceDto> Choices { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class StrategyImportViewModel
{
    public string Kind { get; set; } = "strategies";
    public string? Token { get; set; }
    public StrategyImportResultDto? Result { get; set; }
    public Dictionary<int, List<string>> Messages { get; set; } = new();
}
