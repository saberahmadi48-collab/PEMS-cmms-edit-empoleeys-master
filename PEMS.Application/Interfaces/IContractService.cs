using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IContractService
{
    Task<List<ContractDto>> GetAllAsync();

    // طبق «PEMS — Global Grid Hardening» P1: GetAllAsync بالا کل جدول Contracts را بدون
    // Skip/Take بارگذاری می‌کند (GetByIdAsync هم از همین متد استفاده می‌کند - دست‌نخورده ماند).
    // این متد جدید و جدا صفحه‌بندی واقعی سمت SQL برای Index دارد.
    Task<PagedResult<ContractDto>> GetPagedAsync(int page, int pageSize);
    Task<ContractDto?> GetByIdAsync(int id);
    /// <summary>
    /// قدیمی/بدون کاربر: ساخت قرارداد با همان تراکنش و قفل شماره‌ی قرارداد (P0.4)، اما بدون بررسی مجوز و بدون رویداد ایجادکننده (کاربر نامشخص) - قراردادی که
    /// این‌گونه ساخته شود باید با EnsureCreatorEventAsync ترمیم شود. اعتبارسنجی نامعتبر InvalidOperationException می‌اندازد (رفتار قبلی). مسیر UI از
    /// CreateWithCreatorEventAsync استفاده می‌کند.
    /// </summary>
    Task<int> CreateAsync(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection = false);

    /// <summary>
    /// P0.4: مسیر دستیِ UI. ۱) مجوز آگاه-از-کاربر (Contracts.Create) پیش از هر درج بررسی می‌شود (رد/نامشخص = بدون قرارداد، با ردیف ممیزی)؛ ۲) روی SQL Server
    /// قرارداد + AuditLogهای عمومی + رویداد «Contracts/Create/Allow» برای <paramref name="creatorUserId"/> یک تراکنش‌اند و زیر قفل کاربردی شماره‌ی قرارداد
    /// (همان قفل مسیر مناقصه) اجرا می‌شوند؛ ۳) پس از Commit نامطمئن وضعیت واقعی از دیتابیس خوانده می‌شود.
    /// </summary>
    Task<ContractCreateResult> CreateWithCreatorEventAsync(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection, int creatorUserId, string? ipAddress);

    /// <summary>ترمیم رویداد ایجادکننده برای هر قرارداد (حتی قراردادهای دستی/قدیمی)؛ همان منطق ITenderService.EnsureContractCreatorEventAsync.</summary>
    Task<ContractCreatorEventStatus> EnsureCreatorEventAsync(int contractId, string? ipAddress = null);
    Task ChangeStatusAsync(int contractId, string status);
}
