using PEMS.Application.Common;

namespace PEMS.Application.DTOs;

/// <summary>بازیگر عملیات: همیشه EmployeeId (نه UserId) + نام کاربری برای ثبت در تاریخچه.</summary>
public sealed record StrategyActor(int? EmployeeId, string? UserName, IReadOnlyList<string> Roles);

/// <summary>نتیجه‌ی عملیات سرویس: خطاها کد-محورند و در لایه‌ی وب ترجمه می‌شوند (هرگز 500 برای ورودی بد).</summary>
public class OpResult
{
    public bool Success => Errors.Count == 0;
    public List<RuleError> Errors { get; init; } = new();
    public int? Id { get; set; }
    public string? InfoCode { get; set; }

    public static OpResult Ok(int? id = null, string? infoCode = null) => new() { Id = id, InfoCode = infoCode };
    public static OpResult Fail(params RuleError[] errors) => new() { Errors = errors.ToList() };
    public static OpResult Fail(IEnumerable<RuleError> errors) => new() { Errors = errors.ToList() };
}

public class StrategyInputDto
{
    public string? StrategyType { get; set; }
    public string? ConsequenceCategory { get; set; }
    public bool IsHiddenFailure { get; set; }
    public int? EquipmentClassId { get; set; }
    public int? FailureModeId { get; set; }
    public int? FailureCauseId { get; set; }
    public string? Title { get; set; }
    public string? FunctionalFailure { get; set; }
    public string? FailureEffect { get; set; }
    public string? Justification { get; set; }
    public string? RecommendedTriggerType { get; set; }
    public string? Frequency { get; set; }
    public int? DefaultIntervalDays { get; set; }
    public string? Description { get; set; }
    public DateTime? StartDate { get; set; }
    public string? TaskType { get; set; }
    public int? OwnerDisciplineId { get; set; }
    public string? RequiredSkills { get; set; }
    public decimal? EstimatedDurationHours { get; set; }
    public string? CbmParameter { get; set; }
    public string? ThresholdUnit { get; set; }
    public decimal? AlarmThreshold { get; set; }
    public decimal? TripThreshold { get; set; }
    public int? PfIntervalDays { get; set; }
    public int? InspectionIntervalDays { get; set; }
    public string? Source { get; set; }
    public DateTime? ReviewDate { get; set; }
    public int? ConditionMonitoringPointId { get; set; }

    // Concurrency: همان RowVersion فرم ویرایش (Round-trip شده در hidden field)
    public byte[]? RowVersion { get; set; }
}

/// <summary>هویت تجهیز که همه‌جا (فرم، لیست، Plan، WO) یکسان و فقط‌خواندنی نمایش داده می‌شود.</summary>
public class EquipmentIdentityDto
{
    public int EquipmentId { get; set; }
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public int? EquipmentClassId { get; set; }
    public string? EquipmentClassName { get; set; }
    public string? EquipmentClassCode { get; set; }
    public string? Area { get; set; }
    public string? Unit { get; set; }
    public string? CriticalityClass { get; set; }
}

public class StrategyAssignmentRowDto
{
    public int AssignmentId { get; set; }
    public int MaintenanceStrategyId { get; set; }
    public int StrategyVersion { get; set; }
    public EquipmentIdentityDto Equipment { get; set; } = new();
    public string Status { get; set; } = "";
    public string OverrideStatus { get; set; } = "None";
    public int? OverrideIntervalDays { get; set; }
    public string? OverrideTaskNote { get; set; }
    public string? OverrideJustification { get; set; }
    public DateTime AssignedDate { get; set; }
    public int PlanCount { get; set; }
}

