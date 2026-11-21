using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>SCRUM-559: the item fields the More actions menu edits.</summary>
/// <param name="EquipmentTag">Tag number of the related equipment (21k+ records, so typed, not picked); empty clears it.</param>
public sealed record VdrItemEditInput(string? DocumentDescription, int? DocumentClass, DateTime? PlannedFirstIssueDate, string? EquipmentTag,
    string? DisciplineCodeRaw, string? VendorFormatDocumentNumber);

/// <summary>SCRUM-559: outcome of a menu action; <paramref name="Error"/> is a resource key (Vdr_Err_*).</summary>
public sealed record VdrActionResult(bool Success, string? Error)
{
    public static VdrActionResult Ok() => new(true, null);
    public static VdrActionResult Fail(string key) => new(false, key);
}

public interface IVDRService
{
    Task<List<VDRItemDto>> GetAllAsync(int? projectId = null, int? vendorId = null, string? workflowStatus = null);

    // طبق سند Global Data Grid Standard فاز ۲: نسخه‌ی صفحه‌بندی‌شده‌ی سمت-SQL برای
    // Views/VDR/Index.cshtml - GetAllAsync بالا دست‌نخورده باقی می‌ماند (مصرف‌کننده‌ی دیگری
    // ندارد جز همین Index که حالا به این متد سوییچ می‌کند، اما حذف نشد تا ریسک بی‌ربط ایجاد نشود)
    Task<PagedResult<VDRListItemDto>> GetPagedAsync(
        int? projectId, int? vendorId, string? workflowStatus, int page, int pageSize, string? search = null);

    /// <summary>
    /// SCRUM-409: همه‌ی ردیف‌های منطبق با همان فیلترهای فهرست (بدون صفحه‌بندی) برای خروجی Excel؛ حداکثر <paramref name="maxRows"/> ردیف
    /// برگردانده می‌شود و Total تعداد واقعی منطبق است تا فراخواننده سقف را تشخیص دهد.
    /// </summary>
    Task<(List<VDRListItemDto> Items, int Total)> GetAllMatchingAsync(
        int? projectId, int? vendorId, string? workflowStatus, string? search, int maxRows);

    Task<VDRItemDto?> GetByIdAsync(int id);

    Task<int> CreateManualAsync(VDRItemDto item);

    /// <summary>SCRUM-559: edits the item fields of the More actions menu; an inactive item is refused. Error = resource key.</summary>
    Task<VdrActionResult> UpdateDetailsAsync(int vdrItemId, VdrItemEditInput input, string? userName);

    /// <summary>SCRUM-559: soft delete (IsActive = false) with a reason in the audit; refused while a review runs. Error = resource key.</summary>
    Task<VdrActionResult> DeactivateAsync(int vdrItemId, string? reason, string? userName);

    Task<VDRDashboardDto> GetDashboardAsync(int? projectId = null);
}
