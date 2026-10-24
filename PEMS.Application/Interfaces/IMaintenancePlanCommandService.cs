using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ساخت Maintenance Plan از یک «تخصیص» (نسخه‌ی استراتژی + تگ). تجهیز همیشه از تخصیص مشتق
/// می‌شود - هیچ equipmentId از کلاینت پذیرفته نمی‌شود.
/// </summary>
public interface IMaintenancePlanCommandService
{
    Task<PlanFormContextDto?> GetFormContextAsync(int assignmentId);

    Task<OpResult> CreateAsync(PlanInputDto input, StrategyActor actor, DateTime? now = null);

    Task<OpResult> DeactivateAsync(int maintenancePlanId, StrategyActor actor);
}

public interface IStrategyCoverageService
{
    Task<StrategyCoverageDto> GetCoverageAsync(int topUncoveredCritical = 100);
}

public interface IFailureModeCatalogService
{
    /// <summary>جستجوی حالت‌های خرابی ISO 14224 برای یک کلاس (شامل والدین کلاس + حالت‌های عمومی)؛ q با نرمال‌سازی.</summary>
    Task<List<FailureModeOptionDto>> SearchAsync(int? equipmentClassId, string? query, int take = 30);

    Task<FailureModeEditDto?> GetAsync(int failureModeId);

    Task<OpResult> SaveAsync(FailureModeEditDto dto);

    Task<OpResult> SetActiveAsync(int failureModeId, bool isActive);

    /// <summary>برای تکمیل WO: آیا Code در کاتالوگ فعال وجود دارد؟ (Null = تهی/نامشخص)</summary>
    Task<bool> IsKnownCodeAsync(string? code);
}