public class StrategyHistoryRowDto
{
    public int VersionNumber { get; set; }
    public string Action { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? ChangesSummary { get; set; }
    public string? Comment { get; set; }
    public string? ActorName { get; set; }
    public DateTime Timestamp { get; set; }
}

public class StrategyPlanUsageDto
{
    public int MaintenancePlanId { get; set; }
    public string PlanNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public int StrategyId { get; set; }
    public int StrategyVersion { get; set; }
    public bool OutOfSync { get; set; }
}

public class StrategyVersionRowDto
{
    public int MaintenanceStrategyId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public DateTime? ApprovedAt { get; set; }
}

public class StrategyDetailDto
{
    public int MaintenanceStrategyId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public Guid FamilyId { get; set; }
    public StrategyInputDto Input { get; set; } = new();
    public string? EquipmentClassName { get; set; }
    public string? FailureModeName { get; set; }
    public string? FailureModeNameFa { get; set; }
    public string? OwnerDisciplineName { get; set; }
    public int? LegacyEquipmentId { get; set; }
    public string? LegacyEquipmentTag { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? AuthorEmployeeId { get; set; }
    public bool IsActive { get; set; }

    public List<StrategyAssignmentRowDto> Assignments { get; set; } = new();
    public List<StrategyHistoryRowDto> History { get; set; } = new();
    public List<StrategyPlanUsageDto> Plans { get; set; } = new();
    public List<StrategyVersionRowDto> Versions { get; set; } = new();
}

// ==================== تخصیص (Assignment) ====================

public class BulkAssignFilter
{
    public int? EquipmentClassId { get; set; }
    public List<string> Areas { get; set; } = new();
    public List<int> DisciplineIds { get; set; } = new();
    public string? TagSearch { get; set; }
    public bool IncludeClassDescendants { get; set; } = true;
}

public static class BulkAssignConflict
{
    public const string None = "None";
    public const string AlreadyAssigned = "AlreadyAssigned";
    public const string ConflictingStrategy = "ConflictingStrategy";
    public const string CriticalityBlocks = "CriticalityBlocks";
    public const string ClassMismatch = "ClassMismatch";
}

public class BulkAssignCandidateDto
{
    public EquipmentIdentityDto Equipment { get; set; } = new();
    public string Conflict { get; set; } = BulkAssignConflict.None;
    public int? ConflictingStrategyId { get; set; }
    public bool WillReceive => Conflict == BulkAssignConflict.None;
}

public class BulkAssignPreviewDto
{
    public int MaintenanceStrategyId { get; set; }
    public int TotalMatching { get; set; }
    public bool Truncated { get; set; }
    public List<BulkAssignCandidateDto> Candidates { get; set; } = new();
    public int WillReceiveCount => Candidates.Count(c => c.WillReceive);
    public int ConflictCount => Candidates.Count(c => !c.WillReceive);
}

public class BulkAssignSkipDto
{
    public string Tag { get; set; } = "";
    public string Reason { get; set; } = "";   // BulkAssignConflict.* یا "NotFound"
    public int? OtherStrategyId { get; set; }
}

public class BulkAssignResultDto
{
    /// <summary>گزارش نتیجه: هر تگ ردشده با دلیل (برای نمایش در صفحه‌ی استراتژی).</summary>
    public List<BulkAssignSkipDto> Skipped { get; set; } = new();

