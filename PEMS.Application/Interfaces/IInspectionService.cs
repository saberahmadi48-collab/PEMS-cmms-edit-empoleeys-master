using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق CMMS Production Baseline v1 - CMMS-07 (Inspection): بازرسی دوره‌ای کمی تجهیز
/// (Condition Monitoring) - نقطه‌ی ورود دوم و مستقل به InspectionRecord، جدا از پذیرش ITP
/// که QCController مدیریت می‌کند.
/// </summary>
public interface IInspectionService
{
    // طبق ممیزی مستقل فاز ۶B Wave B1 (P1-02): GetPagedFleetAsync دیگر با یک GetFleetAsync
    // کامل (بدون Skip/Take) کار نمی‌کند - اولویت‌بندی Alert/Overdue/DueSoon مستقیماً به یک
    // Expression قابل ترجمه به SQL (CASE WHEN روی همون فیلدهای خام Measurement/Limit/
    // NextInspectionDate) تبدیل شده، پس Skip/Take واقعاً روی IQueryable اجرا می‌شود. طبق grep
    // در کل ریپو، GetFleetAsync() هیچ مصرف‌کننده‌ای جز همین متد نداشت - حذف شد.
    Task<PagedResult<InspectionListItemDto>> GetPagedFleetAsync(int page, int pageSize);

    Task<List<InspectionListItemDto>> GetForEquipmentAsync(int equipmentId);

    [PEMS.Application.Common.PemsOperation("Inspection", "Create", "Inspection.Create")]
    Task<(bool Success, string? Error, int? InspectionRecordId)> CreateAsync(
        int equipmentId, string inspectionType, DateTime inspectionDate, string? inspector,
        decimal? measurementValue, string? measurementUnit, decimal? lowerLimit, decimal? upperLimit,
        string? recommendation, DateTime? nextInspectionDate, string? description);
}
