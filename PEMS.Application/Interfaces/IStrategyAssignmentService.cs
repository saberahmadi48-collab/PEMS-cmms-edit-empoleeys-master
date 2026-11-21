using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// تخصیص نسخه‌ی استراتژی به تگ‌ها (RCM کلاس‌محور): تکی، دسته‌جمعی با پیش‌نمایش/گزارش نتیجه،
/// Override مستند سطح تگ با تایید مستقل، و نمای دوطرفه‌ی تگ.
/// </summary>
public interface IStrategyAssignmentService
{
    Task<BulkAssignPreviewDto> PreviewAsync(int maintenanceStrategyId, BulkAssignFilter filter, int maxRows = 500);

    /// <summary>ارزیابی تعارض/بحرانی‌بودن برای فهرست مشخصی از تگ‌ها (برای صفحه‌ی تخصیص دسته‌جمعی).</summary>
    Task<List<BulkAssignCandidateDto>> EvaluateAsync(int maintenanceStrategyId, IReadOnlyCollection<int> equipmentIds);

    /// <summary>
    /// تخصیص اتمیک: یا همه‌ی تگ‌های بدون تعارض ثبت می‌شوند یا (در صورت شکست ذخیره‌سازی) هیچ‌کدام
    /// (RolledBack=true). تگ‌های دارای تعارض/تکراری رد و در گزارش شمرده می‌شوند.
    /// </summary>
    Task<BulkAssignResultDto> AssignAsync(int maintenanceStrategyId, IReadOnlyCollection<int> equipmentIds, StrategyActor actor);

    Task<OpResult> UnassignAsync(int assignmentId, string? comment, StrategyActor actor);

    Task<OpResult> RequestOverrideAsync(int assignmentId, int? intervalDays, string? taskNote, string? justification, StrategyActor actor);

    Task<OpResult> DecideOverrideAsync(int assignmentId, bool approve, string? comment, StrategyActor actor);

    Task<TagMaintenanceViewDto?> GetTagViewAsync(int equipmentId);

    /// <summary>تخصیص‌های فعالِ مجاز برای ساخت Plan (استراتژی Approved/Active از نوع تولیدکننده‌ی Plan).</summary>
    Task<List<AssignmentChoiceDto>> GetPlanEligibleAssignmentsAsync(int? equipmentId = null, int? strategyId = null);

    Task<EquipmentIdentityDto?> GetEquipmentIdentityAsync(int equipmentId);
}