    public int Assigned { get; set; }
    public int SkippedAlreadyAssigned { get; set; }
    public int SkippedConflict { get; set; }
    public int SkippedCriticality { get; set; }
    public int SkippedNotFound { get; set; }
    public List<RuleError> Errors { get; set; } = new();
    public bool RolledBack { get; set; }
    public bool Success => Errors.Count == 0;
}

// ==================== Plan ====================

public class PlanFormContextDto
{
    public int AssignmentId { get; set; }
    public int StrategyId { get; set; }
    public int StrategyVersion { get; set; }
    public string StrategyType { get; set; } = "";
    public string ConsequenceCategory { get; set; } = "";
    public string? FailureModeName { get; set; }
    public string? FailureModeNameFa { get; set; }
    public string? RecommendedTriggerType { get; set; }
    public int? SuggestedIntervalDays { get; set; }
    public string? SuggestedTitle { get; set; }
    public int? InspectionIntervalDays { get; set; }
    public EquipmentIdentityDto Equipment { get; set; } = new();
}

public class PlanInputDto
{
    public int MaintenanceStrategyAssignmentId { get; set; }
    public string? Title { get; set; }
    public string? TriggerType { get; set; }
    public int? IntervalDays { get; set; }
    public string? MeterType { get; set; }
    public decimal? MeterInterval { get; set; }
    public DateTime? NextDueDate { get; set; }
    public bool AllowPastDueDate { get; set; }
    public string? Description { get; set; }
    public int? JobPlanId { get; set; }
    public string? ScheduleMode { get; set; }
}

public class AssignmentChoiceDto
{
    public int AssignmentId { get; set; }
    public string Label { get; set; } = "";
    public string Tag { get; set; } = "";
    public string StrategyType { get; set; } = "";
    public string? FailureModeName { get; set; }
    public string? FailureModeNameFa { get; set; }
    public int StrategyVersion { get; set; }
    public string? EquipmentClassName { get; set; }
}

// ==================== Coverage ====================

public class CoverageRowDto
{
    public string Key { get; set; } = "";
    public int TotalEquipment { get; set; }
    public int WithApprovedStrategy { get; set; }
    public int WithoutStrategy { get; set; }
    public double CoveragePercent => TotalEquipment == 0 ? 0 : Math.Round(100.0 * WithApprovedStrategy / TotalEquipment, 1);
}

public class UncoveredCriticalRowDto
{
    public EquipmentIdentityDto Equipment { get; set; } = new();
}

public class StrategyCoverageDto
{
    public int TotalEquipment { get; set; }
    public int CoveredEquipment { get; set; }
    public List<CoverageRowDto> ByCriticality { get; set; } = new();
    public List<CoverageRowDto> ByClass { get; set; } = new();
    public List<UncoveredCriticalRowDto> CriticalWithoutStrategy { get; set; } = new();
    public int CriticalWithoutStrategyTotal { get; set; }
    public int UnratedEquipment { get; set; }
    public int UnclassifiedEquipment { get; set; }
}

/// <summary>نمای دوطرفه: از تگ همه‌ی استراتژی‌ها/Planها/WOهای باز.</summary>
public class TagMaintenanceViewDto
{
    public EquipmentIdentityDto Equipment { get; set; } = new();
    public List<StrategyAssignmentRowDto> Assignments { get; set; } = new();
    public List<TagPlanRowDto> Plans { get; set; } = new();
    public List<TagWorkOrderRowDto> OpenWorkOrders { get; set; } = new();
}

public class TagPlanRowDto
{
    public int MaintenancePlanId { get; set; }
    public string PlanNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime? NextDueDate { get; set; }
    public int? StrategyId { get; set; }
    public int? StrategyVersion { get; set; }
}

public class TagWorkOrderRowDto
{
    public int WorkOrderId { get; set; }
    public string WorkOrderNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
}

// ==================== Failure-mode catalog ====================

public class FailureModeOptionDto
{
    public int FailureModeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public int? EquipmentClassId { get; set; }
    public string? EquipmentClassName { get; set; }
    public List<FailureCauseOptionDto> Causes { get; set; } = new();
}

public class FailureCauseOptionDto
{
    public int FailureCauseId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
}

public class FailureModeEditDto
{
    public int? FailureModeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameFa { get; set; }
    public string? Description { get; set; }
    public int? EquipmentClassId { get; set; }
    public List<int> CauseIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

// ==================== Import ====================

public class StrategyImportRowResultDto
{
    public int RowNumber { get; set; }
    public string? Reference { get; set; }
    public bool Accepted { get; set; }
    public List<RuleError> Errors { get; set; } = new();
}

public class StrategyImportResultDto
{
    public int RowsRead { get; set; }
    public int RowsAccepted { get; set; }
    public int RowsRejected => RowsRead - RowsAccepted;
    public bool Committed { get; set; }
    public List<StrategyImportRowResultDto> Rows { get; set; } = new();
}
